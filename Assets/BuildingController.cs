using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BuildingController : MonoBehaviour
{
    public CircleCollider2D Collider;

    public Infrastructure.BuildingType BuildingType;

    private void Start()
    {
        Collider = GetComponent<CircleCollider2D>();
    }
}
