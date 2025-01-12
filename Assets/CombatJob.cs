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
    public float Time;

    public void Execute()
    {
        var combatNeighbors = UtilityFunctions.BroadPhase(Units, SpatialHash, Meta, UtilityFunctions.CellRangeType.CombatRange);

        for (int i = 0; i < Units.Length; i++)
        {
            Retarget(i, combatNeighbors.GetValuesForKey(i));
            ProcessCombat(i);
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
            unit.MoveStartPosition = unit.Position;
            if (!unit.Attacking)
            {
                unit.Resolved = false;
            }
        }
        else
        {
            if (!unit.AttackMoveResolved)
            {
                unit.TargetPosition = unit.AttackMoveDestination;
            }
            else if (!unit.ResolvedCombat)
            {
                var relLengthSq = math.lengthsq(unit.TargetPosition - unit.Position);
                var threshold = unit.AttackRadius + unit.Radius;
                if (relLengthSq < threshold * threshold)
                {
                    unit.TargetPosition = unit.Position;
                    unit.StopPosition = unit.Position;
                    unit.Resolved = true;
                    unit.ResolvedCombat = true;
                }
            }
        }

        Units[i] = unit;
    }

    private void Retarget(int i, NativeMultiHashMap<int, int>.Enumerator neighborIndexes)
    {
        var unit = Units[i];
        var targetIdx = unit.Target;

        if (unit.Attacking || (targetIdx == -1 && !unit.Resolved && unit.AttackMoveResolved))
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
                unit.MoveStartPosition = unit.Position;
                unit.Resolved = false;
            }
        }

        Units[i] = unit;
    }

    private void ProcessCombat(int i)
    {
        var unit = Units[i];

        if (!unit.Attacking)
        {
            if (unit.Target != -1)
            {
                if (InRange(unit))
                {
                    unit.Attacking = true;
                }
            }
        }
        else
        {
            if (unit.Target == -1 || !InRange(unit))
            {
                unit.Attacking = false;
            }
            else
            {
                if (Time > unit.LastAttackTime + unit.AttackSpeed)
                {
                    // TODO: Maybe add this to an event queue and process it later? 
                    var targetUnit = Units[unit.Target];
                    targetUnit.Health -= unit.Damage;
                    Units[unit.Target] = targetUnit;

                    unit.LastAttackTime = Time;
                }
            }
        }

        Units[i] = unit;
    }

    private bool InRange(MovementComponent unit)
    {
        var neighbor = Units[unit.Target];
        var relLenSq = math.lengthsq(neighbor.Position - unit.Position);

        var attackRadius = unit.AttackRadius + unit.Radius + neighbor.Radius;
        return relLenSq <= attackRadius * attackRadius;
    }
}
