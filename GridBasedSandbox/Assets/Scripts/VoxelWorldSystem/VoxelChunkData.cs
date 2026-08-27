using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  VoxelChunkData  — Pure data container for one chunk column.
//  NO MonoBehaviour. This can be created on a background thread.
//
//  Layout
//    _blocks[x, y, z]
//    x ∈ [0, width)   – local X within the chunk
//    y ∈ [0, height)  – local Y (maps to world Y via settings.LocalYToWorld)
//    z ∈ [0, width)   – local Z within the chunk
//    value = block id (byte), 0 = Air
//
//  ChunkCoord is the chunk's address in chunk-space (not block-space).
//  World block origin = ChunkCoord * chunkWidth.
//
//  SECTION OCCUPANCY (new)
//  With taller worlds, most of a column's height is empty air above the
//  terrain (or, deep down, solid stone with no caves nearby). Rather than
//  splitting into separate per-section chunk objects (a much bigger change —
//  see the migration guide for when that's worth it), this column tracks,
//  per 16-block-tall "section", whether it contains any non-air block at all.
//  VoxelMeshBuilder skips iterating sections flagged empty entirely.
//
//  This flag only ever turns ON via SetBlock — mining out the last block in
//  a section doesn't clear it until RecomputeSectionOccupancy() runs again.
//  That's deliberate: it can never incorrectly skip a section that still has
//  blocks, it just occasionally does a little unnecessary work on a section
//  a player fully dug out. Call RecomputeSectionOccupancy() after any bulk
//  edit where you want that reclaimed.
// ─────────────────────────────────────────────────────────────────────────────

public class VoxelChunkData
{
    public const int SECTION_HEIGHT = 16;

    // ── Public identity ───────────────────────────────────────────────────────
    public readonly Vector2Int ChunkCoord;   // (cx, cz) in chunk-space
    public readonly int        Width;
    public readonly int        Height;

    // ── Raw block storage ─────────────────────────────────────────────────────
    private readonly byte[,,] _blocks;
    private readonly bool[]   _sectionHasBlocks;

    // ── Dirty flag — set when blocks change so the renderer knows to rebuild ──
    public bool IsDirty { get; set; } = true;

    // ── Constructor ───────────────────────────────────────────────────────────
    public VoxelChunkData(Vector2Int coord, int width, int height)
    {
        ChunkCoord = coord;
        Width      = width;
        Height     = height;
        _blocks    = new byte[width, height, width];

        int sectionCount = Mathf.CeilToInt(height / (float)SECTION_HEIGHT);
        _sectionHasBlocks = new bool[sectionCount];
    }

    // ── Block accessors ───────────────────────────────────────────────────────

    /// <summary>Returns the block id at local (x, y, z). Returns 0 (Air) for out-of-bounds.</summary>
    public byte GetBlock(int lx, int ly, int lz)
    {
        if (!IsInBounds(lx, ly, lz)) return 0;
        return _blocks[lx, ly, lz];
    }

    /// <summary>Sets the block id at local (lx, ly, lz) and marks the chunk dirty.</summary>
    public void SetBlock(int lx, int ly, int lz, byte id)
    {
        if (!IsInBounds(lx, ly, lz)) return;
        _blocks[lx, ly, lz] = id;
        IsDirty = true;

        if (id != 0) _sectionHasBlocks[ly / SECTION_HEIGHT] = true;
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────

    /// <summary>World block X origin of this chunk.</summary>
    public int WorldOriginX => ChunkCoord.x * Width;

    /// <summary>World block Z origin of this chunk.</summary>
    public int WorldOriginZ => ChunkCoord.y * Width;

    /// <summary>Converts a world block position to a local position within this chunk.
    /// Does NOT validate bounds — caller must check IsInBounds separately if needed.</summary>
    public Vector3Int WorldToLocal(int wx, int wy, int wz, int minHeight)
    {
        return new Vector3Int(wx - WorldOriginX, wy - minHeight, wz - WorldOriginZ);
    }

    public bool IsInBounds(int lx, int ly, int lz)
        => lx >= 0 && lx < Width
        && ly >= 0 && ly < Height
        && lz >= 0 && lz < Width;

    // ── Bulk fill (used by terrain generator) ────────────────────────────────

    /// <summary>Fills the entire chunk with a single block id (e.g. 0 to clear).</summary>
    public void Fill(byte id)
    {
        for (int x = 0; x < Width;  x++)
        for (int y = 0; y < Height; y++)
        for (int z = 0; z < Width;  z++)
            _blocks[x, y, z] = id;

        for (int s = 0; s < _sectionHasBlocks.Length; s++)
            _sectionHasBlocks[s] = id != 0;

        IsDirty = true;
    }

    // ── Section occupancy (mesh-skip optimization) ───────────────────────────

    public int SectionCount => _sectionHasBlocks.Length;

    public bool SectionHasBlocks(int sectionIndex)
        => sectionIndex >= 0 && sectionIndex < _sectionHasBlocks.Length && _sectionHasBlocks[sectionIndex];

    /// <summary>Full rescan of every section's occupancy. Cheap relative to generation
    /// itself — call once after bulk-filling a chunk (OverworldGenerator does this).</summary>
    public void RecomputeSectionOccupancy()
    {
        for (int s = 0; s < _sectionHasBlocks.Length; s++)
            _sectionHasBlocks[s] = false;

        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        for (int z = 0; z < Width; z++)
        {
            if (_blocks[x, y, z] != 0)
                _sectionHasBlocks[y / SECTION_HEIGHT] = true;
        }
    }
}