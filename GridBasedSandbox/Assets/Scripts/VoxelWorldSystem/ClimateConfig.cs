using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  ClimateConfig — Groups the five climate noise axes used by OverworldGenerator.
//
//  Each axis drives a different aspect of biome selection and terrain shaping:
//
//  • continentalness — Low = ocean/coastal, High = inland/elevated.
//    Drives the base surface height via each biome's heightCurve.
//
//  • erosion — Low = mountainous/rough, High = flat/eroded.
//    Blends shaped height back toward a flat midpoint.
//
//  • temperature — Cold→Hot. Pure biome selector (doesn't change height).
//
//  • humidity — Dry→Wet. Pure biome selector (doesn't change height).
//
//  • weirdness — Determines peaks-valleys character; at high values, ridged
//    noise is added on top of the shaped height to create mountain ridge lines.
//    Also activates chaos terrain (3D density mode) when combined with
//    ChaosConfig.maskThreshold in OverworldGenerator.
//
//  worldScale is a global multiplier on ALL noise scales — increase to stretch
//  everything out for larger biomes and terrain features.
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class ClimateConfig
{
    [Tooltip("Global multiplier applied to all climate noise scales. " +
             "Increase for larger, wider biomes and terrain features.")]
    [Range(0.25f, 4f)] public float worldScale = 1f;

    [Header("Continentalness (ocean ↔ inland)")]
    [Tooltip("Low values → ocean/coastal. High values → inland/elevated. " +
             "Drives the base surface height via each biome's height curve.")]
    public NoiseLayerConfig continentalness = new NoiseLayerConfig
    {
        scale                = 400f,
        octaves              = 4,
        persistence          = 0.5f,
        lacunarity           = 2f,
        seedOffsetMultiplier = 1f,
        domainWarpAmplitude  = 0f,
        domainWarpScale      = 200f
    };

    [Header("Erosion (flat ↔ rough)")]
    [Tooltip("Low values → rough/mountainous. High values → flat/eroded plains. " +
             "Pulls shaped height back toward a neutral midpoint.")]
    public NoiseLayerConfig erosion = new NoiseLayerConfig
    {
        scale                = 250f,
        octaves              = 3,
        persistence          = 0.5f,
        lacunarity           = 2f,
        seedOffsetMultiplier = 3.3f,
        domainWarpAmplitude  = 0f,
        domainWarpScale      = 200f
    };

    [Header("Temperature (cold ↔ hot)")]
    [Tooltip("Pure biome selector — does not directly affect terrain height.")]
    public NoiseLayerConfig temperature = new NoiseLayerConfig
    {
        scale                = 600f,
        octaves              = 2,
        persistence          = 0.5f,
        lacunarity           = 2f,
        seedOffsetMultiplier = 5.1f,
        domainWarpAmplitude  = 0f,
        domainWarpScale      = 200f
    };

    [Header("Humidity (dry ↔ wet)")]
    [Tooltip("Pure biome selector — does not directly affect terrain height.")]
    public NoiseLayerConfig humidity = new NoiseLayerConfig
    {
        scale                = 600f,
        octaves              = 2,
        persistence          = 0.5f,
        lacunarity           = 2f,
        seedOffsetMultiplier = 7.9f,
        domainWarpAmplitude  = 0f,
        domainWarpScale      = 200f
    };

    [Header("Weirdness (normal ↔ chaotic peaks)")]
    [Tooltip("High values add ridged mountain ridges on top of the shaped height " +
             "and can activate chaos (3D density) terrain when combined with ChaosConfig.")]
    public NoiseLayerConfig weirdness = new NoiseLayerConfig
    {
        scale                = 350f,
        octaves              = 3,
        persistence          = 0.5f,
        lacunarity           = 2f,
        seedOffsetMultiplier = 11.3f,
        domainWarpAmplitude  = 0f,
        domainWarpScale      = 200f
    };

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Samples all five climate axes at world position (wx, wz).
    /// Returns a <see cref="ClimatePoint"/> with all five values in [0..1].
    /// Thread-safe, deterministic.</summary>
    public ClimatePoint Sample(float wx, float wz, int seed)
    {
        float ws = worldScale;
        // Scale coordinates by world scale BEFORE passing to noise — this stretches
        // all features proportionally without changing each layer's relative shape.
        float sx = wx / ws;
        float sz = wz / ws;

        return new ClimatePoint(
            continentalness.SampleF(sx, sz, seed),
            erosion        .SampleF(sx, sz, seed),
            temperature    .SampleF(sx, sz, seed),
            humidity       .SampleF(sx, sz, seed),
            weirdness      .SampleF(sx, sz, seed)
        );
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  ClimatePoint — Plain data struct holding one sample of all 5 climate axes.
//  All values are normalised [0..1]. Passed between pipeline stages so we
//  don't re-sample the noise multiple times for the same column.
// ─────────────────────────────────────────────────────────────────────────────

public readonly struct ClimatePoint
{
    /// <summary>Low = ocean/coastal, High = inland/elevated.</summary>
    public readonly float Continentalness;
    /// <summary>Low = rough/mountainous, High = flat/eroded.</summary>
    public readonly float Erosion;
    /// <summary>Cold (0) → Hot (1). Biome selector only.</summary>
    public readonly float Temperature;
    /// <summary>Dry (0) → Wet (1). Biome selector only.</summary>
    public readonly float Humidity;
    /// <summary>Normal (0) → Chaotic peaks (1). Ridge + chaos activation.</summary>
    public readonly float Weirdness;

    public ClimatePoint(float cont, float erosion, float temp, float hum, float weird)
    {
        Continentalness = cont;
        Erosion         = erosion;
        Temperature     = temp;
        Humidity        = hum;
        Weirdness       = weird;
    }
}

