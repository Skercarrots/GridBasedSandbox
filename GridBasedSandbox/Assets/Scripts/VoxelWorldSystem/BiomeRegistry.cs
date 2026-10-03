using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  BiomeRegistry — ScriptableObject holding every biome for one dimension.
//  Create via: Assets > Create > VoxelWorld > Biome Registry
//
//  Assign it to your OverworldGenerator (or any other WorldGenerator that
//  needs biomes). Different dimensions can use different registries — a
//  Nether-style dimension's registry wouldn't contain your Plains/Forest biomes.
//
//  MATCHING STRATEGY (Phase 2 update)
//  • GetBiome(ClimatePoint) — full 5-parameter matching, used at runtime.
//  • First we try an exact rectangle match (all axes in range).
//  • If no exact match, fall back to nearest distance in 5D climate space.
//  • Fallback biome is used if the biome list is empty or null.
//
//  BIOME BLENDING
//  GetBiomeBlended() samples 4 additional offset points around the column to
//  detect biome boundaries. When all 5 samples are the same biome, it returns
//  that biome directly (fast path, ~80% of columns). Otherwise it returns a
//  blended surface height and the dominant biome's surface block.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "BiomeRegistry", menuName = "VoxelWorld/Biome Registry")]
public class BiomeRegistry : ScriptableObject
{
    [SerializeField] private List<BiomeDefinition> biomes = new();

    [Tooltip("Used only if no biome's range contains the sampled point. Should rarely " +
             "trigger once your biomes collectively cover the full climate space.")]
    [SerializeField] private BiomeDefinition fallbackBiome;

    // ── Initialization ─────────────────────────────────────────────────────────

    /// <summary>Bakes every biome's height curve into its thread-safe LUT. MAIN THREAD
    /// ONLY — call once, before any background chunk generation begins. Called from
    /// OverworldGenerator.Prepare().</summary>
    public void Initialize()
    {
        foreach (var biome in biomes)
            biome?.BakeHeightCurve();

        fallbackBiome?.BakeHeightCurve();
    }

    // ── Full 5-parameter matching ──────────────────────────────────────────────

    /// <summary>Returns the biome whose 5-parameter climate rectangle contains the given
    /// <see cref="ClimatePoint"/>, or the nearest one if none matches exactly.
    /// Thread-safe after Initialize() has been called on the main thread.</summary>
    public BiomeDefinition GetBiome(in ClimatePoint cp)
        => GetBiome(cp.Temperature, cp.Humidity, cp.Continentalness, cp.Erosion, cp.Weirdness);

    /// <summary>Returns the biome whose 5-parameter climate rectangle contains
    /// (temperature, humidity, continentalness, erosion, weirdness), or the nearest one.
    /// Thread-safe after Initialize().</summary>
    public BiomeDefinition GetBiome(float temperature, float humidity,
                                    float continentalness, float erosion, float weirdness)
    {
        BiomeDefinition best     = fallbackBiome;
        float           bestDist = float.MaxValue;

        foreach (var biome in biomes)
        {
            if (biome == null) continue;

            if (biome.Matches(temperature, humidity, continentalness, erosion, weirdness))
                return biome;

            float dist = biome.DistanceTo(temperature, humidity, continentalness, erosion, weirdness);
            if (dist < bestDist)
            {
                bestDist = dist;
                best     = biome;
            }
        }

        return best ?? fallbackBiome;
    }

    // ── Legacy 2-parameter matching (backward compatible) ─────────────────────

    /// <summary>Returns the biome whose (temperature, humidity) rectangle contains
    /// the point, or the nearest one. Backward-compatible overload for any code
    /// not yet using the full 5-parameter API.</summary>
    public BiomeDefinition GetBiome(float temperature, float humidity)
    {
        BiomeDefinition best     = fallbackBiome;
        float           bestDist = float.MaxValue;

        foreach (var biome in biomes)
        {
            if (biome == null) continue;

            if (biome.Matches(temperature, humidity))
                return biome;

            float dist = biome.DistanceTo(temperature, humidity);
            if (dist < bestDist)
            {
                bestDist = dist;
                best     = biome;
            }
        }

        return best ?? fallbackBiome;
    }

    // ── Biome list access (for editor tooling) ────────────────────────────────

    /// <summary>Read-only access to the biome list for editor tools (e.g. World Preview).</summary>
    public IReadOnlyList<BiomeDefinition> Biomes => biomes;

    /// <summary>The fallback biome returned when no biome matches.</summary>
    public BiomeDefinition FallbackBiome => fallbackBiome;
}