using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  DimensionRegistry — Central ScriptableObject catalog of all playable
//  dimensions in the project.
//  Create via: Assets > Create > VoxelWorld > Dimension Registry
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "DimensionRegistry", menuName = "VoxelWorld/Dimension Registry")]
public class DimensionRegistry : ScriptableObject
{
    [SerializeField] private List<DimensionProfile> dimensions = new();

    private Dictionary<string, DimensionProfile> _lookup;
    private static DimensionRegistry _instance;
    public static DimensionRegistry Instance => _instance;

    public void Initialize()
    {
        _instance = this;
        _lookup = new Dictionary<string, DimensionProfile>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < dimensions.Count; i++)
        {
            var dim = dimensions[i];
            if (dim == null || string.IsNullOrEmpty(dim.dimensionId)) continue;

            if (_lookup.ContainsKey(dim.dimensionId))
            {
                Debug.LogWarning($"[DimensionRegistry] Duplicate dimensionId '{dim.dimensionId}' on '{dim.name}'. Skipping.");
                continue;
            }

            _lookup[dim.dimensionId] = dim;
        }
    }

    public DimensionProfile GetDimension(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_lookup == null) Initialize();

        _lookup.TryGetValue(id, out var profile);
        return profile;
    }

    public IReadOnlyList<DimensionProfile> AllDimensions => dimensions;
}

