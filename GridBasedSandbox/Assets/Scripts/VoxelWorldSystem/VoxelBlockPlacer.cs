using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  VoxelBlockPlacer  — Bridges player input with VoxelWorldManager.
//
//  CHANGED: Cross-occupancy check against GridSystem.
//  PlaceBlock() now refuses to place a voxel block in any cell that GridSystem
//  considers occupied by a live entity (robot, decorative object, etc.).
//  This fixes the bug where you could place solid terrain through a robot's
//  body. The check is one GridSystem.IsCellOccupied() call — cheap, no physics
//  queries, no extra raycasts. It works because GridSystem.cellSize = 1 and
//  gridOffset = (0,0,0) by convention, so grid cell indices are identical to
//  the integer block coordinates the voxel world uses.
//
//  RemoveBlock() intentionally has NO matching check — removing the block under
//  an entity is fine (the entity falls, physics handles it). The block being
//  removed is terrain, not the entity's cell. Accidental entity removal is
//  already prevented by chunkLayer filtering — hitting an entity's collider
//  doesn't register as a terrain hit, so RemoveBlock() never fires.
//
//  (Original header preserved below)
//
//  DESIGN INTENT
//  Your existing SimpleObjectPlacer/GridSystem are left UNTOUCHED.
//  This script is a NEW, parallel placer that handles voxel blocks only.
//  Both can coexist in the same scene; the inventory system is shared.
// ─────────────────────────────────────────────────────────────────────────────

public class VoxelBlockPlacer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private VoxelWorldManager worldManager;
    [SerializeField] private VoxelWorldSettings settings;

    // ── CHANGED: cross-occupancy ─────────────────────────────────────────────
    [Tooltip("The scene's GridSystem. Used to prevent placing a voxel block in a " +
             "cell already occupied by a live entity. Leave empty to skip the check " +
             "(no crash — placement just won't respect entity occupancy).")]
    [SerializeField] private GridSystem gridSystem;

    [Header("Settings")]
    [Tooltip("Only WorldRaycaster hits on this layer count as terrain for placement/removal — " +
             "keeps this script from reacting to placed objects (robots, buttons, decorations) " +
             "that WorldRaycaster's own hittableLayers mask also includes.")]
    [SerializeField] private LayerMask chunkLayer;

    [Header("Default block to place (when no item selected, or item has no voxelBlockId)")]
    [SerializeField] private byte defaultBlockId = 3; // e.g. Grass

    public bool IsActive { get; set; } = true;

    // ── Public API (call from GameInputManager) ────────────────────────────

    /// <summary>
    /// Places a block in the cell ADJACENT to the hit face.
    /// Pass the blockId to place (0 = fall back to defaultBlockId).
    /// Refuses to place if a live entity (robot, decorative object) already
    /// occupies that cell — see GridSystem cross-occupancy note above.
    /// </summary>
    public void PlaceBlock(byte blockId = 0)
    {
        if (!IsActive || worldManager == null) return;

        if (!TryGetTerrainHit(out Vector3 point, out Vector3 normal)) return;

        Vector3 placePoint = point + normal * WorldRaycaster.HitBias;
        Vector3Int blockPos = WorldPointToBlockCoord(placePoint);

        // ── CHANGED: cross-occupancy guard ────────────────────────────────────
        // Block coords and GridSystem cell coords are the same integer space
        // (cellSize = 1, gridOffset = 0 — don't change these on GridSystem).
        if (gridSystem != null && gridSystem.IsCellOccupied(blockPos))
        {
            // Silent no-op: a live entity is standing in this voxel cell.
            // Don't place terrain through it.
            return;
        }

        byte id = blockId > 0 ? blockId : defaultBlockId;
        worldManager.TrySetBlock(blockPos.x, blockPos.y, blockPos.z, id);
    }

    /// <summary>Removes (sets to Air) the block that was directly hit.</summary>
    public void RemoveBlock()
    {
        if (!IsActive || worldManager == null) return;

        if (!TryGetTerrainHit(out Vector3 point, out Vector3 normal)) return;

        Vector3 removePoint = point - normal * WorldRaycaster.HitBias;
        Vector3Int blockPos = WorldPointToBlockCoord(removePoint);

        worldManager.TrySetBlock(blockPos.x, blockPos.y, blockPos.z, 0);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool TryGetTerrainHit(out Vector3 point, out Vector3 normal)
    {
        var raycaster = WorldRaycaster.Instance;
        if (raycaster == null || !raycaster.HasHit)
        {
            point = default;
            normal = default;
            return false;
        }

        int hitLayer = raycaster.Collider.gameObject.layer;
        if (((1 << hitLayer) & chunkLayer) == 0)
        {
            point = default;
            normal = default;
            return false;
        }

        point = raycaster.Point;
        normal = raycaster.Normal;
        return true;
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