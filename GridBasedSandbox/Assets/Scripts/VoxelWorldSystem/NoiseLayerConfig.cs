using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  NoiseLayerConfig — One tunable fBm noise layer (scale, octaves, persistence,
//  lacunarity, and a per-layer seed offset so multiple layers sampled with the
//  same world seed don't line up with each other).
//
//  Used by OverworldGenerator for continentalness / erosion / temperature /
//  humidity — each gets its own instance so they can be tuned independently
//  in the Inspector, instead of one noise curve driving everything.
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

    /// <summary>Samples this layer at world (wx, wz). Returns a normalised [0..1] value.</summary>
    public float Sample(int wx, int wz, int seed)
    {
        // BUG FIX 1 — "seed 0 = blocky terrain":
        //   The old formula was: offset = seed * 0.1f * seedOffsetMultiplier
        //   When seed=0 triggers Random.Range in VoxelWorldManager, the result can be
        //   a very large int (e.g. 1,847,234,123). Multiplied by 0.1f that's ~184M.
        //   Adding wx (max ~1000 blocks) to 184M is meaningless in float32 precision —
        //   all X values look identical to the noise function → flat uniform terrain.
        //   Fix: constrain the offset to a reasonable range with modulo.
        //
        // BUG FIX 2 — "mirrored world":
        //   Unity's Mathf.PerlinNoise mirrors at 0: PerlinNoise(-x, z) == PerlinNoise(x, z).
        //   Blocks on the negative side of the world produced a mirror image of positive blocks.
        //   Fix: add a large base offset (10000) so sample coords are always positive,
        //   and use different primes for X and Z so axes don't share the same symmetry point.
        float offsetX = 10000f + (seed * 127.1f * seedOffsetMultiplier) % 9999f;
        float offsetZ = 10000f + (seed * 311.7f * seedOffsetMultiplier) % 9999f;

        float amplitude = 1f;
        float frequency = 1f;
        float value     = 0f;
        float maxValue  = 0f;

        for (int o = 0; o < octaves; o++)
        {
            float sampleX = (wx + offsetX) / scale * frequency;
            float sampleZ = (wz + offsetZ) / scale * frequency;

            value    += Mathf.PerlinNoise(sampleX, sampleZ) * amplitude;
            maxValue += amplitude;

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return value / maxValue;
    }
}