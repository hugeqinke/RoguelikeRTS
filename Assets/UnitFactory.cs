using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UnitFactory : MonoBehaviour
{
    public List<UnitStatsComponent> UnitStats;
    private Dictionary<UnitType, UnitStatsComponent> _mapping;

    private void Start()
    {
        _mapping = new Dictionary<UnitType, UnitStatsComponent>();
        foreach (var unitStat in UnitStats)
        {
            _mapping.Add(unitStat.UnitType, unitStat);
        }
    }

    public UnitStatsComponent CreateUnitStatsComponent(UnitType unitType)
    {
        return _mapping[unitType];
    }
}
