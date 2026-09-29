using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  OverworldGenerator — Biome-driven terrain with 3D cave carving.
//  Create via: Assets > Create > VoxelWorld > Generators > Overworld
//
//  Replaces the old static VoxelTerrainGenerator.cs. Same "pure logic, safe
//  to call off the main thread" design intent — see WorldGenerator's
//  threading note.
//
//  PIPELINE (per column)
//  1. Sample continentalness + erosion → shaped surface height.
//  2. Sample temperature + humidity → pick a biome from biomeRegistry.
//  3. Fill the column using that biome's surface/subsurface blocks, and the
//     settings' global stone/water blocks.
//  4. Carve caves with a 3D noise pass, skipping the first few blocks below
//     the surface so caves don't punch through hillsides or beaches.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "OverworldGenerator", menuName = "VoxelWorld/Generators/Overworld")]
public class OverworldGenerator : WorldGenerator
{
    [Header("Biomes")]
    [SerializeField] private BiomeRegistry biomeRegistry;

    [Header("Shape noise")]
    [SerializeField] private NoiseLayerConfig continentalness = new NoiseLayerConfig
        { scale = 300f, octaves = 4, persistence = 0.5f, lacunarity = 2f, seedOffsetMultiplier = 1f };
    [SerializeField] private NoiseLayerConfig erosion = new NoiseLayerConfig
        { scale = 180f, octaves = 3, persistence = 0.5f, lacunarity = 2f, seedOffsetMultiplier = 3.3f };

    [Header("Biome-selection noise")]
    [SerializeField] private NoiseLayerConfig temperature = new NoiseLayerConfig
        { scale = 500f, octaves = 2, persistence = 0.5f, lacunarity = 2f, seedOffsetMultiplier = 5.1f };
    [SerializeField] private NoiseLayerConfig humidity = new NoiseLayerConfig
        { scale = 500f, octaves = 2, persistence = 0.5f, lacunarity = 2f, seedOffsetMultiplier = 7.9f };

    [Header("Caves")]
    [Tooltip("Larger = bigger, smoother caverns. Smaller = tighter tunnels.")]
    [SerializeField] private float caveScale = 24f;
    [Tooltip("Fraction of 3D noise values that become air. Higher = fewer caves.")]
    [Range(0f, 1f)] [SerializeField] private float caveThreshold = 0.62f;
    [Tooltip("Blocks of solid ground below the surface that caves never carve through.")]
    [SerializeField] private int caveMinDepthBelowSurface = 4;

    [Header("Bedrock")]
    [Tooltip("The block type used for the unbreakable bedrock layer at the bottom of the world.")]
    [SerializeField] private VoxelBlockType bedrockBlock;
    [Tooltip("Minimum number of bedrock layers at the bottom of the world.")]
    [Range(1, 5)] [SerializeField] private int bedrockLayerMin = 1;
    [Tooltip("Maximum number of bedrock layers (actual thickness varies per-column via noise).")]
    [Range(1, 8)] [SerializeField] private int bedrockLayerMax = 5;

    public override void Prepare()
    {
        // Bake every biome's AnimationCurve into a thread-safe LUT before this
        // generator's Generate() ever runs on a background thread — see the
        // threading notes on WorldGenerator.Prepare() and BiomeDefinition.
        if (biomeRegistry != null)
            biomeRegistry.Initialize();
    }

    public override void Generate(VoxelChunkData data, VoxelWorldSettings settings, int seed)
    {
        int worldOriginX = data.WorldOriginX;
        int worldOriginZ = data.WorldOriginZ;
        int minY = settings.minHeight;
        int maxY = settings.maxHeight;

        // Cache bedrock id once (0 = no bedrock block assigned → skip layer)
        byte bedrockId = bedrockBlock != null ? bedrockBlock.blockId : (byte)0;

        for (int lx = 0; lx < data.Width; lx++)
        for (int lz = 0; lz < data.Width; lz++)
        {
            int wx = worldOriginX + lx;
            int wz = worldOriginZ + lz;

            float cont         = continentalness.Sample(wx, wz, seed);
            float erosionValue = erosion.Sample(wx, wz, seed);
            float temp         = temperature.Sample(wx, wz, seed);
            float hum          = humidity.Sample(wx, wz, seed);

            BiomeDefinition biome = biomeRegistry.GetBiome(temp, hum);

            // Shape height from continentalness via this biome's curve, then let
            // erosion pull it toward the midpoint — flattens plains, barely touches mountains.
            float shaped    = biome.EvaluateHeightCurve(cont) * biome.heightMultiplier;
            float flattened = Mathf.Lerp(shaped, 0.5f, erosionValue * 0.5f);
            int   surfaceY  = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(minY, maxY, flattened)), minY, maxY);

            // Bedrock: deterministic ragged layer at world bottom.
            // Noise-driven thickness per column, using cheap primes for spatial variation.
            int bedrockTop = minY; // world-Y at or below which blocks become bedrock
            if (bedrockId != 0)
            {
                float bedrockNoise = VoxelNoise.Sample3D(wx * 4.17f, 0f, wz * 4.17f, 8f);
                int thickness = bedrockLayerMin + Mathf.FloorToInt(bedrockNoise * (bedrockLayerMax - bedrockLayerMin + 1));
                thickness = Mathf.Clamp(thickness, bedrockLayerMin, bedrockLayerMax);
                bedrockTop = minY + thickness - 1; // inclusive top of bedrock band
            }

            for (int ly = 0; ly < data.Height; ly++)
            {
                int wy = settings.LocalYToWorld(ly);
                byte block = AssignBlock(wy, surfaceY, bedrockTop, bedrockId, biome, settings);

                // Cave carving — never carve bedrock or blocks near the surface
                if (block != 0 && block != bedrockId && wy <= surfaceY - caveMinDepthBelowSurface)
                {
                    float cave = VoxelNoise.Sample3D(wx, wy, wz, caveScale);
                    if (cave > caveThreshold) block = 0;
                }

                data.SetBlock(lx, ly, lz, block);
            }
        }

        data.RecomputeSectionOccupancy();
        data.IsDirty = true;
    }

    /// <summary>Determines which block to place at world Y <paramref name="wy"/>
    /// given the surface height, bedrock boundary, current biome, and global settings.</summary>
    private static byte AssignBlock(int wy, int surfaceY, int bedrockTop, byte bedrockId,
                                     BiomeDefinition biome, VoxelWorldSettings settings)
    {
        // Above surface → air or water
        if (wy > surfaceY)
        {
            bool underwater = wy <= settings.seaLevel && settings.waterBlock != null;
            return underwater ? settings.waterBlock.blockId : (byte)0;
        }

        // Surface block
        if (wy == surfaceY)
            return biome.surfaceBlock.blockId;

        // Subsurface filler (dirt, sand, etc.)
        if (wy >= surfaceY - biome.subsurfaceDepth)
            return biome.subsurfaceBlock.blockId;

        // Bedrock layer at world bottom
        if (bedrockId != 0 && wy <= bedrockTop)
            return bedrockId;

        // Everything else is stone
        return settings.stoneBlock != null ? settings.stoneBlock.blockId : (byte)1;
    }
}