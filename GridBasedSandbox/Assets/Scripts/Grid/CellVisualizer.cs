using UnityEngine;

public class CellVisualizer : MonoBehaviour
{
    private Camera playerCamera;
    public float maxRayDistance = 5f;
    public GameObject cellUIPrefab;
    public GridSystem gridSystem;
    private GameObject cellUIInstance;

    // Ajuste para evitar Z-Fighting
    private const float Z_FIGHTING_OFFSET = 0.01f;
    
    // Pequeno offset para corrigir a precisão do Raycast
    private const float HIT_BIAS = 0.001f;

    private void Start()
    {
        if (gridSystem == null) Debug.LogWarning("CellVisualizer: GridSystem não está referenciado!");
        if (playerCamera == null) playerCamera = Camera.main;
        
        if (cellUIPrefab != null)
        {
            cellUIInstance = Instantiate(cellUIPrefab, Vector3.zero, Quaternion.Euler(90, 0, 0));
            cellUIInstance.SetActive(false);
        }
    }

    private void Update()
    {
        PerformRay();
    }

    private void PerformRay()
    {
        if (playerCamera == null || gridSystem == null) return;

        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance))
        {
            // APLICAÇÃO DO NUDGE:
            // Ajustamos o ponto de cálculo para fora da superfície usando a normal
            Vector3 adjustedPoint = hit.point + (hit.normal * HIT_BIAS);

            Vector3Int gridIndex = gridSystem.WorldToGridPosition(adjustedPoint);
            Vector3 cellCenterWorldPosition = gridSystem.GridToWorldPosition(gridIndex);

            // Ajuste para a base (baseado na metade do tamanho)
            float halfCellSize = gridSystem.cellSize / 2f;
            float baseFloorY = cellCenterWorldPosition.y - halfCellSize;

            cellUIInstance.SetActive(true);
            cellUIInstance.transform.position = new Vector3(
                cellCenterWorldPosition.x,
                baseFloorY + Z_FIGHTING_OFFSET,
                cellCenterWorldPosition.z
            );
        }
        else
        {
            if (cellUIInstance != null) cellUIInstance.SetActive(false);
        }
    }
}