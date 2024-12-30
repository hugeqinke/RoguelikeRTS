using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public class UnitController : MonoBehaviour, IComponent
{

    // Spherical selection triggers
    // I could just have a singular OBB trigger instead but that requires some 
    // additional code for the frustrum check, and woudl have
    // problems of its own, i.e. overtextended selection regions
    // on the corners for larger models and slightly more expensive computation
    public List<SphereCollider> SelectionTriggers;
    public GameObject SelectionHighlight;

    public float Radius;

    public Kinematic Kinematic;
    public BasicMovement BasicMovement;
    public Owner Owner;

    public GameObject Target;
    public float AttackRadius;
    public bool Attacking;

    public bool InAlertRange;

    public Config Config;
}

[System.Serializable]
public struct Config
{
    // Metadata
    public Owner Owner;
    public float Radius;

    // Movement
    public float Force;
    public float MaxSpeed;
    public float Mass;
    public float Friction;
    public float MaxAcceleration;
    public float MaxDeceleration;
    public float ReturnRadius;
    public float TimeHorizon;

    // Arrive
    public float SlowRadius;
    public float StopRadius;
}

public struct MovementComponent
{
    // Meta
    public Owner Owner;
    public float Radius;

    // Config
    public float MaxSpeed;
    public float ReturnRadius;
    public float TimeHorizon;

    // State
    public bool Resolved;
    public bool HoldingPosition;
    public float LastPushedByFriendlyNeighborTime;
    public float LastMoveTime; // Last TimeStep where velocity sqrDist was greater than zero
    public int SidePreference; // Side preference key: -1 -> left / 0 -> none / 1 -> right

    // Physics
    public float Force;
    public float Mass;
    public float Friction;
    public float3 PreferredPosition;
    public float3 PreferredVelocity;
    public float3 Velocity;
    public float3 Position;
    public float Orientation;

    // Arrive
    public float SlowRadius;
    public float SlowRadiusSq;
    public float StopRadius;
    public float StopRadiusSq;

    public MovementComponent(UnitController unitController)
    {
        Owner = unitController.Config.Owner;
        Radius = unitController.Config.Radius;

        MaxSpeed = unitController.Config.MaxSpeed;
        ReturnRadius = unitController.Config.ReturnRadius;
        TimeHorizon = unitController.Config.TimeHorizon;

        Resolved = true;
        HoldingPosition = false;
        LastPushedByFriendlyNeighborTime = -math.INFINITY;
        LastMoveTime = -math.INFINITY;
        SidePreference = 0;

        Force = unitController.Config.Force;
        Mass = unitController.Config.Mass;
        Friction = unitController.Config.Friction;
        PreferredPosition = unitController.transform.position;
        PreferredVelocity = float3.zero;
        Velocity = float3.zero;
        Position = unitController.transform.position;
        Orientation = 0;

        SlowRadius = unitController.Config.SlowRadius;
        StopRadius = unitController.Config.StopRadius;

        SlowRadiusSq = SlowRadius * SlowRadius;
        StopRadiusSq = StopRadius * StopRadius;
    }
}

[System.Serializable]
public struct Kinematic
{
    public float SpeedCap;
    public Vector3 PreferredVelocity;

    public Vector3 Velocity;
    public Vector3 Position;
    public float Orientation;
}

[System.Serializable]
public class BasicMovement
{
    public Vector3 TargetPosition;
    public float TargetOrientation;

    public Vector3 RelativeDeltaStart;
    public bool Resolved = true;
    public bool HoldingPosition;
    public float ReturnRadius;
    public float LastPushedByFriendlyNeighborTime;
    public float LastMoveTime = Mathf.NegativeInfinity; // Last TimeStep where velocity sqrDist was greater than zero
    public bool DBG;

    // Avoidance
    public float TimeHorizon;

    /* Side preference key:
       -1 -> left
        0 -> none
        1 -> right        
    */
    public int SidePreference;
}

public enum Owner
{
    Player,
    AI
}