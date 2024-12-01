using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Simulator : MonoBehaviour
{
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
