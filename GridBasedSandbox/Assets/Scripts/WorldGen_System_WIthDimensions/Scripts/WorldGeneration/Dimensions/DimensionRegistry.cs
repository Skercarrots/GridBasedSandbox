using System;
using System.Collections.Generic;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
//  DimensionRegistry  —  Central catalogue of all registered DimensionProfile SOs.
//
//  USAGE
//  1. Create a DimensionRegistry asset via
//     Assets > Create > WorldGen > Dimension Registry
//  2. Drag your DimensionProfile assets into the `dimensions` list.
//  3. Assign the DimensionRegistry to VoxelWorldManagerExtended in the Inspector.
//  4. Call DimensionRegistry.SetActiveDimension("backrooms_level0") from any
//     gameplay code (portal triggers, menus, debug consoles, etc.).
//
//  SWITCHING DIMENSIONS
//  · Call SetActiveDimension(id).
//  · VoxelWorldManagerExtended listens to OnDimensionChanged and flushes all
//    cached chunk data before reloading with the new generator + passes.
//  · The player is teleported to DimensionProfile.defaultSpawnPosition.
//
//  THREAD SAFETY
//  · All writes happen on the main thread (triggered by gameplay events).
//  · ActiveProfile / ActiveGenerator / ActivePasses are read-only at generation
//    time and are never written during a frame that is also generating chunks.
// ═══════════════════════════════════════════════════════════════════════════════

[CreateAssetMenu(fileName = "DimensionRegistry", menuName = "WorldGen/Dimension Registry")]
public class DimensionRegistry : ScriptableObject
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Registered Dimensions (order = fallback priority)")]
    [Tooltip("All DimensionProfile assets known to this game.  " +
             "The first entry is loaded on startup unless overridden.")]
    public List<DimensionProfile> dimensions = new();

    [Header("Startup Dimension")]
    [Tooltip("ID of the dimension loaded when the scene first starts.  " +
             "Leave blank to use the first entry in the list.")]
    public string startingDimensionId = "";

    // ── Runtime state ─────────────────────────────────────────────────────────

    private Dictionary<string, DimensionProfile> _lookup;

    /// <summary>Currently active dimension profile.</summary>
    public DimensionProfile ActiveProfile { get; private set; }

    /// <summary>Generator instance for the active dimension.</summary>
    public IWorldGenerator ActiveGenerator { get; private set; }

    /// <summary>Pass instances for the active dimension (in order).</summary>
    public List<IGenerationPass> ActivePasses { get; private set; } = new();

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired on the main thread when a new dimension becomes active.
    /// VoxelWorldManagerExtended subscribes to this to flush and reload chunks.
    /// </summary>
    public event Action<DimensionProfile> OnDimensionChanged;

    // ── Initialisation ────────────────────────────────────────────────────────

    /// <summary>
    /// Must be called once from VoxelWorldManagerExtended.Awake().
    /// Builds the lookup dictionary and activates the starting dimension.
    /// </summary>
    public void Initialize()
    {
        _lookup = new Dictionary<string, DimensionProfile>(dimensions.Count);

        foreach (var profile in dimensions)
        {
            if (profile == null) continue;

            if (_lookup.ContainsKey(profile.dimensionId))
            {
                Debug.LogWarning($"[DimensionRegistry] Duplicate dimensionId '{profile.dimensionId}'.  " +
                                 "Only the first entry is used.");
                continue;
            }
            _lookup[profile.dimensionId] = profile;
        }

        if (_lookup.Count == 0)
        {
            Debug.LogError("[DimensionRegistry] No dimensions registered!");
            return;
        }

        // Pick starting dimension
        string startId = string.IsNullOrEmpty(startingDimensionId)
            ? dimensions[0].dimensionId
            : startingDimensionId;

        ActivateDimension(startId, fireEvent: false);

        Debug.Log($"[DimensionRegistry] Initialized with {_lookup.Count} dimension(s).  " +
                  $"Active: '{ActiveProfile?.dimensionId}'.");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Switches the active dimension.  Fires OnDimensionChanged so
    /// VoxelWorldManagerExtended can flush and reload the world.
    /// Safe to call from any gameplay code on the main thread.
    /// </summary>
    /// <param name="id">dimensionId of the target DimensionProfile.</param>
    /// <returns>True if the switch succeeded, false if the id was not found.</returns>
    public bool SetActiveDimension(string id)
    {
        if (ActiveProfile != null && ActiveProfile.dimensionId == id)
        {
            Debug.Log($"[DimensionRegistry] Dimension '{id}' is already active.");
            return true;
        }

        return ActivateDimension(id, fireEvent: true);
    }

    /// <summary>Returns the DimensionProfile for <paramref name="id"/>, or null.</summary>
    public DimensionProfile GetProfile(string id)
    {
        _lookup.TryGetValue(id, out var profile);
        return profile;
    }

    /// <summary>Returns all registered dimension IDs.</summary>
    public IEnumerable<string> GetAllIds()
    {
        if (_lookup == null) yield break;
        foreach (var key in _lookup.Keys) yield return key;
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private bool ActivateDimension(string id, bool fireEvent)
    {
        if (_lookup == null || !_lookup.TryGetValue(id, out var profile))
        {
            Debug.LogError($"[DimensionRegistry] Unknown dimension id '{id}'.");
            return false;
        }

        ActiveProfile   = profile;
        ActiveGenerator = profile.CreateGenerator();
        ActivePasses    = profile.CreatePasses();

        Debug.Log($"[DimensionRegistry] Activated dimension '{id}'  " +
                  $"(generator: {ActiveGenerator?.GetType().Name ?? "null"}, " +
                  $"passes: {ActivePasses.Count}).");

        if (fireEvent) OnDimensionChanged?.Invoke(profile);
        return true;
    }
}
