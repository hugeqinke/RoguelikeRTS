using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Jobs;
using UnityEngine.UIElements;

// [BurstCompile]
struct UpdateJob : IJobParallelForTransform
{
    [ReadOnly] public NativeArray<MovementComponent> MovementComponents;
    [ReadOnly] public NativeArray<float3> Velocities;

    public NativeArray<float3> Positions;

    // Misc
    public float DeltaTime;

    public void Execute(int index, TransformAccess transform)
    {
        var movement = MovementComponents[index];
        var velocity = Velocities[index];

        // Update position
        var delta = velocity * DeltaTime;
        // var dir = movement.PreferredPosition - movement.Position;

        // var dirLengthSq = math.lengthsq(dir);
        // var deltaLengthSq = math.lengthsq(delta);

        // if (dirLengthSq < deltaLengthSq && delta.x * dir.z - delta.z * dir.x < math.EPSILON)
        // {
        //     // Apply this change only if the unit is going exactly in the preferred direction
        //     // and the remaining length is less than the distance that the unit is trying to move
        //     delta = math.sqrt(dirLengthSq) * math.normalizesafe(delta);
        // }

        // Update
        var position = movement.Position + delta;
        transform.position = position;
        Positions[index] = position;
    }
}

// [BurstCompile]
struct IntegrateForcesJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<MovementComponent> MovementComponents;
    public NativeArray<float3> Velocities;

    public float DeltaTime;

    public void Execute(int index)
    {
        var movement = MovementComponents[index];
        var velocity = Velocities[index];

        // Apply body forces
        // var baseVel = movement.MaxSpeed * math.normalizesafe(prefDir);
        var prefDir = movement.PreferredPosition - movement.Position;
        var bodyForce = movement.MaxSpeed * math.normalizesafe(prefDir);

        velocity += bodyForce * DeltaTime;

        Velocities[index] = velocity;
    }
}

struct GatherContactsJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<MovementComponent> MovementComponents;
    // Spatial hashes
    [ReadOnly] public NativeMultiHashMap<int, int> MediumSpatialHash;
    public SpatialHashMeta MediumSpatialHashMeta;

    [ReadOnly] public NativeHashMap<long, Contact> ReadContacts;
    public NativeHashMap<long, Contact>.ParallelWriter WriteContacts;

    public void Execute(int index)
    {
        var numCollisions = GatherCollisions(index, out int[] collisions);
        for (int i = 0; i < numCollisions; i++)
        {
            var collisionIdx = collisions[i];
            var collisionMovement = MovementComponents[collisionIdx];

            var contactHash = GenerateContactHash(index, collisionIdx);

            // warm start
            if (ReadContacts.TryGetValue(contactHash, out Contact item))
            {
                var contact = new Contact()
                {
                    Body1 = index,
                    Body2 = collisionIdx,
                    Position = collisionMovement.Position,
                    Velocity = collisionMovement.Velocity,
                    Pn = item.Pn,
                    Pt = item.Pt
                };

                WriteContacts.TryAdd(contactHash, contact);
            }
            else
            {
                var contact = new Contact()
                {
                    Body1 = collisionIdx,
                    Position = collisionMovement.Position,
                    Velocity = collisionMovement.Velocity,
                    Pn = 0,
                    Pt = 0
                };

                WriteContacts.TryAdd(contactHash, contact);
            }
        }
    }

    private uint GenerateContactHash(int index, int collisionIdx)
    {
        var minIdx = (uint)math.min(index, collisionIdx);
        var maxIdx = (uint)math.max(index, collisionIdx);

        uint minBits = 0x0000ffff & minIdx;
        uint maxBits = 0xffff0000 & (maxIdx << 16);

        return minBits | maxBits;
    }

    private int GatherCollisions(int index, out int[] collisions)
    {
        var movement = MovementComponents[index];

        var collisionLimit = 20;

        collisions = new int[collisionLimit];
        var collisionCount = 0;

        var coord = GetCoordinate(index);
        var hashCount = GetHashes(coord.Row, coord.Column, out int[] hashes);
        for (int hashIdx = 0; hashIdx < hashCount && collisionCount < collisionLimit; hashIdx++)
        {
            var hash = hashes[hashIdx];
            var neighbors = MediumSpatialHash.GetValuesForKey(hash);

            foreach (var neighborIdx in neighbors)
            {
                if (neighborIdx == index)
                {
                    continue;
                }

                var neighborMovement = MovementComponents[neighborIdx];

                var relLengthSq = math.lengthsq(movement.Position - neighborMovement.Position);

                var collideRadius = movement.Radius + neighborMovement.Radius;
                if (relLengthSq < collideRadius * collideRadius)
                {
                    collisions[collisionCount] = neighborIdx;
                    collisionCount++;

                    if (collisionCount >= collisionLimit)
                    {
                        break;
                    }
                }
            }
        }

        return collisionCount;
    }

    private int GetHashes(int row, int col, out int[] hashes)
    {
        hashes = new int[9];

        var directions = new int[9, 2]{
                    {0, 0},
                    {1, 0},
                    {0, 1},
                    {-1, 0},
                    {0, -1},
                    {1, 1},
                    {-1, 1},
                    {1, -1},
                    {-1, -1},
                };

        int count = 0;

        for (int i = 0; i < 9; i++)
        {
            var newRow = row + directions[i, 0];
            var newCol = col + directions[i, 1];

            if (newRow >= 0 && newRow < MediumSpatialHashMeta.Rows
                    && newCol >= 0 && newCol < MediumSpatialHashMeta.Columns)
            {
                hashes[i] = GetHash(newRow, newCol);
                count++;
            }
        }

        return count;
    }

    private Coordinate GetCoordinate(int idx)
    {
        float3 position = MovementComponents[idx].Position;

        var rel = position - MediumSpatialHashMeta.Origin;

        var row = (int)math.floor(rel.z / MediumSpatialHashMeta.Size);
        row = math.clamp(row, 0, MediumSpatialHashMeta.Rows);

        var column = (int)math.floor(rel.x / MediumSpatialHashMeta.Size);
        column = math.clamp(column, 0, MediumSpatialHashMeta.Columns);

        return new Coordinate
        {
            Row = row,
            Column = column
        };
    }

    private int GetHash(int row, int column)
    {
        return row * MediumSpatialHashMeta.Columns + column;
    }
}

public struct Coordinate
{
    public int Row;
    public int Column;
}

struct Contact
{
    public int Body1;
    public int Body2;
    public float Pn;
    public float Pt;
    public float3 Velocity;
    public float3 Position;
}

struct PrestepJob : IJobParallelFor
{
    public NativeMultiHashMap<int, int> Colliders;
    [ReadOnly] public NativeMultiHashMap<long, Contact> Contacts;

    public void Execute(int index)
    {

    }
}

// [BurstCompile]
struct ComputeImpulsesJob : IJobParallelFor
{
    [ReadOnly] public NativeHashMap<long, Contact> Contacts;
    [ReadOnly] public NativeArray<MovementComponent> MovementComponents;
    [ReadOnly] public NativeArray<float3> Velocities;
    public NativeArray<float3> ResolveVelocities;

    public float DeltaTime;

    public void Execute(int index)
    {
        var movement = MovementComponents[index];
        var velocity = Velocities[index];

        // Collect collisions
        // var contacts = Contacts.GetValuesForKey(index);

        // foreach (var contact in contacts)
        // {
        //     var collisionIdx = contact.Index;
        //     var collisionMovement = MovementComponents[collisionIdx];
        //     var collisionVelocity = Velocities[collisionIdx];

        //     // calculated norms, tangents, etc
        //     var normal = math.normalizesafe(movement.Position - collisionMovement.Position);
        //     var kNormal = movement.Mass + collisionMovement.Mass;
        //     var mass = 1.0f / kNormal;

        //     /* TODO - incorporate bias */

        //     // calculate and apply normal forces
        //     var dv = collisionVelocity - velocity;
        //     var vn = math.dot(dv, normal);

        //     var dPn = mass * vn;
        //     dPn = math.max(dPn, 0);

        //     var Pn = dPn * normal;
        //     velocity += Pn * (1 / movement.Mass);

        //     // This isn't permanent - carry this over temporarily
        //     // so I can make the appropriate tangent force calculate
        //     collisionVelocity -= Pn * (1 / movement.Mass);

        //     // calculate and apply tangent forces
        //     // I'm not too sure if friction forces are absolutely necessary,
        //     // but they make things a lot less slidy
        //     dv = collisionVelocity - velocity;

        //     var friction = movement.Friction * collisionMovement.Friction;
        //     var tangent = new float3(normal.z, normal.y, -normal.x);
        //     var vt = math.dot(dv, tangent);
        //     var dPt = mass * vt;

        //     var maxPt = friction * dPn;
        //     dPt = math.clamp(dPt, -maxPt, maxPt);

        //     var Pt = dPt * tangent;

        //     velocity += Pt / movement.Mass;
        // }

        ResolveVelocities[index] = velocity;
    }

    private float3 clamp(float3 v, float max)
    {
        var lenSq = math.lengthsq(v);
        if (lenSq > max)
        {
            var dir = math.normalizesafe(v);
            v = max * dir;
        }

        return v;
    }
}

public class Simulator : MonoBehaviour
{
    public float NeighborScanRadius;
    public float ResponseCoefficient;

    public InputManager InputManager;
    public static Simulator Instance;

    public int ResolveCollisionIterations;

    public float MovingNeighborRadius;
    private static float rotateAmount = 30f;

    public float AlertRadius;

    // Misc
    public HashSet<MoveGroup> MoveGroups;
    public Dictionary<GameObject, MoveGroup> MoveGroupMap;

    // Monobehavior adapters
    public Dictionary<GameObject, int> PlayerIndexMap;
    public Dictionary<GameObject, int> IndexMap; // Maps unit gameobject to index for easier input updates
    public Dictionary<GameObject, UnitController> UnitControllers;

    // Job system orders
    public List<GameObject> Units;
    public Transform[] Transforms;
    public List<MovementComponent> MovementComponents;
    private TransformAccessArray _accessArray;

    // Spatial hash
    public bool DBGMediumHash;
    public SpatialHashConfig MediumSpatialHashConfig;
    private SpatialHashMeta _mediumSpatialHashMeta;

    // Physics Systems
    private NativeHashMap<long, Contact> _readContacts;
    private NativeHashMap<long, Contact> _writeContacts;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        _accessArray.Dispose();
        _writeContacts.Dispose();
    }

    // Start is called before the first frame update
    void Start()
    {
        MoveGroups = new HashSet<MoveGroup>();
        MoveGroupMap = new Dictionary<GameObject, MoveGroup>();

        // Initialize size doesn't really matter here, since the write hashmap
        // will have a more accurate estimate of capacity
        _readContacts = new NativeHashMap<long, Contact>(1000, Allocator.Persistent);

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

        _accessArray = new TransformAccessArray(Transforms);
    }

    public void UpdateMoveGroup(HashSet<GameObject> units)
    {
        var moveGroup = new MoveGroup();
        foreach (var unit in units)
        {
            if (MoveGroupMap.ContainsKey(unit))
            {
                var oldMoveGroup = MoveGroupMap[unit];
                if (oldMoveGroup.Units.Contains(unit))
                {
                    oldMoveGroup.Units.Remove(unit);
                }

                MoveGroupMap.Remove(unit);
            }

            // Add to new MoveGroup
            moveGroup.Units.Add(unit);
            MoveGroupMap.Add(unit, moveGroup);
        }

        MoveGroups.Add(moveGroup);
    }

    public void SetMovementValues(Dictionary<GameObject, Vector3> positions)
    {
        foreach (var kvp in positions)
        {
            var unit = kvp.Key;
            var idx = IndexMap[unit];
            var movementComponent = MovementComponents[idx];
            movementComponent.PreferredPosition = positions[unit];

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
        var resolvedGroups = new List<MoveGroup>();
        foreach (var moveGroup in MoveGroups)
        {
            if (moveGroup.Resolved(this))
            {
                resolvedGroups.Add(moveGroup);
            }
        }

        foreach (var group in resolvedGroups)
        {
            MoveGroups.Remove(group);
            foreach (var unit in group.Units)
            {
                MoveGroupMap.Remove(unit);
            }
        }
    }

    private NativeMultiHashMap<int, int> CreateSpatialHash(SpatialHashConfig config)
    {
        var origin = config.Origin;
        var spatialhash = new NativeMultiHashMap<int, int>(Units.Count, Allocator.TempJob);

        for (int i = 0; i < Units.Count; i++)
        {
            var unit = Units[i];
            var idx = IndexMap[unit];
            Vector3 position = MovementComponents[idx].Position;

            var rel = position - origin;
            var row = Mathf.FloorToInt(rel.z / config.Size);
            var column = Mathf.FloorToInt(rel.x / config.Size);

            var hash = row * config.Columns + column;
            spatialhash.Add(hash, i);
        }

        return spatialhash;
    }

    private void FixedUpdate()
    {
        var allocSize = Units.Count;

        // Create spatial hash
        var mediumSpatialHash = CreateSpatialHash(MediumSpatialHashConfig);

        var movementComponents = new NativeArray<MovementComponent>(allocSize, Allocator.TempJob);
        var velocities = new NativeArray<float3>(allocSize, Allocator.TempJob);

        for (int i = 0; i < allocSize; i++)
        {
            movementComponents[i] = MovementComponents[i];
            velocities[i] = MovementComponents[i].Velocity;
        }

        var dt = Time.fixedDeltaTime;

        // Integrate body forces
        var integrateForcesJob = new IntegrateForcesJob()
        {
            MovementComponents = movementComponents,
            Velocities = velocities,
            DeltaTime = dt
        };

        var integrateForcesJobHandle = integrateForcesJob.Schedule(allocSize, 32);

        _writeContacts = new NativeHashMap<long, Contact>(allocSize * 4, Allocator.Persistent);

        // Gather collisions
        var gatherContactsJob = new GatherContactsJob()
        {
            WriteContacts = _writeContacts.AsParallelWriter(),
            ReadContacts = _readContacts,
            MovementComponents = movementComponents,
            MediumSpatialHash = mediumSpatialHash,
            MediumSpatialHashMeta = _mediumSpatialHashMeta
        };

        var gatherContactsJobHandle = gatherContactsJob.Schedule(allocSize, 32, integrateForcesJobHandle);

        // Resolve collisions
        var computeImpulses = new NativeArray<float3>(allocSize, Allocator.TempJob);
        var computeImpulsesJob = new ComputeImpulsesJob()
        {
            // Positionals
            MovementComponents = movementComponents,
            Velocities = velocities,
            ResolveVelocities = computeImpulses,

            // Collisions
            Contacts = _writeContacts,

            // Misc
            DeltaTime = dt
        };

        var computeImpulsesJobHandle = computeImpulsesJob.Schedule(allocSize, 32, gatherContactsJobHandle);

        // Update Job
        var positions = new NativeArray<float3>(allocSize, Allocator.TempJob);
        var updateJob = new UpdateJob()
        {
            // Containers
            MovementComponents = movementComponents,
            Positions = positions,
            Velocities = computeImpulses,
            // Misc
            DeltaTime = dt
        };

        var updateJobHandle = updateJob.Schedule(_accessArray, computeImpulsesJobHandle);
        updateJobHandle.Complete();

        // Commit update
        for (int i = 0; i < allocSize; i++)
        {
            var movement = MovementComponents[i];
            movement.Position = positions[i];
            movement.Velocity = computeImpulses[i];
            MovementComponents[i] = movement;
        }

        mediumSpatialHash.Dispose();
        movementComponents.Dispose();
        velocities.Dispose();
        positions.Dispose();

        _readContacts.Dispose();
        _readContacts = _writeContacts;
    }

    private void OnDrawGizmos()
    {
        if (DBGMediumHash)
        {
            var origin = MediumSpatialHashConfig.Origin;
            var size = new Vector3(MediumSpatialHashConfig.Size, 0, MediumSpatialHashConfig.Size);

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

    // private void CategorizeUnits(
    //         out HashSet<GameObject> unresolvedUnits,
    //         out HashSet<GameObject> resolvedUnits)
    // {
    //     unresolvedUnits = new HashSet<GameObject>();
    //     resolvedUnits = new HashSet<GameObject>();

    //     for (int i = 0; i < UnitStore.Count; i++)
    //     {
    //         var unitComponent = UnitStore.Archetypes[i].UnitComponent;
    //         if (!unitComponent.BasicMovement.Resolved)
    //         {
    //             unresolvedUnits.Add(unitComponent.gameObject);
    //         }
    //         else
    //         {
    //             resolvedUnits.Add(unitComponent.gameObject);
    //         }
    //     }
    // }

    // private void CheckCombatState()
    // {
    //     for (int i = 0; i < UnitStore.Count; i++)
    //     {
    //         var unitArchetype = UnitStore.Archetypes[i];
    //         var unitComponent = unitArchetype.UnitComponent;
    //         unitComponent.Attacking = false;
    //         unitComponent.InAlertRange = false;

    //         if (unitComponent.Target != null)
    //         {
    //             var relativeDir = unitComponent.Target.transform.position - unitComponent.gameObject.transform.position;
    //             var sqrDst = relativeDir.sqrMagnitude;

    //             var targetUnitArchetype = UnitStore.GetArchetype(unitComponent.Target);
    //             var targetUnitComponent = targetUnitArchetype.UnitComponent;

    //             // Check if should switch Attack targets
    //             // TODO: Can just use opposing layer mask

    //             // Check if in alert radius - if in alert radius, we'll do special stuff
    //             var alertRadius = AlertRadius + unitComponent.Radius + targetUnitComponent.Radius;
    //             if (sqrDst < alertRadius * alertRadius)
    //             {
    //                 unitComponent.InAlertRange = true;
    //             }

    //             // Check if can attack

    //             var radius = unitComponent.AttackRadius
    //                 + unitComponent.Radius
    //                 + targetUnitComponent.Radius;


    //             if (sqrDst < radius * radius)
    //             {
    //                 // TODO: I want a centralized area to update velocity here, rather
    //                 // than scattering it willy nilly
    //                 unitComponent.Kinematic.Velocity = Vector3.zero;
    //                 unitComponent.Attacking = true;
    //             }

    //             if (!unitComponent.Attacking)
    //             {
    //                 var overlaps = Physics.OverlapSphere(unitComponent.transform.position, AlertRadius + unitComponent.Radius, Util.Layers.PlayerAndAIUnitMask);
    //                 var obstacles = CombatClearPath(unitComponent.gameObject, Mathf.Sqrt(sqrDst));

    //                 GameObject nearTarget = null;
    //                 var nearSqrDst = Mathf.Infinity;
    //                 foreach (var overlap in overlaps)
    //                 {
    //                     var overlapArchetype = UnitStore.GetArchetype(overlap.gameObject);
    //                     var overlapUnitComponent = overlapArchetype.UnitComponent;
    //                     if (overlap.gameObject == unitComponent.Target || overlapUnitComponent.Owner == unitComponent.Owner)
    //                     {
    //                         continue;
    //                     }

    //                     // Check if this unit is with an acceptable radius to the attack target
    //                     var dir = overlap.gameObject.transform.position - overlapUnitComponent.transform.position;
    //                     var retargetRadius = AlertRadius + overlapUnitComponent.Radius + unitComponent.Radius;
    //                     if (dir.sqrMagnitude < retargetRadius * retargetRadius)
    //                     {
    //                         // check if there's no clear path to the current attack target
    //                         if (obstacles.Count != 0)
    //                         {
    //                             if (dir.sqrMagnitude < nearSqrDst)
    //                             {
    //                                 nearTarget = overlap.gameObject;
    //                                 nearSqrDst = dir.sqrMagnitude;
    //                             }
    //                         }
    //                     }
    //                 }

    //                 if (nearTarget != null)
    //                 {
    //                     unitComponent.Target = nearTarget.gameObject;
    //                     unitComponent.BasicMovement.TargetPosition = unitComponent.Target.transform.position;
    //                 }

    //             }
    //         }
    //     }
    // }

    // private void ProcessResolvedUnits(HashSet<GameObject> units)
    // {
    //     foreach (var unit in units)
    //     {
    //         var unitArchetype = UnitStore.GetArchetype(unit);
    //         var unitComponent = unitArchetype.UnitComponent;

    //         if (!unitComponent.BasicMovement.HoldingPosition)
    //         {
    //             var relativeSqrDst = (unitComponent.BasicMovement.TargetPosition - unit.transform.position).sqrMagnitude;
    //             var thresholdSqrRadius = unitComponent.BasicMovement.ReturnRadius * unitComponent.BasicMovement.ReturnRadius;
    //             if (relativeSqrDst > thresholdSqrRadius)
    //             {
    //                 unitComponent.BasicMovement.Resolved = false;

    //                 // Clear from old movegroup
    //                 if (InputManager.MoveGroupMap.ContainsKey(unit))
    //                 {
    //                     var oldMoveGroup = InputManager.MoveGroupMap[unit];
    //                     if (oldMoveGroup.Units.Contains(unit))
    //                     {
    //                         oldMoveGroup.Units.Remove(unit);
    //                     }

    //                     InputManager.MoveGroupMap.Remove(unit);
    //                 }

    //                 // Add to new MoveGroup
    //                 var moveGroup = new MoveGroup();
    //                 moveGroup.Units.Add(unit);
    //                 InputManager.MoveGroupMap.Add(unit, moveGroup);
    //             }
    //             else
    //             {
    //                 DecidePassivePhysicsAction(unit, unitComponent);
    //             }
    //         }
    //     }
    // }

    // private int ChooseSignVelocity(GameObject unit, GameObject neighbor)
    // {
    //     var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;
    //     var neighborUnitComponent = UnitStore.GetArchetype(neighbor).UnitComponent;

    //     var neighborDesiredDir = neighborUnitComponent.BasicMovement.TargetPosition - neighbor.transform.position;
    //     var relativeDir = neighbor.transform.position - unit.transform.position;

    //     var a = unit.transform.position;
    //     var b = unit.transform.position + relativeDir;
    //     var c = unit.transform.position + neighborDesiredDir;

    //     if (MathUtil.LeftOf(a, b, c) > 0)
    //     {
    //         return -1;
    //     }

    //     return 1;
    // }

    // private int ChooseSign(GameObject unit, GameObject neighbor)
    // {
    //     var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;
    //     var neighborUnitComponent = UnitStore.GetArchetype(neighbor).UnitComponent;

    //     var relativeDir = (unit.transform.position - neighbor.transform.position).normalized;
    //     var rightDir = Quaternion.Euler(0, rotateAmount, 0) * relativeDir;
    //     var rightTarget = neighbor.transform.position + rightDir * (neighborUnitComponent.Radius + 2 * unitComponent.Radius);
    //     var rightSqrDst = (unitComponent.BasicMovement.TargetPosition - rightTarget).sqrMagnitude;

    //     var leftDir = Quaternion.Euler(0, -rotateAmount, 0) * relativeDir;
    //     var leftTarget = neighbor.transform.position + leftDir * (neighborUnitComponent.Radius + 2 * unitComponent.Radius);
    //     var leftSqrDst = (unitComponent.BasicMovement.TargetPosition - leftTarget).sqrMagnitude;

    //     if (rightSqrDst < leftSqrDst)
    //     {
    //         return 1;
    //     }

    //     return -1;
    // }

    // private bool ResolveHalfPlaneConstraints(GameObject unit, GameObject neighbor)
    // {
    //     var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;
    //     var neighborUnitComponent = UnitStore.GetArchetype(neighbor).UnitComponent;

    //     var relativeDir = unit.transform.position - neighbor.transform.position;
    //     var plane = new MathUtil.Plane(relativeDir, neighbor.transform.position);
    //     var onPositiveHalfPlane = MathUtil.OnPositiveHalfPlane(plane, unitComponent.BasicMovement.TargetPosition, 0);

    //     // Note - apply the second part of this check to Resolved units ONLY since
    //     // units moving in opposite directions could stick to each other if one
    //     // of them has a bad side preference choice
    //     return !onPositiveHalfPlane || (neighborUnitComponent.BasicMovement.Resolved && onPositiveHalfPlane && unitComponent.BasicMovement.SidePreference != 0);
    // }

    // private bool NeighborInCombat(GameObject unit, GameObject neighbor)
    // {
    //     var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;
    //     var neighborUnitComponent = UnitStore.GetArchetype(neighbor).UnitComponent;
    //     return neighborUnitComponent.Attacking || (neighborUnitComponent.InAlertRange && neighborUnitComponent.Target == unitComponent.Target);
    // }

    // private bool ResolveFriendNonCombatConstraints(GameObject unit, GameObject neighbor, float avoidancePriority)
    // {
    //     var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;
    //     var neighborUnitComponent = UnitStore.GetArchetype(neighbor).UnitComponent;
    //     if (NeighborInCombat(unit, neighbor))
    //     {
    //         return true;
    //     }

    //     // Test if same group or going towards the same direction
    //     var sameGroup = false;
    //     if (InputManager.MoveGroupMap.ContainsKey(neighbor) && InputManager.MoveGroupMap.ContainsKey(unit))
    //     {
    //         sameGroup = InputManager.MoveGroupMap[neighbor] == InputManager.MoveGroupMap[unit];
    //     }

    //     var sameDirection = false;
    //     var unitDesiredDir = unitComponent.BasicMovement.TargetPosition - unit.transform.position;

    //     if (neighborUnitComponent.Kinematic.Velocity.sqrMagnitude > Mathf.Epsilon)
    //     {
    //         // Only check same direction if neighbor's velocity is greater than zero
    //         // otherwise, if a unit is stationary and got pushed, this might cause
    //         // problems with avoiding units getting stuck on this neighbor
    //         var neighborDesiredDir = neighborUnitComponent.BasicMovement.TargetPosition - neighbor.transform.position;

    //         if (neighborDesiredDir.sqrMagnitude > Mathf.Epsilon)
    //         {
    //             if (Vector3.Angle(unitDesiredDir, neighborDesiredDir) < 30)
    //             {
    //                 sameDirection = true;
    //             }
    //         }
    //     }

    //     return !sameDirection && !sameGroup;
    // }

    // private bool ResolveFriendConstraints(GameObject unit, GameObject neighbor, float avoidancePriority)
    // {

    //     var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;
    //     var neighborUnitComponent = UnitStore.GetArchetype(neighbor).UnitComponent;

    //     if (unitComponent.Owner != neighborUnitComponent.Owner)
    //     {
    //         return true;
    //     }

    //     if (neighborUnitComponent.Attacking)
    //     {
    //         return true;
    //     }

    //     var avoidanceType = GetAvoidanceType(neighbor);

    //     var validResolvedState = neighborUnitComponent.BasicMovement.Resolved && neighborUnitComponent.BasicMovement.HoldingPosition;
    //     var validResolveState = !neighborUnitComponent.BasicMovement.Resolved || validResolvedState;

    //     if ((int)avoidanceType >= avoidancePriority
    //             && validResolveState
    //             && (ResolveFriendNonCombatConstraints(unit, neighbor, avoidancePriority) || NeighborInCombat(unit, neighbor)))
    //     {
    //         return true;
    //     }
    //     else
    //     {
    //         return false;
    //     }
    // }

    // private bool ResolveEnemyConstraints(GameObject unit, GameObject neighbor)
    // {
    //     var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;
    //     var neighborUnitComponent = UnitStore.GetArchetype(neighbor).UnitComponent;

    //     if (unitComponent != neighborUnitComponent)
    //     {
    //         if (unitComponent.Target == neighbor)
    //         {
    //             return false;
    //         }
    //         else
    //         {
    //             return true;
    //         }
    //     }

    //     return true;
    // }

    // private List<GameObject> CombatClearPath(GameObject unit, float range)
    // {
    //     var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;

    //     var overlaps = Physics.OverlapSphere(unit.transform.position, range, Util.Layers.PlayerAndAIUnitMask);

    //     var desiredDir = unitComponent.BasicMovement.TargetPosition - unitComponent.transform.position;
    //     var rotation = Quaternion.Euler(0, Vector3.SignedAngle(Vector3.forward, desiredDir, Vector3.up), 0);
    //     var forward = rotation * Vector3.forward;

    //     var leftPoint = unit.transform.position + (unitComponent.Radius - 0.1f) * (rotation * Vector3.left);
    //     var leftPlane = new MathUtil.Plane(Vector3.Cross(Vector3.up, forward), leftPoint);

    //     var rightPoint = unit.transform.position + (unitComponent.Radius - 0.1f) * (rotation * Vector3.right);
    //     var rightPlane = new MathUtil.Plane(Vector3.Cross(forward, Vector3.up), rightPoint);

    //     var backPlane = new MathUtil.Plane(forward, unit.transform.position);

    //     var units = new List<GameObject>();
    //     foreach (var overlap in overlaps)
    //     {
    //         if (overlap.gameObject != unit)
    //         {

    //             var overlapUnitComponent = UnitStore.GetArchetype(overlap.gameObject).UnitComponent;

    //             // Check if in bounds of clear path
    //             if (
    //                 MathUtil.OnPositiveHalfPlane(leftPlane, overlap.gameObject.transform.position, overlapUnitComponent.Radius)
    //                 && MathUtil.OnPositiveHalfPlane(rightPlane, overlap.gameObject.transform.position, overlapUnitComponent.Radius)
    //                 && MathUtil.OnPositiveHalfPlane(backPlane, overlap.gameObject.transform.position, overlapUnitComponent.Radius))
    //             {
    //                 // Check if valid criteria
    //                 var friendConstraint = overlapUnitComponent.Owner == unitComponent.Owner && NeighborInCombat(unit, overlap.gameObject);
    //                 var enemyConstraint = overlapUnitComponent.Owner != unitComponent.Owner && overlap.gameObject != unitComponent.Target;
    //                 if (friendConstraint || enemyConstraint)
    //                 {
    //                     units.Add(overlap.gameObject);
    //                 }
    //             }
    //         }
    //     }

    //     return units;
    // }

    // private List<GameObject> ClearPath(GameObject unit)
    // {
    //     var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;
    //     var range = unitComponent.Radius + unitComponent.Kinematic.SpeedCap * unitComponent.BasicMovement.TimeHorizon;
    //     var overlaps = Physics.OverlapSphere(unit.transform.position, range, Util.Layers.PlayerAndAIUnitMask);

    //     var desiredDir = unitComponent.BasicMovement.TargetPosition - unitComponent.transform.position;
    //     var rotation = Quaternion.Euler(0, Vector3.SignedAngle(Vector3.forward, desiredDir, Vector3.up), 0);
    //     var forward = rotation * Vector3.forward;

    //     var leftPoint = unit.transform.position + unitComponent.Radius * (rotation * Vector3.left);
    //     var leftPlane = new MathUtil.Plane(Vector3.Cross(Vector3.up, forward), leftPoint);

    //     var rightPoint = unit.transform.position + unitComponent.Radius * (rotation * Vector3.right);
    //     var rightPlane = new MathUtil.Plane(Vector3.Cross(forward, Vector3.up), rightPoint);

    //     var backPlane = new MathUtil.Plane(forward, unit.transform.position);

    //     var units = new List<GameObject>();
    //     foreach (var overlap in overlaps)
    //     {
    //         if (overlap.gameObject != unit)
    //         {
    //             var overlapUnitComponent = UnitStore.GetArchetype(overlap.gameObject).UnitComponent;
    //             if (
    //                 MathUtil.OnPositiveHalfPlane(leftPlane, overlap.gameObject.transform.position, overlapUnitComponent.Radius)
    //                 && MathUtil.OnPositiveHalfPlane(rightPlane, overlap.gameObject.transform.position, overlapUnitComponent.Radius)
    //                 && MathUtil.OnPositiveHalfPlane(backPlane, overlap.gameObject.transform.position, overlapUnitComponent.Radius))
    //             {
    //                 var avoidanceType = GetAvoidanceType(overlap.gameObject);

    //                 units.Add(overlap.gameObject);
    //                 if (avoidanceType == AvoidanceType.Stationary)
    //                 {
    //                     units.Add(overlap.gameObject);
    //                 }
    //                 else
    //                 {
    //                     var sqrDst = (overlap.gameObject.transform.position - unit.transform.position).sqrMagnitude;
    //                     var radius = MovingNeighborRadius + unitComponent.Radius + overlapUnitComponent.Radius;
    //                     if (sqrDst < radius * radius)
    //                     {
    //                         units.Add(overlap.gameObject);
    //                     }
    //                 }
    //             }
    //         }
    //     }

    //     return units;
    // }

    // private bool IsHeadOn(GameObject unit, GameObject neighbor)
    // {
    //     var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;
    //     var neighborUnitComponent = UnitStore.GetArchetype(neighbor).UnitComponent;

    //     var unitPreferredDir = unitComponent.BasicMovement.TargetPosition - unit.transform.position;
    //     var neighborPreferredDir = neighborUnitComponent.BasicMovement.TargetPosition - neighbor.transform.position;

    //     var angle = Vector3.Angle(unitPreferredDir, -neighborPreferredDir);
    //     if (angle < 90)
    //     {
    //         return true;
    //     }

    //     return false;
    // }

    // private void ProcessUnresolvedUnits(HashSet<GameObject> units)
    // {
    //     // Calculate preferred velocities
    //     foreach (var unit in units)
    //     {
    //         var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;
    //         var dir = unitComponent.BasicMovement.TargetPosition - unit.transform.position;
    //         unitComponent.Kinematic.PreferredVelocity = dir.normalized * unitComponent.Kinematic.SpeedCap;

    //         if (unitComponent.Attacking)
    //         {
    //             unitComponent.Kinematic.PreferredVelocity = Vector3.zero;
    //         }
    //     }

    //     foreach (var unit in units)
    //     {
    //         // check for free path
    //         var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;
    //         if (unitComponent.Attacking)
    //         {
    //             continue;
    //         }

    //         var preferredDir = unitComponent.Kinematic.PreferredVelocity.normalized;

    //         var neighbors = ClearPath(unit);

    //         if (neighbors.Count > 0)
    //         {
    //             // choose nearest valid neighbor
    //             // filter neighbors where target 
    //             GameObject nearNeighbor = null;
    //             var nearSqrDst = Mathf.Infinity;
    //             var avoidancePriority = -1;

    //             foreach (var neighbor in neighbors)
    //             {
    //                 var dir = unit.transform.position - neighbor.transform.position;
    //                 var sqrDst = dir.sqrMagnitude;

    //                 // Choose the neighbor with the following criteria:
    //                 // - Closest
    //                 // - Neighbor has to be "behind" the target position (relative to this unit).  The 
    //                 //   exception here is if the unit's already in the middle of avoiding, then we can consider
    //                 //   this option
    //                 // - Unit isn't in the same group as a neighbor or other unit going in the same target sameDirection
    //                 //   This enables a "flocking" behavior when avoiding 
    //                 // - Neighbor has to be moving, or holding position
    //                 if (sqrDst < nearSqrDst
    //                     && ResolveHalfPlaneConstraints(unit, neighbor)
    //                     && ResolveFriendConstraints(unit, neighbor, avoidancePriority)
    //                     && ResolveEnemyConstraints(unit, neighbor))
    //                 {
    //                     nearSqrDst = sqrDst;
    //                     nearNeighbor = neighbor;

    //                     var avoidanceType = GetAvoidanceType(neighbor);
    //                     avoidancePriority = (int)avoidanceType;
    //                 }
    //             }

    //             if (nearNeighbor != null)
    //             {
    //                 var avoidanceType = GetAvoidanceType(nearNeighbor);
    //                 var nearNeighborUnitComponent = UnitStore.GetArchetype(nearNeighbor).UnitComponent;

    //                 if (avoidanceType == AvoidanceType.Moving)
    //                 {
    //                     if (nearNeighborUnitComponent.BasicMovement.SidePreference != 0
    //                             && unitComponent.BasicMovement.SidePreference != nearNeighborUnitComponent.BasicMovement.SidePreference)
    //                     {
    //                         // This is to make sure two moving units eventually resolve their incoming collisoin.  If two units have
    //                         // opposite signs for their side preferences, then they'll run into each other forever
    //                         unitComponent.BasicMovement.SidePreference = nearNeighborUnitComponent.BasicMovement.SidePreference;
    //                     }
    //                     else if (nearNeighborUnitComponent.BasicMovement.SidePreference == 0 && unitComponent.BasicMovement.SidePreference == 0)
    //                     {
    //                         unitComponent.BasicMovement.SidePreference = ChooseSignVelocity(unit, nearNeighbor);
    //                     }

    //                     var relativeDir = (unit.transform.position - nearNeighbor.transform.position).normalized;
    //                     relativeDir = Quaternion.Euler(
    //                         0,
    //                         unitComponent.BasicMovement.SidePreference * 45,
    //                         0) * relativeDir;

    //                     var target = nearNeighbor.transform.position + relativeDir * (nearNeighborUnitComponent.Radius + unitComponent.Radius);

    //                     var desiredDir = (target - unit.transform.position).normalized;
    //                     unitComponent.Kinematic.PreferredVelocity = desiredDir * unitComponent.Kinematic.SpeedCap;
    //                 }
    //                 else
    //                 {
    //                     if (unitComponent.BasicMovement.SidePreference == 0)
    //                     {
    //                         unitComponent.BasicMovement.SidePreference = ChooseSign(unit, nearNeighbor);
    //                     }

    //                     var relativeDir = (unit.transform.position - nearNeighbor.transform.position).normalized;
    //                     relativeDir = Quaternion.Euler(
    //                         0,
    //                         unitComponent.BasicMovement.SidePreference * rotateAmount,
    //                         0) * relativeDir;

    //                     var target = nearNeighbor.transform.position + relativeDir * (nearNeighborUnitComponent.Radius + 2 * unitComponent.Radius);
    //                     var desiredDir = (target - unit.transform.position).normalized;
    //                     unitComponent.Kinematic.PreferredVelocity = desiredDir * unitComponent.Kinematic.SpeedCap;

    //                     /*
    //                         Post check to make sure the side preference doesn't violate any constraints
    //                         For instance - if a neighbor outside the viable neighbors is blocking the side
    //                         that this unit's trying to go, this unit should try swapping sides
    //                     */
    //                     if (unitComponent.BasicMovement.SidePreference != 0)
    //                     {
    //                         var rotation = Quaternion.Euler(
    //                             0,
    //                             Vector3.SignedAngle(Vector3.forward, unitComponent.Kinematic.PreferredVelocity, Vector3.up),
    //                             0);

    //                         var forward = rotation * Vector3.forward;

    //                         var leftPoint = unit.transform.position + unitComponent.Radius * (rotation * Vector3.left);
    //                         var leftPlane = new MathUtil.Plane(Vector3.Cross(Vector3.up, forward), leftPoint);

    //                         var rightPoint = unit.transform.position + unitComponent.Radius * (rotation * Vector3.right);
    //                         var rightPlane = new MathUtil.Plane(Vector3.Cross(forward, Vector3.up), rightPoint);

    //                         var backPlane = new MathUtil.Plane(forward, unit.transform.position);

    //                         var overlaps = Physics.OverlapSphere(
    //                             unitComponent.transform.position,
    //                             unitComponent.Radius + unitComponent.Kinematic.SpeedCap * 0.05f,
    //                             Util.Layers.PlayerAndAIUnitMask);

    //                         foreach (var overlap in overlaps)
    //                         {
    //                             if (overlap.gameObject == unit)
    //                             {
    //                                 continue;
    //                             }

    //                             if (unitComponent.BasicMovement.DBG)
    //                             {
    //                                 Debug.DrawLine(unit.transform.position, overlap.transform.position, Color.black);
    //                             }

    //                             var neighbor = overlap.gameObject;

    //                             // Make sure neighbor is blocking the direction the direction 
    //                             // that this unit's headed towards
    //                             var neighborUnitComponent = neighbor.GetComponent<UnitController>();
    //                             if (
    //                                 MathUtil.OnPositiveHalfPlane(leftPlane, neighbor.transform.position, neighborUnitComponent.Radius)
    //                                 && MathUtil.OnPositiveHalfPlane(rightPlane, neighbor.transform.position, neighborUnitComponent.Radius)
    //                                 && MathUtil.OnPositiveHalfPlane(backPlane, neighbor.transform.position, neighborUnitComponent.Radius))
    //                             {
    //                                 if (!neighbors.Contains(neighbor) && neighborUnitComponent.BasicMovement.HoldingPosition)
    //                                 {
    //                                     unitComponent.BasicMovement.SidePreference = -unitComponent.BasicMovement.SidePreference;
    //                                 }
    //                             }
    //                         }
    //                     }


    //                     if (unitComponent.BasicMovement.DBG)
    //                     {
    //                         Debug.DrawLine(nearNeighbor.transform.position, target, Color.cyan);
    //                         Debug.DrawRay(unit.transform.position, desiredDir * unitComponent.Kinematic.SpeedCap, Color.yellow);
    //                         Debug.DrawRay(unit.transform.position, preferredDir * 5, Color.blue);

    //                     }
    //                 }
    //             }
    //         }
    //         else
    //         {
    //             unitComponent.BasicMovement.SidePreference = 0;
    //         }
    //     }

    //     foreach (var unit in units)
    //     {
    //         var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;
    //         unitComponent.Kinematic.Velocity = unitComponent.Kinematic.PreferredVelocity;
    //         // compute new position
    //         var delta = unitComponent.Kinematic.Velocity * Time.fixedDeltaTime;
    //         var dir = unitComponent.BasicMovement.TargetPosition - unit.transform.position;
    //         if (dir.magnitude < delta.magnitude)
    //         {
    //             delta = unitComponent.Kinematic.Velocity.normalized * dir.magnitude;
    //         }

    //         // Debug.Log(unitComponent.Kinematic.Velocity.magnitude);

    //         unitComponent.Kinematic.Position += delta;
    //         unit.transform.position = unitComponent.Kinematic.Position;

    //         var moveGroup = InputManager.MoveGroupMap.ContainsKey(unit) ? InputManager.MoveGroupMap[unit] : null;
    //         if (TriggerStop(unit, moveGroup))
    //         {
    //             ForceStop(unit);
    //             unitComponent.BasicMovement.Resolved = true;
    //         }
    //     }
    // } 

    // private void FixedUpdate()
    // {
    //     // Process unresolved units
    //     CategorizeUnits(
    //         out HashSet<GameObject> unresolvedUnits,
    //         out HashSet<GameObject> resolvedUnits);
    //     CheckCombatState();

    //     ProcessUnresolvedUnits(unresolvedUnits);
    //     ProcessResolvedUnits(resolvedUnits);

    //     // Post processing
    //     var neighborDictionary = new Dictionary<GameObject, List<GameObject>>();
    //     for (int i = 0; i < UnitStore.Count; i++)
    //     {
    //         var unitComponent = UnitStore.Archetypes[i].UnitComponent;
    //         var unit = unitComponent.gameObject;

    //         neighborDictionary.Add(unit, GetNeighbors(unit));

    //         if (unitComponent.Kinematic.Velocity.sqrMagnitude > 0)
    //         {
    //             unitComponent.BasicMovement.LastMoveTime = Time.fixedTime;
    //         }
    //     }

    //     for (int i = 0; i < Iterations; i++)
    //     {
    //         for (int unitIdx = 0; unitIdx < UnitStore.Count; unitIdx++)
    //         {
    //             var unitComponent = UnitStore.Archetypes[unitIdx].UnitComponent;
    //             foreach (var neighbor in neighborDictionary[unitComponent.gameObject])
    //             {
    //                 ResolveCollision(unitComponent.gameObject, neighbor);
    //             }
    //         }
    //     }

    //     for (int i = 0; i < UnitStore.Count; i++)
    //     {
    //         var unitComponent = UnitStore.Archetypes[i].UnitComponent;
    //         if (unitComponent.Kinematic.Velocity.sqrMagnitude > Mathf.Epsilon)
    //         {
    //             var orientation = MathUtil.NormalizeOrientation(
    //                 Vector3.SignedAngle(
    //                     Vector3.forward,
    //                     unitComponent.Kinematic.Velocity,
    //                     Vector3.up));

    //             unitComponent.Kinematic.Orientation = orientation;
    //         }
    //     }
    // }

    // private void DecidePassivePhysicsAction(GameObject unit, UnitController unitComponent)
    // {
    //     var neighbors = GetNeighbors(unit);

    //     // Get nearest colliding neighbor, where the neighbor is moving towards
    //     // this unit
    //     var calculatedVelocity = Vector3.zero;
    //     var count = 0;

    //     foreach (var neighbor in neighbors)
    //     {
    //         var neighborUnitComponent = UnitStore.GetArchetype(neighbor).UnitComponent;

    //         if (neighborUnitComponent.Owner != unitComponent.Owner)
    //         {
    //             continue;
    //         }

    //         if (InPushRadius(unit, neighbor))
    //         {
    //             // Calculate how much an "inactive" unit should be pushed
    //             var relativeDir = unit.transform.position - neighborUnitComponent.transform.position;
    //             var appliedVelocity = relativeDir.normalized * neighborUnitComponent.Kinematic.Velocity.magnitude;
    //             var velocity = neighborUnitComponent.Kinematic.Velocity;

    //             if (!neighborUnitComponent.BasicMovement.Resolved)
    //             {
    //                 var angle = Vector3.SignedAngle(velocity, relativeDir, Vector3.up);
    //                 var angleSign = Mathf.Sign(angle);
    //                 var normalVelocity = Quaternion.Euler(0, angleSign * 90, 0) * velocity;

    //                 // Add a lower bound so that turn rate is faster
    //                 var t = Mathf.Max(Mathf.InverseLerp(0, 90, Mathf.Abs(angle)), 0.1f);
    //                 var perpIntensity = Mathf.Lerp(0, 1, t);
    //                 var forwardIntensity = 1 - perpIntensity;
    //                 appliedVelocity = perpIntensity * normalVelocity + forwardIntensity * velocity;
    //             }

    //             calculatedVelocity += appliedVelocity;
    //             count++;
    //         }
    //     }

    //     if (count > 0)
    //     {
    //         calculatedVelocity /= count;
    //     }

    //     // match the velocity
    //     unitComponent.Kinematic.Velocity = calculatedVelocity;
    //     unitComponent.Kinematic.Position += unitComponent.Kinematic.Velocity * Time.fixedDeltaTime;
    //     unit.transform.position = unitComponent.Kinematic.Position;
    // }

    private bool InPushRadius(GameObject unit, GameObject neighbor)
    {
        var neighborUnitComponent = neighbor.GetComponent<UnitController>();
        var unitComponent = unit.GetComponent<UnitController>();

        var relativeDir = unit.transform.position - neighbor.transform.position;
        var dist = relativeDir.magnitude;
        dist -= unitComponent.Radius;
        dist -= neighborUnitComponent.Radius;
        // Add a small offset to prevent "stuttering" due to the impulse force
        // (from resolving collisions) kicking neighbors out of the push influence region
        dist -= 0.025f;

        return dist < Mathf.Epsilon && Vector3.Dot(relativeDir, neighborUnitComponent.Kinematic.Velocity) > 0;
    }

    // private void ResolveCollision(GameObject unit, GameObject neighbor)
    // {
    //     var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;
    //     var neighborUnitComponent = UnitStore.GetArchetype(neighbor).UnitComponent;

    //     var relativeDir = unit.transform.position - neighbor.transform.position;
    //     relativeDir.y = 0;

    //     var sqrDst = relativeDir.sqrMagnitude;
    //     if (sqrDst < Mathf.Epsilon)
    //     {
    //         // Add offset for the off case that the unit is right on top of another
    //         // unit - thought maybe it's better to just teleport one of the
    //         // units out of the body
    //         relativeDir = Vector3.right * 0.0001f;
    //         sqrDst = relativeDir.sqrMagnitude;
    //     }

    //     var thresholdRadius = unitComponent.Radius + neighborUnitComponent.Radius;

    //     // TODO: handle sqrDst <= Mathf.Epsilon case
    //     if (sqrDst < thresholdRadius * thresholdRadius)
    //     {
    //         if (AllyCollisionConstraints(unit, neighbor)
    //             && EnemyCollisionConstraints(unit, neighbor))
    //         {
    //             var dst = Mathf.Sqrt(sqrDst);
    //             var delta = ResponseCoefficient * 0.5f * (thresholdRadius - dst);

    //             // decide if unit is moving unit or stationary unit
    //             var pushVec = relativeDir.normalized * delta;
    //             unitComponent.Kinematic.Position += pushVec;
    //             unitComponent.transform.position = unitComponent.Kinematic.Position;

    //             if (unitComponent.Owner == neighborUnitComponent.Owner)
    //             {
    //                 unitComponent.BasicMovement.LastPushedByFriendlyNeighborTime = Time.fixedTime;
    //             }
    //         }
    //     }
    // }

    // private bool EnemyCollisionConstraints(GameObject unit, GameObject neighbor)
    // {
    //     var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;
    //     var neighborUnitComponent = UnitStore.GetArchetype(neighbor).UnitComponent;

    //     if (unitComponent.Owner == neighborUnitComponent.Owner)
    //     {
    //         return true;
    //     }

    //     return (!unitComponent.BasicMovement.Resolved && neighborUnitComponent.BasicMovement.Resolved)
    //         || (unitComponent.BasicMovement.Resolved
    //             && unitComponent.BasicMovement.LastPushedByFriendlyNeighborTime >= neighborUnitComponent.BasicMovement.LastPushedByFriendlyNeighborTime
    //             && neighborUnitComponent.BasicMovement.Resolved);
    // }

    // private bool AllyCollisionConstraints(GameObject unit, GameObject neighbor)
    // {
    //     var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;
    //     var neighborUnitComponent = UnitStore.GetArchetype(neighbor).UnitComponent;


    //     if (unitComponent.Owner != neighborUnitComponent.Owner)
    //     {
    //         return true;
    //     }

    //     if (neighborUnitComponent.Attacking)
    //     {
    //         return true;
    //     }
    //     else
    //     {
    //         var isUnitMoving = unitComponent.Kinematic.Velocity.sqrMagnitude > 0;
    //         var isNeighborMoving = neighborUnitComponent.Kinematic.Velocity.sqrMagnitude > 0;
    //         var bothMoving = isUnitMoving && isNeighborMoving;

    //         // Use last move time to prevent pushing units that have been stationary for a while
    //         // This is an aesthetic choice
    //         // Try playing around with TOI or some other collision resolver on top of the
    //         // iterative relaxing I'm doing right now
    //         var bothStationary = !isUnitMoving
    //             && !isNeighborMoving
    //             && unitComponent.BasicMovement.LastMoveTime >= neighborUnitComponent.BasicMovement.LastMoveTime;

    //         var neighborStationary = isUnitMoving && !isNeighborMoving;

    //         return bothMoving || bothStationary || neighborStationary;
    //     }
    // }

    // private bool TriggerStop(GameObject unit, MoveGroup moveGroup)
    // {
    //     var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;

    //     if (ArrivedAtTarget(unit))
    //     {
    //         return true;
    //     }

    //     // I don't think this check does a whole lot
    //     // var currentDir = unitComponent.BasicMovement.TargetPosition - unit.transform.position;
    //     // var angle = Vector3.Dot(
    //     //     currentDir.normalized,
    //     //     unitComponent.BasicMovement.RelativeDeltaStart.normalized);

    //     // if (angle < 0)
    //     // {
    //     //     return true;
    //     // }

    //     var neighbors = GetNeighbors(unit);

    //     if (moveGroup != null)
    //     {
    //         foreach (var neighbor in neighbors)
    //         {
    //             if (moveGroup.Units.Contains(neighbor))
    //             {
    //                 // Handle the case where units are in the same move group
    //                 // Important subcase 
    //                 // - If units of the same group are colliding, near the destination, and going in opposite direction
    //                 // - If neighboring unit in same group has reached the destination
    //                 var neighborUnitComponent = UnitStore.GetArchetype(neighbor).UnitComponent;

    //                 if (IsColliding(unit, neighbor))
    //                 {
    //                     var unitVelocity = unitComponent.Kinematic.Velocity.normalized;
    //                     var neighborUnitVelocity = neighborUnitComponent.Kinematic.Velocity.normalized;
    //                     if (NearTarget(unit) && Vector3.Dot(unitVelocity, neighborUnitVelocity) < 0)
    //                     {
    //                         return true;
    //                     }

    //                     if (neighborUnitComponent.BasicMovement.Resolved)
    //                     {
    //                         return true;
    //                     }
    //                 }
    //             }
    //         }
    //     }


    //     return false;
    // }

    // private List<GameObject> GetBlockers(GameObject unit, List<Collider> overlaps)
    // {
    //     var blockers = new List<GameObject>();
    //     foreach (var overlap in overlaps)
    //     {
    //         if (overlap.gameObject != unit)
    //         {
    //             var overlapUnitComponent = UnitStore.GetArchetype(overlap.gameObject).UnitComponent;
    //             // Ignore if target position is right on this unit
    //             if (overlapUnitComponent.BasicMovement.HoldingPosition)
    //             {
    //                 blockers.Add(overlapUnitComponent.gameObject);
    //             }
    //         }
    //     }

    //     return blockers;
    // }

    // public void ForceStop(GameObject unit)
    // {
    //     var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;
    //     unitComponent.BasicMovement.TargetPosition = unit.transform.position;
    //     unitComponent.Kinematic.Velocity = Vector3.zero;
    // }

    private bool NearTarget(GameObject unit)
    {
        var unitComponent = unit.GetComponent<UnitController>();
        var position = unitComponent.Kinematic.Position;
        var targetPosition = unitComponent.BasicMovement.TargetPosition;

        var stopRadius = unitComponent.Radius * 1.5f;
        return (position - targetPosition).sqrMagnitude <= stopRadius * stopRadius;
    }

    private bool ArrivedAtTarget(GameObject unit)
    {
        var unitComponent = unit.GetComponent<UnitController>();
        var position = unitComponent.Kinematic.Position;
        var targetPosition = unitComponent.BasicMovement.TargetPosition;

        return (position - targetPosition).sqrMagnitude <= Mathf.Epsilon;
    }

    private bool IsColliding(GameObject unit, GameObject neighbor)
    {
        var neighborUnitComponent = neighbor.GetComponent<UnitController>();
        var unitComponent = unit.GetComponent<UnitController>();

        var dist = (neighbor.transform.position - unit.transform.position).magnitude;
        dist -= unitComponent.Radius;
        dist -= neighborUnitComponent.Radius;

        return dist < Mathf.Epsilon;
    }

    private GameObject GetCollidingNeighbor(GameObject unit, List<GameObject> neighbors)
    {
        GameObject collidingNeighbor = null;
        var collidingDst = Mathf.Infinity;

        foreach (var neighbor in neighbors)
        {
            var neighborUnitComponent = neighbor.GetComponent<UnitController>();
            var unitComponent = unit.GetComponent<UnitController>();

            var dist = (neighbor.transform.position - unit.transform.position).magnitude;
            dist -= unitComponent.Radius;
            dist -= neighborUnitComponent.Radius;

            if (dist <= Mathf.Epsilon && dist < collidingDst)
            {
                collidingNeighbor = neighbor;
                collidingDst = dist;
            }
        }

        return collidingNeighbor;
    }

    // private List<GameObject> GetNeighbors(GameObject unit)
    // {
    //     var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;
    //     var unitRadius = unitComponent.Radius;

    //     var neighbors = new List<GameObject>();

    //     for (int i = 0; i < UnitStore.Count; i++)
    //     {
    //         var neighborUnitComponent = UnitStore.Archetypes[i].UnitComponent;
    //         var neighbor = neighborUnitComponent.gameObject;
    //         if (neighbor == unit)
    //         {
    //             continue;
    //         }

    //         var neighborRadius = neighborUnitComponent.Radius;

    //         var dst = (unit.transform.position - neighbor.transform.position).magnitude;
    //         dst -= neighborRadius + unitRadius;

    //         if (dst < NeighborScanRadius)
    //         {
    //             neighbors.Add(neighbor);
    //         }
    //     }

    //     return neighbors;
    // }

    // private void SetupEnvironment()
    // {
    //     // Setup ECS
    //     var objs = GameObject.FindGameObjectsWithTag(Util.Tags.PlayerUnit);
    //     foreach (var obj in objs)
    //     {
    //         var unitComponent = obj.GetComponent<UnitController>();
    //         unitComponent.Kinematic.Position = obj.transform.position;
    //         unitComponent.BasicMovement.TargetPosition = obj.transform.position;

    //         UnitStore.Add(obj, new UnitArchetype()
    //         {
    //             UnitComponent = unitComponent
    //         });

    //         if (obj.GetComponent<PlayerController>())
    //         {
    //             PlayerUnitStore.Add(obj, new UnitArchetype()
    //             {
    //                 UnitComponent = unitComponent
    //             });
    //         }
    //     }
    // }

    // private void OnDrawGizmos()
    // {
    //     if (UnitStore != null)
    //     {
    //         for (int i = 0; i < UnitStore.Count; i++)
    //         {
    //             var unitArchetype = UnitStore.Archetypes[i];
    //             var unitComponent = unitArchetype.UnitComponent;
    //             var forward = Quaternion.Euler(0, unitComponent.Kinematic.Orientation, 0) * Vector3.forward;
    //             Debug.DrawRay(
    //                 unitComponent.gameObject.transform.position + 0.5f * forward * unitComponent.Radius,
    //                 forward * unitComponent.Radius * 0.5f,
    //                 Color.red);
    //         }
    //     }
    // }

    // private AvoidanceType GetAvoidanceType(GameObject unit)
    // {
    //     var unitComponent = UnitStore.GetArchetype(unit).UnitComponent;
    //     if (unitComponent.Kinematic.Velocity.sqrMagnitude > 0)
    //     {
    //         return AvoidanceType.Moving;
    //     }
    //     else
    //     {
    //         return AvoidanceType.Stationary;
    //     }
    // }
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