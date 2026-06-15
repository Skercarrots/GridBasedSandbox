using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  VoxelWorldSettings  — Single SO that holds every tunable knob.
//  Create via: Assets > Create > VoxelWorld > World Settings
//
//  Keep one instance per "world profile" (e.g. overworld, cave, test level).
//  Assign it to VoxelWorldManager in the Inspector.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "WorldSettings", menuName = "VoxelWorld/World Settings")]
public class VoxelWorldSettings : ScriptableObject
{
    // ── Block Registry ────────────────────────────────────────────────────────
    [Header("Block Registry")]
    public VoxelBlockRegistry blockRegistry;

    // ── Chunk Dimensions ──────────────────────────────────────────────────────
    [Header("Chunk Dimensions")]
    [Tooltip("Number of blocks per chunk on X and Z axes.")]
    [Range(4, 32)] public int chunkWidth  = 16;

    [Tooltip("Number of blocks in a chunk on the Y axis.")]
    [Range(1, 64)] public int chunkHeight = 9;   // height range −4 … +4 = 9 slices

    // ── World Height ──────────────────────────────────────────────────────────
    [Header("World Height Limits")]
    [Tooltip("Minimum block Y in world space (inclusive). Default −4 for your spec.")]
    public int minHeight = -4;

    [Tooltip("Maximum block Y in world space (inclusive). Default +4 for your spec.")]
    public int maxHeight =  4;

    // ── Streaming / View Distance ─────────────────────────────────────────────
    [Header("Streaming")]
    [Tooltip("How many chunks are loaded around the player on X and Z (in chunk units).")]
    [Range(1, 12)] public int viewDistanceInChunks = 4;

    [Tooltip("Max chunks rebuilt per frame — controls hitching vs latency trade-off.")]
    [Range(1, 8)]  public int maxChunkBuildsPerFrame = 2;

    // ── Terrain Generation ────────────────────────────────────────────────────
    [Header("Terrain Generation")]
    [Tooltip("Master seed. 0 = random at runtime.")]
    public int seed = 0;

    [Tooltip("Noise scale — larger = smoother hills.")]
    [Range(10f, 200f)] public float noiseScale = 60f;

    [Tooltip("Number of noise octaves for detail layering.")]
    [Range(1, 6)] public int octaves = 3;

    [Tooltip("How quickly amplitude falls per octave.")]
    [Range(0f, 1f)] public float persistence = 0.5f;

    [Tooltip("How quickly frequency rises per octave.")]
    [Range(1f, 4f)] public float lacunarity = 2f;

    // ── Texture Atlas ─────────────────────────────────────────────────────────
    [Header("Texture Atlas")]
    [Tooltip("Number of tiles in one row/column of the atlas texture.")]
    [Range(1, 32)] public int atlasSize = 4;   // 4×4 = 16 block types

    // ── Render Material ───────────────────────────────────────────────────────
    [Header("Rendering")]
    [Tooltip("Material that holds the block atlas texture. Must use a shader with vertex colours if you want tinting.")]
    public Material chunkMaterial;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>UV size of one tile in a square atlas.</summary>
    public float TileUVSize => 1f / atlasSize;

    /// <summary>Total block height of the world (minHeight to maxHeight inclusive).</summary>
    public int TotalWorldHeight => maxHeight - minHeight + 1;

    /// <summary>Converts a world-space block Y to a local chunk slice index.</summary>
    public int WorldYToLocal(int worldY) => worldY - minHeight;

    /// <summary>Converts a local Y slice back to world-space block Y.</summary>
    public int LocalYToWorld(int localY) => localY + minHeight;
}
