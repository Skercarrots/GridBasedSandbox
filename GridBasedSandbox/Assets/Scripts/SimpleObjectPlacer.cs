using UnityEngine;

public class SimpleObjectPlacer : MonoBehaviour
{
    private Camera playerCamera;
    [SerializeField] private float maxRayDistance = 5f;
    [SerializeField] private GridSystem gridSystem;
    [SerializeField] private InventoryManager inventoryManager;
    [SerializeField] private LayerMask placedObjectLayer;

    private int _placedLayerIndex;       // layer index  (e.g. 8)
    private int _placedLayerMask;        // bitmask      (e.g. 1 << 8 = 256)

    private const float HIT_BIAS = 0.001f;

    private void Start()
    {
        if (gridSystem == null) Debug.LogWarning("SimpleObjectPlacer: GridSystem não está referenciado!");
        if (playerCamera == null) playerCamera = Camera.main;

        // Resolve once here — used everywhere else
        _placedLayerIndex = LayerMaskToIndex(placedObjectLayer);
        _placedLayerMask  = 1 << _placedLayerIndex;
    }

    private Vector3Int? GetGridIndexAtPosition()
    {
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance))
        {
            Vector3 adjustedPoint = hit.point + (hit.normal * HIT_BIAS);
            return gridSystem.WorldToGridPosition(adjustedPoint);
        }
        return null;
    }

    public Vector3Int? GetCellFromPlacedItem()
    {
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        Debug.DrawRay(ray.origin, ray.direction * maxRayDistance, Color.red, 2f);

        // Uses the pre-built bitmask — guaranteed to match what was assigned
        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, _placedLayerMask))
        {
            PlacedItem placedItem = hit.collider.GetComponentInParent<PlacedItem>();

            if (placedItem != null)
            {
                // Use the object's own position, not the ray surface contact point
                return gridSystem.WorldToGridPosition(placedItem.transform.position);
            }

            Debug.LogWarning("O objeto atingido não possui o componente PlacedItem.");
        }

        return null;
    }

    public void PlaceObjectInCell()
    {
        // 1. Validação inicial: se não houver posição, saímos logo
        if (!(GetGridIndexAtPosition() is Vector3Int gridIndex))
        {
            //Debug.Log("Nenhuma célula válida selecionada.");
            return;
        }

        // 2. Validação de estado: se estiver ocupado, saímos logo
        if (gridSystem.IsCellOccupied(gridIndex))
        {
            //Debug.Log($"A célula {gridIndex} já está ocupada!");
            return;
        }

        // 3. Rota principal: o código que "faz o trabalho" fica sem indentação profunda
        Vector3 cellCenter = gridSystem.GridToWorldPosition(gridIndex);
        Vector3 placementPos = new Vector3(
            cellCenter.x,
            cellCenter.y - (gridSystem.cellSize / 2f),
            cellCenter.z
        );

        ItemData selectedItem = inventoryManager.GetSelectedItem();
        if (selectedItem == null)
        {
            //Debug.Log("Nenhum item selecionado para colocar.");
            return;
        }

        GameObject newObj = Instantiate(selectedItem.itemPrefab, placementPos, Quaternion.identity);
        PlacedItem newObjItem = newObj.AddComponent<PlacedItem>();
        newObjItem.SetItemData(selectedItem);
        SetLayerRecursively(newObj, _placedLayerIndex);

        gridSystem.PlaceObjectInCell(gridIndex, newObj);
    }

    public void RemoveObjectFromCell()
    {
        if(!(GetCellFromPlacedItem() is Vector3Int gridIndex))
        {
            //Debug.Log("Nenhuma célula válida selecionada para remoção.");
            return;
        }
        gridSystem.RemoveObjectFromCell(gridIndex);
    }

    private void SetLayerRecursively(GameObject obj, int layer)
    {
        foreach (Transform t in obj.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
    }

    private int LayerMaskToIndex(LayerMask mask)
    {
        if (mask.value == 0)
        {
            Debug.LogWarning("SimpleObjectPlacer: placedObjectLayer não está configurado! Usando layer 0.");
            return 0;
        }

        int value = mask.value;
        int index = 0;
        while ((value & 1) == 0) { value >>= 1; index++; }
        return index;
    }

    public PlacedItem GetPlacedItemUnderCursor()
    {
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, _placedLayerMask))
            return hit.collider.GetComponentInParent<PlacedItem>();
        return null;
    }
}