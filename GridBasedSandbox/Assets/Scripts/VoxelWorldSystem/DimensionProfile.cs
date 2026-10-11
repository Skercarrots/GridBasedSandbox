using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  DimensionProfile — Bundles a generator, world settings, and environment
//  atmosphere into a cohesive, data-driven dimension definition.
//  Create via: Assets > Create > VoxelWorld > Dimension Profile
//
//  One profile per dimension (Overworld, Backrooms Level 0, Nether, Void, ...).
//  Assign profiles to DimensionRegistry and let DimensionManager coordinate them.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "DimensionProfile_New", menuName = "VoxelWorld/Dimension Profile")]
public class DimensionProfile : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Unique programmatic identifier (e.g. 'overworld', 'backrooms_level0').")]
    public string dimensionId = "overworld";

    [Tooltip("Human-readable title displayed in UI, HUD, and loading screens.")]
    public string dimensionName = "Overworld";

    [Header("World & Generator")]
    [Tooltip("WorldGenerator asset responsible for procedural terrain/structures in this dimension.")]
    public WorldGenerator generator;

    [Tooltip("Chunk dimensions, height limits, block registry, and voxel materials.")]
    public VoxelWorldSettings settings;

    [Header("Environment & Atmosphere")]
    [Tooltip("Atmospheric profile (lighting, fog, ambient audio, skybox).")]
    public DimensionEnvironmentProfile environmentProfile;

    [Header("Spawn & Bounds")]
    [Tooltip("Default column coordinate (wx, wz) where player spawns if no previous position exists.")]
    public Vector2Int defaultSpawnColumn = Vector2Int.zero;

    [Tooltip("If true, world is an enclosed indoor structure (suppresses skybox, sets dark ceiling bounds).")]
    public bool isEnclosed = false;

    [Tooltip("Enclosed dimensions only: minimum contiguous air blocks above a floor for it to count as a valid " +
             "spawn fallback. Must be larger than the dimension's ceiling-void (plenum) height so the spawn " +
             "search never picks the space above the ceiling.")]
    [Range(2, 8)] public int minSpawnClearance = 3;

    [Tooltip("Salt mixed into master seed so dimensions have distinct procedural layouts. " +
             "0 = use the base seed untouched (keeps the primary dimension identical on every re-entry).")]
    public int seedSalt = 0;

    [Header("Return Portal")]
    [Tooltip("If true, entering this dimension spawns a return rift on the wall anchor provided by the " +
             "generator (WorldGenerator.TryGetArrivalPoint).")]
    public bool spawnReturnPortal = false;

    [Tooltip("Dimension the return rift leads to.")]
    public string returnDimensionId = "overworld";
}