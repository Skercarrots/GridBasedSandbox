using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  OreDefinition — ScriptableObject describing one ore type's vein generation.
//  Create via: Assets > Create > GridBasedSandbox/Voxel World/Ore Definition
//
//  PLACEMENT ALGORITHM (stateless, order-independent)
//  For each chunk being generated:
//    1. Seed a DeterministicRng from Hash(worldSeed, chunkX, chunkZ, oreId).
//    2. For each attempt (0..attemptsPerChunk):
//       a. Pick a random (x, y, z) within the chunk, clamped to [minY, maxY].
//       b. Check heightDistribution probability at this Y.
//       c. Generate an ellipsoidal vein of size [minVeinSize..maxVeinSize].
//       d. For each block in the vein: if the target is a replaceable block
//          within THIS chunk's bounds, set it to the ore block.
//
//  CROSS-CHUNK VEINS
//  Veins that straddle chunk borders are handled by evaluating vein origins
//  from neighboring chunks' coordinate spaces. Each chunk evaluates its own
//  origins AND the 26 neighboring chunks' origins, only writing blocks that
//  fall within its own bounds. This ensures veins look identical regardless
//  of chunk load order, with zero inter-chunk communication.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "OreDefinition", menuName = "GridBasedSandbox/Voxel World/Ore Definition")]
public class OreDefinition : ScriptableObject
{
    [Header("Block")]
    [Tooltip("The ore block type to place. Must be registered in VoxelBlockRegistry.")]
    public VoxelBlockType oreBlock;

    [Tooltip("Blocks this ore can replace (typically Stone only). " +
             "The ore vein will only overwrite blocks whose ID matches one of these.")]
    public VoxelBlockType[] replaceableBlocks;

    [Header("Depth Range")]
    [Tooltip("Minimum world Y for this ore. Veins won't generate below this.")]
    public int minY = -64;

    [Tooltip("Maximum world Y for this ore. Veins won't generate above this.")]
    public int maxY = 40;

    [Tooltip("Height distribution curve. X = normalised Y (0 = minY, 1 = maxY), " +
             "Y = relative probability (0 = never, 1 = full density). " +
             "Use a triangle peak for ores concentrated at a specific depth, " +
             "or flat for even distribution.")]
    public AnimationCurve heightDistribution = AnimationCurve.Linear(0, 1, 1, 1);

    [Header("Veins")]
    [Tooltip("Number of vein placement attempts per 16×16 chunk. " +
             "Each attempt picks a random position and may or may not produce a vein " +
             "depending on height distribution probability.")]
    [Range(1, 40)] public int attemptsPerChunk = 8;

    [Tooltip("Minimum number of ore blocks per vein.")]
    [Range(1, 32)] public int minVeinSize = 4;

    [Tooltip("Maximum number of ore blocks per vein.")]
    [Range(1, 64)] public int maxVeinSize = 12;

    [Header("Shape")]
    [Tooltip("Vein shape elongation: 0 = roughly spherical, 1 = very elongated blob. " +
             "Higher values create streak-like veins that follow a random axis.")]
    [Range(0f, 1f)] public float elongation = 0.3f;

    [Header("Optional Filters")]
    [Tooltip("If non-empty, this ore only spawns in chunks where the dominant biome " +
             "is in this list. Leave empty for worldwide spawning.")]
    public BiomeDefinition[] biomeFilter;

    // ── Thread-safe height distribution LUT ──────────────────────────────────

    private const int HEIGHT_LUT_SAMPLES = 64;
    private float[] _bakedHeightLUT;

    /// <summary>Bakes the heightDistribution AnimationCurve into a thread-safe LUT.
    /// MAIN THREAD ONLY — call from OverworldGenerator.Prepare() before any background
    /// generation is dispatched.</summary>
    public void BakeHeightDistribution()
    {
        _bakedHeightLUT = new float[HEIGHT_LUT_SAMPLES];
        for (int i = 0; i < HEIGHT_LUT_SAMPLES; i++)
        {
            float t = i / (float)(HEIGHT_LUT_SAMPLES - 1);
            _bakedHeightLUT[i] = heightDistribution.Evaluate(t);
        }
    }

    /// <summary>Thread-safe stand-in for heightDistribution.Evaluate(t).
    /// Returns the probability [0..1] of spawning at normalised height t.</summary>
    public float EvaluateHeightProbability(float t)
    {
        if (_bakedHeightLUT == null)
            return heightDistribution.Evaluate(t);

        t = Mathf.Clamp01(t);
        float f  = t * (HEIGHT_LUT_SAMPLES - 1);
        int i0   = Mathf.FloorToInt(f);
        int i1   = Mathf.Min(i0 + 1, HEIGHT_LUT_SAMPLES - 1);
        float fr = f - i0;
        return Mathf.Lerp(_bakedHeightLUT[i0], _bakedHeightLUT[i1], fr);
    }

    /// <summary>Returns true if the given block ID is in the replaceableBlocks list.</summary>
    public bool CanReplace(byte blockId)
    {
        if (replaceableBlocks == null) return false;
        for (int i = 0; i < replaceableBlocks.Length; i++)
        {
            if (replaceableBlocks[i] != null && replaceableBlocks[i].blockId == blockId)
                return true;
        }
        return false;
    }
}

