using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RoguelikeRTS
{
    public class Simulator : MonoBehaviour
    {
        public float NeighborScanRadius;
        public float TargetInfluenceRadius;
        public float PushableRadius;
        public float ResponseCoefficient;

        public InputManager InputManager;
        public static Entity Entity;
        public static Simulator Instance;

        public RVOProperties RVOProperties;

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

                if (InputManager.MoveGroupMap.ContainsKey(unit))
                {
                    if (!unitComponent.BasicMovement.Resolved)
                    {
                        unresolvedUnits.Add(unit);
                    }
                }
                else
                {
                    resolvedUnits.Add(unit);
                }
            }
        }

        private void FixedUpdate()
        {
            // Set unresolved units preferred velocity
            // Adjust velocity with RVO
            // Apply velocity and post-processing logic to unresolved units 

            // Calculate and apply velocity to resolved units
            CategorizeUnits(
                out HashSet<GameObject> unresolvedUnits,
                out HashSet<GameObject> resolvedUnits);

            SetPreferredVelocities(unresolvedUnits);
            ApplyActiveKinematics(unresolvedUnits);

            // foreach (var unit in units)
            // {
            //     var unitComponent = unit.FetchCo1ponent<UnitComponent>();
            //     if (InputManager.MoveGroupMap.ContainsKey(unit))
            //     {
            //         if (!unitComponent.BasicMovement.Resolved)
            //         {
            //             var moveGroup = InputManager.MoveGroupMap[unit];
            //             if (TriggerStop(unit, moveGroup))
            //             {
            //                 unitComponent.BasicMovement.Resolved = true;
            //             }

            //             DecideActivePhysicsAction(unit, unitComponent);
            //         }
            //     }
            //     else
            //     {
            //         DecidePassivePhysicsAction(unit, unitComponent);
            //     }
            // }

            var units = Entity.Fetch(new List<System.Type>()
            {
                typeof(UnitComponent)
            });

            // Process resolved units
            foreach (var unit in units)
            {
                var unitComponent = unit.FetchComponent<UnitComponent>();
                if (unitComponent.BasicMovement.Resolved)
                {
                    DecidePassivePhysicsAction(unit, unitComponent);
                }
                else
                {
                    if (TriggerStop(unit, InputManager.MoveGroupMap[unit]))
                    {

                    }
                }
            }

            // Post processing
            foreach (var unit in units)
            {
                var neighbors = GetNeighbors(unit);
                // resolve collision
                foreach (var neighbor in neighbors)
                {
                    ResolveCollision(unit, neighbor);
                }

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
            Debug.Log("Triggered");
            var neighbors = GetNeighbors(unit);

            // Get nearest colliding neighbor, where the neighbor is moving towards
            // this unit
            var calculatedVelocity = Vector3.zero;
            var count = 0;

            foreach (var neighbor in neighbors)
            {
                if (InPushRadius(unit, neighbor))
                {
                    // Calculate how much an "inactive" unit should be pushed
                    var nearUnitComponent = neighbor.FetchComponent<UnitComponent>();
                    var relativeDir = unit.transform.position - nearUnitComponent.transform.position;
                    var appliedVelocity = relativeDir.normalized * nearUnitComponent.Kinematic.Velocity.magnitude;
                    var velocity = nearUnitComponent.Kinematic.Velocity;

                    if (!nearUnitComponent.BasicMovement.Resolved)
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
            unitComponent.UpdateVelocity(calculatedVelocity);
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
                if (unitComponent.BasicMovement.Resolved == neighborUnitComponent.BasicMovement.Resolved)
                {
                    var dst = Mathf.Sqrt(sqrDst);

                    var delta = ResponseCoefficient * 0.5f * (thresholdRadius - dst);
                    var pushVec = relativeDir.normalized * delta;

                    unitComponent.Kinematic.Position += pushVec;
                    unitComponent.transform.position = unitComponent.Kinematic.Position;

                    neighborUnitComponent.Kinematic.Position -= pushVec;
                    neighbor.transform.position = neighborUnitComponent.Kinematic.Position;
                }
                else if (unitComponent.BasicMovement.Resolved)
                {
                    var dst = Mathf.Sqrt(sqrDst);

                    // var delta = ResponseCoefficient * 0.5f * (thresholdRadius - dst);
                    var delta = ResponseCoefficient * 0.5f * (thresholdRadius - dst);
                    var pushVec = relativeDir.normalized * delta;

                    unitComponent.Kinematic.Position += pushVec;
                    unitComponent.transform.position = unitComponent.Kinematic.Position;
                }
            }
        }

        private bool TriggerStop(GameObject unit, MoveGroup moveGroup)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();

            if (ArrivedAtTarget(unit))
            {
                return true;
            }

            var currentDir = unitComponent.BasicMovement.TargetPosition - unit.transform.position;
            var angle = Vector3.Dot(
                currentDir.normalized,
                unitComponent.BasicMovement.RelativeDeltaStart.normalized);

            if (angle < 0)
            {
                return true;
            }

            var neighbors = GetNeighbors(unit);
            foreach (var neighbor in neighbors)
            {
                if (moveGroup.Units.Contains(neighbor))
                {
                    // Handle the case where units are in the same move group
                    // Important subcase 
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

            return false;
        }

        private void ApplyActiveKinematics(HashSet<GameObject> units)
        {
            RVO.Simulator.Instance.doStepCustom();

            foreach (var unit in units)
            {
                var unitComponent = unit.FetchComponent<UnitComponent>();
                if (!unitComponent.BasicMovement.Resolved)
                {
                    unitComponent.UpdatePosition(Time.fixedDeltaTime);
                    unit.transform.position = unitComponent.Kinematic.Position;
                }
            }
        }

        private void SetPreferredVelocities(HashSet<GameObject> units)
        {
            foreach (var unit in units)
            {
                var unitComponent = unit.FetchComponent<UnitComponent>();
                var steeringResult = Steering.ArriveBehavior.GetSteering(
                    unitComponent.Kinematic,
                    unitComponent.Arrive,
                    unitComponent.BasicMovement.TargetPosition);

                if (steeringResult != null)
                {
                    var velocity = unitComponent.Kinematic.Velocity + steeringResult.Acceleration * Time.fixedDeltaTime;
                    unitComponent.Agent.prefVelocity_ = new RVO.Vector2(velocity);
                    // var relativeDir = (unitComponent.BasicMovement.TargetPosition - unit.transform.position).normalized;
                    // var velocity = relativeDir * unitComponent.Kinematic.SpeedCap;
                    // unitComponent.Agent.prefVelocity_ = new RVO.Vector2(velocity);
                }
                else
                {
                    unitComponent.Agent.prefVelocity_ = new RVO.Vector2(0, 0);
                }
            }
        }

        private bool ShouldStop(GameObject unit, List<GameObject> neighbors)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();
            var sqrDst = (unit.transform.position - unitComponent.Kinematic.Position).sqrMagnitude;

            if (ArrivedAtTarget(unit))
            {
                return true;
            }

            var desiredVelocity = (unitComponent.BasicMovement.TargetPosition - unit.transform.position).normalized * unitComponent.Kinematic.SpeedCap;

            foreach (var neighbor in neighbors)
            {
                if (IsColliding(unit, neighbor))
                {
                    if (NearTarget(unit))
                    {
                        return true;
                    }

                    var neighborUnitComponent = neighbor.FetchComponent<UnitComponent>();
                    if (neighborUnitComponent.BasicMovement.Resolved)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void DecideActivePhysicsAction(GameObject unit, UnitComponent unitComponent)
        {
            if (unitComponent.BasicMovement.Resolved)
            {
                ForceStop(unit);
            }
            else
            {
                var steeringResult = Steering.ArriveBehavior.GetSteering(
                    unitComponent.Kinematic,
                    unitComponent.Arrive,
                    unitComponent.BasicMovement.TargetPosition);

                if (steeringResult != null)
                {
                    unitComponent.Kinematic.Velocity += steeringResult.Acceleration * Time.fixedDeltaTime;
                    unitComponent.Kinematic.Position += unitComponent.Kinematic.Velocity * Time.fixedDeltaTime;

                    unit.transform.position = unitComponent.Kinematic.Position;
                }
            }
        }

        private bool InTargetInfluenceRadius(GameObject unit)
        {
            var unitComponent = unit.GetComponent<UnitComponent>();
            var position = unitComponent.Kinematic.Position;
            var targetPosition = unitComponent.BasicMovement.TargetPosition;

            var radius = TargetInfluenceRadius;
            return (position - targetPosition).sqrMagnitude <= radius * radius;
        }

        private void ForceStop(GameObject unit)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();
            unitComponent.BasicMovement.TargetPosition = unit.transform.position;
            unitComponent.UpdateVelocity(Vector3.zero);
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
                dst -= (neighborRadius + unitRadius);

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
                unitComponent.Agent = RVO.Simulator.Instance.createAgentAdapter(
                    unit,
                    new RVO.Vector2(unit.transform.position),
                    RVOProperties.NeighborRadius,
                    RVOProperties.MaxNeighbors,
                    RVOProperties.TimeHorizonAgents,
                    RVOProperties.TimeHorizonObstacles,
                    unitComponent.Radius,
                    unitComponent.Kinematic.SpeedCap,
                    new RVO.Vector2(unitComponent.Kinematic.Velocity)
                );
            }

            // Setup RVO parameters
            RVO.Simulator.instance_ = new RVO.Simulator();
            RVO.Simulator.Instance.setTimeStep(Time.fixedDeltaTime);
            foreach (var unit in units)
            {
                var unitComponent = unit.FetchComponent<UnitComponent>();
                RVO.Simulator.Instance.addAgent(unitComponent.Agent);
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
