using UnityEngine;

// CHANGED: Cross-occupancy check against VoxelWorldManager.
// PlaceObjectInCell() now refuses to place an entity in a grid cell that
// contains solid voxel terrain. This fixes the bug where you could spawn
// a robot inside a solid block.
//
// The check is one IsSolidBlock() call — no raycasts, no allocs.
// It uses VoxelWorldManager.Instance (singleton) so no Inspector ref is
// needed; SimpleObjectPlacer was never aware of the voxel world before, and
// a singleton read is the minimal-coupling way to add this.
//
// HOW IT WORKS WITH RAYCASTER ADJUSTED POINT:
//   When the player hits a top face (normal = up), WorldRaycaster.AdjustedPoint
//   is nudged slightly above the surface, e.g. Y ≈ 4.001 for terrain at Y = 3.
//   GridSystem.WorldToGridPosition floors that to grid Y = 4 — the empty cell
//   above the block. IsSolidBlock(x, 4, z) returns false → entity placed. ✓
//   If instead there IS a solid block at Y = 4 (e.g. placing against a wall
//   from the side), IsSolidBlock returns true → placement blocked. ✓

public class SimpleObjectPlacer : MonoBehaviour
{
    [SerializeField] private GridSystem gridSystem;
    [SerializeField] private InventoryManager inventoryManager;
    [SerializeField] private LayerMask placedObjectLayer;

    private int _placedLayerIndex;

    private void Start()
    {
        if (gridSystem == null) Debug.LogWarning("SimpleObjectPlacer: GridSystem não está referenciado!");
        _placedLayerIndex = LayerMaskToIndex(placedObjectLayer);
    }

    private Vector3Int? GetGridIndexAtPosition()
    {
        var raycaster = WorldRaycaster.Instance;
        if (raycaster == null || !raycaster.HasHit) return null;
        return gridSystem.WorldToGridPosition(raycaster.AdjustedPoint);
    }

    public Vector3Int? GetCellFromPlacedItem()
    {
        var raycaster = WorldRaycaster.Instance;
        if (raycaster == null || !raycaster.HasHit) return null;

        PlacedItem placedItem = raycaster.Collider.GetComponentInParent<PlacedItem>();
        if (placedItem == null)
        {
            Debug.LogWarning("O objeto atingido não possui o componente PlacedItem.");
            return null;
        }

        return gridSystem.WorldToGridPosition(placedItem.transform.position);
    }

    public void PlaceObjectInCell()
    {
        if (!(GetGridIndexAtPosition() is Vector3Int gridIndex))
            return;

        if (gridSystem.IsCellOccupied(gridIndex))
            return;

        // ── CHANGED: cross-occupancy guard against voxel terrain ─────────────
        // Prevents spawning an entity inside a solid block. Uses the same
        // integer cell space as the voxel grid (GridSystem.cellSize = 1,
        // gridOffset = 0 — keep these at their defaults).
        var world = VoxelWorldManager.Instance;
        if (world != null && world.IsSolidBlock(gridIndex.x, gridIndex.y, gridIndex.z))
        {
            // Silent no-op: voxel terrain is occupying this cell — can't place here.
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
        var raycaster = WorldRaycaster.Instance;
        return raycaster != null ? raycaster.HoveredEntity : null;
    }
}