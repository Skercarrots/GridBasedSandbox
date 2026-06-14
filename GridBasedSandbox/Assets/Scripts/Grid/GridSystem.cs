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
    [Header("Configurações Básicas")]
    public float cellSize = 1f;
    public Vector3 gridOffset = Vector3.zero;
    
    [Header("Configurações de Alinhamento")]
    public bool snapToCenter = true; // Se true, alinha ao centro. Se false, alinha ao canto (0,0,0).

    [Header("Visualização (Gizmos)")]
    public bool showGizmos = true;
    public int visualRange = 5;

    private Dictionary<Vector3Int, GridCell> gridMap = new Dictionary<Vector3Int, GridCell>();

    public Vector3Int WorldToGridPosition(Vector3 worldPosition)
    {
        int x = Mathf.FloorToInt((worldPosition.x - gridOffset.x) / cellSize);
        int y = Mathf.FloorToInt((worldPosition.y - gridOffset.y) / cellSize);
        int z = Mathf.FloorToInt((worldPosition.z - gridOffset.z) / cellSize);
        
        return new Vector3Int(x, y, z);
    }

    public Vector3 GridToWorldPosition(Vector3Int gridPosition)
    {
        // Se snapToCenter for true, adicionamos metade da célula para centralizar.
        float offset = snapToCenter ? (cellSize / 2f) : 0f;

        return new Vector3(
            (gridPosition.x * cellSize) + offset,
            (gridPosition.y * cellSize) + offset,
            (gridPosition.z * cellSize) + offset
        ) + gridOffset;
    }

    public GridCell GetOrCreateCell(Vector3 worldPosition)
    {
        Vector3Int gridPos = WorldToGridPosition(worldPosition);

        if (!gridMap.ContainsKey(gridPos))
        {
            gridMap[gridPos] = new GridCell { GridPosition = gridPos, IsOccupied = false };
        }

        return gridMap[gridPos];
    }

    // Nova função para verificar disponibilidade
    public bool IsCellOccupied(Vector3Int gridPos)
    {
        if (gridMap.TryGetValue(gridPos, out GridCell cell))
        {
            return cell.IsOccupied;
        }
        return false;
    }

    public void PlaceObjectInCell(Vector3Int gridPos, GameObject obj)
    {
        GridCell cell = GetOrCreateCell(GridToWorldPosition(gridPos));
        cell.Occupy(obj);
    }

    public void RemoveObjectFromCell(Vector3Int gridPos)
    {
        if (gridMap.TryGetValue(gridPos, out GridCell cell))
        {
            cell.Clear();
        }
    }

    //Other testing methods (not used in main logic, but can be useful for debugging)
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
                    
                    // Se não estiver centralizado, o centro do Gizmo deve ser ajustado para visualizar corretamente
                    Vector3 drawPos = snapToCenter ? worldCenter : worldCenter + (Vector3.one * (cellSize / 2f));
                    
                    Gizmos.DrawWireCube(drawPos, Vector3.one * cellSize);
                }
            }
        }
    }
}