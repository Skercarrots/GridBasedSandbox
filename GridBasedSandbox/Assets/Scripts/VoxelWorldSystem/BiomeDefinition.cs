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
}