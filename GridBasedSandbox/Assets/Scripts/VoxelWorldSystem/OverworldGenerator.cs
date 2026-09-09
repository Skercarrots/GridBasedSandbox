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

            for (int ly = 0; ly < data.Height; ly++)
            {
                int wy = settings.LocalYToWorld(ly);
                byte block = AssignBlock(wy, surfaceY, biome, settings);

                if (block != 0 && wy <= surfaceY - caveMinDepthBelowSurface)
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

    private static byte AssignBlock(int wy, int surfaceY, BiomeDefinition biome, VoxelWorldSettings settings)
    {
        if (wy > surfaceY)
        {
            bool underwater = wy <= settings.seaLevel && settings.waterBlock != null;
            return underwater ? settings.waterBlock.blockId : (byte)0;
        }

        if (wy == surfaceY)
            return biome.surfaceBlock.blockId;

        if (wy >= surfaceY - biome.subsurfaceDepth)
            return biome.subsurfaceBlock.blockId;

        return settings.stoneBlock != null ? settings.stoneBlock.blockId : (byte)1;
    }
}