using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Assertions.Must;

public class TestSimulator : MonoBehaviour
{
    public List<TestUnit> TestUnits;
    public float Slop;
    public int Substeps;

    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        var sdt = Time.fixedDeltaTime / Substeps;

        for (int substep = 0; substep < Substeps; substep++)
        {
            for (int i = 0; i < TestUnits.Count; i++)
            {
                var unit = TestUnits[i];

                var dir = (Vector3.zero - unit.transform.position).normalized;
                unit.Velocity += dir * unit.Acceleration * sdt;

                unit.Velocity = Vector3.ClampMagnitude(unit.Velocity, unit.MaxSpeed);
                unit.OldPosition = unit.transform.position;
                unit.transform.position += unit.Velocity * sdt;
            }

            for (int i = 0; i < TestUnits.Count - 1; i++)
            {
                for (int j = i + 1; j < TestUnits.Count; j++)
                {
                    var bodyi = TestUnits[i];
                    var bodyj = TestUnits[j];
                    var dir = bodyj.transform.position - bodyi.transform.position;
                    var separation = dir.magnitude;

                    var collideRadius = bodyi.transform.localScale.x * 0.5f + bodyj.transform.localScale.x * 0.5f;

                    if (separation < collideRadius)
                    {
                        var totalMass = bodyi.Mass + bodyj.Mass;

                        var slop = collideRadius - separation;

                        var x1 = -(bodyj.Mass / totalMass) * slop * dir.normalized;
                        var x2 = bodyi.Mass / totalMass * slop * dir.normalized;

                        bodyi.transform.position += x1;
                        bodyj.transform.position += x2;
                    }
                }
            }

            // end position constraint
            for (int i = 0; i < TestUnits.Count; i++)
            {
                var unit = TestUnits[i];
                var dir = Vector3.zero - unit.transform.position;
                var moveDir = unit.transform.position - unit.OldPosition;

                if (dir.sqrMagnitude < moveDir.sqrMagnitude && Vector3.Cross(dir, moveDir).sqrMagnitude < 0.001f)
                {
                    unit.transform.position = Vector3.zero;
                }
            }

            // fix velocity 
            for (int i = 0; i < TestUnits.Count; i++)
            {
                var unit = TestUnits[i];

                var dir = Vector3.zero - unit.transform.position;

                if (dir.sqrMagnitude < 0.001f)
                {
                    unit.Velocity = Vector3.zero;
                }
                else
                {
                    unit.Velocity = (unit.transform.position - unit.OldPosition) / sdt;
                }
            }
        }
    }

    // Gather collisions

    private ulong GetHash(int i, int j)
    {
        return (0xffffffff00000000 & (ulong)(i << 32)) | (0x00000000ffffffff & (ulong)j);
    }
}
