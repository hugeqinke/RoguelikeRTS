using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine.Diagnostics;

namespace RoguelikeRTS
{
    public class Simulator : MonoBehaviour
    {
        public float NeighborScanRadius;
        public float ResponseCoefficient;

        public InputManager InputManager;
        public static Entity Entity;
        public static Simulator Instance;

        public int Iterations;

        public float MovingNeighborRadius;
        private static float rotateAmount = 30f;

        public float AlertRadius;

        private void Awake()
        {
            Entity = new Entity();
            Instance = this;
        }

        // Start is called before the first frame update
        void Start()
        {
            SetupEnvironment();
        }

        private void CategorizeUnits(
                out HashSet<GameObject> unresolvedUnits,
                out HashSet<GameObject> resolvedUnits)
        {
            unresolvedUnits = new HashSet<GameObject>();
            resolvedUnits = new HashSet<GameObject>();

            // Units main processing
            var units = Entity.Fetch(new List<System.Type>()
            {
                typeof(UnitComponent)
            });

            foreach (var unit in units)
            {
                var unitComponent = unit.FetchComponent<UnitComponent>();
                // if (unitComponent.Attacking)
                // {
                //     continue;
                // }

                if (!unitComponent.BasicMovement.Resolved)
                {
                    unresolvedUnits.Add(unit);
                }
                else
                {
                    resolvedUnits.Add(unit);
                }
            }
        }

        private void CheckCombatState()
        {
            var units = Entity.Fetch(new List<System.Type>()
            {
                typeof(UnitComponent)
            });

            foreach (var unit in units)
            {
                var unitComponent = unit.FetchComponent<UnitComponent>();
                unitComponent.Attacking = false;
                unitComponent.InAlertRange = false;

                if (unitComponent.Target != null)
                {
                    var relativeDir = unitComponent.Target.transform.position - unit.transform.position;
                    var sqrDst = relativeDir.sqrMagnitude;
                    var targetUnitComponent = unitComponent.Target.FetchComponent<UnitComponent>();

                    // Check if should switch Attack targets
                    // TODO: Can just use opposing layer mask

                    // Check if in alert radius - if in alert radius, we'll do special stuff
                    var alertRadius = AlertRadius + unitComponent.Radius + targetUnitComponent.Radius;
                    if (sqrDst < alertRadius * alertRadius)
                    {
                        unitComponent.InAlertRange = true;
                    }

                    // Check if can attack

                    var radius = unitComponent.AttackRadius
                        + unitComponent.Radius
                        + targetUnitComponent.Radius;


                    if (sqrDst < radius * radius)
                    {
                        // TODO: I want a centralized area to update velocity here, rather
                        // than scattering it willy nilly
                        unitComponent.Kinematic.Velocity = Vector3.zero;
                        unitComponent.Attacking = true;
                    }

                    if (!unitComponent.Attacking)
                    {
                        var overlaps = Physics.OverlapSphere(unit.transform.position, AlertRadius + unitComponent.Radius, Util.Layers.PlayerAndAIUnitMask);
                        var obstacles = CombatClearPath(unit, Mathf.Sqrt(sqrDst));

                        GameObject nearTarget = null;
                        var nearSqrDst = Mathf.Infinity;
                        foreach (var overlap in overlaps)
                        {
                            var overlapUnitComponent = overlap.gameObject.FetchComponent<UnitComponent>();
                            if (overlap.gameObject == unitComponent.Target || overlapUnitComponent.Owner == unitComponent.Owner)
                            {
                                continue;
                            }

                            // Check if this unit is with an acceptable radius to the attack target
                            var dir = overlap.gameObject.transform.position - unit.transform.position;
                            var retargetRadius = AlertRadius + overlapUnitComponent.Radius + unitComponent.Radius;
                            if (dir.sqrMagnitude < retargetRadius * retargetRadius)
                            {
                                // check if there's no clear path to the current attack target
                                if (obstacles.Count != 0)
                                {
                                    if (dir.sqrMagnitude < nearSqrDst)
                                    {
                                        nearTarget = overlap.gameObject;
                                        nearSqrDst = dir.sqrMagnitude;
                                    }
                                }
                            }
                        }

                        if (nearTarget != null)
                        {
                            unitComponent.Target = nearTarget.gameObject;
                            unitComponent.BasicMovement.TargetPosition = unitComponent.Target.transform.position;
                        }

                    }
                }
            }
        }

        private void ProcessResolvedUnits(HashSet<GameObject> units)
        {
            foreach (var unit in units)
            {
                var unitComponent = unit.FetchComponent<UnitComponent>();
                if (!unitComponent.BasicMovement.HoldingPosition)
                {
                    var relativeSqrDst = (unitComponent.BasicMovement.TargetPosition - unit.transform.position).sqrMagnitude;
                    var thresholdSqrRadius = unitComponent.BasicMovement.ReturnRadius * unitComponent.BasicMovement.ReturnRadius;
                    if (relativeSqrDst > thresholdSqrRadius)
                    {
                        unitComponent.BasicMovement.Resolved = false;

                        // Clear from old movegroup
                        if (InputManager.MoveGroupMap.ContainsKey(unit))
                        {
                            var oldMoveGroup = InputManager.MoveGroupMap[unit];
                            if (oldMoveGroup.Units.Contains(unit))
                            {
                                oldMoveGroup.Units.Remove(unit);
                            }

                            InputManager.MoveGroupMap.Remove(unit);
                        }

                        // Add to new MoveGroup
                        var moveGroup = new MoveGroup();
                        moveGroup.Units.Add(unit);
                        InputManager.MoveGroupMap.Add(unit, moveGroup);
                    }
                    else
                    {
                        DecidePassivePhysicsAction(unit, unitComponent);
                    }
                }
            }
        }

        private int ChooseSignVelocity(GameObject unit, GameObject neighbor)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();
            var neighborUnitComponent = neighbor.FetchComponent<UnitComponent>();

            var neighborDesiredDir = neighborUnitComponent.BasicMovement.TargetPosition - neighbor.transform.position;
            var relativeDir = neighbor.transform.position - unit.transform.position;

            var a = unit.transform.position;
            var b = unit.transform.position + relativeDir;
            var c = unit.transform.position + neighborDesiredDir;

            if (MathUtil.LeftOf(a, b, c) > 0)
            {
                return -1;
            }

            return 1;
        }

        private int ChooseSign(GameObject unit, GameObject neighbor)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();
            var neighborUnitComponent = neighbor.FetchComponent<UnitComponent>();
            var desiredDir = unitComponent.BasicMovement.TargetPosition - unit.transform.position;

            var relativeDir = (unit.transform.position - neighbor.transform.position).normalized;
            var rightDir = Quaternion.Euler(0, rotateAmount, 0) * relativeDir;
            var rightTarget = neighbor.transform.position + rightDir * (neighborUnitComponent.Radius + 2 * unitComponent.Radius);
            var rightSqrDst = (unitComponent.BasicMovement.TargetPosition - rightTarget).sqrMagnitude;

            var leftDir = Quaternion.Euler(0, -rotateAmount, 0) * relativeDir;
            var leftTarget = neighbor.transform.position + leftDir * (neighborUnitComponent.Radius + 2 * unitComponent.Radius);
            var leftSqrDst = (unitComponent.BasicMovement.TargetPosition - leftTarget).sqrMagnitude;

            if (rightSqrDst < leftSqrDst)
            {
                return 1;
            }

            return -1;
        }

        private bool ResolveHalfPlaneConstraints(GameObject unit, GameObject neighbor)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();
            var neighborUnitComponent = neighbor.FetchComponent<UnitComponent>();

            var relativeDir = unit.transform.position - neighbor.transform.position;
            var plane = new MathUtil.Plane(relativeDir, neighbor.transform.position);
            var onPositiveHalfPlane = MathUtil.OnPositiveHalfPlane(plane, unitComponent.BasicMovement.TargetPosition, 0);

            // Note - apply the second part of this check to Resolved units ONLY since
            // units moving in opposite directions could stick to each other if one
            // of them has a bad side preference choice
            return !onPositiveHalfPlane || (neighborUnitComponent.BasicMovement.Resolved && onPositiveHalfPlane && unitComponent.BasicMovement.SidePreference != 0);
        }

        private bool NeighborInCombat(GameObject unit, GameObject neighbor)
        {
            var neighborUnitComponent = neighbor.FetchComponent<UnitComponent>();
            var unitComponent = unit.FetchComponent<UnitComponent>();
            return neighborUnitComponent.Attacking || (neighborUnitComponent.InAlertRange && neighborUnitComponent.Target == unitComponent.Target);
        }

        private bool ResolveFriendNonCombatConstraints(GameObject unit, GameObject neighbor, float avoidancePriority)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();
            var neighborUnitComponent = neighbor.FetchComponent<UnitComponent>();

            if (NeighborInCombat(unit, neighbor))
            {
                return true;
            }

            // Test if same group or going towards the same direction
            var sameGroup = false;
            if (InputManager.MoveGroupMap.ContainsKey(neighbor) && InputManager.MoveGroupMap.ContainsKey(unit))
            {
                sameGroup = InputManager.MoveGroupMap[neighbor] == InputManager.MoveGroupMap[unit];
            }

            var sameDirection = false;
            var unitDesiredDir = unitComponent.BasicMovement.TargetPosition - unit.transform.position;

            if (neighborUnitComponent.Kinematic.Velocity.sqrMagnitude > Mathf.Epsilon)
            {
                // Only check same direction if neighbor's velocity is greater than zero
                // otherwise, if a unit is stationary and got pushed, this might cause
                // problems with avoiding units getting stuck on this neighbor
                var neighborDesiredDir = neighborUnitComponent.BasicMovement.TargetPosition - neighbor.transform.position;

                if (neighborDesiredDir.sqrMagnitude > Mathf.Epsilon)
                {
                    if (Vector3.Angle(unitDesiredDir, neighborDesiredDir) < 30)
                    {
                        sameDirection = true;
                    }
                }
            }

            return !sameDirection && !sameGroup;
        }

        private bool ResolveFriendConstraints(GameObject unit, GameObject neighbor, float avoidancePriority)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();
            var neighborUnitComponent = neighbor.FetchComponent<UnitComponent>();
            if (unitComponent.Owner != neighborUnitComponent.Owner)
            {
                return true;
            }

            if (neighborUnitComponent.Attacking)
            {
                return true;
            }

            var avoidanceType = GetAvoidanceType(neighbor);

            var validResolvedState = neighborUnitComponent.BasicMovement.Resolved && neighborUnitComponent.BasicMovement.HoldingPosition;
            var validResolveState = !neighborUnitComponent.BasicMovement.Resolved || validResolvedState;

            if ((int)avoidanceType >= avoidancePriority
                    && validResolveState
                    && (ResolveFriendNonCombatConstraints(unit, neighbor, avoidancePriority) || NeighborInCombat(unit, neighbor)))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private bool ResolveEnemyConstraints(GameObject unit, GameObject neighbor)
        {
            var neighborUnitComponent = neighbor.FetchComponent<UnitComponent>();
            var unitComponent = unit.FetchComponent<UnitComponent>();

            if (unitComponent != neighborUnitComponent)
            {
                if (unitComponent.Target == neighbor)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }

            return true;
        }

        private List<GameObject> CombatClearPath(GameObject unit, float range)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();
            var overlaps = Physics.OverlapSphere(unit.transform.position, range, Util.Layers.PlayerAndAIUnitMask);

            var desiredDir = unitComponent.BasicMovement.TargetPosition - unitComponent.transform.position;
            var rotation = Quaternion.Euler(0, Vector3.SignedAngle(Vector3.forward, desiredDir, Vector3.up), 0);
            var forward = rotation * Vector3.forward;

            var leftPoint = unit.transform.position + (unitComponent.Radius - 0.1f) * (rotation * Vector3.left);
            var leftPlane = new MathUtil.Plane(Vector3.Cross(Vector3.up, forward), leftPoint);

            var rightPoint = unit.transform.position + (unitComponent.Radius - 0.1f) * (rotation * Vector3.right);
            var rightPlane = new MathUtil.Plane(Vector3.Cross(forward, Vector3.up), rightPoint);

            var backPlane = new MathUtil.Plane(forward, unit.transform.position);

            var units = new List<GameObject>();
            foreach (var overlap in overlaps)
            {
                if (overlap.gameObject != unit)
                {
                    var overlapUnitComponent = overlap.gameObject.FetchComponent<UnitComponent>();

                    // Check if in bounds of clear path
                    if (
                        MathUtil.OnPositiveHalfPlane(leftPlane, overlap.gameObject.transform.position, overlapUnitComponent.Radius)
                        && MathUtil.OnPositiveHalfPlane(rightPlane, overlap.gameObject.transform.position, overlapUnitComponent.Radius)
                        && MathUtil.OnPositiveHalfPlane(backPlane, overlap.gameObject.transform.position, overlapUnitComponent.Radius))
                    {
                        // Check if valid criteria
                        var friendConstraint = overlapUnitComponent.Owner == unitComponent.Owner && NeighborInCombat(unit, overlap.gameObject);
                        var enemyConstraint = overlapUnitComponent.Owner != unitComponent.Owner && overlap.gameObject != unitComponent.Target;
                        if (friendConstraint || enemyConstraint)
                        {
                            units.Add(overlap.gameObject);
                        }
                    }
                }
            }

            return units;
        }

        private List<GameObject> ClearPath(GameObject unit)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();
            var range = unitComponent.Radius + unitComponent.Kinematic.SpeedCap * unitComponent.BasicMovement.TimeHorizon;
            var overlaps = Physics.OverlapSphere(unit.transform.position, range, Util.Layers.PlayerAndAIUnitMask);

            var desiredDir = unitComponent.BasicMovement.TargetPosition - unitComponent.transform.position;
            var rotation = Quaternion.Euler(0, Vector3.SignedAngle(Vector3.forward, desiredDir, Vector3.up), 0);
            var forward = rotation * Vector3.forward;

            var leftPoint = unit.transform.position + unitComponent.Radius * (rotation * Vector3.left);
            var leftPlane = new MathUtil.Plane(Vector3.Cross(Vector3.up, forward), leftPoint);

            var rightPoint = unit.transform.position + unitComponent.Radius * (rotation * Vector3.right);
            var rightPlane = new MathUtil.Plane(Vector3.Cross(forward, Vector3.up), rightPoint);

            var backPlane = new MathUtil.Plane(forward, unit.transform.position);

            var units = new List<GameObject>();
            foreach (var overlap in overlaps)
            {
                if (overlap.gameObject != unit)
                {
                    var overlapUnitComponent = overlap.gameObject.FetchComponent<UnitComponent>();
                    if (
                        MathUtil.OnPositiveHalfPlane(leftPlane, overlap.gameObject.transform.position, overlapUnitComponent.Radius)
                        && MathUtil.OnPositiveHalfPlane(rightPlane, overlap.gameObject.transform.position, overlapUnitComponent.Radius)
                        && MathUtil.OnPositiveHalfPlane(backPlane, overlap.gameObject.transform.position, overlapUnitComponent.Radius))
                    {
                        var avoidanceType = GetAvoidanceType(overlap.gameObject);

                        units.Add(overlap.gameObject);
                        if (avoidanceType == AvoidanceType.Stationary)
                        {
                            units.Add(overlap.gameObject);
                        }
                        else
                        {
                            var sqrDst = (overlap.gameObject.transform.position - unit.transform.position).sqrMagnitude;
                            var radius = MovingNeighborRadius + unitComponent.Radius + overlapUnitComponent.Radius;
                            if (sqrDst < radius * radius)
                            {
                                units.Add(overlap.gameObject);
                            }
                        }
                    }
                }
            }

            return units;
        }

        private bool IsHeadOn(GameObject unit, GameObject neighbor)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();
            var neighborComponent = neighbor.FetchComponent<UnitComponent>();

            var unitPreferredDir = unitComponent.BasicMovement.TargetPosition - unit.transform.position;
            var neighborPreferredDir = neighborComponent.BasicMovement.TargetPosition - neighbor.transform.position;

            var angle = Vector3.Angle(unitPreferredDir, -neighborPreferredDir);
            if (angle < 90)
            {
                return true;
            }

            return false;
        }

        private void ProcessUnresolvedUnits(HashSet<GameObject> units)
        {
            // Calculate preferred velocities
            foreach (var unit in units)
            {
                var unitComponent = unit.FetchComponent<UnitComponent>();
                var dir = unitComponent.BasicMovement.TargetPosition - unit.transform.position;
                unitComponent.Kinematic.PreferredVelocity = dir.normalized * unitComponent.Kinematic.SpeedCap;

                if (unitComponent.Attacking)
                {
                    unitComponent.Kinematic.PreferredVelocity = Vector3.zero;
                }
            }

            foreach (var unit in units)
            {
                // check for free path
                var unitComponent = unit.FetchComponent<UnitComponent>();
                if (unitComponent.Attacking)
                {
                    continue;
                }

                var preferredDir = unitComponent.Kinematic.PreferredVelocity.normalized;

                var neighbors = ClearPath(unit);

                if (neighbors.Count > 0)
                {
                    // choose nearest valid neighbor
                    // filter neighbors where target 
                    GameObject nearNeighbor = null;
                    var nearSqrDst = Mathf.Infinity;
                    var avoidancePriority = -1;

                    foreach (var neighbor in neighbors)
                    {
                        var dir = unit.transform.position - neighbor.transform.position;
                        var sqrDst = dir.sqrMagnitude;

                        // Choose the neighbor with the following criteria:
                        // - Closest
                        // - Neighbor has to be "behind" the target position (relative to this unit).  The 
                        //   exception here is if the unit's already in the middle of avoiding, then we can consider
                        //   this option
                        // - Unit isn't in the same group as a neighbor or other unit going in the same target sameDirection
                        //   This enables a "flocking" behavior when avoiding 
                        // - Neighbor has to be moving, or holding position
                        if (sqrDst < nearSqrDst
                            && ResolveHalfPlaneConstraints(unit, neighbor)
                            && ResolveFriendConstraints(unit, neighbor, avoidancePriority)
                            && ResolveEnemyConstraints(unit, neighbor))
                        {
                            nearSqrDst = sqrDst;
                            nearNeighbor = neighbor;

                            var avoidanceType = GetAvoidanceType(neighbor);
                            avoidancePriority = (int)avoidanceType;
                        }
                    }

                    if (nearNeighbor != null)
                    {
                        var avoidanceType = GetAvoidanceType(nearNeighbor);
                        var nearNeighborUnitComponent = nearNeighbor.FetchComponent<UnitComponent>();
                        if (avoidanceType == AvoidanceType.Moving)
                        {
                            if (nearNeighborUnitComponent.BasicMovement.SidePreference != 0
                                    && unitComponent.BasicMovement.SidePreference != nearNeighborUnitComponent.BasicMovement.SidePreference)
                            {
                                // This is to make sure two moving units eventually resolve their incoming collisoin.  If two units have
                                // opposite signs for their side preferences, then they'll run into each other forever
                                unitComponent.BasicMovement.SidePreference = nearNeighborUnitComponent.BasicMovement.SidePreference;
                            }
                            else if (nearNeighborUnitComponent.BasicMovement.SidePreference == 0 && unitComponent.BasicMovement.SidePreference == 0)
                            {
                                unitComponent.BasicMovement.SidePreference = ChooseSignVelocity(unit, nearNeighbor);
                            }

                            var relativeDir = (unit.transform.position - nearNeighbor.transform.position).normalized;
                            relativeDir = Quaternion.Euler(
                                0,
                                unitComponent.BasicMovement.SidePreference * 45,
                                0) * relativeDir;

                            var target = nearNeighbor.transform.position + relativeDir * (nearNeighborUnitComponent.Radius + unitComponent.Radius);

                            var desiredDir = (target - unit.transform.position).normalized;
                            unitComponent.Kinematic.PreferredVelocity = desiredDir * unitComponent.Kinematic.SpeedCap;
                        }
                        else
                        {
                            if (unitComponent.BasicMovement.SidePreference == 0)
                            {
                                unitComponent.BasicMovement.SidePreference = ChooseSign(unit, nearNeighbor);
                            }

                            var relativeDir = (unit.transform.position - nearNeighbor.transform.position).normalized;
                            relativeDir = Quaternion.Euler(
                                0,
                                unitComponent.BasicMovement.SidePreference * rotateAmount,
                                0) * relativeDir;

                            var target = nearNeighbor.transform.position + relativeDir * (nearNeighborUnitComponent.Radius + 2 * unitComponent.Radius);
                            var desiredDir = (target - unit.transform.position).normalized;
                            unitComponent.Kinematic.PreferredVelocity = desiredDir * unitComponent.Kinematic.SpeedCap;

                            /*
                                Post check to make sure the side preference doesn't violate any constraints
                                For instance - if a neighbor outside the viable neighbors is blocking the side
                                that this unit's trying to go, this unit should try swapping sides
                            */
                            if (unitComponent.BasicMovement.SidePreference != 0)
                            {
                                var rotation = Quaternion.Euler(
                                    0,
                                    Vector3.SignedAngle(Vector3.forward, unitComponent.Kinematic.PreferredVelocity, Vector3.up),
                                    0);

                                var forward = rotation * Vector3.forward;

                                var leftPoint = unit.transform.position + unitComponent.Radius * (rotation * Vector3.left);
                                var leftPlane = new MathUtil.Plane(Vector3.Cross(Vector3.up, forward), leftPoint);

                                var rightPoint = unit.transform.position + unitComponent.Radius * (rotation * Vector3.right);
                                var rightPlane = new MathUtil.Plane(Vector3.Cross(forward, Vector3.up), rightPoint);

                                var backPlane = new MathUtil.Plane(forward, unit.transform.position);

                                var overlaps = Physics.OverlapSphere(
                                    unitComponent.transform.position,
                                    unitComponent.Radius + unitComponent.Kinematic.SpeedCap * 0.05f,
                                    Util.Layers.PlayerAndAIUnitMask);

                                foreach (var overlap in overlaps)
                                {
                                    if (overlap.gameObject == unit)
                                    {
                                        continue;
                                    }

                                    if (unitComponent.BasicMovement.DBG)
                                    {
                                        Debug.DrawLine(unit.transform.position, overlap.transform.position, Color.black);
                                    }

                                    var neighbor = overlap.gameObject;

                                    // Make sure neighbor is blocking the direction the direction 
                                    // that this unit's headed towards
                                    var neighborUnitComponent = neighbor.GetComponent<UnitComponent>();
                                    if (
                                        MathUtil.OnPositiveHalfPlane(leftPlane, neighbor.transform.position, neighborUnitComponent.Radius)
                                        && MathUtil.OnPositiveHalfPlane(rightPlane, neighbor.transform.position, neighborUnitComponent.Radius)
                                        && MathUtil.OnPositiveHalfPlane(backPlane, neighbor.transform.position, neighborUnitComponent.Radius))
                                    {
                                        if (!neighbors.Contains(neighbor) && neighborUnitComponent.BasicMovement.HoldingPosition)
                                        {
                                            unitComponent.BasicMovement.SidePreference = -unitComponent.BasicMovement.SidePreference;
                                        }
                                    }
                                }
                            }


                            if (unitComponent.BasicMovement.DBG)
                            {
                                Debug.DrawLine(nearNeighbor.transform.position, target, Color.cyan);
                                Debug.DrawRay(unit.transform.position, desiredDir * unitComponent.Kinematic.SpeedCap, Color.yellow);
                                Debug.DrawRay(unit.transform.position, preferredDir * 5, Color.blue);

                            }
                        }
                    }
                }
                else
                {
                    unitComponent.BasicMovement.SidePreference = 0;
                }
            }

            foreach (var unit in units)
            {
                var unitComponent = unit.FetchComponent<UnitComponent>();
                unitComponent.Kinematic.Velocity = unitComponent.Kinematic.PreferredVelocity;
                // compute new position
                var delta = unitComponent.Kinematic.Velocity * Time.fixedDeltaTime;
                var dir = unitComponent.BasicMovement.TargetPosition - unit.transform.position;
                if (dir.magnitude < delta.magnitude)
                {
                    delta = unitComponent.Kinematic.Velocity.normalized * dir.magnitude;
                }

                // Debug.Log(unitComponent.Kinematic.Velocity.magnitude);

                unitComponent.Kinematic.Position += delta;
                unit.transform.position = unitComponent.Kinematic.Position;

                var moveGroup = InputManager.MoveGroupMap.ContainsKey(unit) ? InputManager.MoveGroupMap[unit] : null;
                if (TriggerStop(unit, moveGroup))
                {
                    ForceStop(unit);
                    unitComponent.BasicMovement.Resolved = true;
                }
            }
        }

        private void FixedUpdate()
        {
            // Process unresolved units
            CategorizeUnits(
                out HashSet<GameObject> unresolvedUnits,
                out HashSet<GameObject> resolvedUnits);

            CheckCombatState();
            ProcessUnresolvedUnits(unresolvedUnits);
            ProcessResolvedUnits(resolvedUnits);

            // Post processing
            var units = Entity.Fetch(new List<System.Type>()
            {
                typeof (UnitComponent)
            });

            var neighborDictionary = new Dictionary<GameObject, List<GameObject>>();
            foreach (var unit in units)
            {
                neighborDictionary.Add(unit, GetNeighbors(unit));

                var unitComponent = unit.FetchComponent<UnitComponent>();

                if (unitComponent.Kinematic.Velocity.sqrMagnitude > 0)
                {
                    unitComponent.BasicMovement.LastMoveTime = Time.fixedTime;
                }
            }

            for (int i = 0; i < Iterations; i++)
            {
                foreach (var unit in units)
                {
                    foreach (var neighbor in neighborDictionary[unit])
                    {
                        ResolveCollision(unit, neighbor);
                    }
                }
            }

            foreach (var unit in units)
            {
                var unitComponent = unit.FetchComponent<UnitComponent>();
                if (unitComponent.Kinematic.Velocity.sqrMagnitude > Mathf.Epsilon)
                {
                    var orientation = MathUtil.NormalizeOrientation(
                        Vector3.SignedAngle(
                            Vector3.forward,
                            unitComponent.Kinematic.Velocity,
                            Vector3.up));

                    unitComponent.Kinematic.Orientation = orientation;
                }
            }
        }

        private void DecidePassivePhysicsAction(GameObject unit, UnitComponent unitComponent)
        {
            var neighbors = GetNeighbors(unit);

            // Get nearest colliding neighbor, where the neighbor is moving towards
            // this unit
            var calculatedVelocity = Vector3.zero;
            var count = 0;

            foreach (var neighbor in neighbors)
            {
                var neighborUnitComponent = neighbor.FetchComponent<UnitComponent>();
                if (neighborUnitComponent.Owner != unitComponent.Owner)
                {
                    continue;
                }

                if (InPushRadius(unit, neighbor))
                {
                    // Calculate how much an "inactive" unit should be pushed
                    var relativeDir = unit.transform.position - neighborUnitComponent.transform.position;
                    var appliedVelocity = relativeDir.normalized * neighborUnitComponent.Kinematic.Velocity.magnitude;
                    var velocity = neighborUnitComponent.Kinematic.Velocity;

                    if (!neighborUnitComponent.BasicMovement.Resolved)
                    {
                        var angle = Vector3.SignedAngle(velocity, relativeDir, Vector3.up);
                        var angleSign = Mathf.Sign(angle);
                        var normalVelocity = Quaternion.Euler(0, angleSign * 90, 0) * velocity;

                        // Add a lower bound so that turn rate is faster
                        var t = Mathf.Max(Mathf.InverseLerp(0, 90, Mathf.Abs(angle)), 0.1f);
                        var perpIntensity = Mathf.Lerp(0, 1, t);
                        var forwardIntensity = 1 - perpIntensity;
                        appliedVelocity = perpIntensity * normalVelocity + forwardIntensity * velocity;
                    }

                    calculatedVelocity += appliedVelocity;
                    count++;
                }
            }

            if (count > 0)
            {
                calculatedVelocity /= count;
            }

            // match the velocity
            unitComponent.Kinematic.Velocity = calculatedVelocity;
            unitComponent.Kinematic.Position += unitComponent.Kinematic.Velocity * Time.fixedDeltaTime;
            unit.transform.position = unitComponent.Kinematic.Position;
        }

        private bool InPushRadius(GameObject unit, GameObject neighbor)
        {
            var neighborUnitComponent = neighbor.GetComponent<UnitComponent>();
            var unitComponent = unit.GetComponent<UnitComponent>();

            var relativeDir = unit.transform.position - neighbor.transform.position;
            var dist = relativeDir.magnitude;
            dist -= unitComponent.Radius;
            dist -= neighborUnitComponent.Radius;
            // Add a small offset to prevent "stuttering" due to the impulse force
            // (from resolving collisions) kicking neighbors out of the push influence region
            dist -= 0.025f;

            return dist < Mathf.Epsilon && Vector3.Dot(relativeDir, neighborUnitComponent.Kinematic.Velocity) > 0;
        }

        private void ResolveCollision(GameObject unit, GameObject neighbor)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();
            var neighborUnitComponent = neighbor.FetchComponent<UnitComponent>();

            var relativeDir = unit.transform.position - neighbor.transform.position;
            relativeDir.y = 0;

            var sqrDst = relativeDir.sqrMagnitude;
            if (sqrDst < Mathf.Epsilon)
            {
                // Add offset for the off case that the unit is right on top of another
                // unit - thought maybe it's better to just teleport one of the
                // units out of the body
                relativeDir = Vector3.right * 0.0001f;
                sqrDst = relativeDir.sqrMagnitude;
            }

            var thresholdRadius = unitComponent.Radius + neighborUnitComponent.Radius;

            // TODO: handle sqrDst <= Mathf.Epsilon case
            if (sqrDst < thresholdRadius * thresholdRadius)
            {
                if (AllyCollisionConstraints(unit, neighbor)
                    && EnemyCollisionConstraints(unit, neighbor))
                {
                    var dst = Mathf.Sqrt(sqrDst);
                    var delta = ResponseCoefficient * 0.5f * (thresholdRadius - dst);

                    // decide if unit is moving unit or stationary unit
                    var pushVec = relativeDir.normalized * delta;
                    unitComponent.Kinematic.Position += pushVec;
                    unitComponent.transform.position = unitComponent.Kinematic.Position;
                }
            }
        }

        private bool EnemyCollisionConstraints(GameObject unit, GameObject neighbor)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();
            var neighborComponent = neighbor.FetchComponent<UnitComponent>();

            if (unitComponent.Owner == neighborComponent.Owner)
            {
                return true;
            }

            return !unitComponent.BasicMovement.Resolved && neighborComponent.BasicMovement.Resolved;
        }

        private bool AllyCollisionConstraints(GameObject unit, GameObject neighbor)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();
            var neighborUnitComponent = neighbor.FetchComponent<UnitComponent>();

            if (unitComponent.Owner != neighborUnitComponent.Owner)
            {
                return true;
            }

            if (neighborUnitComponent.Attacking)
            {
                return true;
            }
            else
            {
                var isUnitMoving = unitComponent.Kinematic.Velocity.sqrMagnitude > 0;
                var isNeighborMoving = neighborUnitComponent.Kinematic.Velocity.sqrMagnitude > 0;
                var bothMoving = isUnitMoving && isNeighborMoving;

                // Use last move time to prevent pushing units that have been stationary for a while
                // This is an aesthetic choice
                // Try playing around with TOI or some other collision resolver on top of the
                // iterative relaxing I'm doing right now
                var bothStationary = !isUnitMoving
                    && !isNeighborMoving
                    && unitComponent.BasicMovement.LastMoveTime >= neighborUnitComponent.BasicMovement.LastMoveTime;

                var neighborStationary = isUnitMoving && !isNeighborMoving;

                return bothMoving || bothStationary || neighborStationary;
            }
        }

        private bool TriggerStop(GameObject unit, MoveGroup moveGroup)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();

            if (ArrivedAtTarget(unit))
            {
                return true;
            }

            // I don't think this check does a whole lot
            // var currentDir = unitComponent.BasicMovement.TargetPosition - unit.transform.position;
            // var angle = Vector3.Dot(
            //     currentDir.normalized,
            //     unitComponent.BasicMovement.RelativeDeltaStart.normalized);

            // if (angle < 0)
            // {
            //     return true;
            // }

            var neighbors = GetNeighbors(unit);

            if (moveGroup != null)
            {
                foreach (var neighbor in neighbors)
                {
                    if (moveGroup.Units.Contains(neighbor))
                    {
                        // Handle the case where units are in the same move group
                        // Important subcase 
                        // - If units of the same group are colliding, near the destination, and going in opposite direction
                        // - If neighboring unit in same group has reached the destination
                        var neighborUnitComponent = neighbor.FetchComponent<UnitComponent>();
                        if (IsColliding(unit, neighbor))
                        {
                            var unitVelocity = unitComponent.Kinematic.Velocity.normalized;
                            var neighborUnitVelocity = neighborUnitComponent.Kinematic.Velocity.normalized;
                            if (NearTarget(unit) && Vector3.Dot(unitVelocity, neighborUnitVelocity) < 0)
                            {
                                return true;
                            }

                            if (neighborUnitComponent.BasicMovement.Resolved)
                            {
                                return true;
                            }
                        }
                    }
                }
            }


            return false;
        }

        private List<GameObject> GetBlockers(GameObject unit, List<Collider> overlaps)
        {
            var blockers = new List<GameObject>();
            foreach (var overlap in overlaps)
            {
                if (overlap.gameObject != unit)
                {
                    var unitComponent = overlap.gameObject.FetchComponent<UnitComponent>();
                    // Ignore if target position is right on this unit
                    if (unitComponent.BasicMovement.HoldingPosition)
                    {
                        blockers.Add(unitComponent.gameObject);
                    }
                }
            }

            return blockers;
        }

        public void ForceStop(GameObject unit)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();
            unitComponent.BasicMovement.TargetPosition = unit.transform.position;
            unitComponent.Kinematic.Velocity = Vector3.zero;
        }

        private bool NearTarget(GameObject unit)
        {
            var unitComponent = unit.GetComponent<UnitComponent>();
            var position = unitComponent.Kinematic.Position;
            var targetPosition = unitComponent.BasicMovement.TargetPosition;

            var stopRadius = unitComponent.Radius * 1.5f;
            return (position - targetPosition).sqrMagnitude <= stopRadius * stopRadius;
        }

        private bool ArrivedAtTarget(GameObject unit)
        {
            var unitComponent = unit.GetComponent<UnitComponent>();
            var position = unitComponent.Kinematic.Position;
            var targetPosition = unitComponent.BasicMovement.TargetPosition;

            var stopRadius = unitComponent.Arrive.StopRadius;
            return (position - targetPosition).sqrMagnitude <= stopRadius * stopRadius;
        }

        private bool IsColliding(GameObject unit, GameObject neighbor)
        {
            var neighborUnitComponent = neighbor.GetComponent<UnitComponent>();
            var unitComponent = unit.GetComponent<UnitComponent>();

            var dist = (neighbor.transform.position - unit.transform.position).magnitude;
            dist -= unitComponent.Radius;
            dist -= neighborUnitComponent.Radius;

            return dist < Mathf.Epsilon;
        }

        private GameObject GetCollidingNeighbor(GameObject unit, List<GameObject> neighbors)
        {
            GameObject collidingNeighbor = null;
            var collidingDst = Mathf.Infinity;

            foreach (var neighbor in neighbors)
            {
                var neighborUnitComponent = neighbor.GetComponent<UnitComponent>();
                var unitComponent = unit.GetComponent<UnitComponent>();

                var dist = (neighbor.transform.position - unit.transform.position).magnitude;
                dist -= unitComponent.Radius;
                dist -= neighborUnitComponent.Radius;

                if (dist <= Mathf.Epsilon && dist < collidingDst)
                {
                    collidingNeighbor = neighbor;
                    collidingDst = dist;
                }
            }

            return collidingNeighbor;
        }

        private List<GameObject> GetNeighbors(GameObject unit)
        {
            var units = Entity.Fetch(new List<System.Type>()
            {
                typeof(UnitComponent)
            });

            var unitComponent = unit.FetchComponent<UnitComponent>();
            var unitRadius = unitComponent.Radius;

            var neighbors = new List<GameObject>();

            foreach (var neighbor in units)
            {
                if (neighbor == unit)
                {
                    continue;
                }

                var neighborComponent = neighbor.GetComponent<UnitComponent>();
                var neighborRadius = neighborComponent.Radius;

                var dst = (unit.transform.position - neighbor.transform.position).magnitude;
                dst -= neighborRadius + unitRadius;

                if (dst < NeighborScanRadius)
                {
                    neighbors.Add(neighbor);
                }
            }

            return neighbors;
        }

        private void SetupEnvironment()
        {
            // Setup ECS
            var objs = GameObject.FindGameObjectsWithTag(Util.Tags.PlayerUnit);
            foreach (var obj in objs)
            {
                EntityFactory.RegisterItem(obj);
            }

            var units = Entity.Fetch(new List<System.Type>() { typeof(UnitComponent) });

            foreach (var unit in units)
            {
                var unitComponent = unit.FetchComponent<UnitComponent>();
                unitComponent.Kinematic.Position = unit.transform.position;
                unitComponent.BasicMovement.TargetPosition = unit.transform.position;
            }
        }

        private void OnDrawGizmos()
        {
            if (Entity != null)
            {
                var units = Entity.Fetch(new List<System.Type>()
            {
                typeof(UnitComponent)
            });

                foreach (var unit in units)
                {
                    var unitComponent = unit.FetchComponent<UnitComponent>();
                    var forward = Quaternion.Euler(0, unitComponent.Kinematic.Orientation, 0) * Vector3.forward;
                    Debug.DrawRay(
                        unit.transform.position + 0.5f * forward * unitComponent.Radius,
                        forward * unitComponent.Radius * 0.5f,
                        Color.red);
                }
            }
        }

        private AvoidanceType GetAvoidanceType(GameObject unit)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();
            if (unitComponent.Kinematic.Velocity.sqrMagnitude > 0)
            {
                return AvoidanceType.Moving;
            }
            else
            {
                return AvoidanceType.Stationary;
            }
        }
    }

    public enum AvoidanceType
    {
        Stationary,
        Moving
    }

    public class UnitState
    {
        public UnitComponent UnitComponent;
        public GameObject CollidingNeighbor;
        public bool Arrived;
    }

    [System.Serializable]
    public class RVOProperties
    {
        public float NeighborRadius;
        public int MaxNeighbors;
        public float TimeHorizonAgents;
        public float TimeHorizonObstacles;
    }
}
