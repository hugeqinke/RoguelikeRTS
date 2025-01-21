using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Infrastructure
{
    public enum InputState
    {
        None,
        PlaceBuilding,
        PlaceFloatingBuilding,
        SelectedBuilding
    }

    public enum BuildingType
    {
        Invalid,
        CommandCenter,
        Barracks,
        Factory,
        EngineeringBay,
        Refinery,
        Starport,
        SupplyDepot
    }
}

public class InfrastructureManager : MonoBehaviour
{
    public Grid Grid;
    public InfrastructureGridWrapper GridWrapper;

    public Infrastructure.InputState InputState;

    public BuildingPreview CommandCenterPreview;
    public BuildingPreview EngineeringBayPreview;
    public BuildingPreview BarracksPreview;
    public BuildingPreview RefineryPreview;
    public BuildingPreview FactoryPreview;
    public BuildingPreview StarportPreview;
    public BuildingPreview SupplyDepotPreview;
    public BuildingPreview InvalidPreview;
    public BuildingPreview _activePreview;
    public Infrastructure.BuildingType _activeBuildingType;

    public BuildingController CommandCenterPrefab;
    public BuildingController EngineeringBayPrefab;
    public BuildingController BarracksPrefab;
    public BuildingController RefineryPrefab;
    public BuildingController FactoryPrefab;
    public BuildingController StarportPrefab;
    public BuildingController SupplyDepotPrefab;

    public Image FloatingBar;
    public FloatingIcon FloatingCommandCenterImage;
    public FloatingIcon FloatingEngineeringBayImage;
    public FloatingIcon FloatingBarracksImage;
    public FloatingIcon FloatingRefineryImage;
    public FloatingIcon FloatingFactoryImage;
    public FloatingIcon FloatingStarportImage;
    public FloatingIcon FloatingSupplyDepotImage;
    public FloatingIcon _currentIcon;

    public HashSet<BuildingController> FloatingBuildings;

    // TODO: poolify this
    public GameObject OverlapPrefab;
    private List<GameObject> _overlapMarkers;

    // Selected buildings behaviors
    public BuildingController SelectedBuilding;

    public GameObject BuildingInterface;
    public GameObject BarracksInterface;
    public GameObject FactoryInterface;
    public GameObject StarportInterface;
    public GameObject EmptyInterface;

    private GameObject _activeInterface;

    public PlayerManager PlayerManager;


    private void Start()
    {
        Grid.Init();
        GridWrapper = new InfrastructureGridWrapper(Grid);

        PlayerManager = GameObject.FindGameObjectWithTag(Util.Tags.PlayerManager).GetComponent<PlayerManager>();

        _overlapMarkers = new List<GameObject>();
        FloatingBuildings = new HashSet<BuildingController>();

        _activeInterface = BuildingInterface;
    }

    private void EnterNone()
    {
        _activeInterface.SetActive(false);
        _activeInterface = BuildingInterface;
        _activeInterface.SetActive(true);

        ChangeSelectedBuilding(null);
    }

    private BuildingPreview PreviewSprite(Infrastructure.BuildingType buildingType)
    {
        switch (buildingType)
        {
            case Infrastructure.BuildingType.CommandCenter:
                return CommandCenterPreview;
            case Infrastructure.BuildingType.EngineeringBay:
                return EngineeringBayPreview;
            case Infrastructure.BuildingType.Barracks:
                return BarracksPreview;
            case Infrastructure.BuildingType.Refinery:
                return RefineryPreview;
            case Infrastructure.BuildingType.Factory:
                return FactoryPreview;
            case Infrastructure.BuildingType.Starport:
                return StarportPreview;
            case Infrastructure.BuildingType.SupplyDepot:
                return SupplyDepotPreview;
        }

        return InvalidPreview;
    }

    public void SetupPlaceParameters(Infrastructure.BuildingType buildingType)
    {
        if (_activePreview != null)
        {
            _activePreview.gameObject.SetActive(false);
        }

        _activePreview = PreviewSprite(buildingType);
        _activePreview.gameObject.SetActive(true);
        _activeBuildingType = buildingType;

        SetPreviewPosition();
    }

    public void EnterPlaceFloatingBuilding(FloatingIcon icon)
    {
        _currentIcon = icon;
        SetupPlaceParameters(icon.BuildingType);
        InputState = Infrastructure.InputState.PlaceFloatingBuilding;
    }

    public void EnterPlaceBuildingState(Infrastructure.BuildingType buildingType)
    {
        SetupPlaceParameters(buildingType);
        InputState = Infrastructure.InputState.PlaceBuilding;
    }

    public void ResetPlacementParameters()
    {
        _activePreview.gameObject.SetActive(false);
        _activePreview = null;
        _activeBuildingType = Infrastructure.BuildingType.Invalid;
        InputState = Infrastructure.InputState.None;
    }

    private HashSet<BuildingController> GetOverlappedBuildings(Cell minCell)
    {
        var overlappedBuildings = new HashSet<BuildingController>();

        for (int row = 0; row < _activePreview.Rows; row++)
        {
            for (int col = 0; col < _activePreview.Columns; col++)
            {
                var cell = new Cell()
                {
                    Row = minCell.Row + row,
                    Column = minCell.Column + col
                };

                if (GridWrapper.IsOccupied(cell.Row, cell.Column))
                {
                    var building = GridWrapper.GetBuilding(cell);
                    if (building != null && !overlappedBuildings.Contains(building))
                    {
                        overlappedBuildings.Add(building);
                    }
                }
            }
        }

        return overlappedBuildings;
    }

    private void PreviewOverlaps(Cell minCell)
    {
        ClearOverlapMarkers();
        var overlappedBuildings = GetOverlappedBuildings(minCell);

        foreach (var overlappedBuilding in overlappedBuildings)
        {
            var cells = GridWrapper.GetBuildingCells(overlappedBuilding);
            while (cells.MoveNext())
            {
                var cell = cells.Current;
                var cellPosition = Grid.GetCellPosition(cell.Row, cell.Column);
                var overlap = Instantiate(OverlapPrefab);
                overlap.transform.position = cellPosition;
                _overlapMarkers.Add(overlap);
            }
        }
    }

    private void SetPreviewPosition()
    {
        // Get raw cell
        var center = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        PreviewCenter(out Cell cell, out Cell minCell, out Vector3 position);
        _activePreview.transform.position = position;
        PreviewOverlaps(minCell);
    }

    private int EvenMultiplier(int value)
    {
        return value & 1 ^ 1;
    }

    private void PreviewCenter(out Cell cell, out Cell minCell, out Vector3 position)
    {
        var center = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());

        var rowMulti = EvenMultiplier(_activePreview.Rows);
        var colMulti = EvenMultiplier(_activePreview.Columns);

        var rowShift = rowMulti * Grid.CellSize * 0.5f;
        var colShift = colMulti * Grid.CellSize * 0.5f;

        cell = Grid.GetCell(center, rowShift, colShift);

        // Clamp
        int rowOffset = _activePreview.Rows / 2;
        int minRow = rowOffset - rowMulti;
        int maxRow = Grid.Rows - rowOffset - 1;

        var colAdjustment = _activePreview.Columns & 0x1 ^ 0x1;
        int colOffset = _activePreview.Columns / 2;
        int minCol = colOffset - colMulti;
        int maxCol = Grid.Columns - colOffset - 1;

        cell.Row = Mathf.Clamp(cell.Row, minRow, maxRow);
        cell.Column = Mathf.Clamp(cell.Column, minCol, maxCol);

        position = Grid.GetCellPosition(cell.Row, cell.Column, rowShift, colShift);

        minCell.Row = cell.Row - minRow;
        minCell.Column = cell.Column - minCol;
    }

    private void ClearOverlapMarkers()
    {
        foreach (var overlapMarker in _overlapMarkers)
        {
            Destroy(overlapMarker.gameObject);
        }

        _overlapMarkers = new List<GameObject>();
    }

    private void PlaceBuilding()
    {
        PreviewCenter(out Cell center, out Cell minCell, out Vector3 position);
        var building = Instantiate(GetBuildingPrefab());
        building.transform.position = position;

        // 1. Remove overlapping
        ClearOverlapMarkers();
        var overlappedBuildings = GetOverlappedBuildings(minCell);
        GridWrapper.Remove(overlappedBuildings);

        foreach (var overlappedBuilding in overlappedBuildings)
        {
            // TODO: start floating animation routine
            FloatingBuildings.Add(overlappedBuilding);

            var controller = overlappedBuilding.GetComponent<BuildingController>();
            var image = Instantiate(GetFloatingImage(controller.BuildingType));
            image.Init(FloatingBar);

            Destroy(controller.gameObject);
        }

        // 2. Don't remove overlapping
        GridWrapper.AddBuildingController(building, _activePreview, minCell);
    }

    private FloatingIcon GetFloatingImage(Infrastructure.BuildingType buildingType)
    {
        switch (buildingType)
        {
            case Infrastructure.BuildingType.Barracks:
                return FloatingBarracksImage;
            case Infrastructure.BuildingType.CommandCenter:
                return FloatingCommandCenterImage;
            case Infrastructure.BuildingType.Factory:
                return FloatingFactoryImage;
            case Infrastructure.BuildingType.Refinery:
                return FloatingRefineryImage;
            case Infrastructure.BuildingType.Starport:
                return FloatingStarportImage;
            case Infrastructure.BuildingType.EngineeringBay:
                return FloatingEngineeringBayImage;
            case Infrastructure.BuildingType.SupplyDepot:
                return FloatingSupplyDepotImage;
            default:
                return null;
        }
    }

    private BuildingController GetBuildingPrefab()
    {
        switch (_activeBuildingType)
        {
            case Infrastructure.BuildingType.Barracks:
                return BarracksPrefab;
            case Infrastructure.BuildingType.CommandCenter:
                return CommandCenterPrefab;
            case Infrastructure.BuildingType.Factory:
                return FactoryPrefab;
            case Infrastructure.BuildingType.Refinery:
                return RefineryPrefab;
            case Infrastructure.BuildingType.Starport:
                return StarportPrefab;
            case Infrastructure.BuildingType.EngineeringBay:
                return EngineeringBayPrefab;
            case Infrastructure.BuildingType.SupplyDepot:
                return SupplyDepotPrefab;
            default:
                return null;
        }
    }

    private void ProcessPlaceFloatingBuilding()
    {
        if (EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        SetPreviewPosition();

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            PlaceBuilding();
            ResetPlacementParameters();
            Destroy(_currentIcon.gameObject);
        }
    }

    private void ProcessPlaceBuilding()
    {
        if (EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        SetPreviewPosition();

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            PlaceBuilding();
            ResetPlacementParameters();
        }
    }

    private void ProcessNone()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            var mousePosition = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            var overlap = Physics2D.OverlapPoint(mousePosition, LayerMask.GetMask(Util.Layers.BuildingLayer));

            if (overlap != null)
            {
                var building = overlap.GetComponent<BuildingController>();
                EnterSelectedBuilding(building);
            }
        }
    }

    private void ProcessSelectedBuilding()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame
                && !EventSystem.current.IsPointerOverGameObject())
        {
            var mousePosition = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            var overlap = Physics2D.OverlapPoint(mousePosition, LayerMask.GetMask(Util.Layers.BuildingLayer));

            if (overlap != null)
            {
                var building = overlap.GetComponent<BuildingController>();
                EnterSelectedBuilding(building);
            }
            else
            {
                EnterNone();
            }
        }
    }

    private UnitCosts GetUnitCosts(UnitType unitType)
    {
        switch (unitType)
        {
            case UnitType.Marine:
                return new UnitCosts()
                {
                    BuildCapacity = 1,
                    Mineral = 50,
                    Gas = 0,
                    Supply = 1
                };
            case UnitType.Ghost:
                return new UnitCosts()
                {
                    BuildCapacity = 1,
                    Mineral = 150,
                    Gas = 150,
                    Supply = 5
                };
            case UnitType.BattleCruiser:
                return new UnitCosts()
                {
                    BuildCapacity = 1,
                    Mineral = 300,
                    Gas = 300,
                    Supply = 5
                };
            case UnitType.Hellion:
                return new UnitCosts()
                {
                    BuildCapacity = 1,
                    Mineral = 100,
                    Gas = 50,
                    Supply = 2
                };
            case UnitType.Marauder:
                return new UnitCosts()
                {
                    BuildCapacity = 1,
                    Mineral = 50,
                    Gas = 50,
                    Supply = 2
                };
            case UnitType.Medivac:
                return new UnitCosts()
                {
                    BuildCapacity = 1,
                    Mineral = 50,
                    Gas = 50,
                    Supply = 1
                };
            case UnitType.Reaper:
                return new UnitCosts()
                {
                    BuildCapacity = 1,
                    Mineral = 50,
                    Gas = 50,
                    Supply = 1
                };
            case UnitType.SiegeTank:
                return new UnitCosts()
                {
                    BuildCapacity = 1,
                    Mineral = 150,
                    Gas = 150,
                    Supply = 3
                };
            default:
                return new UnitCosts();
        }
    }

    public void CreateUnit(UnitType unitType)
    {
        // Spend build
        var costs = GetUnitCosts(unitType);
        if (CanMakeUnit(costs))
        {
            SelectedBuilding.RemainingBuildCapacity -= costs.BuildCapacity;
            PlayerManager.MineralCount -= costs.Mineral;
            PlayerManager.GasCount -= costs.Gas;
            PlayerManager.RemainingSupply -= costs.Supply;

            PlayerManager.AddUnit(unitType);
            PlayerManager.UpdateText();
            SelectedBuilding.UpdateVisuals();
        }
    }

    private bool CanMakeUnit(UnitCosts cost)
    {
        return SelectedBuilding != null
            && SelectedBuilding.RemainingBuildCapacity >= cost.BuildCapacity
            && PlayerManager.MineralCount >= cost.Mineral
            && PlayerManager.GasCount >= cost.Gas
            && PlayerManager.RemainingSupply >= cost.Supply;
    }

    public void ChangeSelectedBuilding(BuildingController building)
    {
        if (SelectedBuilding != null)
        {
            SelectedBuilding.Deselect();
        }

        if (building != null)
        {
            SelectedBuilding = building;
            SelectedBuilding.Select();
        }
    }

    private void EnterSelectedBuilding(BuildingController building)
    {
        ChangeSelectedBuilding(building);
        InputState = Infrastructure.InputState.SelectedBuilding;

        _activeInterface.gameObject.SetActive(false);

        switch (SelectedBuilding.BuildingType)
        {
            case Infrastructure.BuildingType.Barracks:
                _activeInterface = BarracksInterface;
                break;
            case Infrastructure.BuildingType.Factory:
                _activeInterface = FactoryInterface;
                break;
            case Infrastructure.BuildingType.Starport:
                _activeInterface = StarportInterface;
                break;
            default:
                _activeInterface = EmptyInterface;
                break;
        }

        _activeInterface.gameObject.SetActive(true);
    }

    private void Update()
    {
        if (InputState == Infrastructure.InputState.None)
        {
            ProcessNone();
        }
        else if (InputState == Infrastructure.InputState.PlaceBuilding)
        {
            ProcessPlaceBuilding();
        }
        else if (InputState == Infrastructure.InputState.PlaceFloatingBuilding)
        {
            ProcessPlaceFloatingBuilding();
        }
        else if (InputState == Infrastructure.InputState.SelectedBuilding)
        {
            ProcessSelectedBuilding();
        }
    }

    private void OnDrawGizmos()
    {
        /* if (GridWrapper != null)
        {
            for (int row = 0; row < Grid.Rows; row++)
            {
                for (int col = 0; col < Grid.Columns; col++)
                {
                    var cellPosition = Grid.GetCellPosition(row, col);

                    if (GridWrapper.IsOccupied(row, col))
                    {
                        Gizmos.color = Color.green;
                    }
                    else
                    {
                        Gizmos.color = Color.black;
                    }

                    Gizmos.DrawWireCube(cellPosition, new Vector3(Grid.CellSize, Grid.CellSize, 0));
                }
            }
        } */
    }
}

public struct UnitCosts
{
    public int Mineral;
    public int Gas;
    public int BuildCapacity;
    public int Supply;
}
