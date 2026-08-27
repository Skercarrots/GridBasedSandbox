using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
//  WorldGenNoiseUtils  —  Shared, thread-safe noise helpers for all generators.
//
//  All methods are pure static and use only Mathf.PerlinNoise, which is
//  documented as safe to call from worker threads as of Unity 2021+.
// ═══════════════════════════════════════════════════════════════════════════════

public static class WorldGenNoiseUtils
{
    // ── Fractional Brownian Motion (fBm) ──────────────────────────────────────

    /// <summary>
    /// Returns a value in [0, 1] by layering <paramref name="octaves"/> of
    /// Perlin noise with increasing frequency and decreasing amplitude.
    /// </summary>
    public static float FBM(
        float x, float z,
        float offsetX, float offsetZ,
        float scale,
        int   octaves,
        float persistence,
        float lacunarity)
    {
        float amplitude = 1f;
        float frequency = 1f;
        float value     = 0f;
        float maxValue  = 0f;

        for (int o = 0; o < octaves; o++)
        {
            float sx = (x + offsetX) / scale * frequency;
            float sz = (z + offsetZ) / scale * frequency;

            value    += Mathf.PerlinNoise(sx, sz) * amplitude;
            maxValue += amplitude;

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return value / maxValue;
    }

    // ── 2D Value Noise ────────────────────────────────────────────────────────

    /// <summary>
    /// Single-octave Perlin sample, seeded via offset.
    /// Useful for per-room variation (width, height, etc.).
    /// </summary>
    public static float Sample2D(float x, float z, float offsetX, float offsetZ, float scale)
        => Mathf.PerlinNoise((x + offsetX) / scale, (z + offsetZ) / scale);

    // ── 3D Pseudo-Noise via two 2D samples ───────────────────────────────────

    /// <summary>
    /// Approximates 3D noise by adding two perpendicular 2D samples.
    /// Fast enough for cave/carver passes.
    /// </summary>
    public static float Sample3D(float x, float y, float z, float offsetX, float offsetZ, float scale)
    {
        float a = Mathf.PerlinNoise((x + offsetX) / scale, y / scale);
        float b = Mathf.PerlinNoise(y / scale, (z + offsetZ) / scale);
        return (a + b) * 0.5f;
    }

    // ── Seeded offset helpers ─────────────────────────────────────────────────

    /// <summary>Generates a repeatable float offset from a seed + channel index.</summary>
    public static float SeedOffset(int seed, int channel = 0)
        => (seed * 0.1f) + (channel * 1000.73f);

    /// <summary>Generates a repeatable integer from seed + extra hash input.</summary>
    public static int HashInts(int a, int b, int seed)
    {
        unchecked
        {
            int h = seed;
            h ^= a * 374761393;
            h ^= b * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return h ^ (h >> 16);
        }
    }

    /// <summary>Maps a hash to [0, 1].</summary>
    public static float HashToFloat(int a, int b, int seed)
        => (HashInts(a, b, seed) & 0x7FFFFFFF) / (float)0x7FFFFFFF;

    /// <summary>Maps a hash to [min, max].</summary>
    public static float HashToRange(int a, int b, int seed, float min, float max)
        => Mathf.Lerp(min, max, HashToFloat(a, b, seed));

    /// <summary>Maps a hash to an integer in [min, max] inclusive.</summary>
    public static int HashToInt(int a, int b, int seed, int min, int max)
        => min + Mathf.Abs(HashInts(a, b, seed)) % (max - min + 1);
}
