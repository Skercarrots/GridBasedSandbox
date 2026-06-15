using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  VoxelTerrainGenerator  — Pure static logic class (no MonoBehaviour).
//  Fills a VoxelChunkData from scratch using layered Perlin noise.
//
//  DESIGN PRINCIPLES
//  • Completely decoupled from rendering — only depends on VoxelChunkData and
//    VoxelWorldSettings. Safe to call on a background thread.
//  • Height is clamped to [settings.minHeight … settings.maxHeight].
//  • Block assignment is driven by a simple layer stack that is easy to expand.
//
//  EXTENDING
//  To add biomes: pass a BiomeData SO and branch inside AssignBlock.
//  To add caves: add a 3-D noise pass after the height pass.
//  To add structures: call a separate StructurePlacer after Generate().
// ─────────────────────────────────────────────────────────────────────────────

public static class VoxelTerrainGenerator
{
    // ── Block ID constants (keep in sync with your VoxelBlockType SOs) ────────
    // Using constants keeps the generator readable without passing the registry.
    private const byte AIR   = 0;
    private const byte STONE = 1;
    private const byte DIRT  = 2;
    private const byte GRASS = 3;
    private const byte SAND  = 4;
    private const byte WATER = 5;   // optional — place if below sea level

    // Sea level in world-space Y. Tweak freely.
    private const int SEA_LEVEL = 0;

    // How many dirt layers sit below the grass top.
    private const int DIRT_DEPTH = 2;

    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>Populates <paramref name="data"/> with terrain blocks.
    /// Call once per chunk, either on the main thread or a worker thread.</summary>
    public static void Generate(VoxelChunkData data, VoxelWorldSettings settings, int seed)
    {
        // Pre-compute a per-seed offset so chunks don't tile at the origin.
        float offsetX = seed * 0.1f;
        float offsetZ = seed * 0.1f;

        int worldOriginX = data.WorldOriginX;
        int worldOriginZ = data.WorldOriginZ;
        int minY         = settings.minHeight;
        int maxY         = settings.maxHeight;
        int totalHeight  = data.Height;

        for (int lx = 0; lx < data.Width; lx++)
        for (int lz = 0; lz < data.Width; lz++)
        {
            int wx = worldOriginX + lx;
            int wz = worldOriginZ + lz;

            // ── Sample fractional Brownian motion height ───────────────────
            float normalizedHeight = FractionalBrownianMotion(
                wx, wz,
                offsetX, offsetZ,
                settings.noiseScale,
                settings.octaves,
                settings.persistence,
                settings.lacunarity);

            // Map [0..1] to world Y range
            int surfaceY = Mathf.RoundToInt(Mathf.Lerp(minY, maxY, normalizedHeight));
            surfaceY     = Mathf.Clamp(surfaceY, minY, maxY);

            // ── Fill column ───────────────────────────────────────────────
            for (int ly = 0; ly < totalHeight; ly++)
            {
                int wy = settings.LocalYToWorld(ly);
                data.SetBlock(lx, ly, lz, AssignBlock(wy, surfaceY, minY));
            }
        }

        data.IsDirty = true;
    }

    // ── Block assignment ─────────────────────────────────────────────────────

    /// <summary>Decides which block id belongs at world Y <paramref name="wy"/>
    /// given the terrain surface at <paramref name="surfaceY"/>.</summary>
    private static byte AssignBlock(int wy, int surfaceY, int minY)
    {
        if (wy > surfaceY)
        {
            // Above surface — air (or water if below sea level)
            return (wy <= SEA_LEVEL) ? WATER : AIR;
        }

        if (wy == surfaceY)
        {
            // Top-most solid layer
            if (surfaceY <= SEA_LEVEL) return SAND;   // underwater beaches
            return GRASS;
        }

        if (wy >= surfaceY - DIRT_DEPTH)
        {
            return DIRT;
        }

        // Everything deeper is stone (or the absolute bedrock at minY)
        return wy == minY ? STONE : STONE;
    }

    // ── Fractional Brownian Motion ───────────────────────────────────────────

    private static float FractionalBrownianMotion(
        int wx, int wz,
        float offsetX, float offsetZ,
        float scale,
        int octaves,
        float persistence,
        float lacunarity)
    {
        float amplitude = 1f;
        float frequency = 1f;
        float value     = 0f;
        float maxValue  = 0f;   // used for normalisation

        for (int o = 0; o < octaves; o++)
        {
            float sampleX = (wx + offsetX) / scale * frequency;
            float sampleZ = (wz + offsetZ) / scale * frequency;

            // Mathf.PerlinNoise returns [0..1]
            value    += Mathf.PerlinNoise(sampleX, sampleZ) * amplitude;
            maxValue += amplitude;

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return value / maxValue;   // normalise to [0..1]
    }
}
