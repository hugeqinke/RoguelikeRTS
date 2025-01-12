using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Jobs;
using Unity.Burst;

public class Simulator : MonoBehaviour
{
    public float NeighborScanRadius;
    public float ResponseCoefficient;

    public InputManager InputManager;
    public static Simulator Instance;

    public int ResolveCollisionIterations;

    public float MovingNeighborRadius;
    private static float rotateAmount = 30f;

    public float CombatClearRange;


    // Misc
    public MoveGroupPool MoveGroupPool;

    // Monobehavior adapters
    public Dictionary<GameObject, int> PlayerIndexMap;
    public Dictionary<GameObject, int> IndexMap; // Maps unit gameobject to index for easier input updates
    public Dictionary<GameObject, UnitController> UnitControllers;

    // Job system orders
    public List<GameObject> Units;
    public List<MovementComponent> MovementComponents;

    // Physics
    public SpatialHashConfig MediumSpatialHashConfig;
    private SpatialHashMeta _mediumSpatialHashMeta;

    public int Substeps;

    public bool DBGMediumHash;
    public bool DBGTarget;
    public bool DBGResolvedState;
    public bool DBGForward;
    public bool DBGAttack;


    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
    }

    // Start is called before the first frame update
    void Start()
    {
        MoveGroupPool = new MoveGroupPool();

        _mediumSpatialHashMeta = new SpatialHashMeta
        {
            Columns = MediumSpatialHashConfig.Columns,
            Rows = MediumSpatialHashConfig.Rows,
            Origin = MediumSpatialHashConfig.Origin,
            Size = MediumSpatialHashConfig.Size
        };

        Bake();
    }

    private void Bake()
    {
        var objs = GameObject.FindGameObjectsWithTag(Util.Tags.PlayerUnit);

        IndexMap = new Dictionary<GameObject, int>();
        PlayerIndexMap = new Dictionary<GameObject, int>();
        UnitControllers = new Dictionary<GameObject, UnitController>();

        Units = new List<GameObject>();
        MovementComponents = new List<MovementComponent>();


        for (int i = 0; i < objs.Length; i++)
        {
            var obj = objs[i];
            Units.Add(obj);

            var unitController = obj.GetComponent<UnitController>();
            MovementComponents.Add(new MovementComponent(unitController));

            IndexMap.Add(obj, i);
            UnitControllers.Add(obj, unitController);
            if (unitController.Config.Owner == Owner.Player)
            {
                PlayerIndexMap.Add(obj, i);
            }
        }
    }

    public void SetAttackValues(GameObject unit, GameObject target)
    {
        var unitIdx = IndexMap[unit];
        var targetIdx = IndexMap[target];
        var movementComponent = MovementComponents[unitIdx];
        movementComponent.Target = targetIdx;
        MovementComponents[unitIdx] = movementComponent;
    }

    public void SetMovementValues(GameObject unit, float3 position)
    {
        var idx = IndexMap[unit];
        var movementComponent = MovementComponents[idx];
        movementComponent.TargetPosition = position;
        movementComponent.StopPosition = position;
        movementComponent.MoveStartPosition = movementComponent.Position;
        movementComponent.Resolved = false;

        var dir = position - movementComponent.Position;
        if (math.lengthsq(dir) > Mathf.Epsilon)
        {
            movementComponent.Orientation = MathUtil.signedangle(
                math.forward(),
                dir,
                math.up()
            );

            movementComponent.PreferredDir = math.normalize(dir);
        }

        MovementComponents[idx] = movementComponent;
    }

    public void SetMovementValues(Dictionary<GameObject, Vector3> positions)
    {
        foreach (var kvp in positions)
        {
            var unit = kvp.Key;
            SetMovementValues(kvp.Key, kvp.Value);
        }
    }

    public void ResetMovement(HashSet<GameObject> units)
    {
        foreach (var unit in units)
        {
            var idx = IndexMap[unit];

            var movementComponent = MovementComponents[idx];
            movementComponent.Velocity = float3.zero;
            movementComponent.Resolved = false;
            movementComponent.HoldingPosition = false;
            movementComponent.Target = -1;
            MovementComponents[idx] = movementComponent;
        }
    }

    private void ClearDeadUnits()
    {
        var remap = new Dictionary<int, int>();

        var newUnits = new List<GameObject>();
        var newMovementComponents = new List<MovementComponent>();

        var deadUnits = new List<GameObject>();

        // TODO: Consider jobifying this?
        for (int i = 0; i < MovementComponents.Count; i++)
        {
            var movementComponent = MovementComponents[i];
            var unit = Units[i];

            if (movementComponent.Health <= 0)
            {
                PlayerIndexMap.Remove(unit);
                IndexMap.Remove(unit);
                UnitControllers.Remove(unit);

                deadUnits.Add(unit);

                Destroy(unit.gameObject);
            }
            else
            {
                var newIndex = newUnits.Count;

                remap.Add(i, newIndex);
                IndexMap[unit] = newIndex;

                newUnits.Add(unit);
                newMovementComponents.Add(movementComponent);
            }
        }

        for (int i = 0; i < newMovementComponents.Count; i++)
        {
            var movementComponent = newMovementComponents[i];
            if (remap.ContainsKey(movementComponent.Target))
            {
                var newIndex = remap[movementComponent.Target];
                movementComponent.Target = newIndex;
            }
            else
            {
                movementComponent.Target = -1;
            }

            newMovementComponents[i] = movementComponent;
        }

        InputManager.ClearDeadUnits(deadUnits);

        Units = newUnits;
        MovementComponents = newMovementComponents;
    }

    private void LateUpdate()
    {
        MoveGroupPool.Prune(this);

        ClearDeadUnits();

        // Unit stuff updates
        for (int i = 0; i < UnitControllers.Count; i++)
        {
            var unit = UnitControllers[Units[i]];
            var movement = MovementComponents[i];

            var lookatPosition = unit.Billboard.transform.position + Camera.main.transform.forward;
            // lookatPosition.y = unit.HealthBar.transform.position.y;
            unit.Billboard.transform.LookAt(lookatPosition);

            var t = Mathf.InverseLerp(0, movement.MaxHealth, movement.Health);
            var length = Mathf.Lerp(0, unit.BackgroundBar.transform.localScale.x, t);
            var scale = unit.HealthBar.transform.localScale;
            scale.x = length;
            unit.HealthBar.transform.localScale = scale;

            var position = unit.BackgroundBar.transform.position;
            position.x -= unit.BackgroundBar.transform.localScale.x * 0.5f;
            position.x += length * 0.5f;
            unit.HealthBar.transform.position = position;
        }
    }

    private int ComputeSpatialHash(SpatialHashMeta meta, Cell cell)
    {
        var hash = cell.Row * meta.Columns + cell.Column;
        return hash;
    }

    private Cell ComputeRowColumns(SpatialHashMeta meta, int idx)
    {
        var position = MovementComponents[idx].Position;
        var rel = position - meta.Origin;
        var row = Mathf.FloorToInt(rel.z / meta.Size);
        var column = Mathf.FloorToInt(rel.x / meta.Size);

        return new Cell()
        {
            Row = row,
            Column = column
        };
    }

    private NativeMultiHashMap<int, int> CreateSpatialHash(SpatialHashMeta meta)
    {
        var spatialhash = new NativeMultiHashMap<int, int>(Units.Count * 8, Allocator.TempJob);

        for (int i = 0; i < Units.Count; i++)
        {
            var unit = Units[i];
            var hash = ComputeSpatialHash(meta, ComputeRowColumns(meta, i));
            spatialhash.Add(hash, i);
        }

        return spatialhash;
    }

    private void Update()
    {
        // Unit updates
        for (int i = 0; i < Units.Count; i++)
        {
            var unitController = UnitControllers[Units[i]];
            unitController.DBG_Movement = MovementComponents[i];
        }

        var mediumSpatialHash = CreateSpatialHash(_mediumSpatialHashMeta);

        var allocSize = Units.Count;
        var movementComponents = new NativeArray<MovementComponent>(allocSize, Allocator.TempJob);
        for (int i = 0; i < MovementComponents.Count; i++)
        {
            movementComponents[i] = MovementComponents[i];
        }

        var combatJob = new CombatJob()
        {
            Units = movementComponents,
            SpatialHash = mediumSpatialHash,
            Meta = _mediumSpatialHashMeta,
            CombatClearRange = CombatClearRange,
            Time = Time.time
        };

        var combatJobHandle = combatJob.Schedule();
        // TODO: could move this to late update instead
        combatJobHandle.Complete();

        for (int i = 0; i < MovementComponents.Count; i++)
        {
            MovementComponents[i] = movementComponents[i];
        }

        movementComponents.Dispose();
        mediumSpatialHash.Dispose();
    }

    private void FixedUpdate()
    {
        // Create spatial hash
        // Profiler.BeginSample("Create spatial hash");
        var mediumSpatialHash = CreateSpatialHash(_mediumSpatialHashMeta);
        // Profiler.EndSample();

        var allocSize = Units.Count;
        var movementComponents = new NativeArray<MovementComponent>(allocSize, Allocator.TempJob);
        for (int i = 0; i < MovementComponents.Count; i++)
        {
            var movementComponent = MovementComponents[i];
            movementComponent.DBG = UnitControllers[Units[i]].DBG;
            MovementComponents[i] = movementComponent;
            movementComponents[i] = MovementComponents[i];
        }

        var physicsJob = new PhysicsJob()
        {
            SpatialHashMap = mediumSpatialHash,
            SpatialHashMeta = _mediumSpatialHashMeta,
            Substeps = 4,
            MovingNeighborRadius = MovingNeighborRadius,
            Units = movementComponents,
            DeltaTime = Time.fixedDeltaTime,
            CurrentTime = Time.fixedTime,
            RotateAmount = rotateAmount * Mathf.Deg2Rad,
            CombatClearRange = CombatClearRange
        };

        var physicsJobHandle = physicsJob.Schedule();
        physicsJobHandle.Complete();

        for (int i = 0; i < Units.Count; i++)
        {
            var unit = Units[i];
            var movement = movementComponents[i];

            unit.transform.position = movement.Position;

            var vLenSq = math.lengthsq(movement.Velocity);
            if (vLenSq > Mathf.Epsilon)
            {
                movement.Orientation = Vector3.SignedAngle(Vector3.forward, movement.Velocity, Vector3.up);
            }

            MovementComponents[i] = movement;
        }

        mediumSpatialHash.Dispose();
        movementComponents.Dispose();
    }

    private void OnDrawGizmos()
    {
        if (DBGTarget)
        {
            for (int i = 0; i < MovementComponents.Count; i++)
            {
                var movementComponent = MovementComponents[i];
                Gizmos.DrawWireSphere(movementComponent.TargetPosition, 0.5f);
            }
        }

        if (DBGAttack)
        {
            for (int i = 0; i < MovementComponents.Count; i++)
            {
                var movement = MovementComponents[i];

                if (movement.Target != -1)
                {
                    var target = MovementComponents[movement.Target];
                    Gizmos.DrawLine(movement.Position, target.Position);
                }

                if (movement.Attacking)
                {
                    Gizmos.color = Color.red;
                }
                else
                {
                    Gizmos.color = Color.grey;
                }

                Gizmos.DrawWireSphere(movement.Position + new float3(0, 0.5f, 0), 0.5f);

            }
        }

        if (DBGResolvedState)
        {
            for (int i = 0; i < MovementComponents.Count; i++)
            {
                var movementComponent = MovementComponents[i];
                if (movementComponent.Resolved)
                {
                    Gizmos.color = Color.grey;
                }
                else
                {
                    Gizmos.color = Color.green;
                }

                Gizmos.DrawWireSphere(movementComponent.Position + new float3(0, 0.5f, 0), 0.5f);
                // Gizmos.color = Color.green;
                // Gizmos.DrawLine(movementComponent.Position, movementComponent.TargetPosition);
                // Gizmos.color = Color.red;
                // Gizmos.DrawLine(movementComponent.Position, movementComponent.StopPosition);
            }
        }

        if (DBGForward)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < MovementComponents.Count; i++)
            {
                var movementComponent = MovementComponents[i];
                var forward = Quaternion.Euler(0, movementComponent.Orientation, 0) * Vector3.forward;
                Gizmos.DrawRay(movementComponent.Position, forward * 0.5f);
            }
        }

        if (DBGMediumHash)
        {
            var origin = MediumSpatialHashConfig.Origin;
            var size = new Vector3(MediumSpatialHashConfig.Size, 0, MediumSpatialHashConfig.Size);

            Gizmos.color = Color.white;
            for (int row = 0; row < MediumSpatialHashConfig.Rows; row++)
            {
                for (int col = 0; col < MediumSpatialHashConfig.Columns; col++)
                {
                    var center = origin;
                    center.z += MediumSpatialHashConfig.Size * 0.5f;
                    center.z += row * MediumSpatialHashConfig.Size;
                    center.x += MediumSpatialHashConfig.Size * 0.5f;
                    center.x += col * MediumSpatialHashConfig.Size;

                    Gizmos.DrawWireCube(center, size);
                }
            }
        }
    }
}

public enum AvoidanceType
{
    Stationary,
    Moving
}

public class UnitState
{
    public UnitController UnitComponent;
    public GameObject CollidingNeighbor;
    public bool Arrived;
}

[System.Serializable]
public class RVOProperties
{
    public float NeighborRadius;
    public int MaxNeighbors;
    public float TimeHorizonAgents;
    public float TimeHorizonObstacles;
}

public class ReferenceStore<T>
{
    public Dictionary<int, int> References;
    public T[] Archetypes;
    public int Count;

    public ReferenceStore(int size)
    {
        References = new Dictionary<int, int>();
        Archetypes = new T[size];
        Count = 0;
    }

    public void Add(GameObject obj, T archetype)
    {
        References.Add(obj.GetInstanceID(), Count);
        Archetypes[Count] = archetype;
        Count++;
    }

    public T GetArchetype(GameObject obj)
    {
        var instanceId = obj.GetInstanceID();
        var archetypeId = References[instanceId];
        return Archetypes[archetypeId];
    }
}

public struct UnitArchetype
{
    public UnitController UnitComponent;
}

public class MoveGroup
{
    public HashSet<GameObject> Units;

    public MoveGroup()
    {
        Units = new HashSet<GameObject>();
    }

    public bool Resolved(Simulator simulator)
    {
        foreach (var unit in Units)
        {
            var idx = simulator.IndexMap[unit];
            var movementComponent = simulator.MovementComponents[idx];

            if (!movementComponent.Resolved)
            {
                return false;
            }
        }

        return true;
    }
}

public struct SpatialHashMeta
{
    public int Rows;
    public int Columns;
    public float Size;
    public float3 Origin;
}

[System.Serializable]
public class SpatialHashConfig
{
    public int Rows;
    public int Columns;
    public float Size;
    public GameObject Center;

    public Vector3 Origin
    {
        get
        {
            var delta = new Vector3(Columns * Size, 0, Rows * Size) * 0.5f;
            return Center.transform.position - delta;
        }
    }
}

public struct Cell
{
    public int Row;
    public int Column;

    public Cell(int row, int column)
    {
        Row = row;
        Column = column;
    }
}

public class MoveGroupPool
{
    public Dictionary<int, HashSet<int>> MoveGroups;
    public Stack<int> FreePool;
    public int MaxMoveGroup = 100;

    public MoveGroupPool()
    {
        MoveGroups = new Dictionary<int, HashSet<int>>();
        FreePool = new Stack<int>();
        for (int i = MaxMoveGroup - 1; i >= 0; i--)
        {
            FreePool.Push(i);
        }
    }

    private int GetFreeId()
    {
        if (FreePool.Count == 0)
        {
            for (int i = MaxMoveGroup + 19; i >= MaxMoveGroup; i--)
            {
                FreePool.Push(i);
            }

            MaxMoveGroup += 20;
        }

        var freeId = FreePool.Pop();
        return freeId;
    }

    public void Assign(Simulator simulator, IEnumerable<GameObject> units)
    {
        var freeId = GetFreeId();

        var moveGroupUnits = new HashSet<int>();
        foreach (var unit in units)
        {
            var idx = simulator.IndexMap[unit];

            var movement = simulator.MovementComponents[idx];

            if (movement.CurrentGroup != -1)
            {
                if (MoveGroups.ContainsKey(movement.CurrentGroup))
                {
                    MoveGroups[movement.CurrentGroup].Remove(idx);
                }
            }

            movement.CurrentGroup = freeId;
            simulator.MovementComponents[idx] = movement;

            moveGroupUnits.Add(idx);
        }

        MoveGroups.Add(freeId, moveGroupUnits);
    }


    public void Prune(Simulator simulator)
    {
        var newMoveGroup = new Dictionary<int, HashSet<int>>();
        var removeIds = new List<int>();
        foreach (var groupKVP in MoveGroups)
        {
            var keep = false;
            foreach (var idx in groupKVP.Value)
            {
                if (!simulator.MovementComponents[idx].Resolved)
                {
                    keep = true;
                    break;
                }
            }

            if (keep)
            {
                newMoveGroup.Add(groupKVP.Key, groupKVP.Value);
            }
            else
            {
                removeIds.Add(groupKVP.Key);
            }
        }

        foreach (var removeId in removeIds)
        {
            Free(simulator, removeId);
        }

        MoveGroups = newMoveGroup;
    }

    public MovementComponent Reassign(Simulator simulator, MovementComponent unit, int unitId)
    {
        var moveGroupUnits = new HashSet<int>();
        if (unit.CurrentGroup != -1)
        {
            if (MoveGroups.ContainsKey(unit.CurrentGroup))
            {
                MoveGroups[unit.CurrentGroup].Remove(unitId);
            }
        }

        var freeId = GetFreeId();

        unit.CurrentGroup = freeId;

        moveGroupUnits.Add(unitId);
        MoveGroups.Add(freeId, moveGroupUnits);

        return unit;
    }

    public void Free(Simulator simulator, int groupId)
    {
        foreach (var idx in MoveGroups[groupId])
        {
            var movement = simulator.MovementComponents[idx];
            movement.CurrentGroup = -1;
            movement.TargetPosition = movement.StopPosition;
            simulator.MovementComponents[idx] = movement;
        }

        FreePool.Push(groupId);
        MoveGroups.Remove(groupId);
    }
}
