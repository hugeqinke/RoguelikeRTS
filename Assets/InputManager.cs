using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
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
        else if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            Debug.Log("Pressed action");
            IssueOrder();
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

    private void ResetKinematics(IEnumerable<GameObject> units)
    {
        foreach (var unit in units)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();
            unitComponent.Kinematic.Velocity = Vector3.zero;
        }
    }

    private Dictionary<GameObject, Vector3> CalculateTargetPositions(Vector3 targetCenter)
    {
        // calculate the bounding box that contains all units
        var minX = Mathf.Infinity;
        var minUnitPosX = Mathf.Infinity;

        var minZ = Mathf.Infinity;
        var minUnitPosZ = Mathf.Infinity;

        var maxX = -Mathf.Infinity;
        var maxUnitPosX = -Mathf.Infinity;

        var maxZ = -Mathf.Infinity;
        var maxUnitPosZ = -Mathf.Infinity;

        var rotation = Camera.main.transform.rotation;

        foreach (var unit in _selectedUnits)
        {
            var position = rotation * unit.transform.position;

            if (position.x < minX)
            {
                minUnitPosX = unit.transform.position.x;
                minX = position.x;
            }

            if (position.x > maxX)
            {
                maxUnitPosX = unit.transform.position.x;
                maxX = position.x;
            }

            if (position.z < minZ)
            {
                minUnitPosZ = unit.transform.position.z;
                minZ = position.z;
            }

            if (position.z > maxZ)
            {
                maxUnitPosZ = unit.transform.position.z;
                maxZ = position.z;
            }
        }

        var minPoint = new Vector3(minUnitPosX, 0, minUnitPosZ);
        var maxPoint = new Vector3(maxUnitPosX, 0, maxUnitPosZ);

        var v1 = new Vector3(minPoint.x, 0, maxPoint.z);
        var v2 = new Vector3(maxPoint.x, 0, minPoint.z);

#if UNITY_EDITOR
        Debug.DrawLine(minPoint, v1, Color.magenta, 10);
        Debug.DrawLine(v1, maxPoint, Color.magenta, 10);
        Debug.DrawLine(maxPoint, v2, Color.magenta, 10);
        Debug.DrawLine(v2, minPoint, Color.magenta, 10);

        Debug.Log(v1 + " " + v2 + " " + minPoint + " " + maxPoint);
#endif


        // Don't do full camera rotation because I want to assume that the bounding box
        // is flat
        var cameraRotation = Quaternion.Euler(0, Camera.main.transform.rotation.y, 0);
        var relativeTargetCenter = targetCenter - minPoint;
        relativeTargetCenter = cameraRotation * relativeTargetCenter;

        var width = Mathf.Abs(maxPoint.x - minPoint.x);
        var height = Mathf.Abs(maxPoint.z - minPoint.z);

        if (
            relativeTargetCenter.x <= width
            && relativeTargetCenter.x >= 0
            && relativeTargetCenter.z <= height
            && relativeTargetCenter.z >= 0)
        {
            // Handle inner click
            return null;
        }
        else
        {
            return CalculateOuterPosition(targetCenter, minPoint, maxPoint);
        }
    }

    private Dictionary<GameObject, Vector3> CalculateInnerPosition(Vector3 targetCenter)
    {
        var unitCount = _selectedUnits.Count;
        var columnCount = Mathf.Sqrt(unitCount);

        // asssume tight positioning
        var minUnitSize = Mathf.NegativeInfinity;
        foreach (var unit in _selectedUnits)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();
            if (unitComponent.Radius < minUnitSize)
            {
                minUnitSize = unitComponent.Radius;
            }
        }

        var topLeft =
        for (int i = 0; i < columnCount; i++)
        {

        }
    }

    private Dictionary<GameObject, Vector3> CalculateOuterPosition(
            Vector3 targetCenter,
            Vector3 minPoint,
            Vector3 maxPoint)
    {
        var center = (minPoint + maxPoint) * 0.5f;
        var result = new Dictionary<GameObject, Vector3>();

        foreach (var unit in _selectedUnits)
        {
            var relative = unit.transform.position - center;
            var position = targetCenter + relative;
            result.Add(unit, position);
        }

        return result;
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
            TopPlane = new MathUtil.Plane(Vector3.Cross(camera.transform.right, maxDir), camera.transform.position),
            BottomPlane = new MathUtil.Plane(Vector3.Cross(minDir, camera.transform.right), camera.transform.position)
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
    private void IssueOrder()
    {
        // Reset the states of all selected units
        ResetKinematics(_selectedUnits);

        var ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hitInfo, 600, InputUtils.GroundLayerMask))
        {
            // Calculate target positions
            var positions = CalculateTargetPositions(hitInfo.point);

            // Update target positions
            foreach (var unit in _selectedUnits)
            {
                var unitComponent = unit.FetchComponent<UnitComponent>();
                unitComponent.BasicMovement.TargetPosition = positions[unit];
            }
        }
    }

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
    // WARNING: this won't reset if i didn't reset domain and
    // I change the layer names in the Editor
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

    public static LayerMask GroundLayerMask
    {
        get
        {
            if (_groundLayerMask == null)
            {
                _groundLayerMask = LayerMask.GetMask(Util.Layers.GroundLayer);
            }

            return _groundLayerMask.Value;
        }
    }
    private static LayerMask? _groundLayerMask;

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
        public static string GroundLayer = "Ground";
    }

    public static class Tags
    {
        public static string PlayerUnit = "PlayerUnit";
    }
}
