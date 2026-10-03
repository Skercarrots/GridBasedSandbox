using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

// ─────────────────────────────────────────────────────────────────────────────
//  BiomeDefinition — ScriptableObject describing one biome's terrain shape
//  and surface blocks. Create via: Assets > Create > VoxelWorld > Biome Definition
//
//  MATCHING (Phase 2 update)
//  A biome claims a rectangle in 5-dimensional climate space:
//    (temperature, humidity, continentalness, erosion, weirdness)
//  OverworldGenerator samples all 5 axes per column and calls
//  BiomeRegistry.GetBiome() which returns the closest match.
//
//  Previous temperature/humidity fields are preserved with [FormerlySerializedAs]
//  so existing Biome_Dunes.asset continues to deserialize without data loss.
//
//  HEIGHT
//  heightCurve remaps continentalness [0..1] to a shaped [0..1] before it's
//  lerped into world Y. A flat biome uses a nearly-flat curve; a mountainous
//  biome uses a curve that ramps up steeply at high continentalness.
//
//  SURFACE RULES (Phase 2 addition)
//  After the height is computed, surface rules can override the surface block:
//    • steepSlopeBlock     — placed on steep slopes (gradient > threshold)
//    • snowlineBlock       — placed on peaks above snowlineY
//    • beachBlock          — placed near sea level on coastal columns
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "Biome_New", menuName = "VoxelWorld/Biome Definition")]
public class BiomeDefinition : ScriptableObject
{
    [Header("Identity")]
    public string biomeName = "New Biome";

    // ── Climate ranges (5-parameter matching) ─────────────────────────────────
    // [FormerlySerializedAs] keeps Biome_Dunes.asset working — its temperature
    // and humidity fields deserialize into the new names without any data loss.

    [Header("Climate Ranges (all 0..1)")]
    [FormerlySerializedAs("minTemperature")]
    [Range(0f, 1f)] public float minTemperature = 0f;

    [FormerlySerializedAs("maxTemperature")]
    [Range(0f, 1f)] public float maxTemperature = 1f;

    [FormerlySerializedAs("minHumidity")]
    [Range(0f, 1f)] public float minHumidity = 0f;

    [FormerlySerializedAs("maxHumidity")]
    [Range(0f, 1f)] public float maxHumidity = 1f;

    [Range(0f, 1f)] public float minContinentalness = 0f;
    [Range(0f, 1f)] public float maxContinentalness = 1f;

    [Range(0f, 1f)] public float minErosion = 0f;
    [Range(0f, 1f)] public float maxErosion = 1f;

    [Range(0f, 1f)] public float minWeirdness = 0f;
    [Range(0f, 1f)] public float maxWeirdness = 1f;

    // ── Surface blocks ─────────────────────────────────────────────────────────
    [Header("Surface Blocks")]
    public VoxelBlockType surfaceBlock;
    public VoxelBlockType subsurfaceBlock;

    [Tooltip("How many layers of subsurfaceBlock sit below the surface before stone starts.")]
    public int subsurfaceDepth = 3;

    // ── Surface Rules (Phase 2) ───────────────────────────────────────────────
    [Header("Surface Rules")]
    [Tooltip("Block placed on steep slopes (gradient magnitude > slopeSteepnessThreshold). " +
             "Leave null to use surfaceBlock everywhere.")]
    public VoxelBlockType steepSlopeBlock;

    [Tooltip("Slope gradient magnitude above which steepSlopeBlock is used instead of surfaceBlock. " +
             "1.0 = 45°, larger = steeper threshold.")]
    [Range(0f, 2f)] public float slopeSteepnessThreshold = 1.2f;

    [Tooltip("Block placed above snowlineY (e.g. Snow). Leave null to disable.")]
    public VoxelBlockType snowlineBlock;

    [Tooltip("World Y above which snowlineBlock replaces surfaceBlock.")]
    public int snowlineY = 80;

    [Tooltip("Block placed on columns at or below sea level within beachDepthBelowSeaLevel " +
             "blocks of the surface (coastal beach strips). Leave null to disable.")]
    public VoxelBlockType beachBlock;

    [Tooltip("How many blocks below sea level still count as 'beach'. " +
             "Higher = wider beach strips.")]
    public int beachDepthBelowSeaLevel = 3;

    // ── Shape ─────────────────────────────────────────────────────────────────
    [Header("Shape")]
    [Tooltip("Remaps continentalness [0..1] to a shaped [0..1] before it becomes height. " +
             "Flat biomes: nearly flat curve. Mountains: steep ramp at high continentalness.")]
    public AnimationCurve heightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Tooltip("Overall height range multiplier. 1 = full world height range.")]
    public float heightMultiplier = 1f;

    [Tooltip("Strength of the ridged mountain noise added when weirdness is high. " +
             "0 = no ridges. Recommended: 0–0.5 for most biomes, 0.5–1.0 for mountains.")]
    [Range(0f, 1f)] public float ridgeStrength = 0f;

    // ── Features (Phase 4 hook) ───────────────────────────────────────────────
    [Header("Features (populated in Phase 4)")]
    public List<WorldFeatureEntry> features = new();

    // ── Decoration (legacy hook) ──────────────────────────────────────────────
    [Header("Decoration (legacy)")]
    public GameObject[] decorationPrefabs;
    [Range(0f, 0.1f)] public float decorationDensity = 0.02f;

    // ── Climate matching ───────────────────────────────────────────────────────

    /// <summary>Returns true if the given 5-parameter climate point falls inside
    /// this biome's climate rectangle.</summary>
    public bool Matches(float temperature, float humidity,
                        float continentalness, float erosion, float weirdness)
        => temperature     >= minTemperature     && temperature     <= maxTemperature
        && humidity        >= minHumidity        && humidity        <= maxHumidity
        && continentalness >= minContinentalness && continentalness <= maxContinentalness
        && erosion         >= minErosion         && erosion         <= maxErosion
        && weirdness       >= minWeirdness       && weirdness       <= maxWeirdness;

    /// <summary>Backward-compatible 2-param Matches for registry fallback code.</summary>
    public bool Matches(float temperature, float humidity)
        => temperature >= minTemperature && temperature <= maxTemperature
        && humidity    >= minHumidity    && humidity    <= maxHumidity;

    /// <summary>Squared distance from a climate point to this biome's rectangle in
    /// 5D space — 0 if the point is inside it. Used as a fallback when nothing matches.
    /// Weights temperature and humidity equally; continentalness/erosion/weirdness
    /// have smaller weights since they're more freely specified.</summary>
    public float DistanceTo(float temperature, float humidity,
                            float continentalness, float erosion, float weirdness)
    {
        float dt   = Mathf.Max(minTemperature     - temperature,     0f, temperature     - maxTemperature);
        float dh   = Mathf.Max(minHumidity        - humidity,        0f, humidity        - maxHumidity);
        float dc   = Mathf.Max(minContinentalness - continentalness, 0f, continentalness - maxContinentalness);
        float de   = Mathf.Max(minErosion         - erosion,         0f, erosion         - maxErosion);
        float dw   = Mathf.Max(minWeirdness       - weirdness,       0f, weirdness       - maxWeirdness);
        return dt*dt + dh*dh + dc*dc*0.5f + de*de*0.5f + dw*dw*0.5f;
    }

    /// <summary>Backward-compatible 2-param DistanceTo.</summary>
    public float DistanceTo(float temperature, float humidity)
    {
        float dt = Mathf.Max(minTemperature - temperature, 0f, temperature - maxTemperature);
        float dh = Mathf.Max(minHumidity - humidity, 0f, humidity - maxHumidity);
        return dt * dt + dh * dh;
    }

    // ── Baked curve (thread-safe sampling) ────────────────────────────────────
    // AnimationCurve.Evaluate() isn't officially safe to call off the main
    // thread, but OverworldGenerator now runs Generate() on background threads.
    // BakeHeightCurve() samples the curve once, on the main thread, into a plain
    // float[] LUT — BiomeRegistry.Initialize() calls this for every biome, which
    // in turn is called from OverworldGenerator.Prepare() before any background
    // generation is dispatched. EvaluateHeightCurve() below is what Generate()
    // actually calls — pure array lookup + lerp, safe from any thread.
    private const int CURVE_BAKE_SAMPLES = 128;
    private float[] _bakedHeightCurve;

    /// <summary>Samples heightCurve into a LUT. MAIN THREAD ONLY — call before any
    /// background generation starts, never from inside Generate() itself.</summary>
    public void BakeHeightCurve()
    {
        _bakedHeightCurve = new float[CURVE_BAKE_SAMPLES];
        for (int i = 0; i < CURVE_BAKE_SAMPLES; i++)
        {
            float t = i / (float)(CURVE_BAKE_SAMPLES - 1);
            _bakedHeightCurve[i] = heightCurve.Evaluate(t);
        }
    }

    /// <summary>Thread-safe stand-in for heightCurve.Evaluate(t). Uses the baked LUT
    /// if BakeHeightCurve() has run; otherwise falls back to evaluating the curve
    /// directly (only safe if called from the main thread — e.g. editor tooling
    /// that generates a chunk without going through VoxelWorldManager).</summary>
    public float EvaluateHeightCurve(float t)
    {
        if (_bakedHeightCurve == null)
            return heightCurve.Evaluate(t);

        t = Mathf.Clamp01(t);
        float f  = t * (CURVE_BAKE_SAMPLES - 1);
        int i0   = Mathf.FloorToInt(f);
        int i1   = Mathf.Min(i0 + 1, CURVE_BAKE_SAMPLES - 1);
        float fr = f - i0;
        return Mathf.Lerp(_bakedHeightCurve[i0], _bakedHeightCurve[i1], fr);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  WorldFeatureEntry — Placeholder struct for Phase 4 (Structure Framework).
//  Defined here so BiomeDefinition compiles cleanly in Phase 2 even without
//  the full Phase 4 WorldFeature class present.
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class WorldFeatureEntry
{
    [Tooltip("Feature asset — assigned in Phase 4 once WorldFeature SO is created.")]
    public ScriptableObject feature;  // will become WorldFeature in Phase 4

    [Tooltip("Weight relative to other features in this biome.")]
    [Range(0f, 1f)] public float weight = 1f;
}