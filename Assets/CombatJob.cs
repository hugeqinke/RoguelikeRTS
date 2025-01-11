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

    public void Execute()
    {
        var neighbors = UtilityFunctions.BroadPhase(Units, SpatialHash, Meta);
        for (int i = 0; i < Units.Length; i++)
        {
            ProcessCombat(i, neighbors);
        }
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
