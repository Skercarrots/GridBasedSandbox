using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  NoiseLayerConfig — One tunable fBm noise layer (scale, octaves, persistence,
//  lacunarity, and a per-layer seed offset so multiple layers sampled with the
//  same world seed don't line up with each other).
//
//  Used by OverworldGenerator for continentalness / erosion / temperature /
//  humidity — each gets its own instance so they can be tuned independently
//  in the Inspector, instead of one noise curve driving everything.
//
//  DOMAIN WARPING (Phase 2 addition)
//  If domainWarpAmplitude > 0, the sample point is pre-displaced by a second
//  Perlin noise before the fBm loop runs. This swirls biome borders and adds
//  natural-looking wrinkles to terrain transitions — no new packages needed.
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class NoiseLayerConfig
{
    [Tooltip("Noise scale — larger = smoother/slower-changing.")]
    public float scale = 200f;

    [Tooltip("Number of fBm octaves for this layer.")]
    [Range(1, 6)] public int octaves = 3;

    [Tooltip("Amplitude falloff per octave.")]
    [Range(0f, 1f)] public float persistence = 0.5f;

    [Tooltip("Frequency growth per octave.")]
    [Range(1f, 4f)] public float lacunarity = 2f;

    [Tooltip("Multiplies the world seed before offsetting this layer's sample " +
             "point, so different layers don't sample identical noise at identical coords.")]
    public float seedOffsetMultiplier = 1f;

    // ── Domain warping (Phase 2) ──────────────────────────────────────────────

    [Header("Domain Warp (optional)")]
    [Tooltip("If > 0, domain-warps the sample coordinates by this amplitude (in blocks) " +
             "before the fBm loop. Creates swirling biome borders and natural terrain wrinkles.")]
    public float domainWarpAmplitude = 0f;

    [Tooltip("Scale of the domain warp noise. Larger = wider, smoother swirls.")]
    public float domainWarpScale = 200f;

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Samples this layer at world (wx, wz). Returns a normalised [0..1] value.
    /// Thread-safe, deterministic.</summary>
    public float Sample(int wx, int wz, int seed) => SampleF(wx, wz, seed);

    /// <summary>Samples this layer at float world coordinates (wx, wz). Allows sub-block
    /// resolution sampling from editor tooling. Thread-safe, deterministic.</summary>
    public float SampleF(float wx, float wz, int seed)
    {
        // BUG FIX 1 — "seed 0 = blocky terrain":
        //   Constrain offset to a reasonable range with modulo so large seeds
        //   don't exhaust float32 precision when added to world coords.
        //
        // BUG FIX 2 — "mirrored world":
        //   Unity's Mathf.PerlinNoise mirrors at 0: PerlinNoise(-x, z) == PerlinNoise(x, z).
        //   Add a large base offset (10000) so sample coords are always positive,
        //   and use different primes for X and Z so axes don't share the same symmetry.
        float offsetX = 10000f + (seed * 127.1f * seedOffsetMultiplier) % 9999f;
        float offsetZ = 10000f + (seed * 311.7f * seedOffsetMultiplier) % 9999f;

        float sx = wx + offsetX;
        float sz = wz + offsetZ;

        // Domain warp — displace sample point by a low-frequency Perlin before fBm
        if (domainWarpAmplitude > 0f)
        {
            // Use two orthogonal primes to keep the two warp directions independent
            float warpSeed1 = offsetX + 3571f;
            float warpSeed2 = offsetZ + 6271f;
            float warpX = Mathf.PerlinNoise(sx / domainWarpScale, sz / domainWarpScale + warpSeed1);
            float warpZ = Mathf.PerlinNoise(sx / domainWarpScale + warpSeed2, sz / domainWarpScale);
            // Remap [0..1] → [−0.5..0.5] then scale to amplitude
            sx += (warpX - 0.5f) * domainWarpAmplitude * 2f;
            sz += (warpZ - 0.5f) * domainWarpAmplitude * 2f;
        }

        float amplitude = 1f;
        float frequency = 1f;
        float value     = 0f;
        float maxValue  = 0f;

        for (int o = 0; o < octaves; o++)
        {
            float sampleX = sx / scale * frequency;
            float sampleZ = sz / scale * frequency;

            value    += Mathf.PerlinNoise(sampleX, sampleZ) * amplitude;
            maxValue += amplitude;

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return value / maxValue;
    }
}
