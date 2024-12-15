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

        public int Iterations;

        public List<GameObject> Obstacles;

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

        private void CheckCombatState(HashSet<GameObject> unresolvedUnits)
        {
            foreach (var unit in unresolvedUnits)
            {
                var unitComponent = unit.FetchComponent<UnitComponent>();
                unitComponent.Attacking = false;

                if (unitComponent.Target != null)
                {
                    var relativeDir = unitComponent.Target.transform.position - unit.transform.position;
                    var sqrDst = relativeDir.sqrMagnitude;
                    var targetUnitComponent = unitComponent.Target.FetchComponent<UnitComponent>();

                    var radius = unitComponent.AttackRadius
                        + unitComponent.Radius
                        + targetUnitComponent.Radius;

                    if (sqrDst < radius * radius)
                    {
                        unitComponent.Attacking = true;
                    }
                }
            }
        }

        private void FixedUpdate()
        {
            // Process unresolved units
            CategorizeUnits(
                out HashSet<GameObject> unresolvedUnits,
                out HashSet<GameObject> resolvedUnits);

            CheckCombatState(unresolvedUnits);
            SetPreferredVelocities(unresolvedUnits);
            ApplyActiveKinematics(unresolvedUnits);

            // Process resolved units
            var units = Entity.Fetch(new List<System.Type>()
            {
                typeof(UnitComponent)
            });

            foreach (var unit in units)
            {
                var unitComponent = unit.FetchComponent<UnitComponent>();
                if (unitComponent.BasicMovement.Resolved && !unitComponent.BasicMovement.HoldingPosition)
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

            // Post processing
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

                if (bothMoving || bothStationary || neighborStationary)
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

            return false;
        }

        private void ApplyActiveKinematics(HashSet<GameObject> units)
        {
            RVO.Simulator.Instance.doStepCustom();

            foreach (var unit in units)
            {
                var unitComponent = unit.FetchComponent<UnitComponent>();
                if (unitComponent.Attacking)
                {
                    unitComponent.UpdateVelocity(Vector3.zero);
                    unitComponent.BasicMovement.TargetPosition = unit.transform.position;
                }
                else
                {
                    if (TriggerStop(unit, InputManager.MoveGroupMap[unit]))
                    {
                        unitComponent.BasicMovement.TargetPosition = unit.transform.position;
                        unitComponent.BasicMovement.Resolved = true;
                    }
                    else
                    {
                        unitComponent.UpdatePosition(Time.fixedDeltaTime);
                        unit.transform.position = unitComponent.Kinematic.Position;
                    }
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

                if (unitComponent.Attacking)
                {
                    unitComponent.Agent.prefVelocity_ = new RVO.Vector2(0, 0);
                }
                else if (steeringResult != null)
                {
                    var desiredDir = unitComponent.BasicMovement.TargetPosition - unit.transform.position;
                    var sensorDir = Mathf.Min(3f, desiredDir.magnitude) * desiredDir.normalized;

                    var unitStartPosition = unit.transform.position + new Vector3(0, 0.25f, 0);
                    var layerMask = LayerMask.GetMask(Util.Layers.AIUnit, Util.Layers.PlayerUnitLayer);

                    var rotation = Quaternion.Euler(0, Vector3.SignedAngle(Vector3.forward, sensorDir, Vector3.up), 0);

                    var boxCenter = unitStartPosition + sensorDir * 0.5f;
                    var boxwidth = unitComponent.Radius * 2;
                    var boxLength = sensorDir.magnitude;
                    var boxOverlaps = Physics.OverlapBox(
                        boxCenter,
                        new Vector3(boxwidth, 1, boxLength) * 0.5f,
                        rotation,
                        layerMask);

                    var circleCenter = unitStartPosition + sensorDir;
                    var circleOverlaps = Physics.OverlapSphere(
                        circleCenter,
                        unitComponent.Radius,
                        layerMask);

                    var overlaps = new List<Collider>();
                    foreach (var overlap in boxOverlaps)
                    {
                        overlaps.Add(overlap);
                    }
                    foreach (var overlap in circleOverlaps)
                    {
                        overlaps.Add(overlap);
                    }

                    // Process overlaps
                    var blockers = GetBlockers(unit, overlaps); // TODO: this can just be a bool
                    if (unitComponent.BasicMovement.DBG)
                    {
                        Util.DrawBox(boxCenter,
                            rotation,
                            new Vector3(boxwidth, 1, boxLength), Color.magenta, 0.1f);
                        Debug.Log(blockers.Count);
                    }

                    if (blockers.Count > 0)
                    {
                        GameObject nearNeighbor = null;
                        var nearSqrDst = Mathf.Infinity;

                        foreach (var blocker in blockers)
                        {
                            var sqrDst = (blocker.transform.position - unit.transform.position).sqrMagnitude;
                            if (sqrDst < nearSqrDst)
                            {
                                nearSqrDst = sqrDst;
                                nearNeighbor = blocker;
                            }
                        }

                        if (nearNeighbor != null)
                        {
                            var sign = MathUtil.LeftOf(unit.transform.position, unit.transform.position + sensorDir, nearNeighbor.transform.position);
                            var relativeDirection = nearNeighbor.transform.position - unit.transform.position;

                            var clearDir = Quaternion.Euler(0, Mathf.Sign(sign) * 90, 0) * relativeDirection;
                            clearDir.Normalize();

                            var adjustment = clearDir * (unitComponent.Radius + 0.5f + nearNeighbor.FetchComponent<UnitComponent>().Radius);
                            var targetPos = nearNeighbor.transform.position + adjustment;
                            Debug.DrawLine(unit.transform.position, targetPos, Color.black);

                            var velocityDir = (targetPos - unit.transform.position).normalized;
                            var velocity = velocityDir * unitComponent.Kinematic.SpeedCap;
                            unitComponent.Agent.prefVelocity_ = new RVO.Vector2(velocity);

                            // Debug.DrawRay(unit.transform.position, adjustment * 5, Color.black);
                            // Debug.DrawRay(unit.transform.position, velocity * 10, Color.magenta);
                        }
                        else
                        {
                            var velocity = unitComponent.Kinematic.Velocity + steeringResult.Acceleration * Time.fixedDeltaTime;
                            unitComponent.Agent.prefVelocity_ = new RVO.Vector2(velocity);
                        }
                    }
                    else
                    {
                        // not hits - can go directly towards target
                        var velocity = unitComponent.Kinematic.Velocity + steeringResult.Acceleration * Time.fixedDeltaTime;
                        unitComponent.Agent.prefVelocity_ = new RVO.Vector2(velocity);
                    }
                }
                else
                {
                    unitComponent.Agent.prefVelocity_ = new RVO.Vector2(0, 0);
                }
            }

            // Debug
            foreach (var unit in units)
            {
                var unitComponent = unit.FetchComponent<UnitComponent>();
                var dir = new Vector3(
                    unitComponent.Agent.prefVelocity_.x_,
                    0,
                    unitComponent.Agent.prefVelocity_.y_);
                Debug.DrawRay(unit.transform.position, dir, Color.yellow);
            }
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

            foreach (var obstacle in Obstacles)
            {
                var vertices = new List<Vector3>()
                {
                    obstacle.transform.position + Vector3.right * obstacle.transform.localScale.x * 0.5f,
                    obstacle.transform.position + Vector3.forward * obstacle.transform.localScale.x * 0.5f,
                    obstacle.transform.position + Vector3.left * obstacle.transform.localScale.x * 0.5f,
                    obstacle.transform.position + Vector3.back * obstacle.transform.localScale.x * 0.5f,
                };

                var vertices2D = new List<RVO.Vector2>();
                foreach (var vertex in vertices)
                {
                    vertices2D.Add(new RVO.Vector2(vertex));
                }

                Debug.Log(RVO.Simulator.Instance.addObstacle(vertices2D));
            }

            RVO.Simulator.Instance.kdTree_.buildObstacleTree();
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
