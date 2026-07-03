using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
//  BackroomsStructureConfig  —  Data-only ScriptableObject describing ONE
//  type of spatial unit that can appear in the Backrooms world.
//
//  DESIGN PHILOSOPHY
//  Structures are parameterised ranges, not fixed blueprints.  The generator
//  picks concrete dimensions at runtime using seeded hashing, so each chunk
//  looks distinct while still obeying the config constraints.
//
//  STRUCTURE TYPES
//  ┌──────────────────┬────────────────────────────────────────────────────────┐
//  │ Corridor         │ Long, narrow passage (widthRange.x × widthRange.y,    │
//  │                  │ heightRange.x × heightRange.y tall)                   │
//  ├──────────────────┼────────────────────────────────────────────────────────┤
//  │ Room             │ Wider open space (lengthRange × widthRange × height)  │
//  ├──────────────────┼────────────────────────────────────────────────────────┤
//  │ PillarRoom       │ Room variant with a grid of pillar columns             │
//  ├──────────────────┼────────────────────────────────────────────────────────┤
//  │ CrawlSpace       │ Very low passage (height 1-2), claustrophobic          │
//  └──────────────────┴────────────────────────────────────────────────────────┘
//
//  WEIGHT
//  Each config carries a spawnWeight.  BackroomsLayout uses a weighted-random
//  pick, so rare structure types can be included at low weights.
//
//  BLOCK IDs
//  Block IDs must match what's in your VoxelBlockRegistry.
//  See BackroomsGeneratorAsset for the recommended Backrooms block set.
//
//  Create via: Assets > Create > WorldGen > Backrooms > Structure Config
// ═══════════════════════════════════════════════════════════════════════════════

[CreateAssetMenu(
    fileName = "BackroomsStructure_New",
    menuName = "WorldGen/Backrooms/Structure Config")]
public class BackroomsStructureConfig : ScriptableObject
{
    // ── Identity ──────────────────────────────────────────────────────────────

    [Header("Identity")]
    public string structureName = "Corridor";

    [Tooltip("Type drives which layout algorithm is used.")]
    public BackroomsStructureType structureType = BackroomsStructureType.Corridor;

    [Tooltip("Relative probability of this structure appearing in any given chunk.  " +
             "All weights across all configs are summed; each entry's share = weight / total.")]
    [Range(0f, 100f)]
    public float spawnWeight = 10f;

    // ── Dimensional Ranges ────────────────────────────────────────────────────

    [Header("Dimensional Ranges (min / max in voxels)")]

    [Tooltip("Min/max length along the primary axis (Z for corridors, X for rooms).")]
    public Vector2Int lengthRange = new Vector2Int(8, 24);

    [Tooltip("Min/max width along the secondary axis.")]
    public Vector2Int widthRange  = new Vector2Int(3, 6);

    [Tooltip("Min/max interior height in blocks (floor-to-ceiling, excluding floor/ceiling slabs).")]
    public Vector2Int heightRange = new Vector2Int(3, 5);

    // ── Pillar Room Config (only used when structureType == PillarRoom) ───────

    [Header("Pillar Config (PillarRoom only)")]
    [Tooltip("Spacing between pillar centres in X and Z.")]
    public Vector2Int pillarSpacing = new Vector2Int(4, 4);

    [Tooltip("Pillar radius in blocks (1 = single column, 2 = 2×2 block square, etc.).")]
    [Range(1, 3)]
    public int pillarRadius = 1;

    // ── Block IDs ─────────────────────────────────────────────────────────────

    [Header("Block IDs (must match VoxelBlockRegistry)")]

    [Tooltip("Block used for floors.  Yellow carpet texture recommended.")]
    public byte floorBlockId = 10;

    [Tooltip("Block used for ceilings.")]
    public byte ceilingBlockId = 11;

    [Tooltip("Block used for walls.")]
    public byte wallBlockId = 12;

    [Tooltip("Block used for pillars (PillarRoom only).")]
    public byte pillarBlockId = 13;

    [Tooltip("Block ID 0 = Air; used to carve out the interior.")]
    public byte airBlockId = 0;

    // ── Variation ─────────────────────────────────────────────────────────────

    [Header("Variation")]

    [Tooltip("If true, the structure can be oriented along the X axis instead of Z.")]
    public bool allowRotation = true;

    [Tooltip("Probability [0-1] that the ceiling has small notch cuts (adds visual variety).")]
    [Range(0f, 1f)]
    public float ceilingVariationChance = 0.15f;

    [Tooltip("Probability [0-1] that a wall has a recessed alcove (1-block deep).")]
    [Range(0f, 1f)]
    public float wallAlcoveChance = 0.1f;
}

// ─────────────────────────────────────────────────────────────────────────────

public enum BackroomsStructureType
{
    Corridor,
    Room,
    PillarRoom,
    CrawlSpace,
}
