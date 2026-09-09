using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  BiomeDefinition — ScriptableObject describing one biome's terrain shape
//  and surface blocks. Create via: Assets > Create > VoxelWorld > Biome Definition
//
//  MATCHING
//  A biome claims a rectangle in (temperature, humidity) space. OverworldGenerator
//  samples temperature/humidity per column and asks BiomeRegistry for whichever
//  biome's rectangle contains that point (falling back to nearest match if none do).
//
//  HEIGHT
//  heightCurve remaps continentalness [0..1] to a shaped [0..1] before it's
//  lerped into world Y. A flat biome uses a nearly-flat curve; a mountainous
//  biome uses a curve that ramps up steeply at high continentalness.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "Biome_New", menuName = "VoxelWorld/Biome Definition")]
public class BiomeDefinition : ScriptableObject
{
    [Header("Identity")]
    public string biomeName = "New Biome";

    [Header("Climate range (0..1)")]
    [Range(0f, 1f)] public float minTemperature = 0f;
    [Range(0f, 1f)] public float maxTemperature = 1f;
    [Range(0f, 1f)] public float minHumidity = 0f;
    [Range(0f, 1f)] public float maxHumidity = 1f;

    [Header("Surface blocks")]
    public VoxelBlockType surfaceBlock;
    public VoxelBlockType subsurfaceBlock;
    [Tooltip("How many layers of subsurfaceBlock sit below the surface before stone starts.")]
    public int subsurfaceDepth = 3;

    [Header("Shape")]
    [Tooltip("Remaps continentalness [0..1] to a shaped [0..1] before it becomes height.")]
    public AnimationCurve heightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    public float heightMultiplier = 1f;

    [Header("Decoration (hook for a future decoration pass)")]
    public GameObject[] decorationPrefabs;
    [Range(0f, 0.1f)] public float decorationDensity = 0.02f;

    public bool Matches(float temperature, float humidity)
        => temperature >= minTemperature && temperature <= maxTemperature
        && humidity    >= minHumidity    && humidity    <= maxHumidity;

    /// <summary>Squared distance from a (temperature, humidity) point to this biome's
    /// rectangle — 0 if the point is inside it. Used as a fallback when nothing matches.</summary>
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