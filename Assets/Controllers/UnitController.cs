using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;

public class UnitController : MonoBehaviour, IComponent
{
    // Spherical selection triggers
    // I could just have a singular OBB trigger instead but that requires some 
    // additional code for the frustrum check, and woudl have
    // problems of its own, i.e. overtextended selection regions
    // on the corners for larger models and slightly more expensive computation
    public List<SphereCollider> SelectionTriggers;
    public GameObject SelectionHighlight;

    public Config Config;
    public MovementComponent DBG_Movement;

    public bool DBG_Orientation;
}

[System.Serializable]
public struct Config
{
    // Metadata
    public Owner Owner;
    public float Radius;

    // Movement
    public float MaxSpeed;
    public float Mass;
    public float MaxAcceleration;
    public float TimeHorizon;

    public float PushDuration;

    public bool HoldingPosition;
}

[System.Serializable]
public struct MovementComponent
{
    // Meta
    public Owner Owner;
    public float Radius;

    // Config
    public float MaxSpeed;
    public float Acceleration;
    public float TimeHorizon;

    // State + Movement heuristics
    public bool Resolved;
    public bool HoldingPosition;
    public int SidePreference; // Side preference key: -1 -> left / 0 -> none / 1 -> right

    // Pushing
    public float PushDuration;
    public float LastPushTime;

    public float ResetTargetPositionDuration;
    public float ResetTargetPositionElapsed;

    // Combat
    public int Target;
    public bool Attacking;

    // Physics
    public float Mass;
    public float3 Velocity;
    public float3 MoveStartPosition;
    public float3 Position;
    public float3 OldPosition;
    public float3 TargetPosition;
    public float3 StopPosition; // This is where the unit actually stopped
    public float Orientation;

    public float3 PreferredDir;

    public int CurrentGroup;

    public MovementComponent(UnitController unitController)
    {
        Owner = unitController.Config.Owner;
        Radius = unitController.Config.Radius;

        MaxSpeed = unitController.Config.MaxSpeed;
        Acceleration = unitController.Config.MaxAcceleration;
        TimeHorizon = unitController.Config.TimeHorizon;

        Resolved = true;
        HoldingPosition = unitController.Config.HoldingPosition;
        SidePreference = 0;

        PushDuration = unitController.Config.PushDuration;
        LastPushTime = 0f;

        ResetTargetPositionDuration = 0f;
        ResetTargetPositionElapsed = 0f;

        Target = -1;
        Attacking = false;

        Mass = unitController.Config.Mass;
        Velocity = float3.zero;
        MoveStartPosition = unitController.transform.position;
        Position = unitController.transform.position;
        StopPosition = unitController.transform.position;
        OldPosition = unitController.transform.position;
        TargetPosition = unitController.transform.position;
        Orientation = 0;

        PreferredDir = float3.zero;

        CurrentGroup = -1;
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