using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Simulator : MonoBehaviour
{
    public float NeighborScanRadius;
    public float TargetInfluenceRadius;
    public float PushableRadius;
    public float ResponseCoefficient;

    public InputManager InputManager;
    public static Entity Entity;

    private void Awake()
    {
        Entity = new Entity();
    }

    // Start is called before the first frame update
    void Start()
    {
        LoadPlayerUnits();
    }

    private void FixedUpdate()
    {
        var units = Entity.Fetch(new List<System.Type>()
        {
            typeof(UnitComponent)
        });

        // Iterate through units with active commands
        foreach (var moveGroup in InputManager.MoveGroups)
        {
            foreach (var unit in moveGroup.Units)
            {
                var unitComponent = unit.FetchComponent<UnitComponent>();
                if (unitComponent.BasicMovement.Resolved)
                {
                    continue;
                }
                else
                {
                    // Check stop
                    if (TriggerStop(unit, moveGroup))
                    {
                        unitComponent.BasicMovement.Resolved = true;
                    }

                    DecidePhysicsAction(unit, unitComponent);
                }
            }
        }

        foreach (var unit in units)
        {
            var neighbors = GetNeighbors(unit);
            // resolve collision
            foreach (var neighbor in neighbors)
            {
                ResolveCollision(unit, neighbor);
            }
        }
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
            var dst = Mathf.Sqrt(sqrDst);

            var delta = ResponseCoefficient * 0.5f * (thresholdRadius - dst);
            var pushVec = relativeDir.normalized * delta;

            unitComponent.Kinematic.Position += pushVec;
            unitComponent.transform.position = unitComponent.Kinematic.Position;

            neighborUnitComponent.Kinematic.Position -= pushVec;
            neighbor.transform.position = neighborUnitComponent.Kinematic.Position;
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
        var angle = Vector3.Dot(currentDir.normalized, unitComponent.BasicMovement.RelativeDeltaStart.normalized);
        if (angle < Mathf.Cos(90))
        {
            Debug.Log("Activated");
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
                    if (NearTarget(unit) && Vector3.Dot(unitVelocity, neighborUnitVelocity) < Mathf.Cos(90))
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

    private void DecidePhysicsAction(GameObject unit, UnitComponent unitComponent)
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
            dst -= (neighborRadius + unitRadius);

            if (dst < NeighborScanRadius)
            {
                neighbors.Add(neighbor);
            }
        }

        return neighbors;
    }

    private void LoadPlayerUnits()
    {
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


    // Update is called once per frame
    void Update()
    {

    }
}

public class UnitState
{
    public UnitComponent UnitComponent;
    public GameObject CollidingNeighbor;
    public bool Arrived;
}
