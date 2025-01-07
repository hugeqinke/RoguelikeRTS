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
    public float MoveRotateAmount;
    public float MoveClearance;

    public float AlertRadius;


    // Misc
    public MoveGroupPool MoveGroupPool;

    // Monobehavior adapters
    public Dictionary<GameObject, int> PlayerIndexMap;
    public Dictionary<GameObject, int> IndexMap; // Maps unit gameobject to index for easier input updates
    public Dictionary<GameObject, UnitController> UnitControllers;

    // Job system orders
    public List<GameObject> Units;
    public Transform[] Transforms;
    public List<MovementComponent> MovementComponents;

    // Physics
    public bool DBGMediumHash;
    public SpatialHashConfig MediumSpatialHashConfig;
    private SpatialHashMeta _mediumSpatialHashMeta;

    public int Substeps;

    public bool DBGTarget;
    public bool DBGResolvedState;
    public bool DBGForward;

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

        Transforms = new Transform[objs.Length];

        for (int i = 0; i < objs.Length; i++)
        {
            var obj = objs[i];
            Units.Add(obj);

            var unitController = obj.GetComponent<UnitController>();
            MovementComponents.Add(new MovementComponent(unitController));
            Transforms[i] = obj.transform;

            IndexMap.Add(obj, i);
            UnitControllers.Add(obj, unitController);
            if (unitController.Config.Owner == Owner.Player)
            {
                PlayerIndexMap.Add(obj, i);
            }
        }
    }

    public void SetMovementValues(Dictionary<GameObject, Vector3> positions)
    {
        foreach (var kvp in positions)
        {
            var unit = kvp.Key;
            var idx = IndexMap[unit];
            var movementComponent = MovementComponents[idx];
            movementComponent.TargetPosition = positions[unit];
            movementComponent.MoveStartPosition = movementComponent.Position;
            movementComponent.Resolved = false;

            var dir = positions[unit] - unit.transform.position;
            if (dir.sqrMagnitude > Mathf.Epsilon)
            {
                movementComponent.Orientation = Vector3.SignedAngle(
                    Vector3.forward,
                    dir,
                    Vector3.up
                );
            }

            MovementComponents[idx] = movementComponent;
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
            MovementComponents[idx] = movementComponent;
        }
    }

    private void LateUpdate()
    {
        MoveGroupPool.Prune(this);
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
        for (int i = 0; i < Units.Count; i++)
        {
            var unitController = UnitControllers[Units[i]];
            unitController.DBG_Movement = MovementComponents[i];
        }
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
            var unit = MovementComponents[i];

            // Some preliminary calculations we can't do in the job system
            if (!unit.HoldingPosition && unit.Resolved)
            {
                var relativeSqrDst = math.lengthsq(unit.TargetPosition - unit.Position);
                var thresholdSqrRadius = unit.ReturnRadius * unit.ReturnRadius;

                if (relativeSqrDst > thresholdSqrRadius)
                {
                    unit.Resolved = false;
                    unit.MoveStartPosition = unit.Position;
                }
            }

            movementComponents[i] = unit;

        }

        var physicsJob = new PhysicsJob()
        {
            SpatialHashMap = mediumSpatialHash,
            SpatialHashMeta = _mediumSpatialHashMeta,
            Substeps = 4,
            MovingNeighborRadius = MovingNeighborRadius,
            Units = movementComponents,
            DeltaTime = Time.fixedDeltaTime,
            RotateAmount = rotateAmount * Mathf.Deg2Rad,
            MoveClearance = MoveClearance,
            MoveRotateAmount = MoveRotateAmount
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
            simulator.MovementComponents[idx] = movement;
        }

        FreePool.Push(groupId);
        MoveGroups.Remove(groupId);
    }
}
