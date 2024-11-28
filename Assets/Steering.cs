using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Steering
{
    public static float TimeToTarget = 0.05f;

    public class Result
    {
        public Vector3 Acceleration;
    }


    public static class Steerable
    {
        [System.Serializable]
        public class Parameters
        {
            public float MaxAcceleration;
            public float MaxSpeed;
        }

        [System.Serializable]
        public class Component
        {
            public Vector3 Velocity;
            public Vector3 Position;

            public float Orientation;
        }
    }

    public static class ArriveBehavior
    {
        public static Result GetSteering(Kinematic kinematic, Arrive arrive, Vector3 targetPosition)
        {
            var dir = targetPosition - kinematic.Position;
            var dst = dir.magnitude;

            if (dst <= arrive.StopRadius)
            {
                // Offload to whatever stop logic is upstream
                return null;
            }

            float speed;
            if (dst <= arrive.StopRadius)
            {
                speed = kinematic.SpeedCap * (dst / arrive.StopRadius);
            }
            else
            {
                speed = kinematic.SpeedCap;
            }

            dir.Normalize();

            var targetVelocity = speed * dir;
            var acceleration = (targetVelocity - kinematic.Velocity) / TimeToTarget;

            acceleration = Vector3.ClampMagnitude(acceleration, kinematic.AccelerationCap);
            return new Result()
            {
                Acceleration = acceleration
            };
        }
    }
}

