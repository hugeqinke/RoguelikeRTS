using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum InputState
{
    Idle,
    Selecting,
    BoxSelect
}

public class InputManager : MonoBehaviour
{
    public BoxRenderer BoxRenderer;

    private HashSet<GameObject> _selectedUnits;
    private InputState CurrentInputState;
    private SelectingContext _selectingContext;
    private BoxSelectContext _boxSelectContext;

    // Start is called before the first frame update
    void Start()
    {
        _selectedUnits = new HashSet<GameObject>();
    }

    // Update is called once per frame
    void Update()
    {
        switch (CurrentInputState)
        {
            case InputState.Idle:
                ProcessIdleState();
                break;
            case InputState.Selecting:
                ProcessSelectingState();
                break;
            case InputState.BoxSelect:
                ProcessBoxSelect();
                break;
        }
    }

    // State processors
    private void EnterSelectingState()
    {
        _selectingContext = new SelectingContext()
        {
            StartPosition = Mouse.current.position.ReadValue()
        };

        CurrentInputState = InputState.Selecting;
    }

    private void ProcessSelectingState()
    {
        if (Mouse.current.leftButton.isPressed)
        {
            Vector2 mousePosition = Mouse.current.position.ReadValue();
            var sqrLength = (mousePosition - _selectingContext.StartPosition).sqrMagnitude;

            if (sqrLength > Mathf.Epsilon)
            {
                EnterBoxSelect(_selectingContext.StartPosition);
            }
        }
        else
        {
            ClearCurrentSelection();

            var unit = InputUtils.ScanUnit(_selectingContext.StartPosition);
            if (unit != null)
            {
                AddToSelection(unit);
            }

            EnterIdleState();
        }
    }

    private void EnterIdleState()
    {
        CurrentInputState = InputState.Idle;
    }

    private void ProcessIdleState()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            EnterSelectingState();
        }
    }

    private void EnterBoxSelect(Vector2 startPosition)
    {
        _boxSelectContext = new BoxSelectContext()
        {
            StartPosition = startPosition,
            EndPosition = Mouse.current.position.ReadValue()
        };

        BoxRenderer.gameObject.SetActive(true);
        BoxRenderer.StartPosition = _boxSelectContext.StartPosition;
        BoxRenderer.EndPosition = _boxSelectContext.EndPosition;
        BoxRenderer.SetVerticesDirty();

        CurrentInputState = InputState.BoxSelect;
    }

    private void ProcessBoxSelect()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            _boxSelectContext.StartPosition = Mouse.current.position.ReadValue();
            BoxRenderer.StartPosition = _boxSelectContext.StartPosition;
            BoxRenderer.SetVerticesDirty();
        }

        if (Mouse.current.leftButton.isPressed)
        {
            _boxSelectContext.EndPosition = Mouse.current.position.ReadValue();
            BoxRenderer.EndPosition = _boxSelectContext.EndPosition;
            BoxRenderer.SetVerticesDirty();

            // DBG Convert nearpoint box to farpoint 
        }
        else
        {
            // Clear currently selected units
            ClearCurrentSelection();

            var frustrum = CreateFrustrum();
            var playerUnits = Simulator.Entity.Fetch(new List<System.Type>() { typeof(PlayerComponent) });

            var selectedUnits = new List<GameObject>();
            foreach (var unit in playerUnits)
            {
                if (IsOnFrustrum(frustrum, unit))
                {
                    AddToSelection(unit);
                }
            }

            BoxRenderer.gameObject.SetActive(false);
            EnterIdleState();
        }
    }

    private bool IsOnFrustrum(MathUtil.Frustrum frustrum, GameObject unit)
    {
        var unitComponent = unit.FetchComponent<UnitComponent>();
        var position = unit.transform.position;

        var inFrustrum = false;
        foreach (var trigger in unitComponent.SelectionTriggers)
        {
            var center = position + trigger.center;
            var radius = unitComponent.transform.localScale.x * trigger.radius;
            inFrustrum |= OnPositiveHalfPlane(frustrum.LeftPlane, center, radius)
                        && OnPositiveHalfPlane(frustrum.RightPlane, center, radius)
                        && OnPositiveHalfPlane(frustrum.BottomPlane, center, radius)
                        && OnPositiveHalfPlane(frustrum.TopPlane, center, radius)
                        && OnPositiveHalfPlane(frustrum.FarPlane, center, radius)
                        && OnPositiveHalfPlane(frustrum.NearPlane, center, radius);
        }

        return inFrustrum;
    }

    private bool OnPositiveHalfPlane(MathUtil.Plane plane, Vector3 position, float radius)
    {
        var distanceToPlane = Vector3.Dot(plane.Normal, position) - plane.Distance;
        return distanceToPlane > -radius;
    }

    private MathUtil.Frustrum CreateFrustrum()
    {
        var camera = Camera.main;
        var farPlane = new Plane(-camera.transform.forward, camera.transform.position + camera.transform.forward * camera.farClipPlane);

        // Construct min and max bounding rays
        var minPoint = new Vector2(
            Mathf.Min(_boxSelectContext.StartPosition.x, _boxSelectContext.EndPosition.x),
            Mathf.Min(_boxSelectContext.StartPosition.y, _boxSelectContext.EndPosition.y)
        );

        var minRay = Camera.main.ScreenPointToRay(minPoint);
        farPlane.Raycast(minRay, out float minEnter);
        var minDir = minRay.direction.normalized * minEnter;

        // NOTE: Not sure if scaling is necessary here - maybe leaving the ray as is without
        // the scaling works too (as in it'll create the same planes)
        var maxPoint = new Vector2(
            Mathf.Max(_boxSelectContext.StartPosition.x, _boxSelectContext.EndPosition.x),
            Mathf.Max(_boxSelectContext.StartPosition.y, _boxSelectContext.EndPosition.y)
        );

        var maxRay = Camera.main.ScreenPointToRay(maxPoint);
        farPlane.Raycast(maxRay, out float maxEnter);
        var maxDir = maxRay.direction.normalized * maxEnter;

        var frustrum = new MathUtil.Frustrum()
        {
            NearPlane = new MathUtil.Plane(camera.transform.forward, camera.transform.position + camera.transform.forward * camera.nearClipPlane),
            FarPlane = new MathUtil.Plane(-camera.transform.forward, camera.transform.position + camera.transform.forward * camera.farClipPlane),
            RightPlane = new MathUtil.Plane(Vector3.Cross(maxDir, camera.transform.up), camera.transform.position),
            LeftPlane = new MathUtil.Plane(Vector3.Cross(camera.transform.up, minDir), camera.transform.position),
            TopPlane = new MathUtil.Plane(Vector3.Cross(Vector3.right, maxDir), camera.transform.position),
            BottomPlane = new MathUtil.Plane(Vector3.Cross(minDir, Vector3.right), camera.transform.position)
        };

        // #region DEBUG
        // // Debug
        // Debug.DrawRay(camera.transform.position, frustrum.NearPlane.Normal, Color.yellow);
        // Debug.DrawRay(camera.transform.position, frustrum.FarPlane.Normal, Color.blue);
        // Debug.DrawRay(camera.transform.position, frustrum.LeftPlane.Normal, Color.red);
        // Debug.DrawRay(camera.transform.position, frustrum.RightPlane.Normal, Color.green);
        // Debug.DrawRay(camera.transform.position, frustrum.TopPlane.Normal, Color.black);
        // Debug.DrawRay(camera.transform.position, frustrum.BottomPlane.Normal, Color.white);

        // var vertices = new List<Vector2>()
        // {
        //     _boxSelectContext.StartPosition,
        //     new Vector2(_boxSelectContext.StartPosition.x, _boxSelectContext.EndPosition.y),
        //     new Vector2(_boxSelectContext.EndPosition.x, _boxSelectContext.StartPosition.y),
        //     _boxSelectContext.EndPosition,
        // };

        // foreach (var vertex in vertices)
        // {
        //     var ray = Camera.main.ScreenPointToRay(vertex);
        //     var plane = new Plane(Camera.main.transform.forward, Camera.main.transform.position + Camera.main.transform.forward * Camera.main.farClipPlane);

        //     plane.Raycast(ray, out float dist);
        //     Debug.DrawLine(Camera.main.transform.position, ray.origin + ray.direction * dist, Color.magenta);
        // }
        // #endregion DEBUG

        return frustrum;
    }

    // Helper functions
    private void ClearCurrentSelection()
    {
        foreach (var unit in _selectedUnits)
        {
            DeselectUnit(unit);
        }

        _selectedUnits = new HashSet<GameObject>();
    }

    private void DeselectUnit(GameObject unit)
    {
        var unitComponent = unit.FetchComponent<UnitComponent>();
        unitComponent.SelectionHighlight.gameObject.SetActive(false);
    }

    public void AddToSelection(GameObject unit)
    {
        if (!_selectedUnits.Contains(unit))
        {
            _selectedUnits.Add(unit);

            var unitComponent = unit.FetchComponent<UnitComponent>();
            unitComponent.SelectionHighlight.gameObject.SetActive(true);
        }
    }
}

public class BoxSelectContext
{
    public Vector2 StartPosition;
    public Vector2 EndPosition;
}

public class SelectingContext
{
    public Vector2 StartPosition;
}

public static class InputUtils
{
    public static float MaxRaycastDistance = 100;
    public static LayerMask PlayerUnitLayerMask
    {
        get
        {
            if (_unitSelectionLayerMask == null)
            {
                _unitSelectionLayerMask = LayerMask.GetMask(Util.Layers.PlayerUnitLayer);
            }

            return _unitSelectionLayerMask.Value;
        }
    }

    private static LayerMask? _unitSelectionLayerMask;

    public static GameObject ScanUnit(Vector3 screenPoint)
    {
        var ray = Camera.main.ScreenPointToRay(screenPoint);
        if (Physics.Raycast(ray, out RaycastHit hit, MaxRaycastDistance, PlayerUnitLayerMask))
        {
            var unit = hit.collider.gameObject;
            return unit;
        }

        return null;
    }
}

public static class MathUtil
{
    public class Plane
    {
        public Vector3 Normal;
        public float Distance; // Distance from origin

        public Plane(Vector3 dir, Vector3 inPoint)
        {
            Normal = dir.normalized;
            Distance = Vector3.Dot(Normal, inPoint);
        }
    }

    public class Frustrum
    {
        public Plane FarPlane;
        public Plane NearPlane;
        public Plane RightPlane;
        public Plane LeftPlane;
        public Plane TopPlane;
        public Plane BottomPlane;
    }
}

public static class Util
{
    public static class Layers
    {
        public static string PlayerUnitLayer = "PlayerUnit";
    }

    public static class Tags
    {
        public static string PlayerUnit = "PlayerUnit";
    }
}
