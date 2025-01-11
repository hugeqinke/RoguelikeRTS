using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public struct PhysicsJob : IJob
{
    public NativeArray<MovementComponent> Units;
    public NativeMultiHashMap<int, int> SpatialHashMap;
    public SpatialHashMeta SpatialHashMeta;

    public float MovingNeighborRadius;
    public int Substeps;
    public float DeltaTime;
    public float CurrentTime;
    public float RotateAmount;
    public float CombatClearRange;

    public bool ProhibitNeighborChoice(
            MovementComponent unit,
            MovementComponent neighbor,
            int neighborIdx,
            int avoidancePriority,
            float sqrDst,
            AvoidanceType avoidanceType)
    {
        var radius = MovingNeighborRadius + unit.Radius + neighbor.Radius;

        if (avoidanceType != AvoidanceType.Stationary)
        {
            if (sqrDst > radius * radius)
            {
                return true;
            }
            else
            {
                var neighborVelocityNorm = math.normalizesafe(neighbor.Velocity);
                var unitVelocityNorm = math.normalizesafe(unit.Velocity);
                if (math.dot(neighborVelocityNorm, unitVelocityNorm) > -0.1)
                {
                    return true;
                }
            }
        }

        if (!ValidTargetPositionConstraint(unit, neighbor))
        {
            return true;
        }

        if (unit.Owner == neighbor.Owner && !ResolveFriendConstraints(unit, neighbor, avoidancePriority))
        {
            return true;
        }

        if (unit.Owner != neighbor.Owner && !ResolveEnemyConstraints(unit, neighborIdx))
        {
            return true;
        }

        return false;
    }

    private bool ClearPath(
            MovementComponent unit,
            int unitIdx,
            NativeMultiHashMap<int, int>.Enumerator neighborIndexes)
    {
        MathUtil.OBB obb;
        if (unit.Target != -1)
        {
            var targetUnit = Units[unit.Target];
            var center = (targetUnit.Position + unit.Position) * 0.5f;
            var rel = targetUnit.Position - unit.Position;

            var angle = signedangle(math.right(), rel, math.up());
            obb = new MathUtil.OBB(center, math.length(rel), 2 * (unit.Radius - unit.Radius * 0.5f), angle);

            /* if (unit.DBG)
            {
                var a = obb.Position - obb.Extents.x * obb.XAxis - obb.Extents.z * obb.ZAxis + new float3(0, 1, 0);
                var b = obb.Position - obb.Extents.x * obb.XAxis + obb.Extents.z * obb.ZAxis + new float3(0, 1, 0);
                var c = obb.Position + obb.Extents.x * obb.XAxis + obb.Extents.z * obb.ZAxis + new float3(0, 1, 0);
                var d = obb.Position + obb.Extents.x * obb.XAxis - obb.Extents.z * obb.ZAxis + new float3(0, 1, 0);

                Debug.DrawLine(a, b, Color.magenta);
                Debug.DrawLine(b, c, Color.magenta);
                Debug.DrawLine(c, d, Color.magenta);
                Debug.DrawLine(d, a, Color.magenta);
            } */

            foreach (var neighborIdx in neighborIndexes)
            {
                // Check if neighbor has a clear path
                if (neighborIdx != unit.Target && neighborIdx != unitIdx)
                {
                    var neighbor = Units[neighborIdx];
                    var circle = new MathUtil.Circle(neighbor.Position, neighbor.Radius);
                    if (MathUtil.OverlapCircleOBB(circle, obb))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private int ChooseNeighbor(MovementComponent unit, int unitIdx, NativeMultiHashMap<int, int>.Enumerator neighborIndexes)
    {
        int nearNeighbor = -1;
        var nearSqrDst = math.INFINITY;
        var avoidancePriority = -1;

        foreach (var neighborIdx in neighborIndexes)
        {
            var neighbor = Units[neighborIdx];

            var avoidanceType = GetAvoidanceType(neighbor);
            var sqrDst = math.lengthsq(neighbor.Position - unit.Position);

            // Decide any avoid neighbors
            if (!ProhibitNeighborChoice(
                    unit,
                    neighbor,
                    neighborIdx,
                    avoidancePriority,
                    sqrDst,
                    avoidanceType))
            {

                Debug.DrawLine(unit.Position + new float3(0, 0.5f, 0), neighbor.Position + new float3(0, 0.5f, 0), Color.magenta);
                if (sqrDst < nearSqrDst)
                {
                    nearSqrDst = sqrDst;
                    nearNeighbor = neighborIdx;
                    avoidancePriority = (int)avoidanceType;
                }
            }
        }

        if (nearNeighbor != -1)
        {
            var neighbor = Units[nearNeighbor];
            Debug.DrawLine(unit.Position + new float3(0, 1f, 0), neighbor.Position + new float3(0, 1, 0), Color.green);
        }

        return nearNeighbor;
    }

    private MovementComponent CalculatePreferredDirection(
            int unitIdx,
            MovementComponent unit,
            NativeMultiHashMap<int, int>.Enumerator neighborIndexes)
    {
        var dir = unit.TargetPosition - unit.Position;
        unit.PreferredDir = math.normalizesafe(dir);

        var neighborIdx = ChooseNeighbor(unit, unitIdx, neighborIndexes);
        if (neighborIdx != -1)
        {
            var neighbor = Units[neighborIdx];
            // Debug.DrawLine(unit.Position, neighbor.Position, Color.yellow);

            var avoidanceType = GetAvoidanceType(neighbor);
            if (avoidanceType == AvoidanceType.Moving)
            {
                if (neighbor.SidePreference != 0 && unit.SidePreference != neighbor.SidePreference)
                {
                    // This is to make sure two moving units eventually resolve their incoming collision.  If two units have
                    // opposite signs for their side preferences, then they'll run into each other forever
                    unit.SidePreference = neighbor.SidePreference;
                }
                else if (neighbor.SidePreference == 0 && unit.SidePreference == 0)
                {
                    unit.SidePreference = ChooseSignVelocity(unit, neighbor);
                }

                var relativeDir = math.normalizesafe(unit.Position - neighbor.Position);

                var angle = math.radians(unit.SidePreference * 90);
                unit.PreferredDir = math.mul(quaternion.RotateY(angle), relativeDir);

                // Debug.DrawRay(unit.Position, unit.PreferredDir * 5, Color.magenta);
            }
            else
            {
                float3 target;
                if (unit.SidePreference == 0)
                {
                    unit.SidePreference = ChooseSign(unit, neighbor, out target);
                }
                else
                {
                    var relativeDir = math.normalizesafe(unit.Position - neighbor.Position);
                    relativeDir = math.mul(quaternion.RotateY(unit.SidePreference * RotateAmount), relativeDir);
                    target = neighbor.Position + relativeDir * (neighbor.Radius + 2 * unit.Radius);
                }

                var desiredDir = math.normalizesafe(target - unit.Position);
                unit.PreferredDir = desiredDir;

                /*
                    Post check to make sure the side preference doesn't violate any constraints
                    For instance - if a neighbor outside the viable neighbors is blocking the side
                    that this unit's trying to go, this unit should try swapping sides
                */
                // if (unit.SidePreference != 0)
                // {
                //     var rotation = quaternion.Euler(0, signedangle(math.forward(), unit.PreferredVelocity, math.up()), 0);

                //     var forward = math.mul(rotation, math.forward());

                //     var leftPoint = unit.Position + unit.Radius * math.mul(rotation, math.left());
                //     var leftPlane = new MathUtil.Plane(math.cross(math.up(), forward), leftPoint);

                //     var rightPoint = unit.Position + unit.Radius * math.mul(rotation, math.right());
                //     var rightPlane = new MathUtil.Plane(math.cross(forward, math.up()), rightPoint);

                //     var backPlane = new MathUtil.Plane(forward, unit.Position);

                //     var overlaps = Physics.OverlapSphere(
                //         unit.Position,
                //         unit.Radius + unit.MaxSpeed * 0.05f,
                //         Util.Layers.PlayerAndAIUnitMask);

                //     for (int neighborIdx = 0; neighborIdx < neighborIndicies.Length; neighborIdx++)
                //     {
                //         var neighbor = neighborIndicies[neighborIdx];
                //     }

                //     foreach (var overlap in overlaps)
                //     {
                //         if (overlap.gameObject == unit)
                //         {
                //             continue;
                //         }

                //         // Make sure neighbor is blocking the direction the direction 
                //         // that this unit's headed towards
                //         if (
                //             MathUtil.OnPositiveHalfPlane(leftPlane, neighbor.Position, neighbor.Radius)
                //             && MathUtil.OnPositiveHalfPlane(rightPlane, neighbor.Position, neighbor.Radius)
                //             && MathUtil.OnPositiveHalfPlane(backPlane, neighbor.Position, neighbor.Radius))
                //         {
                //             if (!neighbors.Contains(neighbor) && neighbor.HoldingPosition)
                //             {
                //                 unit.SidePreference = -unit.SidePreference;
                //             }
                //         }
                //     }
                // }
            }
        }
        else
        {
            neighborIndexes.Reset();

            var avgDir = float3.zero;
            var count = 0;

            var sideDir = float3.zero;
            var sideCount = 0;

            var separationDir = float3.zero;
            var separationCount = 0;

            var otherSeparationDir = float3.zero;
            var otherSeparationCount = 0;


            foreach (var idx in neighborIndexes)
            {
                if (unitIdx == idx)
                {
                    continue;
                }

                var neighborUnit = Units[idx];

                if (!neighborUnit.Resolved && neighborUnit.CurrentGroup == unit.CurrentGroup)
                {
                    // This 0.01 value MUST be smaller than whatever I set as a stop threshold
                    // in CheckUnitStop, otherwise unit WILL NOT stop
                    // TODO: maybe make this apparent in the editor

                    var relativeDir = neighborUnit.Position - unit.Position;
                    var threshold = neighborUnit.Radius + unit.Radius + unit.FlockRadius;

                    if (math.dot(neighborUnit.Velocity, unit.Velocity) >= 0
                            && math.lengthsq(relativeDir) <= threshold * threshold)
                    {
                        avgDir += neighborUnit.PreferredDir;
                        count++;
                    }

                    if (neighborUnit.SidePreference != 0)
                    {
                        sideDir += neighborUnit.PreferredDir;
                        sideCount++;

                        var relative = unit.Position - neighborUnit.Position;
                        separationDir += math.normalizesafe(relative);
                        separationCount++;
                    }
                }
                else if (!neighborUnit.Resolved && neighborUnit.CurrentGroup != unit.CurrentGroup)
                {
                    var relative = unit.Position - neighborUnit.Position;
                    otherSeparationDir += math.normalizesafe(relative);
                    otherSeparationCount++;
                }
            }

            var newDir = unit.PreferredDir;

            if (sideCount > 0)
            {
                sideDir /= sideCount;
                newDir += unit.LateralWeight * sideDir;

                if (separationCount > 0)
                {
                    separationDir /= separationCount;
                    newDir += unit.SeparationWeight * separationDir;
                }

                if (otherSeparationCount > 0)
                {
                    otherSeparationDir /= otherSeparationCount;
                    newDir += unit.OtherSeparationWeight * otherSeparationDir;
                }
            }
            else
            {
                if (count > 0)
                {
                    avgDir /= count;
                    newDir += unit.FlockWeight * avgDir;
                }
            }

            // Debug.DrawRay(unit.Position, newDir * 2, Color.blue);

            unit.PreferredDir = math.normalizesafe(newDir);

            unit.SidePreference = 0;
        }

        return unit;
    }

    public bool IsMovable(MovementComponent unit)
    {
        return !unit.Attacking && !unit.HoldingPosition;
    }

    public void Execute()
    {
        var neighbors = UtilityFunctions.BroadPhase(Units, SpatialHashMap, SpatialHashMeta);

        var cellRange = UtilityFunctions.RangeToCellCount(CombatClearRange, SpatialHashMeta);
        var combatNeighbors = UtilityFunctions.BroadPhase(Units, SpatialHashMap, SpatialHashMeta, cellRange);

        // Pre-physics updates
        for (int i = 0; i < Units.Length; i++)
        {
            // Set low Velocity indicators - used to stop units that are stuck
            // in low velocities for too long
            var unit = Units[i];

            if (math.lengthsq(unit.Velocity) > math.EPSILON)
            {
                unit.LastMoveTime = CurrentTime;
            }

            // Set preferred velocites - used to calculate where units should be
            if (!unit.Resolved && unit.Target != -1 && unit.Attacking)
            {
                unit.PreferredDir = float3.zero;
                unit.SidePreference = 0;
            }
            else if (!unit.Resolved && unit.Target != -1 && !unit.Attacking)
            {
                var target = Units[unit.Target];
                var targetSqDst = math.distancesq(target.Position, unit.Position);

                if (targetSqDst < CombatClearRange * CombatClearRange
                        && ClearPath(unit, i, combatNeighbors.GetValuesForKey(i)))
                {
                    unit.PreferredDir = math.normalizesafe(target.Position - unit.Position);
                    // Debug.DrawLine(unit.Position, target.Position, Color.green);
                }
                else
                {
                    unit = CalculatePreferredDirection(i, unit, neighbors.GetValuesForKey(i));
                    var neighbor = Units[unit.Target];
                    // Debug.DrawLine(unit.Position, neighbor.Position, Color.red);
                }
            }
            else if (!unit.Resolved && unit.Target == -1)
            {
                unit = CalculatePreferredDirection(i, unit, neighbors.GetValuesForKey(i));
            }
            else
            {
                unit.SidePreference = 0;
            }

            // Check if this unit got pushed too much and should return to its
            // stop position
            if (unit.Resolved && unit.CurrentGroup == -1)
            {
                var stopDstSq = math.lengthsq(unit.Position - unit.StopPosition);
                if (stopDstSq >= 16)
                {
                    unit.MoveStartPosition = unit.Position;
                    unit.Resolved = false;
                    unit.TargetPosition = unit.StopPosition;
                }
            }

            Units[i] = unit;
        }

        // Apply physics
        var sdt = DeltaTime / Substeps;

        for (int substep = 0; substep < Substeps; substep++)
        {
            for (int i = 0; i < Units.Length; i++)
            {
                var unit = Units[i];

                if (unit.Resolved)
                {
                    if (!unit.HoldingPosition)
                    {
                        unit.Velocity = CalculatePush(i, neighbors);
                    }
                }
                else
                {
                    unit.Velocity += unit.PreferredDir * unit.Acceleration * sdt;
                    if (math.lengthsq(unit.Velocity) > unit.MaxSpeed * unit.MaxSpeed)
                    {
                        unit.Velocity = math.normalizesafe(unit.Velocity) * unit.MaxSpeed;
                    }
                }

                unit.OldPosition = unit.Position;
                unit.Position += unit.Velocity * sdt;

                Units[i] = unit;
            }

            ResolveCollisions(neighbors);

            // end position constraint
            for (int i = 0; i < Units.Length; i++)
            {
                var unit = Units[i];

                if (unit.Resolved)
                {
                    continue;
                }

                var relDir = unit.TargetPosition - unit.Position;
                var moveDir = unit.Position - unit.OldPosition;

                var relDirLenSq = math.lengthsq(relDir);
                var moveDirSq = math.lengthsq(moveDir);

                var cross = math.cross(relDir, moveDir);
                var crossDirSq = math.lengthsq(cross);

                if (relDirLenSq < moveDirSq && crossDirSq < 0.001f)
                {
                    unit.Position = unit.TargetPosition;
                    Units[i] = unit;
                }
            }

            // fix velocity
            // for (int i = 0; i < Units.Length; i++)
            // {
            //     var unit = Units[i];

            //     if (unit.Resolved)
            //     {
            //         continue;
            //     }

            //     unit.Velocity = (unit.Position - unit.OldPosition) / sdt;
            //     Units[i] = unit;
            // }
        }

        // Post
        CheckStop(neighbors);
    }

    private void ResolveCollisions(NativeMultiHashMap<int, int> neighbors)
    {
        for (int i = 0; i < Units.Length; i++)
        {
            var unit = Units[i];

            var neighborIndices = neighbors.GetValuesForKey(i);

            foreach (var neighborIdx in neighborIndices)
            {
                var body1 = Units[i];
                var body2 = Units[neighborIdx];

                var body1EffectiveMass = body1.Mass;
                var body2EffectiveMass = body2.Mass;

                if (body1.Owner == body2.Owner)
                {
                    var body1Movable = IsMovable(body1);
                    var body2Movable = IsMovable(body2);

                    if (body1Movable && !body2Movable)
                    {
                        body1EffectiveMass = 0;
                        body2EffectiveMass = 1;
                    }
                    else if (!body1Movable && body2Movable)
                    {
                        body1EffectiveMass = 1;
                        body2EffectiveMass = 0;
                    }
                    else
                    {
                        if (body1.LastMoveTime > body2.LastMoveTime)
                        {
                            body1EffectiveMass = 0;
                            body2EffectiveMass = 1;
                        }
                        else if (body2.LastMoveTime > body1.LastMoveTime)
                        {
                            body1EffectiveMass = 1;
                            body2EffectiveMass = 0;
                        }
                    }
                }
                else
                {
                    if (math.abs(math.lengthsq(body2.Velocity) - math.lengthsq(body1.Velocity)) < math.EPSILON)
                    {
                        if (body1.LastMoveTime > body2.LastMoveTime)
                        {
                            body1EffectiveMass = 0;
                            body2EffectiveMass = 1;
                        }
                        else if (body1.LastMoveTime < body2.LastMoveTime)
                        {
                            body1EffectiveMass = 1;
                            body2EffectiveMass = 0;
                        }
                    }
                    if (math.lengthsq(body1.Velocity) < math.lengthsq(body2.Velocity))
                    {
                        body1EffectiveMass = 1;
                        body2EffectiveMass = 0;
                    }
                    else
                    {
                        body1EffectiveMass = 0;
                        body2EffectiveMass = 1;
                    }
                }


                var dir = body2.Position - body1.Position;
                var separation = math.length(dir);

                var collideRadius = body1.Radius + body2.Radius;

                if (separation < collideRadius)
                {
                    var totalMass = body1EffectiveMass + body2EffectiveMass;

                    var slop = collideRadius - separation;

                    var x1 = -body2EffectiveMass / totalMass * slop * math.normalizesafe(dir);
                    var x2 = body1EffectiveMass / totalMass * slop * math.normalizesafe(dir);

                    body1.Position += x1;
                    body2.Position += x2;

                    Units[i] = body1;
                    Units[neighborIdx] = body2;
                }
            }
        }

    }

    private int ChooseSign(MovementComponent unit, MovementComponent neighbor, out float3 target)
    {
        var relativeDir = math.normalizesafe(unit.Position - neighbor.Position);
        var rightDir = math.mul(quaternion.RotateY(RotateAmount), relativeDir);
        var rightTarget = neighbor.Position + rightDir * (neighbor.Radius + 2 * unit.Radius);
        var rightSqrDst = math.lengthsq(unit.TargetPosition - rightTarget);

        var leftDir = math.mul(quaternion.RotateY(-RotateAmount), relativeDir);
        var leftTarget = neighbor.Position + leftDir * (neighbor.Radius + 2 * unit.Radius);
        var leftSqrDst = math.lengthsq(unit.TargetPosition - leftTarget);

        if (rightSqrDst < leftSqrDst)
        {
            target = rightTarget;
            return 1;
        }

        target = leftTarget;
        return -1;
    }


    private int ChooseSignVelocity(MovementComponent unit, MovementComponent neighbor)
    {
        var neighborDesiredDir = neighbor.TargetPosition - neighbor.Position;
        var relativeDir = neighbor.Position - unit.Position;

        var a = unit.Position;
        var b = unit.Position + relativeDir;
        var c = unit.Position + neighborDesiredDir;

        if (MathUtil.LeftOf(a, b, c) > 0)
        {
            return -1;
        }

        return 1;
    }

    private AvoidanceType GetAvoidanceType(MovementComponent unit)
    {
        if (!unit.Resolved)
        {
            return AvoidanceType.Moving;
        }
        else
        {
            return AvoidanceType.Stationary;
        }
    }

    private bool ResolveEnemyConstraints(MovementComponent unit, int neighborIdx)
    {
        if (unit.Target == neighborIdx)
        {
            return false;
        }

        return true;
    }

    private bool ResolveFriendConstraints(MovementComponent unit, MovementComponent neighbor, float avoidancePriority)
    {
        if (neighbor.Attacking)
        {
            return true;
        }

        var avoidanceType = GetAvoidanceType(neighbor);
        var validResolveState = !neighbor.Resolved || (neighbor.Resolved && neighbor.HoldingPosition);
        var validNonCombatConstraint = ResolveFriendNonCombatConstraints(unit, neighbor, avoidancePriority);

        if ((int)avoidanceType >= avoidancePriority
                && validResolveState
                && validNonCombatConstraint)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    private bool ResolveFriendNonCombatConstraints(MovementComponent unit, MovementComponent neighbor, float avoidancePriority)
    {
        // Test if same group or going towards the same direction
        var sameGroup = neighbor.CurrentGroup == unit.CurrentGroup;

        return !sameGroup || (sameGroup && math.dot(neighbor.Velocity, unit.Velocity) < 0);
    }

    private bool ValidTargetPositionConstraint(MovementComponent unit, MovementComponent neighbor)
    {
        var relativeDir = unit.Position - neighbor.Position;
        var plane = new MathUtil.Plane(relativeDir, neighbor.Position);
        var onPositiveHalfPlane = MathUtil.OnPositiveHalfPlane(plane, unit.TargetPosition, 0);

        // Note - apply the second part of this check to Resolved units ONLY since
        // units moving in opposite directions could stick to each other if one
        // of them has a bad side preference choice
        // return !onPositiveHalfPlane || (neighbor.Resolved && onPositiveHalfPlane && unit.SidePreference != 0);
        return !onPositiveHalfPlane;
    }

    public bool CheckUnitStop(int unitIdx, NativeMultiHashMap<int, int> neighbors)
    {
        var unit = Units[unitIdx];
        var dir = unit.TargetPosition - unit.Position;
        var dirLenSqr = math.lengthsq(dir);

        // Stop if this unit is very close to the target position
        if (dirLenSqr < 0.001f)
        {
            return true;
        }

        // Stop if this unit "goes over" the target position
        var startingDir = unit.TargetPosition - unit.MoveStartPosition;
        if (math.dot(startingDir, dir) < 0)
        {
            return true;
        }

        var neighborIndicies = neighbors.GetValuesForKey(unitIdx);
        foreach (var neighborIdx in neighborIndicies)
        {
            if (neighborIdx == unitIdx)
            {
                continue;
            }

            var neighbor = Units[neighborIdx];
            var relativeDir = neighbor.Position - unit.Position;
            var threshold = neighbor.Radius + unit.Radius + 0.05f;
            if (math.lengthsq(relativeDir) > threshold * threshold)
            {
                continue;
            }

            var relativeTargetDir = neighbor.TargetPosition - unit.TargetPosition;
            if (math.lengthsq(relativeTargetDir) < 1.25f * 1.25f)
            {
                if (!neighbor.Resolved)
                {
                    if (math.dot(relativeDir, unit.Velocity) >= 0
                            && math.dot(unit.Velocity, neighbor.Velocity) < 0
                            && dirLenSqr < 4)
                    {
                        return true;
                    }
                }
                else
                {
                    // ignore units that are too far from their push zone
                    var neighborTargetLenSq = math.lengthsq(neighbor.StopPosition - neighbor.Position);
                    if (math.dot(relativeDir, unit.Velocity) >= 0.5f
                            && neighborTargetLenSq < 4)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public void CheckStop(NativeMultiHashMap<int, int> neighbors)
    {
        // Detect stopping
        for (int i = 0; i < Units.Length; i++)
        {
            var unit = Units[i];
            if (!unit.Resolved)
            {
                if (unit.Target == -1)
                {
                    if (CheckUnitStop(i, neighbors))
                    {
                        unit.Velocity = Vector3.zero;
                        unit.Resolved = true;
                        unit.StopPosition = unit.Position;
                    }
                }
                else
                {
                    if (unit.Attacking)
                    {
                        unit.Resolved = true;
                    }
                    else
                    {
                        unit.Resolved = false;
                    }
                }
            }

            Units[i] = unit;
        }
    }

    public static float signedangle(float3 from, float3 to, float3 axis)
    {
        float angle = math.acos(math.dot(math.normalize(from), math.normalize(to)));
        float sign = math.sign(math.dot(axis, math.cross(from, to)));
        return math.degrees(angle * sign);
    }

    private bool ProhibitPush(int unitIdx, int neighborIdx)
    {
        var unit = Units[unitIdx];
        var neighbor = Units[neighborIdx];

        var inPushRadius = InPushRadius(unit, neighbor);

        return unitIdx == neighborIdx
            || neighbor.Owner != unit.Owner
            || unit.Attacking
            || !inPushRadius;
    }

    private float3 CalculatePush(int unitIdx, NativeMultiHashMap<int, int> neighbors)
    {
        var unit = Units[unitIdx];

        // Get nearest colliding neighbor, where the neighbor is moving towards
        // this unit
        var calculatedVelocity = float3.zero;
        var count = 0;

        var resolvedVelocity = float3.zero;
        var resolvedCnt = 0;

        var neighborIndexes = neighbors.GetValuesForKey(unitIdx);
        foreach (var neighborIdx in neighborIndexes)
        {
            if (ProhibitPush(unitIdx, neighborIdx))
            {
                continue;
            }

            var neighbor = Units[neighborIdx];

            // Calculate how much an "inactive" unit should be pushed
            var relDir = unit.Position - neighbor.Position;

            var velocity = neighbor.Velocity;

            if (!neighbor.Resolved)
            {
                var angle = signedangle(velocity, relDir, Vector3.up);
                var angleSign = math.sign(angle);
                var rotateRadius = math.radians(angleSign * 90);

                var rotation = quaternion.Euler(0, rotateRadius, 0);
                var perpVelocity = math.mul(rotation, velocity);

                // Add a lower bound so that turn rate is faster
                var t = math.max(math.smoothstep(0, 90, math.abs(angle)), 0.4f);
                var perpIntensity = math.lerp(0, 1, t);
                var forwardIntensity = 1 - perpIntensity;
                // appliedVelocity = perpIntensity * perpVelocity + forwardIntensity * velocity;
                var appliedVelocity = perpIntensity * perpVelocity + velocity;
                calculatedVelocity += appliedVelocity;
                count++;
            }
            else
            {
                if (math.lengthsq(neighbor.Velocity) > 0)
                {
                    var rel = unit.Position - neighbor.Position;
                    var appliedVelocity = math.normalizesafe(rel) * unit.MaxSpeed;
                    resolvedVelocity += appliedVelocity;
                    resolvedCnt++;
                }
            }
        }

        if (count > 0)
        {
            calculatedVelocity /= count;
            return calculatedVelocity;
        }
        else if (resolvedCnt > 0)
        {
            resolvedVelocity /= resolvedCnt;
            return resolvedVelocity;
        }

        return float3.zero;
    }

    private bool InPushRadius(MovementComponent unit, MovementComponent neighbor)
    {
        var relativeDir = unit.Position - neighbor.Position;
        var dist = math.lengthsq(relativeDir);
        dist -= unit.Radius;
        dist -= neighbor.Radius;
        // Add a small offset to prevent "stuttering" due to the impulse force
        // (from resolving collisions) kicking neighbors out of the push influence region
        dist -= 0.1f;

        return dist < Mathf.Epsilon && Vector3.Dot(relativeDir, neighbor.Velocity) > 0;
    }
}

public struct UtilityFunctions
{
    public static NativeMultiHashMap<int, int> BroadPhase(
            NativeArray<MovementComponent> units,
            NativeMultiHashMap<int, int> spatialHashMap,
            SpatialHashMeta spatialHashMeta,
            int customCellRange = -1)
    {
        var neighbors = new NativeMultiHashMap<int, int>(units.Length * 15, Allocator.Temp);
        for (int unitIdx = 0; unitIdx < units.Length; unitIdx++)
        {
            var unit = units[unitIdx];
            var cell = ComputeCell(unit, spatialHashMeta);

            int cellRange;

            if (customCellRange != -1)
            {
                cellRange = customCellRange;
            }
            else
            {
                cellRange = ComputeCellRange(unit, cell, spatialHashMeta);
            }

            var minRow = cell.Row - cellRange;
            var minCol = cell.Column - cellRange;

            var range = 2 * cellRange + 1;

            var hashes = new NativeList<int>(range * range, Allocator.Temp);
            for (int row = 0; row < range; row++)
            {
                for (int col = 0; col < range; col++)
                {
                    var neighborCell = new Cell(minRow + row, minCol + col);
                    if (neighborCell.Row >= 0
                        && neighborCell.Row < spatialHashMeta.Rows
                        && neighborCell.Column >= 0
                        && neighborCell.Column < spatialHashMeta.Columns)
                    {
                        hashes.Add(ComputeSpatialHash(neighborCell, spatialHashMeta));
                    }
                }
            }

            foreach (var hash in hashes)
            {
                if (!spatialHashMap.ContainsKey(hash))
                {
                    continue;
                }

                var neighborIndices = spatialHashMap.GetValuesForKey(hash);
                foreach (var neighborIdx in neighborIndices)
                {
                    if (neighborIdx != unitIdx)
                    {
                        neighbors.Add(unitIdx, neighborIdx);
                    }
                }
            }
        }

        return neighbors;
    }

    private static Cell ComputeCell(MovementComponent unit, SpatialHashMeta meta)
    {
        var position = unit.Position;
        var rel = position - meta.Origin;
        var row = (int)math.floor(rel.z / meta.Size);
        var column = (int)math.floor(rel.x / meta.Size);

        return new Cell()
        {
            Row = row,
            Column = column
        };
    }

    public static int RangeToCellCount(float range, SpatialHashMeta meta)
    {
        var val = range / meta.Size;

        var cnt = (int)val;
        if (cnt < val)
        {
            cnt++;
        }

        return cnt;
    }

    private static int ComputeCellRange(MovementComponent unit, Cell cell, SpatialHashMeta meta)
    {
        if (unit.Resolved)
        {
            return 1;
        }

        var range = unit.Radius + unit.MaxSpeed * unit.TimeHorizon;
        return RangeToCellCount(range, meta);
    }

    private static int ComputeSpatialHash(Cell cell, SpatialHashMeta meta)
    {
        var hash = cell.Row * meta.Columns + cell.Column;
        return hash;
    }
}

public struct ChooseNeighborResult
{
    public int ResolveIndex;
    public bool ClearPathToTarget;
}
