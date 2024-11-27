using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using UnityEngine;
using UnityEngine.UI;

public class BoxRenderer : Graphic
{
    public Vector2 StartPosition
    {
        get { return _startPosition; }
        set
        {
            // Need to compensate for screen shift, since 0,0 starts
            // in one of the corners instead of the middle
            _startPosition = value - new Vector2(Screen.width, Screen.height) * 0.5f;
        }
    }
    private Vector2 _startPosition;

    public Vector2 EndPosition
    {
        get
        {
            return _endPosition;
        }
        set
        {
            // Need to compensate for screen shift, since 0,0 starts
            // in one of the corners instead of the middle
            _endPosition = value - new Vector2(Screen.width, Screen.height) * 0.5f;
        }
    }
    private Vector2 _endPosition;

    public float Thickness;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        // Prepare
        vh.Clear();

        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = Color.white;

        var minVertex = new Vector2(
            Mathf.Min(StartPosition.x, EndPosition.x),
            Mathf.Min(StartPosition.y, EndPosition.y)
        );

        var maxVertex = new Vector2(
            Mathf.Max(StartPosition.x, EndPosition.x),
            Mathf.Max(StartPosition.y, EndPosition.y)
        );

        // v0
        vertex.position = minVertex;
        vh.AddVert(vertex);

        // v1
        vertex.position = new Vector2(maxVertex.x, minVertex.y);
        vh.AddVert(vertex);

        // v2
        vertex.position = new Vector2(maxVertex.x, maxVertex.y);
        vh.AddVert(vertex);

        // v3
        vertex.position = new Vector2(minVertex.x, maxVertex.y);
        vh.AddVert(vertex);

        // v4
        vertex.position = new Vector2(minVertex.x + Thickness, minVertex.y + Thickness);
        vh.AddVert(vertex);

        // v5
        vertex.position = new Vector2(maxVertex.x - Thickness, minVertex.y + Thickness);
        vh.AddVert(vertex);

        // v6
        vertex.position = new Vector2(maxVertex.x - Thickness, maxVertex.y - Thickness);
        vh.AddVert(vertex);

        // v7
        vertex.position = new Vector2(minVertex.x + Thickness, maxVertex.y - Thickness);
        vh.AddVert(vertex);

        // Bottom Left Triangle
        vh.AddTriangle(0, 4, 1);

        // // Bottom Right Triangle
        vh.AddTriangle(1, 4, 5);

        // // Left Bottom Triangle
        vh.AddTriangle(1, 5, 2);

        // // Left Top Triangle
        vh.AddTriangle(2, 5, 6);

        // // Top Left Triangle
        vh.AddTriangle(2, 6, 3);

        // // Top Right Triangle
        vh.AddTriangle(3, 6, 7);

        // // Right Top Triangle
        vh.AddTriangle(3, 7, 0);

        // // Right Bottom Triangle
        vh.AddTriangle(7, 4, 0);
    }
}

