using UnityEngine;

public class CellVisualizer : MonoBehaviour
{
    public GameObject cellUIPrefab;
    public GridSystem gridSystem;
    private GameObject cellUIInstance;

    // Ajuste para evitar Z-Fighting
    private const float Z_FIGHTING_OFFSET = 0.01f;

    private void Start()
    {
        if (gridSystem == null) Debug.LogWarning("CellVisualizer: GridSystem não está referenciado!");

        if (cellUIPrefab != null)
        {
            cellUIInstance = Instantiate(cellUIPrefab, Vector3.zero, Quaternion.Euler(90, 0, 0));
            cellUIInstance.SetActive(false);
        }
    }

    private void Update()
    {
        UpdateHighlight();
    }

    // Reads the shared per-frame hit from WorldRaycaster instead of firing its
    // own Physics.Raycast — see WorldRaycaster.cs for why this used to be one
    // of four near-identical raycasts running every frame, and for why
    // Camera.main was never reliably available here to begin with.
    private void UpdateHighlight()
    {
        if (gridSystem == null || cellUIInstance == null) return;

        var raycaster = WorldRaycaster.Instance;
        if (raycaster == null || !raycaster.HasHit)
        {
            cellUIInstance.SetActive(false);
            return;
        }

        Vector3Int gridIndex = gridSystem.WorldToGridPosition(raycaster.AdjustedPoint);
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
}