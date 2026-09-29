using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  VoxelNoise — Shared noise helpers that don't belong to any single generator.
//
//  THREAD SAFETY
//  All methods in this class are pure functions (no state, no Unity API calls
//  beyond Mathf.PerlinNoise which is stateless). Safe to call from any thread.
//
//  NOISE QUALITY
//  Sample3D (original) is a cheap pseudo-3D noise from averaged 2D Perlin
//  planes — adequate for simple caves but has visible axis-aligned streaks.
//  Sample3DImproved uses rotated domain coordinates to break the axis
//  alignment, giving much better results for chaos terrain and tunnels.
//  Sample3DFbm layers multiple octaves of improved 3D noise for richer detail.
// ─────────────────────────────────────────────────────────────────────────────

public static class VoxelNoise
{
    // ── Original cheap 3D noise (preserved for backward compatibility) ────────

    /// <summary>Cheap pseudo-3D noise from three averaged 2D Perlin planes.
    /// Good enough for simple cave carving. No seed parameter — coordinates
    /// must be pre-offset by the caller if different noise fields are needed.</summary>
    public static float Sample3D(float x, float y, float z, float scale)
    {
        x /= scale; y /= scale; z /= scale;

        float xy = Mathf.PerlinNoise(x, y);
        float yz = Mathf.PerlinNoise(y, z);
        float xz = Mathf.PerlinNoise(x, z);

        return (xy + yz + xz) / 3f;
    }

    // ── Improved 3D noise (rotated domains, seeded) ──────────────────────────

    /// <summary>Improved 3D noise using rotated domain coordinates to reduce
    /// axis-aligned artifacts. Accepts a seed for independent noise fields.
    /// Thread-safe, deterministic.</summary>
    public static float Sample3DImproved(float x, float y, float z, float scale, int seed = 0)
    {
        float ox = 10000f + (seed * 127.1f) % 9999f;
        float oy = 10000f + (seed * 269.5f) % 9999f;
        float oz = 10000f + (seed * 311.7f) % 9999f;
        x = (x + ox) / scale;
        y = (y + oy) / scale;
        z = (z + oz) / scale;

        // Rotated sampling planes — each plane mixes in the third axis with
        // irrational-ish coefficients so artifacts from any one plane are
        // broken up by the others.
        float xy = Mathf.PerlinNoise(x + 0.317f * z, y + 0.291f * z);
        float yz = Mathf.PerlinNoise(y + 0.413f * x, z + 0.367f * x);
        float xz = Mathf.PerlinNoise(x + 0.523f * y, z + 0.479f * y);

        return (xy + yz + xz) / 3f;
    }

    // ── Multi-octave 3D fBm ──────────────────────────────────────────────────

    /// <summary>3D fractional Brownian motion noise with multiple octaves.
    /// Uses <see cref="Sample3DImproved"/> per octave. Returns [0..1].
    /// Thread-safe, deterministic.</summary>
    public static float Sample3DFbm(float x, float y, float z, float scale,
                                     int octaves, float persistence, float lacunarity, int seed)
    {
        float value     = 0f;
        float maxValue  = 0f;
        float amplitude = 1f;
        float frequency = 1f;

        for (int o = 0; o < octaves; o++)
        {
            value    += Sample3DImproved(x * frequency, y * frequency, z * frequency, scale, seed + o * 31) * amplitude;
            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return value / maxValue;
    }

    // ── Ridged noise ─────────────────────────────────────────────────────────

    /// <summary>Ridged noise (inverted absolute-value fBm) for mountain ridges
    /// and dramatic terrain features. Returns [0..1] where 1 = ridge peak.
    /// Thread-safe, deterministic.</summary>
    public static float RidgedNoise2D(float x, float z, float scale,
                                       int octaves, float lacunarity, int seed)
    {
        float ox = 10000f + (seed * 127.1f) % 9999f;
        float oz = 10000f + (seed * 311.7f) % 9999f;

        float value     = 0f;
        float maxValue  = 0f;
        float amplitude = 1f;
        float frequency = 1f;

        for (int o = 0; o < octaves; o++)
        {
            float sx = (x + ox) / scale * frequency;
            float sz = (z + oz) / scale * frequency;

            // Standard ridged formula: 1 - |noise * 2 - 1|
            float raw = Mathf.PerlinNoise(sx, sz);
            float ridged = 1f - Mathf.Abs(raw * 2f - 1f);
            ridged *= ridged; // sharpen the ridges

            value    += ridged * amplitude;
            maxValue += amplitude;
            amplitude *= 0.5f; // ridge persistence is typically fixed at 0.5
            frequency *= lacunarity;
        }

        return value / maxValue;
    }
}