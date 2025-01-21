using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class PlayerManager : MonoBehaviour
{
    public Dictionary<UnitType, int> _unitCounts;

    public int GasCount;
    public int MineralCount;
    public int MaxSupply;
    public int RemainingSupply;

    public TMP_Text GasCountTxt;
    public TMP_Text MineralCountTxt;
    public TMP_Text SupplyTxt;

    private void Start()
    {
        UpdateText();
    }

    public void UpdateText()
    {
        GasCountTxt.text = GasCount.ToString();
        MineralCountTxt.text = MineralCount.ToString();
        SupplyTxt.text = $"{(MaxSupply - RemainingSupply)}/{MaxSupply}";
    }

    public void AddUnit(UnitType unit, int count)
    {
        if (!_unitCounts.ContainsKey(unit))
        {
            _unitCounts.Add(unit, 0);
        }
        else
        {
            _unitCounts[unit] += count;
        }
    }

    private void Awake()
    {
        _unitCounts = new Dictionary<UnitType, int>();
    }
}

public enum UnitType
{
    Marine,
    Ghost,
    BattleCruiser,
    Hellion,
    Marauder,
    Medivac,
    Reaper,
    SiegeTank
}