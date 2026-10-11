using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  BackroomsContinentProfile — Defines a massive continent / super-sector.
//
//  A continent functions as a colossal enclosed macro-structure (e.g. 256x256+),
//  enclosed by a structural perimeter barrier wall, with only a few specific
//  gateways (3x3 transition passages) leading to neighbor continents.
//
//  Inside the continent, a cluster of designated sub-zones / biomes (allowedZones)
//  are scattered, giving each continent its own cohesive architectural theme.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "Continent_New", menuName = "VoxelWorld/Backrooms/Continent Profile")]
public class BackroomsContinentProfile : ScriptableObject
{
    [Header("Identification")]
    [Tooltip("Friendly display name for debug HUD / logs.")]
    public string continentName = "Sector Alpha - Yellow Complex";

    [Tooltip("Relative chance of this continent type being selected.")]
    [Range(0.1f, 10f)]
    public float selectionWeight = 1.0f;

    [Header("Continent Biome Cluster")]
    [Tooltip("Sub-zones and biomes that spawn scattered across this continent.")]
    public List<BackroomsZoneProfile> allowedZones = new List<BackroomsZoneProfile>();

    [Header("Perimeter Bulkhead & Gateways")]
    [Tooltip("Block type for the perimeter wall separating this continent from others (null = default wallpaper).")]
    public VoxelBlockType borderWallBlock;

    [Tooltip("Number of gateway passages on each border edge leading to neighboring continents.")]
    [Range(1, 4)]
    public int gatewaysPerEdge = 2;

    [Tooltip("Width of the gateway passage in blocks (user requested: 3).")]
    [Range(2, 6)]
    public int gatewayWidth = 3;

    // Cached block IDs baked on main thread
    [System.NonSerialized] private byte _cachedBorderWallId;
    [System.NonSerialized] private bool _idsCached;

    public void Prepare(byte defaultCarpet, byte defaultWallpaper, byte defaultCeiling)
    {
        _cachedBorderWallId = borderWallBlock != null ? borderWallBlock.blockId : defaultWallpaper;
        _idsCached = true;

        if (allowedZones != null)
        {
            for (int i = 0; i < allowedZones.Count; i++)
            {
                if (allowedZones[i] != null)
                {
                    allowedZones[i].CacheIds(defaultCarpet, defaultWallpaper, defaultCeiling);
                    if (allowedZones[i].layout != null)
                        allowedZones[i].layout.Prepare();
                }
            }
        }
    }

    public byte GetEffectiveBorderWall(byte defaultWallpaper)
    {
        return _idsCached ? _cachedBorderWallId : (borderWallBlock != null ? borderWallBlock.blockId : defaultWallpaper);
    }
}

