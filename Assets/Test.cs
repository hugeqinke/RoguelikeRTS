using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class Test : MonoBehaviour
{
    public Vector3 Direction;
    public Rigidbody Rigidbody;
    public float MaxSpeed;

    // Start is called before the first frame update
    void Start()
    {
        Rigidbody = GetComponent<Rigidbody>();
    }

    // Update is called once per frame
    void Update()
    {
        ReadInput();
    }

    private void FixedUpdate()
    {
        Rigidbody.velocity = Direction * MaxSpeed;
    }

    private void ReadInput()
    {
        Direction = Vector3.zero;
        if (Keyboard.current.wKey.isPressed)
        {
            Direction.z += 1;
        }
        if (Keyboard.current.sKey.isPressed)
        {
            Direction.z -= 1;
        }
        if (Keyboard.current.aKey.isPressed)
        {
            Direction.x -= 1;
        }
        if (Keyboard.current.dKey.isPressed)
        {
            Direction.x += 1;
        }

        Direction.Normalize();
    }
}
