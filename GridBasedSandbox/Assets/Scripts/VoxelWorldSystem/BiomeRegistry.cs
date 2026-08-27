using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  BiomeRegistry — ScriptableObject holding every biome for one dimension.
//  Create via: Assets > Create > VoxelWorld > Biome Registry
//
//  Assign it to your OverworldGenerator (or any other WorldGenerator that
//  needs biomes). Different dimensions can use different registries — a
//  Nether-style dimension's registry wouldn't contain your Plains/Forest biomes.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "BiomeRegistry", menuName = "VoxelWorld/Biome Registry")]
public class BiomeRegistry : ScriptableObject
{
    [SerializeField] private List<BiomeDefinition> biomes = new();

    [Tooltip("Used only if no biome's range contains the sampled point. Should rarely " +
             "trigger once your biomes collectively cover the full 0..1 x 0..1 range.")]
    [SerializeField] private BiomeDefinition fallbackBiome;

    /// <summary>Returns the biome whose range contains (temperature, humidity), or the closest one.</summary>
    public BiomeDefinition GetBiome(float temperature, float humidity)
    {
        BiomeDefinition best = fallbackBiome;
        float bestDist = float.MaxValue;

        foreach (var biome in biomes)
        {
            if (biome == null) continue;

            if (biome.Matches(temperature, humidity))
                return biome;

            float dist = biome.DistanceTo(temperature, humidity);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = biome;
            }
        }

        return best;
    }
}