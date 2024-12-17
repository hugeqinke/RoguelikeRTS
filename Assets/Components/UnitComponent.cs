using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UnitComponent : MonoBehaviour, IComponent
{
    public GameObject SelectionHighlight;

    // Spherical selection triggers
    // I could just have a singular OBB trigger instead
    // but that requires some additional code, and will have
    // problems of its own, i.e. overtextended selection regions
    // on the corners for larger models
    public List<SphereCollider> SelectionTriggers;

    public float Radius;

    public Arrive Arrive;
    public Kinematic Kinematic;
    public BasicMovement BasicMovement;
    public Owner Owner;

    public GameObject Target;
    public float AttackRadius;
    public bool Attacking;

    public bool InAlertRange;
}

[System.Serializable]
public class Arrive
{
    public float SlowThreshold;
    public float StopRadius;
}

[System.Serializable]
public class Kinematic
{
    public float SpeedCap;
    public float AccelerationCap;
    public Vector3 PreferredVelocity;

    [ReadOnly] public Vector3 Velocity;
    [ReadOnly] public Vector3 Position;
    [ReadOnly] public float Orientation;
}

[System.Serializable]
public class BasicMovement
{
    [ReadOnly] public Vector3 TargetPosition;
    [ReadOnly] public float TargetOrientation;

    public Vector3 RelativeDeltaStart;
    public bool Resolved = true;
    public bool HoldingPosition;
    public float ReturnRadius;
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