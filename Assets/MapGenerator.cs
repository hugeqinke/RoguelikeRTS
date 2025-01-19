using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class MapGenerator : MonoBehaviour
{
    public CampaignEvents CampaignEvents;

    private void Awake()
    {
    }

    // Start is called before the first frame update
    void Start()
    {
        Random.InitState(0);
        CampaignEvents.Init();
    }

    // Update is called once per frame
    void Update()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            var pos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            if (CampaignEvents.TryGetCellInfo(pos, out CampaignEvents.CellType cellType))
            {
                Debug.Log("Pressed " + cellType);
            }
        }
    }
}

[System.Serializable]
public class CampaignEvents
{
    public enum CellType
    {
        Invalid,
        NonCombatEvent,
        CombatEvent,
        HomeBase,
        Complete
    }

    // Cell types and events
    public CellType[,] CellTypeMap;
    public GameObject[,] CellTypeIcons;
    public GameObject CombatEventIcon;
    public GameObject NonCombatEventIcon;
    public GameObject HomebaseIcon;
    public GameObject InvalidIcon;
    public GameObject CompleteFlagIcon;

    private Cell _homeCell;

    // Fog of war
    public GameObject[,] FogDisplays;
    public GameObject FogPrefab;

    public Grid Grid;

    private static List<Cell> _neighborDirections = new List<Cell>()
    {
        new Cell(1, 0),
        new Cell(0, 1),
        new Cell(-1, 0),
        new Cell(0, -1)
    };

    public void Init()
    {
        Grid.Init();

        CellTypeMap = new CellType[Grid.Rows, Grid.Columns];
        CellTypeIcons = new GameObject[Grid.Rows, Grid.Columns];

        for (int row = 0; row < Grid.Rows; row++)
        {
            for (int col = 0; col < Grid.Columns; col++)
            {
                var cellType = RandomCellType();
                CellTypeMap[row, col] = cellType;
            }
        }

        var homeRow = Mathf.RoundToInt(Grid.Rows * 0.5f);
        var homeCol = Mathf.RoundToInt(Grid.Columns * 0.5f);
        _homeCell = new Cell(homeRow, homeCol);
        CellTypeMap[homeRow, homeCol] = CellType.HomeBase;

        // Render cell type columns
        for (int row = 0; row < Grid.Rows; row++)
        {
            for (int col = 0; col < Grid.Columns; col++)
            {
                var cellType = CellTypeMap[row, col];
                var icon = GenerateIcon(cellType);
                icon.transform.position = Grid.GetCellPosition(row, col);
                CellTypeIcons[row, col] = icon;
            }
        }

        // Fog of war
        FogDisplays = new GameObject[Grid.Rows, Grid.Columns];
        for (int row = 0; row < Grid.Rows; row++)
        {
            for (int col = 0; col < Grid.Columns; col++)
            {
                var fog = GameObject.Instantiate(FogPrefab);
                var position = Grid.GetCellPosition(row, col);
                fog.transform.position = position;

                FogDisplays[row, col] = fog;
                FogDisplays[row, col].gameObject.SetActive(true);
            }
        }

        FogDisplays[_homeCell.Row, _homeCell.Column].gameObject.SetActive(false);
        foreach (var neighborDirection in _neighborDirections)
        {
            var cell = _homeCell + neighborDirection;
            if (cell.Row < Grid.Rows && cell.Row >= 0 & cell.Column < Grid.Columns && cell.Column >= 0)
            {
                FogDisplays[cell.Row, cell.Column].gameObject.SetActive(false);
            }
        }
    }

    public CellType RandomCellType()
    {
        var cellType = Random.Range(0, 2);

        switch (cellType)
        {
            case 0:
                return CellType.NonCombatEvent;
            case 1:
                return CellType.CombatEvent;
            default:
                return CellType.NonCombatEvent;
        }
    }

    private GameObject GenerateIcon(CellType cellType)
    {
        switch (cellType)
        {
            case CellType.NonCombatEvent:
                return GameObject.Instantiate(NonCombatEventIcon);
            case CellType.CombatEvent:
                return GameObject.Instantiate(CombatEventIcon);
            case CellType.HomeBase:
                return GameObject.Instantiate(HomebaseIcon);
            default:
                return GameObject.Instantiate(InvalidIcon);
        }
    }

    public bool Interactable(Cell cell)
    {
        var fogDisplay = FogDisplays[cell.Row, cell.Column];
        return !fogDisplay.gameObject.activeInHierarchy;
    }

    public bool TryGetCellInfo(Vector3 position, out CellType cellType)
    {
        var cell = Grid.GetCell(position);

        if (cell.Row < Grid.Rows && cell.Row >= 0
                && cell.Column < Grid.Columns && cell.Column >= 0
                && Interactable(cell))
        {
            cellType = CellTypeMap[cell.Row, cell.Column];
            return true;
        }

        cellType = CellType.Invalid;
        return false;
    }
}

public class InfrastructureGridWrapper
{
    private readonly Grid _grid;

    private Dictionary<BuildingController, List<Cell>> _buildingCellsMap;
    private Dictionary<int, BuildingController> _cellBuildingMap;
    private bool[,] _occupied;

    public InfrastructureGridWrapper(Grid grid)
    {
        _grid = grid;

        _buildingCellsMap = new Dictionary<BuildingController, List<Cell>>();
        _cellBuildingMap = new Dictionary<int, BuildingController>();
        _occupied = new bool[_grid.Rows, _grid.Columns];
    }

    public bool IsOccupied(int row, int col)
    {
        return _occupied[row, col];
    }

    public void Remove(IEnumerable<BuildingController> buildings)
    {
        foreach (var building in buildings)
        {
            var cells = _buildingCellsMap[building];
            foreach (var cell in cells)
            {
                _occupied[cell.Row, cell.Column] = false;
                var hashcode = ComputeHashcode(cell);
                _cellBuildingMap.Remove(hashcode);
            }

            _buildingCellsMap.Remove(building);
        }
    }

    public void AddBuildingController(
            BuildingController building,
            BuildingPreview preview,
            Cell minCell)
    {
        var cells = new List<Cell>();
        for (int row = 0; row < preview.Rows; row++)
        {
            for (int col = 0; col < preview.Columns; col++)
            {
                var cell = new Cell()
                {
                    Row = minCell.Row + row,
                    Column = minCell.Column + col
                };

                cells.Add(cell);
                _occupied[cell.Row, cell.Column] = true;
                _cellBuildingMap.Add(ComputeHashcode(cell), building);
            }
        }

        _buildingCellsMap.Add(building, cells);
    }

    public List<Cell>.Enumerator GetBuildingCells(BuildingController building)
    {
        return _buildingCellsMap[building].GetEnumerator();
    }

    public BuildingController GetBuilding(Cell cell)
    {
        var hashCode = ComputeHashcode(cell);
        if (_cellBuildingMap.ContainsKey(hashCode))
        {
            return _cellBuildingMap[hashCode];
        }

        return null;
    }

    private int ComputeHashcode(Cell cell)
    {
        return cell.Row * _grid.Columns + cell.Column;
    }
}

[System.Serializable]
public class Grid
{
    // Columns
    public GameObject Cell;
    public Transform Center;
    public int Rows;
    public int Columns;

    // Cell properties
    private Vector3 _origin;
    private float _width;
    private float _height;
    public float CellSize;

    public int GenerateHash(int row, int col)
    {
        return row * Columns + col;
    }

    public Vector3 GetCellPosition(int row, int col, float rowOffset = 0, float colOffset = 0)
    {
        var position = _origin + new Vector3(colOffset, rowOffset, 0);
        position.x += CellSize * 0.5f;
        position.y += CellSize * 0.5f;

        position.y += row * CellSize;
        position.x += col * CellSize;

        return position;
    }

    public Cell GetCell(Vector3 position, float rowOffset = 0, float colOffset = 0)
    {
        var tempOrigin = _origin + new Vector3(colOffset, rowOffset, 0);
        var rel = position - tempOrigin;
        var row = Mathf.FloorToInt(rel.y / CellSize);
        var column = Mathf.FloorToInt(rel.x / CellSize);

        return new Cell()
        {
            Row = row,
            Column = column
        };
    }

    public void Init()
    {
        // Init
        CellSize = Cell.transform.localScale.x;
        _width = Rows * CellSize;
        _height = Columns * CellSize;

        _origin = Center.transform.position;
        _origin.x -= _width * 0.5f;
        _origin.y -= _height * 0.5f;

        // Render grid
        var cellPos0 = _origin;
        cellPos0.x += CellSize * 0.5f;
        cellPos0.y += CellSize * 0.5f;

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                var cellPosition = cellPos0;
                cellPosition.x += col * CellSize;
                cellPosition.y += row * CellSize;

                var cell = GameObject.Instantiate(Cell);
                cell.transform.position = cellPosition;
            }
        }
    }
}

/* public abstract class Reward
{
    protected int Quantity;
    public abstract void Execute(CampaignManager campaignManager);
}

public class MineralReward : Reward
{
    public override void Execute(CampaignManager campaignManager)
    {
        campaignManager.CurrentMinerals += Quantity;
    }
}

public class VespineGasReward : Reward
{
    public override void Execute(CampaignManager campaignManager)
    {
        campaignManager.CurrentVespineGas += Quantity;
    }
}

public class MineralsPerTurnReward : Reward
{
    public override void Execute(CampaignManager campaignManager)
    {
        campaignManager.MineralsPerTurn += Quantity;
    }
}

public class VespineGasPerTurn : Reward
{
    public override void Execute(CampaignManager campaignManager)
    {
        campaignManager.VespineGasPerTurn += Quantity;
    }
}
 */