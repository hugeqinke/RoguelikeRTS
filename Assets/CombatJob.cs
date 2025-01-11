using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public struct CombatJob : IJob
{
    public NativeArray<MovementComponent> Units;
    [ReadOnly] public NativeMultiHashMap<int, int> SpatialHash;
    public SpatialHashMeta Meta;
    public float CombatClearRange;

    public void Execute()
    {
        var neighbors = UtilityFunctions.BroadPhase(Units, SpatialHash, Meta);

        var cellRange = UtilityFunctions.RangeToCellCount(CombatClearRange, Meta);
        var combatNeighbors = UtilityFunctions.BroadPhase(Units, SpatialHash, Meta, cellRange);

        for (int i = 0; i < Units.Length; i++)
        {
            Retarget(i, combatNeighbors.GetValuesForKey(i));
            ProcessCombat(i, neighbors);

            UpdateTargetPositions(i);
        }
    }

    private void UpdateTargetPositions(int i)
    {
        var unit = Units[i];

        if (unit.Target != -1)
        {
            var target = Units[unit.Target];

            unit.TargetPosition = target.Position;
            unit.StopPosition = target.Position;
            unit.MoveStartPosition = unit.Position;
            if (!unit.Attacking)
            {
                unit.Resolved = false;
            }
        }

        Units[i] = unit;
    }

    private void Retarget(int i, NativeMultiHashMap<int, int>.Enumerator neighborIndexes)
    {
        var unit = Units[i];
        var targetIdx = unit.Target;

        if (unit.Attacking
            || (targetIdx == -1 && math.lengthsq(unit.Position - unit.TargetPosition) > math.EPSILON))
        {
            return;
        }

        if (unit.HoldingPosition)
        {
            var nearSqrDst = math.INFINITY;
            if (targetIdx != -1)
            {
                var targetUnit = Units[targetIdx];
                nearSqrDst = math.distancesq(targetUnit.Position, unit.Position);
            }

            var nearTargetIdx = -1;

            foreach (var neighborIdx in neighborIndexes)
            {
                var neighbor = Units[neighborIdx];
                if (neighbor.Owner == unit.Owner)
                {
                    continue;
                }

                var neighborSqrDst = math.distancesq(neighbor.Position, unit.Position);
                var threshold = neighbor.Radius + unit.Radius + unit.AttackRadius;
                if (neighborSqrDst < threshold * threshold)
                {
                    if (neighborSqrDst < nearSqrDst)
                    {
                        nearTargetIdx = neighborIdx;
                        nearSqrDst = neighborSqrDst;
                    }
                }
            }

            if (nearTargetIdx != -1)
            {
                unit.Target = nearTargetIdx;
            }
        }
        else
        {
            var sqrDst = math.INFINITY;
            if (targetIdx != -1)
            {
                var targetUnit = Units[targetIdx];
                sqrDst = math.distancesq(targetUnit.Position, unit.Position);
            }

            var nearTargetIdx = -1;

            foreach (var neighborIdx in neighborIndexes)
            {
                var neighbor = Units[neighborIdx];
                if (neighbor.Owner == unit.Owner)
                {
                    continue;
                }

                var neighborSqrDst = math.distancesq(neighbor.Position, unit.Position);

                if (neighborSqrDst < sqrDst)
                {
                    nearTargetIdx = neighborIdx;
                    sqrDst = neighborSqrDst;
                }
            }

            if (nearTargetIdx != -1)
            {
                unit.Target = nearTargetIdx;

                var position = Units[unit.Target].Position;

                unit.TargetPosition = position;
                unit.StopPosition = position;
                unit.MoveStartPosition = unit.Position;
                unit.Resolved = false;
            }
        }



        Units[i] = unit;
    }

    private void ProcessCombat(int i, NativeMultiHashMap<int, int> neighbors)
    {
        var unit = Units[i];

        if (!unit.Attacking)
        {
            if (unit.Target != -1)
            {
                var neighbor = Units[unit.Target];
                var relDir = neighbor.Position - unit.Position;

                var attackRadius = unit.AttackRadius + unit.Radius + neighbor.Radius;
                if (math.lengthsq(relDir) <= attackRadius * attackRadius)
                {
                    unit.Attacking = true;
                }
            }
        }
        else
        {
            if (unit.Target != -1)
            {
                var neighbor = Units[unit.Target];
                var relDir = math.lengthsq(neighbor.Position - unit.Position);

                var attackRadius = unit.AttackRadius + unit.Radius + neighbor.Radius;
                if (math.lengthsq(relDir) > attackRadius * attackRadius)
                {
                    unit.Attacking = false;
                }
            }
            else
            {
                unit.Attacking = false;
            }
        }

        Units[i] = unit;
    }
}
