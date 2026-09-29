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

    [Tooltip("If true, this block cannot be broken by the player or robots (e.g. Bedrock).")]
    public bool isUnbreakable = false;

    // ── Placeholder rendering ────────────────────────────────────────────────
    [Header("Placeholder")]
    [Tooltip("Color used for placeholder rendering when no atlas tile is assigned. " +
             "If left at default (0,0,0,0), auto-derived from a hash of blockName.")]
    public Color placeholderColor = new Color(0, 0, 0, 0);

    /// <summary>Reserved atlas tile (last slot in a 4×4 atlas) used for placeholder
    /// blocks. Should be a solid white tile in the atlas, or any neutral tile that
    /// accepts vertex-color tinting.</summary>
    public static readonly Vector2Int PlaceholderTile = new Vector2Int(3, 3);

    /// <summary>True if this block has no real atlas art and should render as a
    /// vertex-colored placeholder. Air (id 0) is never a placeholder.</summary>
    public bool IsPlaceholder
    {
        get
        {
            if (blockId == 0) return false;
            // A block is placeholder if its defaultTile is negative (unset) or
            // points at the reserved placeholder tile with no per-face overrides.
            if (defaultTile.x < 0) return true;
            if (defaultTile == PlaceholderTile
                && tileTop.x < 0 && tileBottom.x < 0
                && tileFront.x < 0 && tileBack.x < 0
                && tileLeft.x < 0 && tileRight.x < 0)
                return true;
            return false;
        }
    }

    /// <summary>Returns the placeholder color, auto-generating a deterministic
    /// colour from the block name hash if the artist hasn't set one.</summary>
    public Color ResolvedPlaceholderColor
    {
        get
        {
            if (placeholderColor.a > 0.01f) return placeholderColor;
            int hash = blockName?.GetHashCode() ?? blockId;
            float h = Mathf.Abs(hash % 360) / 360f;
            return Color.HSVToRGB(h, 0.6f, 0.85f);
        }
    }

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

    /// <summary>Returns the resolved tile for a given face index (0=Top … 5=Right).
    /// Placeholder blocks always return <see cref="PlaceholderTile"/>.</summary>
    public Vector2Int GetTileForFace(int faceIndex)
    {
        if (IsPlaceholder) return PlaceholderTile;

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
