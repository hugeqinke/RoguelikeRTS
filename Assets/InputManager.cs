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
    private InputState _currentInputState;
    private SelectingContext _selectingContext;
    private BoxSelectContext _boxSelectContext;

    public Simulator Simulator;

    // Start is called before the first frame update
    void Start()
    {
        _selectedUnits = new HashSet<GameObject>();
    }

    // Update is called once per frame
    void Update()
    {
        switch (_currentInputState)
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

        if (Keyboard.current.hKey.wasPressedThisFrame)
        {
            // Hold position
            foreach (var unit in _selectedUnits)
            {
                var unitComponent = Simulator.UnitControllers[unit];
                // Simulator.Instance.ForceStop(unit);
                // unitComponent.BasicMovement.HoldingPosition = true;
            }
        }
    }

    // State processors
    private void EnterSelectingState()
    {
        _selectingContext = new SelectingContext()
        {
            StartPosition = Mouse.current.position.ReadValue()
        };

        _currentInputState = InputState.Selecting;
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
        _currentInputState = InputState.Idle;
    }

    private void ProcessIdleState()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            EnterSelectingState();
        }
        else if (Mouse.current.rightButton.wasPressedThisFrame)
        {
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

        _currentInputState = InputState.BoxSelect;
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

            foreach (var playerUnitIdx in Simulator.PlayerIndexMap)
            {
                var playerUnit = playerUnitIdx.Key;
                if (IsOnFrustrum(frustrum, playerUnit))
                {
                    AddToSelection(playerUnit);
                }
            }

            BoxRenderer.gameObject.SetActive(false);
            EnterIdleState();
        }
    }

    private Dictionary<GameObject, Vector3> CalculateTargetPositions(Vector3 targetCenter)
    {
        // calculate the magic box that contains all units
        var minPoint = new Vector2(Mathf.Infinity, Mathf.Infinity);
        var maxPoint = new Vector2(Mathf.NegativeInfinity, Mathf.NegativeInfinity);

        var rads = -Mathf.Deg2Rad * Camera.main.transform.rotation.eulerAngles.y;

        var basisSpace = new float2x2(
            Mathf.Cos(rads), -Mathf.Sin(rads),
            Mathf.Sin(rads), Mathf.Cos(rads)
        );

        var inverseBasisSpace = math.inverse(basisSpace);

        foreach (var unit in _selectedUnits)
        {
            var unitController = Simulator.UnitControllers[unit];
            var unitPosition = math.mul(inverseBasisSpace, new float2(unit.transform.position.x, unit.transform.position.z));

            var points = new List<float2>()
            {
                unitPosition + new float2(0, 1) * unitController.Radius,
                unitPosition - new float2(0, 1) * unitController.Radius,
                unitPosition + new float2(1, 0) * unitController.Radius,
                unitPosition - new float2(1, 0) * unitController.Radius
            };

            // Debug.DrawRay(Vector3.zero, unit.transform.position, Color.blue, 10);
            // Debug.DrawRay(Vector3.zero, new Vector3(points[0].x, 0, points[0].y), Color.magenta, 10);

            foreach (var point in points)
            {
                if (point.x > maxPoint.x)
                {
                    maxPoint.x = point.x;
                }

                if (point.x < minPoint.x)
                {
                    minPoint.x = point.x;
                }

                if (point.y > maxPoint.y)
                {
                    maxPoint.y = point.y;
                }

                if (point.y < minPoint.y)
                {
                    minPoint.y = point.y;
                }
            }
        }

        // Debug.DrawRay(Vector3.zero, new Vector3(minPoint.x, 0, minPoint.y), Color.red, 10);
        // Debug.DrawRay(Vector3.zero, new Vector3(maxPoint.x, 0, maxPoint.y), Color.green, 10);

        var tempMin = math.mul(basisSpace, minPoint);
        var minWorldPoint = new Vector3(tempMin.x, 0, tempMin.y);

        var tempMax = math.mul(basisSpace, maxPoint);
        var maxWorldPoint = new Vector3(tempMax.x, 0, tempMax.y);

        var tempv1 = math.mul(basisSpace, new float2(minPoint.x, maxPoint.y));
        var v1 = new Vector3(tempv1.x, 0, tempv1.y);

        var tempv2 = math.mul(basisSpace, new float2(maxPoint.x, minPoint.y));
        var v2 = new Vector3(tempv2.x, 0, tempv2.y);


#if UNITY_EDITOR
        // Debug.DrawLine(Vector3.zero, minWorldPoint, Color.black, 10);
        // Debug.DrawLine(Vector3.zero, maxWorldPoint, Color.black, 10);

        // Debug.DrawLine(minWorldPoint, v1, Color.magenta, 10);
        // Debug.DrawLine(v1, maxWorldPoint, Color.magenta, 10);
        // Debug.DrawLine(maxWorldPoint, v2, Color.magenta, 10);
        // Debug.DrawLine(v2, minWorldPoint, Color.magenta, 10);

        // Debug.Log(v1 + " " + v2 + " " + minPoint + " " + maxPoint);
#endif

        // Don't do full camera rotation because I want to assume that the bounding box
        // is flat
        var cameraRotation = Quaternion.Euler(0, Camera.main.transform.rotation.y, 0);
        var relativeTargetCenter = targetCenter - minWorldPoint;
        relativeTargetCenter = cameraRotation * relativeTargetCenter;

        var width = Mathf.Abs(maxWorldPoint.x - minWorldPoint.x);
        var height = Mathf.Abs(maxWorldPoint.z - minWorldPoint.z);

        if (
            relativeTargetCenter.x <= width
            && relativeTargetCenter.x >= 0
            && relativeTargetCenter.z <= height
            && relativeTargetCenter.z >= 0)
        {
            // Handle inner click
            return CalculateInnerPosition(targetCenter);
        }
        else
        {
            return CalculateOuterPosition(targetCenter, minWorldPoint, maxWorldPoint);
        }
    }

    private Dictionary<GameObject, Vector3> CalculateInnerPosition(Vector3 targetCenter)
    {
        var result = new Dictionary<GameObject, Vector3>();
        foreach (var unit in _selectedUnits)
        {
            result.Add(unit, targetCenter);
        }
        return result;
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
        var position = unit.transform.position;

        var inFrustrum = false;
        var unitController = Simulator.UnitControllers[unit];
        foreach (var trigger in unitController.SelectionTriggers)
        {
            var center = position + trigger.center;
            var radius = unitController.transform.localScale.x * trigger.radius;
            inFrustrum |= MathUtil.OnPositiveHalfPlane(frustrum.LeftPlane, center, radius)
                        && MathUtil.OnPositiveHalfPlane(frustrum.RightPlane, center, radius)
                        && MathUtil.OnPositiveHalfPlane(frustrum.BottomPlane, center, radius)
                        && MathUtil.OnPositiveHalfPlane(frustrum.TopPlane, center, radius)
                        && MathUtil.OnPositiveHalfPlane(frustrum.FarPlane, center, radius)
                        && MathUtil.OnPositiveHalfPlane(frustrum.NearPlane, center, radius);
        }

        return inFrustrum;
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

    // private bool IssueAttackOrder()
    // {
    //     ResetKinematics(_selectedUnits);

    //     var screenPoint = Mouse.current.position.ReadValue();
    //     var ray = Camera.main.ScreenPointToRay(screenPoint);

    //     if (Physics.Raycast(ray, out RaycastHit hit, InputUtils.MaxRaycastDistance, InputUtils.AIUnitLayerMask))
    //     {
    //         var enemyUnit = hit.collider.gameObject;

    //         // Set move
    //         var positions = CalculateInnerPosition(enemyUnit.transform.position);
    //         SetMoveParameters(positions);

    //         // Set combat
    //         foreach (var unit in _selectedUnits)
    //         {
    //             var unitComponent = Simulator.UnitControllers[unit];
    //             unitComponent.Target = enemyUnit;
    //         }

    //         return true;
    //     }

    //     return false;
    // }

    // private void SetMoveParameters(Dictionary<GameObject, Vector3> positions)
    // {
    //     var moveGroup = new MoveGroup();
    //     foreach (var unit in _selectedUnits)
    //     {
    //         // Update unit state
    //         var unitComponent = Simulator.UnitControllers[unit];
    //         unitComponent.BasicMovement.TargetPosition = positions[unit];
    //         unitComponent.BasicMovement.RelativeDeltaStart = positions[unit] - unit.transform.position;

    //         var dir = positions[unit] - unit.transform.position;
    //         if (dir.sqrMagnitude > Mathf.Epsilon)
    //         {
    //             unitComponent.Kinematic.Orientation = Vector3.SignedAngle(
    //                 Vector3.forward,
    //                 dir,
    //                 Vector3.up
    //             );
    //         }

    //         // Clear from old movegroup
    //         if (MoveGroupMap.ContainsKey(unit))
    //         {
    //             var oldMoveGroup = MoveGroupMap[unit];
    //             if (oldMoveGroup.Units.Contains(unit))
    //             {
    //                 oldMoveGroup.Units.Remove(unit);
    //             }

    //             MoveGroupMap.Remove(unit);
    //         }

    //         // Add to new MoveGroup
    //         moveGroup.Units.Add(unit);
    //         MoveGroupMap.Add(unit, moveGroup);
    //     }

    //     MoveGroups.Add(moveGroup);
    // }

    private void IssueMoveOrder()
    {
        // Reset the states of all selected units
        Simulator.ResetMovement(_selectedUnits);

        var ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hitInfo, 600, InputUtils.GroundLayerMask))
        {
            var point = hitInfo.point;
            point.y = 0;

            // Update target positions
            Simulator.SetMovementValues(CalculateTargetPositions(point));
            Simulator.MoveGroupPool.Assign(Simulator, _selectedUnits);
        }
    }

    private bool RaycastHitEnemyUnit(out GameObject enemyUnit)
    {
        var screenPoint = Mouse.current.position.ReadValue();
        var ray = Camera.main.ScreenPointToRay(screenPoint);

        if (Physics.Raycast(ray, out RaycastHit hit, InputUtils.MaxRaycastDistance, InputUtils.AIUnitLayerMask))
        {
            enemyUnit = hit.collider.gameObject;
            return true;
        }

        enemyUnit = null;
        return false;
    }

    // Helper functions
    private void IssueOrder()
    {
        // if (!IssueAttackOrder())
        // {
        IssueMoveOrder();
        // }
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
        var unitComponent = Simulator.UnitControllers[unit];
        unitComponent.SelectionHighlight.gameObject.SetActive(false);
    }

    public void AddToSelection(GameObject unit)
    {
        if (!_selectedUnits.Contains(unit))
        {
            _selectedUnits.Add(unit);

            var unitComponent = Simulator.UnitControllers[unit];
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

    public static LayerMask AIUnitLayerMask
    {
        get
        {
            if (_aiUnitLayerMask == null)
            {
                _aiUnitLayerMask = LayerMask.GetMask(Util.Layers.AIUnit);
            }

            return _aiUnitLayerMask.Value;
        }
    }
    private static LayerMask? _aiUnitLayerMask;

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
    public static bool OnPositiveHalfPlane(MathUtil.Plane plane, Vector3 position, float radius)
    {
        var distanceToPlane = Vector3.Dot(plane.Normal, position) - plane.Distance;
        return distanceToPlane > -radius;
    }

    public static bool OnPositiveHalfPlane(MathUtil.Plane plane, float3 position, float radius)
    {
        var distanceToPlane = math.dot(plane.Normal, position) - plane.Distance;
        return distanceToPlane > -radius;
    }

    public static float NormalizeOrientation(float value)
    {
        return NormalizeAngle(value, 0, 360);
    }

    public static float NormalizeAngle(float value, float start, float end)
    {
        var width = end - start;
        var offsetValue = value - start;

        return (offsetValue - (Mathf.Floor(offsetValue / width) * width)) + start;
    }

    public static float Determinant2D(Vector3 v1, Vector3 v2)
    {
        return v1.x * v2.z - v1.z * v2.x;
    }

    public static float LeftOf(Vector3 a, Vector3 b, Vector3 c)
    {
        return Determinant2D(a - c, b - a);
    }

    public static float Determinant2D(float3 v1, float3 v2)
    {
        return v1.x * v2.z - v1.z * v2.x;
    }

    public static float LeftOf(float3 a, float3 b, float3 c)
    {
        return Determinant2D(a - c, b - a);
    }

    public struct Plane
    {
        public float3 Normal;
        public float Distance; // Distance from origin

        public Plane(float3 dir, float3 inPoint)
        {
            Normal = math.normalizesafe(dir);
            Distance = math.dot(Normal, inPoint);
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
        public static string AIUnit = "AIUnit";
        public static string GroundLayer = "Ground";

        private static LayerMask _playerAndAIUnitMask;
        public static LayerMask PlayerAndAIUnitMask
        {
            get
            {
                return LayerMask.GetMask(PlayerUnitLayer, AIUnit);
            }
        }
    }

    public static class Tags
    {
        public static string PlayerUnit = "PlayerUnit";
    }

    public static void DrawBox(Vector3 pos, Quaternion rot, Vector3 scale, Color c, float duration)
    {
        // create matrix
        Matrix4x4 m = new Matrix4x4();
        m.SetTRS(pos, rot, scale);

        var point1 = m.MultiplyPoint(new Vector3(-0.5f, -0.5f, 0.5f));
        var point2 = m.MultiplyPoint(new Vector3(0.5f, -0.5f, 0.5f));
        var point3 = m.MultiplyPoint(new Vector3(0.5f, -0.5f, -0.5f));
        var point4 = m.MultiplyPoint(new Vector3(-0.5f, -0.5f, -0.5f));

        var point5 = m.MultiplyPoint(new Vector3(-0.5f, 0.5f, 0.5f));
        var point6 = m.MultiplyPoint(new Vector3(0.5f, 0.5f, 0.5f));
        var point7 = m.MultiplyPoint(new Vector3(0.5f, 0.5f, -0.5f));
        var point8 = m.MultiplyPoint(new Vector3(-0.5f, 0.5f, -0.5f));

        Debug.DrawLine(point1, point2, c, duration);
        Debug.DrawLine(point2, point3, c, duration);
        Debug.DrawLine(point3, point4, c, duration);
        Debug.DrawLine(point4, point1, c, duration);

        Debug.DrawLine(point5, point6, c, duration);
        Debug.DrawLine(point6, point7, c, duration);
        Debug.DrawLine(point7, point8, c, duration);
        Debug.DrawLine(point8, point5, c, duration);

        Debug.DrawLine(point1, point5, c, duration);
        Debug.DrawLine(point2, point6, c, duration);
        Debug.DrawLine(point3, point7, c, duration);
        Debug.DrawLine(point4, point8, c, duration);
    }
}
