using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  VoxelBlockRegistry  — ScriptableObject that maps byte IDs → VoxelBlockType.
//  Create ONE per project via: Assets > Create > VoxelWorld > Block Registry
//
//  USAGE
//    1. Add all your VoxelBlockType SOs to the `blocks` list.
//    2. Assign this registry to VoxelWorldSettings.
//    3. Any system calls VoxelBlockRegistry.GetBlock(id) at runtime.
//
//  RULES
//    • blockId 0 is Air — must always exist in the list.
//    • IDs must be unique; the editor logs a warning if not.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "BlockRegistry", menuName = "VoxelWorld/Block Registry")]
public class VoxelBlockRegistry : ScriptableObject
{
    [SerializeField] private List<VoxelBlockType> blocks = new();

    // Built at runtime from the list above — O(1) lookup.
    private Dictionary<byte, VoxelBlockType> _lookup;

    // ── Singleton-style accessor (runtime only) ───────────────────────────────
    private static VoxelBlockRegistry _instance;
    public static VoxelBlockRegistry Instance => _instance;

    /// <summary>Call this once from VoxelWorldManager.Awake().</summary>
    public void Initialize()
    {
        _instance = this;
        _lookup   = new Dictionary<byte, VoxelBlockType>(blocks.Count);

        foreach (var block in blocks)
        {
            if (block == null) continue;

            if (_lookup.ContainsKey(block.blockId))
            {
                Debug.LogWarning($"[VoxelBlockRegistry] Duplicate blockId {block.blockId} on '{block.blockName}'. Skipping.");
                continue;
            }
            _lookup[block.blockId] = block;
        }

        if (!_lookup.ContainsKey(0))
            Debug.LogError("[VoxelBlockRegistry] No Air block (id 0) found! Add a VoxelBlockType with blockId = 0.");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Returns the block definition for the given id, or null for Air/unknown.</summary>
    public VoxelBlockType GetBlock(byte id)
    {
        _lookup.TryGetValue(id, out VoxelBlockType block);
        return block;
    }

    /// <summary>Returns true if the block at this id is solid (should cull neighbours).</summary>
    public bool IsSolid(byte id)
    {
        if (id == 0) return false;
        return _lookup.TryGetValue(id, out var b) && b.isSolid;
    }

    public int Count => _lookup?.Count ?? 0;
}
