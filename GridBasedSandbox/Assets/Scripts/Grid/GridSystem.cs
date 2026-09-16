using System.Collections.Generic;
using UnityEngine;

public class GridCell
{
    public Vector3Int GridPosition;
    public bool IsOccupied;
    public GameObject OccupyingObject; 

    public void Occupy(GameObject obj)
    {
        OccupyingObject = obj;
        IsOccupied = true;
    }

    public void Clear()
    {    
        GameObject.Destroy(OccupyingObject);
        OccupyingObject = null;
        IsOccupied = false;
    }
}

public class GridSystem : MonoBehaviour
{
    [Header("Basic Settings")]
    public float cellSize = 1f;
    public Vector3 gridOffset = Vector3.zero;
    
    [Header("Alignment Settings")]
    public bool snapToCenter = true; // If true, aligns to center. If false, aligns to corner (0,0,0).

    [Header("Visualization (Gizmos)")]
    public bool showGizmos = true;
    public int visualRange = 5;

    private Dictionary<Vector3Int, GridCell> gridMap = new Dictionary<Vector3Int, GridCell>();

    /// <summary>
    /// Converts a 3D world position into a discrete grid coordinate.
    /// </summary>
    /// <param name="worldPosition">The position in world space.</param>
    /// <returns>The corresponding <see cref="Vector3Int"/> grid coordinate.</returns>
    public Vector3Int WorldToGridPosition(Vector3 worldPosition)
    {
        int x = Mathf.FloorToInt((worldPosition.x - gridOffset.x) / cellSize);
        int y = Mathf.FloorToInt((worldPosition.y - gridOffset.y) / cellSize);
        int z = Mathf.FloorToInt((worldPosition.z - gridOffset.z) / cellSize);
        
        return new Vector3Int(x, y, z);
    }

    /// <summary>
    /// Converts a grid coordinate into a 3D world position, taking into account cell size, offset, and center alignment.
    /// </summary>
    /// <param name="gridPosition">The discrete grid coordinate.</param>
    /// <returns>The calculated world position Vector3.</returns>
    public Vector3 GridToWorldPosition(Vector3Int gridPosition)
    {
        // If snapToCenter is true, add half the cell size to center it.
        float offset = snapToCenter ? (cellSize / 2f) : 0f;

        return new Vector3(
            (gridPosition.x * cellSize) + offset,
            (gridPosition.y * cellSize) + offset,
            (gridPosition.z * cellSize) + offset
        ) + gridOffset;
    }

    /// <summary>
    /// Retrieves an existing <see cref="GridCell"/> at the specified world position, or creates a new one if it doesn't exist yet.
    /// </summary>
    /// <param name="worldPosition">The world space position to look up.</param>
    /// <returns>The existing or newly created <see cref="GridCell"/>.</returns>
    public GridCell GetOrCreateCell(Vector3 worldPosition)
    {
        Vector3Int gridPos = WorldToGridPosition(worldPosition);

        if (!gridMap.ContainsKey(gridPos))
        {
            gridMap[gridPos] = new GridCell { GridPosition = gridPos, IsOccupied = false };
        }

        return gridMap[gridPos];
    }

    /// <summary>
    /// Checks whether the cell at the specified grid coordinate is currently occupied.
    /// </summary>
    /// <param name="gridPos">The grid coordinate to query.</param>
    /// <returns><c>true</c> if occupied; otherwise <c>false</c>.</returns>
    public bool IsCellOccupied(Vector3Int gridPos)
    {
        if (gridMap.TryGetValue(gridPos, out GridCell cell))
        {
            return cell.IsOccupied;
        }
        return false;
    }

    /// <summary>
    /// Associates a GameObject with the cell at the specified grid coordinate and marks it as occupied.
    /// </summary>
    /// <param name="gridPos">The grid coordinate.</param>
    /// <param name="obj">The GameObject occupying the cell.</param>
    public void PlaceObjectInCell(Vector3Int gridPos, GameObject obj)
    {
        GridCell cell = GetOrCreateCell(GridToWorldPosition(gridPos));
        cell.Occupy(obj);
    }

    /// <summary>
    /// Removes and destroys the occupying GameObject from the cell at the given grid coordinate, clearing the cell.
    /// </summary>
    /// <param name="gridPos">The grid coordinate to clear.</param>
    public void RemoveObjectFromCell(Vector3Int gridPos)
    {
        if (gridMap.TryGetValue(gridPos, out GridCell cell))
        {
            cell.Clear();
        }
    }

    /// <summary>
    /// Generates a list of random grid coordinates within the configured visual range (useful for debugging/testing).
    /// </summary>
    /// <param name="count">Number of random positions to generate.</param>
    public List<Vector3Int> GetRandomGridPositionsInTheWorld(int count)
    {
        List<Vector3Int> positions = new List<Vector3Int>();

        for (int i = 0; i < count; i++)
        {
            int x = Random.Range(-visualRange, visualRange + 1);
            int y = Random.Range(-visualRange, visualRange + 1);
            int z = Random.Range(-visualRange, visualRange + 1);
            positions.Add(new Vector3Int(x, y, z));
        }

        return positions;
    }

    private void OnDrawGizmos()
    {
        if (!showGizmos) return;

        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.3f);

        for (int x = -visualRange; x <= visualRange; x++)
        {
            for (int y = -visualRange; y <= visualRange; y++)
            {
                for (int z = -visualRange; z <= visualRange; z++)
                {
                    Vector3Int posIndex = new Vector3Int(x, y, z);
                    Vector3 worldCenter = GridToWorldPosition(posIndex);
                    
                    // If not centered, adjust the Gizmo center for correct visualization
                    Vector3 drawPos = snapToCenter ? worldCenter : worldCenter + (Vector3.one * (cellSize / 2f));
                    
                    Gizmos.DrawWireCube(drawPos, Vector3.one * cellSize);
                }
            }
        }
    }
}