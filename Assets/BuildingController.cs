using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BuildingController : MonoBehaviour
{
    public int MaxBuildCapacity;
    public int RemainingBuildCapacity;

    public CircleCollider2D Collider;
    public Infrastructure.BuildingType BuildingType;
    public GameObject SelectionCircle;

    public Queue<GameObject> BuildCapacityIcons;
    public GameObject BuildCapacityIcon;
    public float Spacing;
    private float _width;

    private void Start()
    {
        Collider = GetComponent<CircleCollider2D>();
        BuildCapacityIcons = new Queue<GameObject>();
        _width = BuildCapacityIcon.transform.localScale.x;

        UpdateVisuals();
    }

    public void Select()
    {
        SelectionCircle.gameObject.SetActive(true);
    }

    public void Deselect()
    {
        SelectionCircle.gameObject.SetActive(false);
    }

    public void UpdateVisuals()
    {
        var totalWidth = RemainingBuildCapacity * _width + (RemainingBuildCapacity - 1) * Spacing;
        var extent = totalWidth * 0.5f;

        var start = transform.position;
        start.x -= extent;
        start.x += _width * 0.5f;

        if (BuildCapacityIcons.Count > RemainingBuildCapacity)
        {
            var newList = new List<GameObject>();

            var diff = BuildCapacityIcons.Count - RemainingBuildCapacity;
            for (int i = 0; i < diff; i++)
            {
                var icon = BuildCapacityIcons.Dequeue();
                Destroy(icon.gameObject);
            }
        }
        else if (BuildCapacityIcons.Count < RemainingBuildCapacity)
        {
            var diff = RemainingBuildCapacity - BuildCapacityIcons.Count;
            for (int i = 0; i < diff; i++)
            {
                var icon = Instantiate(BuildCapacityIcon);
                BuildCapacityIcons.Enqueue(icon);
            }
        }

        var count = BuildCapacityIcons.Count;
        for (int i = 0; i < count; i++)
        {
            var icon = BuildCapacityIcons.Dequeue();

            var position = start;
            position.x += i * _width + i * Spacing;
            icon.transform.position = position;
            BuildCapacityIcons.Enqueue(icon);
        }
    }
}
