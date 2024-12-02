using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Simulator : MonoBehaviour
{
    public float NeighborScanRadius;
    public float InTargetRadius;
    public float ResponseCoefficient;

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

        foreach (var unit in units)
        {
            var unitComponent = unit.FetchComponent<UnitComponent>();

            // Decide state
            var neighbors = GetNeighbors(unit);
            var collidingNeighbor = GetCollidingNeighbor(unit, neighbors);
            var shouldStop = ShouldStop(unit, collidingNeighbor);

            var unitState = new UnitState()
            {
                UnitComponent = unitComponent,
                CollidingNeighbor = collidingNeighbor,
                Arrived = ArrivedAtTarget(unit),
                ShouldStop = shouldStop
            };

            if (unitState.CollidingNeighbor != null)
            {
                Debug.DrawLine(
                    unit.transform.position,
                    unitState.CollidingNeighbor.transform.position,
                    Color.magenta);
            }

            DecidePhysicsAction(unit, unitComponent, unitState);
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

        var relativeDir = unitComponent.Kinematic.Position - neighborUnitComponent.Kinematic.Position;
        relativeDir.y = 0;

        var sqrDst = relativeDir.sqrMagnitude;
        if (sqrDst < Mathf.Epsilon)
        {
            relativeDir = Vector3.right * 0.0001f;
            sqrDst = relativeDir.sqrMagnitude;
        }

        var thresholdRadius = (unitComponent.Radius + neighborUnitComponent.Radius);

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

    private bool ShouldStop(GameObject unit, GameObject collidingNeighbor)
    {

        var unitComponent = unit.FetchComponent<UnitComponent>();
        var sqrDst = (unit.transform.position - unitComponent.Kinematic.Position).sqrMagnitude;

        if (sqrDst > InTargetRadius)
        {
            return false;
        }
        else
        {
            var desiredVelocity = (unitComponent.BasicMovement.TargetPosition - unit.transform.position).normalized * unitComponent.Kinematic.SpeedCap;

            if (collidingNeighbor != null)
            {
                var neighborUnitComponent = collidingNeighbor.FetchComponent<UnitComponent>();
                var directional = Vector3.Dot(
                    neighborUnitComponent.Kinematic.Velocity,
                    desiredVelocity);

                if (neighborUnitComponent.Kinematic.Velocity.sqrMagnitude < Mathf.Epsilon || directional > 0)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void DecidePhysicsAction(
            GameObject unit,
            UnitComponent unitComponent,
            UnitState unitState)
    {
        if (unitState.Arrived)
        {
            ForceStop(unit);
        }
        else
        {
            if (unitState.ShouldStop)
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
    }

    private void ForceStop(GameObject unit)
    {
        var unitComponent = unit.FetchComponent<UnitComponent>();
        unitComponent.BasicMovement.TargetPosition = unit.transform.position;
        unitComponent.Kinematic.Velocity = Vector3.zero;
    }

    private bool ArrivedAtTarget(GameObject unit)
    {
        var unitComponent = unit.GetComponent<UnitComponent>();
        var position = unitComponent.Kinematic.Position;
        var targetPosition = unitComponent.BasicMovement.TargetPosition;

        var stopRadius = unitComponent.Arrive.StopRadius;
        return (position - targetPosition).sqrMagnitude <= stopRadius * stopRadius;
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
    public bool ShouldStop;
}
