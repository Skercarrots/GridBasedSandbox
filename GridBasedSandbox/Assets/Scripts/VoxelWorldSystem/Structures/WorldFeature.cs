using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  WorldFeature — ScriptableObject configuring the placement rules, density,
//  and terrain requirements for one world feature (trees, rocks, ruins, etc.).
//  Create via: Assets > Create > GridBasedSandbox/Voxel World/Structures/World Feature
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "Feature_New", menuName = "GridBasedSandbox/Voxel World/Structures/World Feature")]
public class WorldFeature : ScriptableObject
{
    [Header("What to place")]
    [Tooltip("Procedural recipe used to generate structure variants.")]
    public StructureRecipe recipe;

    [Tooltip("Optional pre-baked template asset. If set, this template is placed directly " +
             "instead of generating procedurally via recipe.")]
    public VoxelStructureTemplate bakedTemplate;

    [Header("Where to place")]
    [Tooltip("Biomes where this feature is allowed to spawn. Leave empty to allow in all biomes.")]
    public BiomeDefinition[] allowedBiomes;

    [Tooltip("Average number of structures per 16×16 chunk column.")]
    [Range(0f, 16f)] public float density = 0.5f;

    [Tooltip("Minimum spacing in blocks between structure anchors.")]
    [Range(0, 32)] public int minSpacing = 4;

    [Header("Clustering (Optional)")]
    [Tooltip("Optional 2D noise layer that modulates local density. High values create dense " +
             "groves/clusters, while low values thin out placement.")]
    public NoiseLayerConfig clusterNoise;

    [Tooltip("Minimum cluster noise value [0..1] required for placement when clusterNoise is active.")]
    [Range(0f, 1f)] public float clusterThreshold = 0.45f;

    [Header("Terrain Requirements")]
    [Tooltip("Blocks this feature can sit on top of (e.g. Grass, Dirt, Sand). " +
             "Leave empty to allow on any solid ground.")]
    public VoxelBlockType[] allowedGroundBlocks;

    [Tooltip("Maximum terrain slope gradient where this structure can spawn (1.0 = ~45°).")]
    [Range(0f, 2f)] public float maxSlope = 0.5f;

    [Tooltip("Minimum world Y altitude.")]
    [Range(-64, 256)] public int minAltitude = -32;

    [Tooltip("Maximum world Y altitude.")]
    [Range(-64, 256)] public int maxAltitude = 200;

    [Tooltip("If true, features cannot spawn underwater or at sea level.")]
    public bool avoidWater = true;

    [Tooltip("Number of open air blocks required directly above the ground before placing.")]
    [Range(0, 16)] public int requiredAirClearance = 4;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Maximum reach of this feature from its anchor point.</summary>
    public int MaxExtent
    {
        get
        {
            if (bakedTemplate != null)
                return Mathf.Max(bakedTemplate.bounds.x, bakedTemplate.bounds.z, bakedTemplate.bounds.y);
            if (recipe != null)
                return recipe.MaxExtent;
            return 8;
        }
    }

    /// <summary>Returns thread-safe StructureData for placement. Safe off-thread.</summary>
    public StructureData GetStructureData(int seed)
    {
        if (bakedTemplate != null)
            return bakedTemplate.ToData();
        if (recipe != null)
            return recipe.GenerateData(seed);
        return new StructureData();
    }

    /// <summary>Checks whether groundBlockId is valid for this feature.</summary>
    public bool IsGroundAllowed(byte groundBlockId)
    {
        if (allowedGroundBlocks == null || allowedGroundBlocks.Length == 0)
            return groundBlockId != 0; // Any solid block

        for (int i = 0; i < allowedGroundBlocks.Length; i++)
        {
            if (allowedGroundBlocks[i] != null && allowedGroundBlocks[i].blockId == groundBlockId)
                return true;
        }
        return false;
    }

    /// <summary>Checks whether the given biome is allowed.</summary>
    public bool IsBiomeAllowed(BiomeDefinition biome)
    {
        if (allowedBiomes == null || allowedBiomes.Length == 0)
            return true;

        for (int i = 0; i < allowedBiomes.Length; i++)
        {
            if (allowedBiomes[i] == biome)
                return true;
        }
        return false;
    }
}
