using UnityEngine;

/// <summary>
/// Handles instantiation and removal of non-voxel grid entities and decorative objects,
/// ensuring cross-occupancy validation with both the grid system and voxel world.
/// </summary>
public class SimpleObjectPlacer : MonoBehaviour
{
    [SerializeField] private GridSystem gridSystem;
    [SerializeField] private InventoryManager inventoryManager;
    [SerializeField] private LayerMask placedObjectLayer;

    private int _placedLayerIndex;

    private void Start()
    {
        if (gridSystem == null) Debug.LogWarning("SimpleObjectPlacer: GridSystem is not referenced!");
        _placedLayerIndex = LayerMaskToIndex(placedObjectLayer);
    }

    private Vector3Int? GetGridIndexAtPosition()
    {
        var raycaster = WorldRaycaster.Instance;
        if (raycaster == null || !raycaster.HasHit) return null;
        return gridSystem.WorldToGridPosition(raycaster.AdjustedPoint);
    }

    /// <summary>
    /// Gets the grid cell coordinate corresponding to the placed item currently targeted by the raycaster.
    /// </summary>
    /// <returns>The grid coordinate, or <c>null</c> if no placed item is targeted.</returns>
    public Vector3Int? GetCellFromPlacedItem()
    {
        var raycaster = WorldRaycaster.Instance;
        if (raycaster == null || !raycaster.HasHit) return null;

        PlacedItem placedItem = raycaster.Collider.GetComponentInParent<PlacedItem>();
        if (placedItem == null)
        {
            Debug.LogWarning("The hit object does not have a PlacedItem component.");
            return null;
        }

        return gridSystem.WorldToGridPosition(placedItem.transform.position);
    }

    /// <summary>
    /// Places the currently selected inventory entity or decorative item into the targeted grid cell.
    /// Checks for cell occupancy and ensures the cell does not intersect solid voxel terrain.
    /// </summary>
    public void PlaceObjectInCell()
    {
        if (!(GetGridIndexAtPosition() is Vector3Int gridIndex))
            return;

        if (gridSystem.IsCellOccupied(gridIndex))
            return;

        // Cross-occupancy guard against solid voxel terrain
        var world = VoxelWorldManager.Instance;
        if (world != null && world.IsSolidBlock(gridIndex.x, gridIndex.y, gridIndex.z))
        {
            return;
        }

        ItemData selectedItem = inventoryManager.GetSelectedItem();
        if (selectedItem == null || !selectedItem.isPlaceable)
            return;

        Vector3 cellCenter = gridSystem.GridToWorldPosition(gridIndex);
        Vector3 placementPos = new Vector3(
            cellCenter.x,
            cellCenter.y - (gridSystem.cellSize / 2f),
            cellCenter.z
        );

        GameObject newObj = Instantiate(selectedItem.itemPrefab, placementPos, Quaternion.identity);
        PlacedItem newObjItem = newObj.AddComponent<PlacedItem>();
        newObjItem.SetItemData(selectedItem);
        SetLayerRecursively(newObj, _placedLayerIndex);

        gridSystem.PlaceObjectInCell(gridIndex, newObj);
    }

    /// <summary>
    /// Removes and destroys the placed object currently targeted under the cursor.
    /// </summary>
    public void RemoveObjectFromCell()
    {
        if (!(GetCellFromPlacedItem() is Vector3Int gridIndex))
            return;
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
            Debug.LogWarning("SimpleObjectPlacer: placedObjectLayer is not configured! Using layer 0.");
            return 0;
        }

        int value = mask.value;
        int index = 0;
        while ((value & 1) == 0) { value >>= 1; index++; }
        return index;
    }

    /// <summary>
    /// Returns the <see cref="PlacedItem"/> currently hovered under the player's crosshair/cursor.
    /// </summary>
    public PlacedItem GetPlacedItemUnderCursor()
    {
        var raycaster = WorldRaycaster.Instance;
        return raycaster != null ? raycaster.HoveredEntity : null;
    }
}