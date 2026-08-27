using System.Collections.Generic;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
//  BackroomsGeneratorAsset  —  Inspector-configurable generator that produces
//  an infinite liminal Backrooms world from a set of structure configs.
//
//  KEY DECISIONS
//  · Every field is documented and tunable without touching code.
//  · Structure configs are a plain List<BackroomsStructureConfig> — add new
//    types (offices, pool rooms, parking garages…) by creating new
//    BackroomsStructureConfig SOs and appending them here.
//  · solidFillBlockId determines the material of the "void" between rooms
//    (should be same as your wall block for a seamless look, or a different
//    dark block for a cave-like feel).
//
//  RECOMMENDED BLOCK IDS (create matching VoxelBlockType SOs):
//    0  = Air
//    10 = BackroomsFloor   (yellow carpet / linoleum tile texture)
//    11 = BackroomsCeiling (drop-ceiling / fluorescent panel texture)
//    12 = BackroomsWall    (yellow wallpaper texture)
//    13 = BackroomsPillar  (concrete column texture)
//    14 = BackroomsVoid    (completely dark / pitch black — optional)
//
//  Create via: Assets > Create > WorldGen > Generators > Backrooms
// ═══════════════════════════════════════════════════════════════════════════════

[CreateAssetMenu(
    fileName = "BackroomsGeneratorAsset",
    menuName = "WorldGen/Generators/Backrooms")]
public class BackroomsGeneratorAsset : BaseWorldGeneratorAsset
{
    // ── Structure Pool ────────────────────────────────────────────────────────

    [Header("Structure Pool")]
    [Tooltip("All structure types that can appear in this Backrooms level.  " +
             "Each entry carries its own spawnWeight.  Add more for variety.")]
    public List<BackroomsStructureConfig> structureConfigs = new();

    // ── Block IDs ─────────────────────────────────────────────────────────────

    [Header("Block IDs")]
    [Tooltip("Block used to fill all space that is NOT carved out as a structure.  " +
             "Typically the same as wallBlockId on your structure configs.")]
    public byte solidFillBlockId = 12;

    // ── Generator creation ────────────────────────────────────────────────────

    public override IWorldGenerator CreateGenerator()
        => new BackroomsGenerator(structureConfigs, solidFillBlockId);
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// IWorldGenerator implementation for the Backrooms.
/// Holds a reference to the structure config list (read-only at generation time).
/// </summary>
public sealed class BackroomsGenerator : IWorldGenerator
{
    private readonly List<BackroomsStructureConfig> _configs;
    private readonly byte _solidFillId;

    public BackroomsGenerator(List<BackroomsStructureConfig> configs, byte solidFillId)
    {
        _configs     = configs;
        _solidFillId = solidFillId;
    }

    public void Generate(VoxelChunkData data, VoxelWorldSettings settings, int seed)
    {
        BackroomsLayout.Generate(data, settings, _configs, seed, _solidFillId);
    }
}
