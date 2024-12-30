using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestUnit : MonoBehaviour
{
    public float Mass;
    public Vector3 Velocity;
    public Vector3 OldPosition;
    public float Acceleration;
    public float Friction;
    public int MaxSpeed;
    public bool Stationary;

    public Vector3 AdjustmentVelocity;

    // Start is called before the first frame update
    void Start()
    {
        OldPosition = transform.position;
    }

    // Update is called once per frame
    void Update()
    {

    }
}
