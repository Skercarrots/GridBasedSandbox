using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  VoxelBlockPlacer  — Bridges player input with VoxelWorldManager.
//
//  DESIGN INTENT
//  Your existing SimpleObjectPlacer/GridSystem are left UNTOUCHED.
//  This script is a NEW, parallel placer that handles voxel blocks only.
//  Both can coexist in the same scene; the inventory system is shared.
//
//  HOW IT WORKS
//  1. On right-click: raycasts against chunk colliders, determines which
//     block face was hit, selects the adjacent empty cell, and calls
//     VoxelWorldManager.TrySetBlock() with the selected block id.
//  2. On left-click: removes the hit block (sets to Air, id 0).
//  3. Uses the same hit-bias trick as your original SimpleObjectPlacer to
//     avoid floating-point edge hits registering on the wrong block.
//
//  BLOCK ID SOURCE
//  ItemData can optionally carry a voxelBlockId field.
//  If the selected inventory item has voxelBlockId > 0, it is used.
//  This lets your existing inventory system drive block placement with
//  zero changes to InventoryManager.cs.
//
//  ENABLING / DISABLING
//  Call SetActive(true/false) from GameInputManager or toggle
//  via the public IsActive property. When disabled, no raycasts are fired.
// ─────────────────────────────────────────────────────────────────────────────

public class VoxelBlockPlacer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private VoxelWorldManager worldManager;
    [SerializeField] private VoxelWorldSettings settings;
    [SerializeField] private Camera playerCamera;

    [Header("Settings")]
    [SerializeField] private float maxReach       = 6f;
    [SerializeField] private LayerMask chunkLayer;

    [Header("Default block to place (when no item selected)")]
    [SerializeField] private byte defaultBlockId = 3; // e.g. Grass

    // Small offset to push the sample point inside the face we hit
    private const float HIT_BIAS = 0.001f;

    public bool IsActive { get; set; } = true;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Start()
    {
        if (playerCamera == null) playerCamera = Camera.main;
    }

    // ── Public API (call from GameInputManager) ────────────────────────────────

    /// <summary>
    /// Places a block in the cell ADJACENT to the hit face.
    /// Pass the blockId to place (0 = Air = no-op for placement).
    /// </summary>
    public void PlaceBlock(byte blockId = 0)
    {
        if (!IsActive || worldManager == null) return;

        if (!Raycast(out RaycastHit hit)) return;

        // The placement position is one step OUTSIDE the hit block (along its normal)
        Vector3 placePoint = hit.point + hit.normal * HIT_BIAS;
        Vector3Int blockPos = WorldPointToBlockCoord(placePoint);

        byte id = blockId > 0 ? blockId : defaultBlockId;
        worldManager.TrySetBlock(blockPos.x, blockPos.y, blockPos.z, id);
    }

    /// <summary>Removes (sets to Air) the block that was directly hit.</summary>
    public void RemoveBlock()
    {
        if (!IsActive || worldManager == null) return;

        if (!Raycast(out RaycastHit hit)) return;

        // Remove the hit block — bias INWARD so we land inside the block
        Vector3 removePoint = hit.point - hit.normal * HIT_BIAS;
        Vector3Int blockPos = WorldPointToBlockCoord(removePoint);

        worldManager.TrySetBlock(blockPos.x, blockPos.y, blockPos.z, 0);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool Raycast(out RaycastHit hit)
    {
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        return Physics.Raycast(ray, out hit, maxReach, chunkLayer);
    }

    /// <summary>
    /// Converts a world-space float point to integer block coordinates.
    /// Uses FloorToInt for correct negative-coord handling.
    /// </summary>
    public static Vector3Int WorldPointToBlockCoord(Vector3 point)
    {
        return new Vector3Int(
            Mathf.FloorToInt(point.x),
            Mathf.FloorToInt(point.y),
            Mathf.FloorToInt(point.z));
    }
}
