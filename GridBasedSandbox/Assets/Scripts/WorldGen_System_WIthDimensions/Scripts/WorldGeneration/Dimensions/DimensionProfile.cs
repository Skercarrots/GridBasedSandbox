using System.Collections.Generic;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
//  DimensionProfile  —  ScriptableObject that fully describes one world dimension.
//
//  WHAT IS A DIMENSION?
//  Exactly one DimensionProfile is active at any given time.
//  Switching dimensions = swapping which profile VoxelWorldManager references.
//  This mirrors how Minecraft handles the Overworld / Nether / End:
//  same engine, different data.
//
//  WHAT DOES IT HOLD?
//  · dimensionId     — unique string key (e.g. "overworld", "backrooms_level0")
//  · worldSettings   — VoxelWorldSettings SO (chunk size, height range, material…)
//  · generatorAsset  — a BaseWorldGeneratorAsset SO that instantiates the IWorldGenerator
//  · passes          — ordered list of BaseGenerationPassAsset SOs (post-process steps)
//
//  HOW TO CREATE ONE
//  Assets > Create > WorldGen > Dimension Profile
//
//  HOW TO REGISTER / SWITCH
//  See DimensionRegistry.  Call DimensionRegistry.SetActiveDimension(id) from
//  any gameplay code (portal triggers, menu, cheat console, etc.).
// ═══════════════════════════════════════════════════════════════════════════════

[CreateAssetMenu(fileName = "DimensionProfile_New", menuName = "WorldGen/Dimension Profile")]
public class DimensionProfile : ScriptableObject
{
    // ── Identity ──────────────────────────────────────────────────────────────

    [Header("Identity")]
    [Tooltip("Unique string key for this dimension.  Must not contain spaces.  " +
             "Used by DimensionRegistry and save-data.  Examples: 'overworld', 'backrooms_level0'.")]
    public string dimensionId = "overworld";

    [Tooltip("Display name shown in UI / debug overlays.")]
    public string displayName = "Overworld";

    // ── Core Settings ─────────────────────────────────────────────────────────

    [Header("Core Settings")]
    [Tooltip("VoxelWorldSettings asset shared by this dimension.  " +
             "Can be shared across dimensions or unique per dimension.")]
    public VoxelWorldSettings worldSettings;

    // ── Generator ─────────────────────────────────────────────────────────────

    [Header("Generator")]
    [Tooltip("Determines the base terrain generation strategy for this dimension.  " +
             "Assign any BaseWorldGeneratorAsset subclass (OverworldGeneratorAsset, " +
             "BackroomsGeneratorAsset, etc.).")]
    public BaseWorldGeneratorAsset generatorAsset;

    // ── Post-Process Passes ───────────────────────────────────────────────────

    [Header("Post-Process Passes (applied in order after the generator)")]
    [Tooltip("Each entry is a BaseGenerationPassAsset SO.  Passes run top-to-bottom " +
             "on every freshly generated chunk before meshing.  Add, remove, or " +
             "reorder without touching any generator code.")]
    public List<BaseGenerationPassAsset> passes = new();

    // ── Spawn / Spawn Options ─────────────────────────────────────────────────

    [Header("Player Spawn")]
    [Tooltip("Where the player is placed when entering this dimension for the first time.")]
    public Vector3 defaultSpawnPosition = new Vector3(0.5f, 5f, 0.5f);

    // ── Runtime helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Instantiates the IWorldGenerator from the assigned generatorAsset.
    /// Returns null (with a log warning) if no asset is assigned.
    /// </summary>
    public IWorldGenerator CreateGenerator()
    {
        if (generatorAsset == null)
        {
            Debug.LogWarning($"[DimensionProfile '{dimensionId}'] No generatorAsset assigned. " +
                             "Chunks will be empty.");
            return null;
        }
        return generatorAsset.CreateGenerator();
    }

    /// <summary>
    /// Creates and returns the ordered list of IGenerationPass instances
    /// from the assigned pass assets.  Null assets are skipped with a warning.
    /// </summary>
    public List<IGenerationPass> CreatePasses()
    {
        var result = new List<IGenerationPass>(passes.Count);
        for (int i = 0; i < passes.Count; i++)
        {
            if (passes[i] == null)
            {
                Debug.LogWarning($"[DimensionProfile '{dimensionId}'] passes[{i}] is null. Skipping.");
                continue;
            }
            IGenerationPass pass = passes[i].CreatePass();
            if (pass != null) result.Add(pass);
        }
        return result;
    }
}
