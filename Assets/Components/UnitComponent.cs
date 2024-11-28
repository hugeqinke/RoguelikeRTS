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

    public Arrive Arrive;
    public Kinematic Kinematic;
    public BasicMovement BasicMovement;
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

    [ReadOnly] public Vector3 Velocity;
    [ReadOnly] public Vector3 Position;
    [ReadOnly] public float Orientation;
}

[System.Serializable]
public class BasicMovement
{
    [ReadOnly] public Vector3 TargetPosition;
    [ReadOnly] public float TargetOrientation;
}