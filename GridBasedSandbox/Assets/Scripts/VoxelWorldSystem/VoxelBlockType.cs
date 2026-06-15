using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  VoxelBlockType  — ScriptableObject that defines a single block species.
//  Create via: Assets > Create > VoxelWorld > Block Type
//
//  DESIGN NOTES
//  • One SO per block (Air, Dirt, Stone, Grass, Sand…).
//  • UV coords follow a standard atlas layout: each face gets a (col, row)
//    tile address that the mesh builder converts to real UVs at build time.
//  • Per-face atlas overrides let you have a grass-top / dirt-side / dirt-bottom
//    without any extra SOs.
//  • blockId == 0 is ALWAYS Air (no geometry emitted).
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "BlockType_New", menuName = "VoxelWorld/Block Type")]
public class VoxelBlockType : ScriptableObject
{
    // ── Identity ──────────────────────────────────────────────────────────────
    [Header("Identity")]
    [Tooltip("Unique numeric ID used inside chunk data arrays. 0 = Air.")]
    public byte blockId;

    [Tooltip("Human-readable label (used in editor and debug).")]
    public string blockName = "New Block";

    // ── Behaviour flags ───────────────────────────────────────────────────────
    [Header("Behaviour")]
    [Tooltip("Air and other invisible types skip mesh generation entirely.")]
    public bool isSolid = true;

    [Tooltip("Transparent blocks (glass, water) still cull neighbours.")]
    public bool isTransparent = false;

    // ── Texture Atlas ─────────────────────────────────────────────────────────
    [Header("Texture Atlas (tile col, row)")]
    [Tooltip("Fallback tile used for any face that has no override.")]
    public Vector2Int defaultTile = new Vector2Int(0, 0);

    // Per-face overrides — if the x component is -1, defaultTile is used.
    public Vector2Int tileTop    = new Vector2Int(-1, 0);
    public Vector2Int tileBottom = new Vector2Int(-1, 0);
    public Vector2Int tileFront  = new Vector2Int(-1, 0);
    public Vector2Int tileBack   = new Vector2Int(-1, 0);
    public Vector2Int tileLeft   = new Vector2Int(-1, 0);
    public Vector2Int tileRight  = new Vector2Int(-1, 0);

    // ── Helpers ───────────────────────────────────────────────────────────────
    /// <summary>Returns the resolved tile for a given face index (0=Top … 5=Right).</summary>
    public Vector2Int GetTileForFace(int faceIndex)
    {
        Vector2Int candidate = faceIndex switch
        {
            0 => tileTop,
            1 => tileBottom,
            2 => tileFront,
            3 => tileBack,
            4 => tileLeft,
            5 => tileRight,
            _ => defaultTile
        };
        return candidate.x < 0 ? defaultTile : candidate;
    }
}
