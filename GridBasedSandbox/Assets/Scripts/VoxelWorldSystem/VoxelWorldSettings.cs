using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  VoxelWorldSettings  — Single SO that holds every tunable knob.
//  Create via: Assets > Create > VoxelWorld > World Settings
//
//  Keep one instance per "world profile" (e.g. overworld, cave, test level).
//  Assign it to a DimensionProfile, which VoxelWorldManager reads from.
//
//  CHANGED FROM THE ORIGINAL
//  • chunkHeight is gone — it used to be a separate field you had to keep
//    manually in sync with minHeight/maxHeight (easy to forget and silently
//    break world generation). Chunk height is now always TotalWorldHeight,
//    computed from minHeight/maxHeight, so there's nothing to desync.
//  • noiseScale/octaves/persistence/lacunarity moved to OverworldGenerator's
//    "Continentalness" noise layer — height shaping is generator-specific now,
//    not a global setting, since different dimensions may want different shapes.
//  • Added waterBlock/stoneBlock/seaLevel so generators reference real block
//    assets instead of hardcoding byte IDs that can drift from the registry.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "WorldSettings", menuName = "VoxelWorld/World Settings")]
public class VoxelWorldSettings : ScriptableObject
{
    // ── Block Registry ────────────────────────────────────────────────────────
    [Header("Block Registry")]
    public VoxelBlockRegistry blockRegistry;

    // ── Global Blocks ─────────────────────────────────────────────────────────
    [Header("Global Blocks")]
    [Tooltip("Used below sea level for any column whose biome doesn't specify its own water.")]
    public VoxelBlockType waterBlock;

    [Tooltip("Used below every biome's subsurface layer.")]
    public VoxelBlockType stoneBlock;

    [Tooltip("World-space Y at and below which above-surface air becomes water.")]
    public int seaLevel = 0;

    // ── Chunk Dimensions ──────────────────────────────────────────────────────
    [Header("Chunk Dimensions")]
    [Tooltip("Number of blocks per chunk on X and Z axes.")]
    public int chunkWidth = 16;

    // ── World Height ──────────────────────────────────────────────────────────
    [Header("World Height Limits")]
    [Tooltip("Minimum block Y in world space (inclusive).")]
    public int minHeight = -32;

    [Tooltip("Maximum block Y in world space (inclusive).")]
    public int maxHeight = 80;

    // ── Streaming / View Distance ─────────────────────────────────────────────
    [Header("Streaming")]
    [Tooltip("How many chunks are loaded around the player on X and Z (in chunk units).")]
    [Range(1, 64)] public int viewDistanceInChunks = 4;

    [Tooltip("Max chunks remeshed per frame — controls hitching vs latency trade-off. " +
             "Chunk DATA generation is no longer bound by this; see VoxelWorldManager.")]
    [Range(1, 8)] public int maxChunkBuildsPerFrame = 2;

    // ── Generation ─────────────────────────────────────────────────────────────
    [Header("Generation")]
    [Tooltip("Master seed. 0 = random at runtime.")]
    public int seed = 0;

    // ── Texture Atlas ─────────────────────────────────────────────────────────
    [Header("Texture Atlas")]
    [Tooltip("Number of tiles in one row/column of the atlas texture.")]
    [Range(1, 32)] public int atlasSize = 4;

    // ── Render Material ───────────────────────────────────────────────────────
    [Header("Rendering")]
    [Tooltip("Material that holds the block atlas texture. Must use a shader with vertex colours if you want tinting.")]
    public Material chunkMaterial;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>UV size of one tile in a square atlas.</summary>
    public float TileUVSize => 1f / atlasSize;

    /// <summary>Total block height of the world (minHeight to maxHeight inclusive). This
    /// is what chunk data arrays are sized to — change minHeight/maxHeight and every
    /// new chunk picks it up automatically.</summary>
    public int TotalWorldHeight => maxHeight - minHeight + 1;

    /// <summary>Converts a world-space block Y to a local chunk slice index.</summary>
    public int WorldYToLocal(int worldY) => worldY - minHeight;

    /// <summary>Converts a local Y slice back to world-space block Y.</summary>
    public int LocalYToWorld(int localY) => localY + minHeight;
}