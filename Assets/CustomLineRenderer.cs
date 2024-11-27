using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CustomLineRenderer : MonoBehaviour
{
    public Vector3 StartVertex;
    public Vector3 EndVertex;
    public Quaternion Rotation;
    public float LineThickness;

    private MeshFilter _meshFilter;

    // Start is called before the first frame update
    void Start()
    {
        _meshFilter = GetComponent<MeshFilter>();

        var mesh = new Mesh();
        mesh.vertices = new Vector3[4] {
            new Vector3(),
            new Vector3(),
            new Vector3(),
            new Vector3(),
        };

        mesh.triangles = new int[6]{
            0, 1, 2,
            2, 3, 0
        };
        _meshFilter.mesh = mesh;
    }

    public void Draw()
    {

        var dir = (EndVertex - StartVertex).normalized;
        var forward = Quaternion.Euler(0, 90, 0) * dir * LineThickness;
        var back = Quaternion.Euler(0, -90, 0) * dir * LineThickness;

        var mesh = _meshFilter.mesh;
        var vertices = mesh.vertices;
        vertices[0] = new Vector3(StartVertex.x, StartVertex.y, StartVertex.z) + forward;
        vertices[1] = new Vector3(StartVertex.x, StartVertex.y, StartVertex.z) + back;
        vertices[2] = new Vector3(EndVertex.x, EndVertex.y, EndVertex.z) + back;
        vertices[3] = new Vector3(EndVertex.x, EndVertex.y, EndVertex.z) + forward;

        mesh.vertices = vertices;

        var center = (StartVertex + EndVertex) * 0.5f;
        var minX = Mathf.Min(vertices[0].x, vertices[1].x, vertices[2].x, vertices[3].x);
        var minY = Mathf.Min(vertices[0].y, vertices[1].y, vertices[2].y, vertices[3].y);
        var minZ = Mathf.Min(vertices[0].z, vertices[1].z, vertices[2].z, vertices[3].z);

        var maxX = Mathf.Max(vertices[0].x, vertices[1].x, vertices[2].x, vertices[3].x);
        var maxY = Mathf.Max(vertices[0].y, vertices[1].y, vertices[2].y, vertices[3].y);
        var maxZ = Mathf.Max(vertices[0].z, vertices[1].z, vertices[2].z, vertices[3].z);

        var size = new Vector3(maxX - minX, maxY - minY, maxZ - minZ);
        var bounds = new Bounds(center, size);
        mesh.bounds = bounds;

        _meshFilter.mesh = mesh;
    }
}

