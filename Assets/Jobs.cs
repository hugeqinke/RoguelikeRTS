using System.Collections;
using System.Collections.Generic;
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

    public int Substeps;
    public float DeltaTime;

    public bool StopFromCollision(MovementComponent unit, MovementComponent neighbor)
    {
        if (neighbor.Resolved)
        {
            return false;
        }

        var sepDir = neighbor.Position - unit.Position;
        var sepLenSq = math.lengthsq(sepDir);
        var collideRadius = unit.Radius + neighbor.Radius;
        if (sepLenSq < collideRadius * collideRadius)
        {
            var targetDir = neighbor.TargetPosition - unit.TargetPosition;
            var targetLenSq = math.lengthsq(targetDir);

            if (targetLenSq <= 4 * collideRadius * collideRadius
                    && math.dot(unit.Velocity, neighbor.Velocity) <= 0)
            {
                return true;
            }
        }

        return false;
    }

    public bool CheckUnitStop(int unitIdx, ref NativeMultiHashMap<int, int> neighbors)
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

        // Stop if the unit's been going too slow for too long
        if (unit.LowVelocityElapsed > unit.LowVelocityDuration)
        {
            return true;
        }

        if (dirLenSqr < 25)
        {
            // Stop if unit is somewhat near the target position, and collides
            // with another unit going the opposite direction
            var neighborIndicies = neighbors.GetValuesForKey(unitIdx);
            foreach (var neighborIdx in neighborIndicies)
            {
                if (neighborIdx == unitIdx)
                {
                    continue;
                }

                var neighbor = Units[neighborIdx];

                var collisionRadiusSq = 2.25f * unit.Radius * unit.Radius;
                var closeRadiusSq = 1.25f * 1.25f * unit.Radius * unit.Radius;

                if (!neighbor.Resolved && dirLenSqr <= collisionRadiusSq)
                {
                    if (StopFromCollision(unit, neighbor))
                    {
                        return true;
                    }
                }
                else if (neighbor.Resolved && neighbor.CurrentGroup == unit.CurrentGroup)
                {
                    return true;
                }
            }
        }


        return false;
    }

    public void CheckStop(ref NativeMultiHashMap<int, int> neighbors)
    {
        // Detect stopping
        for (int i = 0; i < Units.Length; i++)
        {
            var unit = Units[i];
            if (!unit.Resolved)
            {
                if (CheckUnitStop(i, ref neighbors))
                {
                    unit.Velocity = Vector3.zero;
                    unit.Resolved = true;
                    Units[i] = unit;
                }
            }
        }
    }

    public void Execute()
    {
        // Update heuristics
        for (int i = 0; i < Units.Length; i++)
        {
            var unit = Units[i];
            if (unit.Resolved || (!unit.Resolved && math.lengthsq(unit.Velocity) > 2.25))
            {
                unit.LowVelocityElapsed = 0;
            }
            else
            {
                unit.LowVelocityElapsed += DeltaTime;
            }

            Units[i] = unit;
        }

        var neighbors = BroadPhase(SpatialHashMeta, SpatialHashMap);

        // resolve physics
        var sdt = DeltaTime / Substeps;

        for (int substep = 0; substep < Substeps; substep++)
        {
            for (int i = 0; i < Units.Length; i++)
            {
                var unit = Units[i];

                if (unit.Resolved)
                {
                    unit.Velocity = CalculatePush(i, neighbors.GetValuesForKey(i));
                }
                else
                {
                    var dir = math.normalizesafe(unit.TargetPosition - unit.Position);
                    unit.Velocity += dir * unit.Acceleration * sdt;

                    if (math.lengthsq(unit.Velocity) > unit.MaxSpeed * unit.MaxSpeed)
                    {
                        unit.Velocity = math.normalizesafe(unit.Velocity) * unit.MaxSpeed;
                    }
                }

                unit.OldPosition = unit.Position;
                unit.Position += unit.Velocity * sdt;

                Units[i] = unit;
            }

            for (int i = 0; i < Units.Length; i++)
            {
                var unit = Units[i];

                var neighborIndices = neighbors.GetValuesForKey(i);

                foreach (var neighborIdx in neighborIndices)
                {
                    var body1 = Units[i];
                    var body2 = Units[neighborIdx];

                    var dir = body2.Position - body1.Position;
                    var separation = math.length(dir);

                    var collideRadius = body1.Radius + body2.Radius;

                    if (separation < collideRadius)
                    {
                        var totalMass = body1.Mass + body2.Mass;

                        var slop = collideRadius - separation;

                        var x1 = -body2.Mass / totalMass * slop * math.normalizesafe(dir);
                        var x2 = body1.Mass / totalMass * slop * math.normalizesafe(dir);

                        body1.Position += x1;
                        body2.Position += x2;

                        Units[i] = body1;
                        Units[neighborIdx] = body2;
                    }
                }
            }

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

                var cross = Vector3.Cross(relDir, moveDir);
                var crossDirSq = cross.sqrMagnitude;

                if (relDirLenSq < moveDirSq && crossDirSq < 0.001f)
                {
                    unit.Position = unit.TargetPosition;
                    Units[i] = unit;
                }
            }

            // fix velocity 
            for (int i = 0; i < Units.Length; i++)
            {
                var unit = Units[i];

                if (unit.Resolved)
                {
                    continue;
                }

                unit.Velocity = (unit.Position - unit.OldPosition) / sdt;
                Units[i] = unit;
            }
        }

        // Post
        CheckStop(ref neighbors);
    }

    public static float signedangle(float3 from, float3 to, float3 axis)
    {
        float angle = math.acos(math.dot(math.normalize(from), math.normalize(to)));
        float sign = math.sign(math.dot(axis, math.cross(from, to)));
        return math.degrees(angle * sign);
    }

    private float3 CalculatePush(int unitIdx, NativeMultiHashMap<int, int>.Enumerator neighbors)
    {
        var unit = Units[unitIdx];

        // Get nearest colliding neighbor, where the neighbor is moving towards
        // this unit



        var calculatedVelocity = float3.zero;
        var count = 0;

        var resolvedVelocity = float3.zero;
        var resolvedCnt = 0;

        foreach (var neighborIdx in neighbors)
        {
            if (neighborIdx == unitIdx)
            {
                continue;
            }

            var neighbor = Units[neighborIdx];

            if (InPushRadius(unit, neighbor))
            {
                // Calculate how much an "inactive" unit should be pushed
                var relDir = unit.Position - neighbor.Position;

                var velocity = neighbor.Velocity;

                if (!neighbor.Resolved)
                {
                    var angle = signedangle(velocity, relDir, Vector3.up);
                    var angleSign = math.sign(angle);

                    var rotation = quaternion.Euler(0, angleSign * 90, 0);
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


    private Cell ComputeRowColumns(SpatialHashMeta meta, int idx)
    {
        var position = Units[idx].Position;
        var rel = position - meta.Origin;
        var row = Mathf.FloorToInt(rel.z / meta.Size);
        var column = Mathf.FloorToInt(rel.x / meta.Size);

        return new Cell()
        {
            Row = row,
            Column = column
        };
    }

    private int ComputeSpatialHash(SpatialHashMeta meta, Cell cell)
    {
        var hash = cell.Row * meta.Columns + cell.Column;
        return hash;
    }

    public NativeMultiHashMap<int, int> BroadPhase(SpatialHashMeta meta, NativeMultiHashMap<int, int> spatialHash)
    {
        var neighbors = new NativeMultiHashMap<int, int>(Units.Length * 15, Allocator.Temp);

        for (int i = 0; i < Units.Length; i++)
        {
            var cell = ComputeRowColumns(meta, i);

            var cells = new NativeArray<Cell>(9, Allocator.Temp);
            cells[0] = cell;
            cells[1] = new Cell(cell.Row + 1, cell.Column);
            cells[2] = new Cell(cell.Row + 1, cell.Column + 1);
            cells[3] = new Cell(cell.Row, cell.Column + 1);
            cells[4] = new Cell(cell.Row - 1, cell.Column + 1);
            cells[5] = new Cell(cell.Row - 1, cell.Column);
            cells[6] = new Cell(cell.Row - 1, cell.Column - 1);
            cells[7] = new Cell(cell.Row, cell.Column - 1);
            cells[8] = new Cell(cell.Row + 1, cell.Column - 1);

            var hashes = new NativeList<int>(9, Allocator.Temp);

            foreach (var neighborCell in cells)
            {
                if (neighborCell.Row >= 0
                        && neighborCell.Row < meta.Rows
                        && neighborCell.Column >= 0
                        && neighborCell.Column < meta.Columns)
                {
                    hashes.Add(ComputeSpatialHash(meta, neighborCell));
                }
            }

            foreach (var hash in hashes)
            {
                if (!spatialHash.ContainsKey(hash))
                {
                    continue;
                }

                var indices = spatialHash.GetValuesForKey(hash);
                foreach (var index in indices)
                {
                    if (index != i)
                    {
                        neighbors.Add(i, index);
                    }
                }
            }
        }

        return neighbors;
    }
}
