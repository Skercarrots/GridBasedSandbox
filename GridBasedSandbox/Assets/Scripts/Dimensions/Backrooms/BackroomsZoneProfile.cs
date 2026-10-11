using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  BackroomsZoneProfile — One architectural "biome" inside the Backrooms.
//  Every number that shapes the floor plan lives here, so a dev can tune or clone
//  a zone entirely in the Inspector. Drop the asset into BackroomsGenerator.zones.
//
//  The defaults follow the reference dossier (open-plan halls, wide passages,
//  2x2 megapillars, half-wall counters, soffit beams) but nothing is enforced:
//  push the sliders to build cramped offices, endless galleries, pillar forests…
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "Zone_Backrooms", menuName = "VoxelWorld/Backrooms/Zone Profile")]
public class BackroomsZoneProfile : ScriptableObject
{
    [Header("Identification")]
    [Tooltip("Friendly display name for the debug HUD / logs.")]
    public string zoneName = "Lobby Halls";

    [Tooltip("Relative chance of this zone being picked for a macro sector, compared to the other zones.")]
    [Range(0.1f, 10f)]
    public float selectionWeight = 1.0f;

    [Tooltip("Floor-plan algorithm used by this zone. Empty = the generator's Default Layout.")]
    public BackroomsLayoutStrategy layout;

    [Header("Halls (Open-Plan Layout)")]
    [Tooltip("Target hall edge length in blocks. 8-12 = offices/compartments, 20 = lobby halls, 28+ = colossal halls.")]
    [Range(8, 40)]
    public int hallSize = 20;

    [Tooltip("How far hall boundaries wander from a perfect grid (fraction of the hall size). 0 = rigid grid.")]
    [Range(0f, 0.35f)]
    public float hallSizeJitter = 0.2f;

    [Header("Partitions Between Halls")]
    [Tooltip("Chance that a boundary between two halls is a full-height wall (with openings). " +
             "Higher = denser walls. The rest is split between low walls and fully open boundaries.")]
    [Range(0f, 1f)]
    public float fullWallProbability = 0.3f;

    [Tooltip("Chance that a boundary is a low half-wall (see Half-Wall Height). Full + Half is clamped to 1.")]
    [Range(0f, 1f)]
    public float halfWallProbability = 0.15f;

    [Tooltip("Narrowest opening cut in a wall, in blocks. The reference dossier forbids passages under 4.")]
    [Range(1, 16)]
    public int openingWidthMin = 6;

    [Tooltip("Widest opening cut in a wall, in blocks.")]
    [Range(1, 24)]
    public int openingWidthMax = 10;

    [Tooltip("Height in blocks of half-walls (counters). The top block uses the generator's Half-Wall Cap block.")]
    [Range(1, 3)]
    public int halfWallHeight = 1;

    [Header("Soffit Beams")]
    [Tooltip("How many blocks the ceiling drops over openings and open boundaries (0 = no beams).")]
    [Range(0, 3)]
    public int beamDrop = 1;

    [Tooltip("Width in blocks of the beam strip along a boundary.")]
    [Range(1, 3)]
    public int beamThickness = 2;

    [Header("Megapillars")]
    [Tooltip("Distance in blocks between pillar lattice points (reference: 6-8).")]
    [Range(4, 16)]
    public int pillarSpacing = 7;

    [Tooltip("Pillar edge length in blocks (reference: 2).")]
    [Range(1, 4)]
    public int pillarSize = 2;

    [Tooltip("Chance that a lattice point actually receives a pillar.")]
    [Range(0f, 1f)]
    public float pillarProbability = 0.45f;

    [Tooltip("Minimum free blocks between a pillar and any wall, so pillars never block a route.")]
    [Range(1, 6)]
    public int pillarClearance = 3;

    [Header("Half-Wall Islands (counters / workstations)")]
    [Tooltip("Chance per hall of a free-standing half-wall counter (straight or L-shaped).")]
    [Range(0f, 1f)]
    public float islandProbability = 0.3f;

    [Tooltip("Shortest counter, in blocks.")]
    [Range(3, 20)]
    public int islandLengthMin = 5;

    [Tooltip("Longest counter, in blocks.")]
    [Range(3, 24)]
    public int islandLengthMax = 9;

    [Header("Ceiling Lights & Decay")]
    [Tooltip("Distance in blocks between ceiling light panels (square grid).")]
    [Range(2, 10)]
    public int lightSpacing = 4;

    [Tooltip("Chance that a dark pocket (a whole penumbra cell without lights) forms. 0 = evenly lit.")]
    [Range(0f, 1f)]
    public float penumbraProbability = 0.1f;

    [Tooltip("Edge length in blocks of one penumbra cell (reference: 8-15).")]
    [Range(6, 24)]
    public int penumbraSize = 12;

    [Tooltip("Chance per ceiling block of a missing tile exposing the dark void above.")]
    [Range(0f, 0.5f)]
    public float missingCeilingProbability = 0.03f;

    [Header("Block Overrides (optional)")]
    [Tooltip("Leave empty to use the generator's carpet block.")]
    public VoxelBlockType customCarpet;

    [Tooltip("Leave empty to use the generator's wallpaper block.")]
    public VoxelBlockType customWallpaper;

    [Tooltip("Leave empty to use the generator's ceiling block.")]
    public VoxelBlockType customCeiling;

    // Cached IDs (baked on the main thread by the generator; read off-thread)
    [System.NonSerialized] private byte _cachedCarpetId;
    [System.NonSerialized] private byte _cachedWallpaperId;
    [System.NonSerialized] private byte _cachedCeilingId;
    [System.NonSerialized] private bool _idsCached;

    /// <summary>Bakes block IDs so Generate() never touches Unity objects off-thread. Main thread only.</summary>
    public void CacheIds(byte defaultCarpet, byte defaultWallpaper, byte defaultCeiling)
    {
        _cachedCarpetId = customCarpet != null ? customCarpet.blockId : defaultCarpet;
        _cachedWallpaperId = customWallpaper != null ? customWallpaper.blockId : defaultWallpaper;
        _cachedCeilingId = customCeiling != null ? customCeiling.blockId : defaultCeiling;
        _idsCached = true;
    }

    public byte GetEffectiveCarpet(byte defaultCarpet) => _idsCached ? _cachedCarpetId : (customCarpet != null ? customCarpet.blockId : defaultCarpet);
    public byte GetEffectiveWallpaper(byte defaultWallpaper) => _idsCached ? _cachedWallpaperId : (customWallpaper != null ? customWallpaper.blockId : defaultWallpaper);
    public byte GetEffectiveCeiling(byte defaultCeiling) => _idsCached ? _cachedCeilingId : (customCeiling != null ? customCeiling.blockId : defaultCeiling);

    private void OnValidate()
    {
        if (openingWidthMax < openingWidthMin) openingWidthMax = openingWidthMin;
        if (islandLengthMax < islandLengthMin) islandLengthMax = islandLengthMin;

        // Keep the partition mix a valid probability split
        if (fullWallProbability + halfWallProbability > 1f)
            halfWallProbability = 1f - fullWallProbability;
    }
}
