using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  StructureRecipe — Abstract base class for procedural structure generators.
//
//  Subclasses like TreeRecipe implement procedural generation algorithms
//  (e.g. L-systems, stochastic branching, voxelization) that output voxel blocks
//  deterministically from a random seed.
// ─────────────────────────────────────────────────────────────────────────────

public abstract class StructureRecipe : ScriptableObject
{
    /// <summary>Generates thread-safe structure voxel data from a deterministic seed.
    /// Safe to call on background worker threads.</summary>
    public abstract StructureData GenerateData(int seed);

    /// <summary>Generates a ScriptableObject template asset from a seed.
    /// MAIN THREAD ONLY — useful for editor preview and baking.</summary>
    public virtual VoxelStructureTemplate Generate(int seed)
    {
        var template = CreateInstance<VoxelStructureTemplate>();
        var data = GenerateData(seed);
        template.FromData(data);
        return template;
    }

    /// <summary>Maximum possible extent in any direction (X, Y, Z) from anchor.
    /// Used for cross-chunk placement radius calculation.</summary>
    public abstract int MaxExtent { get; }
}
