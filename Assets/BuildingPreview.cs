using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BuildingPreview : MonoBehaviour
{
    public int Rows;
    public int Columns;

    private InfrastructureManager _infrastructureManager;

    private void Start()
    {
    }

    private void OnDrawGizmos()
    {
        if (_infrastructureManager == null)
        {
            _infrastructureManager = GameObject
                .FindGameObjectWithTag(Util.Tags.InfrastructureManager)
                .GetComponent<InfrastructureManager>();
        }

        var cellSize = _infrastructureManager.Grid.Cell.transform.localScale.x;
        var origin = transform.position;
        origin.x -= Columns * cellSize * 0.5f;
        origin.y -= Rows * cellSize * 0.5f;

        origin.x += cellSize * 0.5f;
        origin.y += cellSize * 0.5f;

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                var position = origin;
                position.x += col * cellSize;
                position.y += row * cellSize;
                Gizmos.DrawWireCube(position, new Vector3(cellSize, cellSize, cellSize));
            }
        }
    }
}
