using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerManager : MonoBehaviour
{
    public List<UnitStatsComponent> Units;

    public int GasCount;
    public int MineralCount;
    public int MaxSupply;
    public int RemainingSupply;

    public TMP_Text GasCountTxt;
    public TMP_Text MineralCountTxt;
    public TMP_Text SupplyTxt;

    public UnitFactory UnitFactory;

    // Unit UI
    public bool ShowUnits;
    public Queue<UnitIcon> UnitIcons;
    public UnitIcon UnitIconPrefab;
    public Image RosterPanel;

    public OverviewIcon OverviewIconPrefab;
    public Image OverviewPanel;
    private Queue<OverviewIcon> _overviewIcons;
    private Dictionary<UnitType, OverviewIcon> _overviewTypeIconMap;
    private HashSet<UnitType> _filters;

    // Sprites
    public Sprite GhostSprite;
    public Sprite BattleCruiserSprite;
    public Sprite HellionSprite;
    public Sprite MarauderSprite;
    public Sprite MarineSprite;
    public Sprite MedivacSprite;
    public Sprite ReaperSprite;
    public Sprite SiegeTankSprite;

    private void Start()
    {
        UpdateText();

        if (ShowUnits)
        {
            LoadUnitIcons();
        }
    }

    private void LoadUnitIcons()
    {
        // Render roster
        Units.Sort((a, b) => a.UnitType.CompareTo(b.UnitType));

        if (UnitIcons.Count > Units.Count)
        {
            var diff = UnitIcons.Count - Units.Count;
            for (int i = 0; i < diff; i++)
            {
                var icon = UnitIcons.Dequeue();
                Destroy(icon.gameObject);
            }
        }
        else if (UnitIcons.Count < Units.Count)
        {
            var diff = Units.Count - UnitIcons.Count;
            for (int i = 0; i < diff; i++)
            {
                var icon = Instantiate(UnitIconPrefab);
                UnitIcons.Enqueue(icon);
            }
        }

        foreach (var unit in Units)
        {
            var icon = UnitIcons.Dequeue();
            icon.UnitType = unit.UnitType;
            icon.Icon.sprite = ChooseIcon(unit.UnitType);
            icon.NameTxt.text = unit.Name;
            icon.HealthBarSlider.maxValue = unit.MaxHealth;
            icon.HealthBarSlider.value = unit.Health;
            icon.HealthTxt.text = $"{unit.Health}/{unit.MaxHealth}";
            icon.Kills.text = unit.Kills.ToString();
            icon.transform.SetParent(RosterPanel.transform);
            UnitIcons.Enqueue(icon);
        }

        // Render overview
        var counts = new Dictionary<UnitType, int>();
        foreach (var unit in Units)
        {
            var unitType = unit.UnitType;
            if (!counts.ContainsKey(unitType))
            {
                counts.Add(unitType, 0);
            }

            counts[unitType] += 1;
        }

        var keys = new List<UnitType>();
        foreach (var count in counts)
        {
            keys.Add(count.Key);
        }

        keys.Sort((a, b) => a.CompareTo(b));

        if (_overviewIcons.Count > keys.Count)
        {
            var diff = _overviewIcons.Count - keys.Count;
            for (int i = 0; i < diff; i++)
            {
                var icon = _overviewIcons.Dequeue();
                Destroy(icon.gameObject);
            }
        }
        else if (_overviewIcons.Count < keys.Count)
        {
            var diff = keys.Count - _overviewIcons.Count;
            for (int i = 0; i < diff; i++)
            {
                var icon = Instantiate(OverviewIconPrefab);
                icon.transform.SetParent(OverviewPanel.transform);
                _overviewIcons.Enqueue(icon);
            }
        }

        _overviewTypeIconMap = new Dictionary<UnitType, OverviewIcon>();
        foreach (var unitType in keys)
        {
            var icon = _overviewIcons.Dequeue();
            var sprite = ChooseIcon(unitType);

            icon.Image.sprite = sprite;
            icon.Text.text = counts[unitType].ToString();
            icon.UnitType = unitType;

            _overviewTypeIconMap[icon.UnitType] = icon;

            _overviewIcons.Enqueue(icon);
        }
    }

    private Sprite ChooseIcon(UnitType unitType)
    {
        switch (unitType)
        {
            case UnitType.Marine:
                return MarineSprite;
            case UnitType.BattleCruiser:
                return BattleCruiserSprite;
            case UnitType.Ghost:
                return GhostSprite;
            case UnitType.Hellion:
                return HellionSprite;
            case UnitType.Marauder:
                return MarauderSprite;
            case UnitType.Medivac:
                return MedivacSprite;
            case UnitType.Reaper:
                return ReaperSprite;
            case UnitType.SiegeTank:
                return SiegeTankSprite;
            default:
                return null;
        }
    }

    public void SetFilter(UnitType unitType, bool selected)
    {
        // Deselect currently selected filters.
        foreach (var filter in _filters)
        {
            _overviewTypeIconMap[filter].Deselect();
        }
        _filters.Clear();

        if (selected)
        {
            _filters.Add(unitType);
        }

        // Update visible icons
        foreach (var icon in UnitIcons)
        {
            if (_filters.Count > 0)
            {
                if (_filters.Contains(icon.UnitType))
                {
                    icon.gameObject.SetActive(true);
                }
                else
                {
                    icon.gameObject.SetActive(false);
                }
            }
            else
            {
                icon.gameObject.SetActive(true);
            }
        }
    }

    public void UpdateText()
    {
        GasCountTxt.text = GasCount.ToString();
        MineralCountTxt.text = MineralCount.ToString();
        SupplyTxt.text = $"{(MaxSupply - RemainingSupply)}/{MaxSupply}";
    }

    public void AddUnit(UnitType unitType)
    {
        var unitStats = UnitFactory.CreateUnitStatsComponent(unitType);
        Units.Add(unitStats);
    }

    private void Awake()
    {
        UnitIcons = new Queue<UnitIcon>();
        _overviewIcons = new Queue<OverviewIcon>();
        _overviewTypeIconMap = new Dictionary<UnitType, OverviewIcon>();
        _filters = new HashSet<UnitType>();
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