#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  CreateDefaultBlocksMenu — Editor utility that creates any missing default
//  VoxelBlockType assets and registers them in the BlockRegistry.
//
//  Menu: GridBasedSandbox > Voxel World > Create Missing Default Blocks
//
//  IDEMPOTENT: checks existing assets by blockId before creating. Running it
//  multiple times is safe — it only creates what's missing.
// ─────────────────────────────────────────────────────────────────────────────

public static class CreateDefaultBlocksMenu
{
    private const string BlocksFolder   = "Assets/ScriptableObjects/Voxel World/Blocks";
    private const string RegistryPath   = "Assets/ScriptableObjects/Voxel World/Block Registry/BlockRegistry.asset";

    // ── Block definitions ────────────────────────────────────────────────────

    private struct BlockDef
    {
        public byte       id;
        public string     name;
        public bool       isSolid;
        public bool       isUnbreakable;
        public bool       isTransparent;
        public Vector2Int defaultTile;   // (-1,0) = placeholder
        public Color      placeholderColor;
    }

    private static readonly BlockDef[] DefaultBlocks = new[]
    {
        // Existing blocks (won't be re-created if already present)
        new BlockDef { id = 0, name = "Air",        isSolid = false, defaultTile = new Vector2Int(0, 0) },
        new BlockDef { id = 1, name = "Stone",      isSolid = true,  defaultTile = new Vector2Int(0, 0) },
        new BlockDef { id = 2, name = "Dirt",        isSolid = true,  defaultTile = new Vector2Int(1, 0) },
        new BlockDef { id = 3, name = "Grass",       isSolid = true,  defaultTile = new Vector2Int(1, 0) },
        new BlockDef { id = 4, name = "Sand",        isSolid = true,  defaultTile = new Vector2Int(3, 0) },
        new BlockDef { id = 5, name = "Water",       isSolid = false, isTransparent = true, defaultTile = new Vector2Int(0, 1) },

        // New blocks (Phase 1)
        new BlockDef { id =  6, name = "Bedrock",    isSolid = true, isUnbreakable = true,
                       defaultTile = new Vector2Int(3, 3),
                       placeholderColor = new Color(0.2f, 0.2f, 0.2f, 1f) },

        new BlockDef { id =  7, name = "Log",        isSolid = true,
                       defaultTile = new Vector2Int(3, 3),
                       placeholderColor = new Color(0.55f, 0.35f, 0.15f, 1f) },

        new BlockDef { id =  8, name = "Leaves",     isSolid = true,
                       defaultTile = new Vector2Int(3, 3),
                       placeholderColor = new Color(0.2f, 0.6f, 0.15f, 1f) },

        new BlockDef { id =  9, name = "Snow",       isSolid = true,
                       defaultTile = new Vector2Int(3, 3),
                       placeholderColor = new Color(0.95f, 0.97f, 1.0f, 1f) },

        new BlockDef { id = 10, name = "Gravel",     isSolid = true,
                       defaultTile = new Vector2Int(3, 3),
                       placeholderColor = new Color(0.55f, 0.5f, 0.45f, 1f) },

        new BlockDef { id = 11, name = "Coal Ore",   isSolid = true,
                       defaultTile = new Vector2Int(3, 3),
                       placeholderColor = new Color(0.25f, 0.25f, 0.25f, 1f) },

        new BlockDef { id = 12, name = "Iron Ore",   isSolid = true,
                       defaultTile = new Vector2Int(3, 3),
                       placeholderColor = new Color(0.72f, 0.55f, 0.42f, 1f) },

        new BlockDef { id = 13, name = "Copper Ore", isSolid = true,
                       defaultTile = new Vector2Int(3, 3),
                       placeholderColor = new Color(0.6f, 0.45f, 0.3f, 1f) },

        new BlockDef { id = 14, name = "Gold Ore",   isSolid = true,
                       defaultTile = new Vector2Int(3, 3),
                       placeholderColor = new Color(0.9f, 0.75f, 0.2f, 1f) },
    };

    // ── Menu entry ───────────────────────────────────────────────────────────

    [MenuItem("GridBasedSandbox/Voxel World/Create Missing Default Blocks")]
    public static void CreateMissingBlocks()
    {
        // Ensure folders exist
        EnsureFolderExists(BlocksFolder);

        // Load existing blocks by id
        var existingById = new Dictionary<byte, VoxelBlockType>();
        string[] guids = AssetDatabase.FindAssets("t:VoxelBlockType", new[] { BlocksFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var block = AssetDatabase.LoadAssetAtPath<VoxelBlockType>(path);
            if (block != null && !existingById.ContainsKey(block.blockId))
                existingById[block.blockId] = block;
        }

        // Create missing blocks
        int created = 0;
        var allBlocks = new List<VoxelBlockType>(existingById.Values);

        foreach (var def in DefaultBlocks)
        {
            if (existingById.ContainsKey(def.id))
            {
                // Already exists — skip but keep in allBlocks for registry
                continue;
            }

            var block = ScriptableObject.CreateInstance<VoxelBlockType>();
            block.blockId          = def.id;
            block.blockName        = def.name;
            block.isSolid          = def.isSolid;
            block.isUnbreakable    = def.isUnbreakable;
            block.isTransparent    = def.isTransparent;
            block.defaultTile      = def.defaultTile;
            block.placeholderColor = def.placeholderColor;

            // Leave per-face overrides at (-1, 0) = use defaultTile
            block.tileTop    = new Vector2Int(-1, 0);
            block.tileBottom = new Vector2Int(-1, 0);
            block.tileFront  = new Vector2Int(-1, 0);
            block.tileBack   = new Vector2Int(-1, 0);
            block.tileLeft   = new Vector2Int(-1, 0);
            block.tileRight  = new Vector2Int(-1, 0);

            string assetName = def.name.Replace(" ", "");
            string assetPath = $"{BlocksFolder}/{assetName}.asset";
            AssetDatabase.CreateAsset(block, assetPath);
            allBlocks.Add(block);
            existingById[def.id] = block;
            created++;

            Debug.Log($"[CreateDefaultBlocks] Created {def.name} (id {def.id}) at {assetPath}");
        }

        // Update the BlockRegistry to include any new blocks
        var registry = AssetDatabase.LoadAssetAtPath<VoxelBlockRegistry>(RegistryPath);
        if (registry != null)
        {
            // Use SerializedObject to modify the registry's blocks list properly
            var so = new SerializedObject(registry);
            var blocksProp = so.FindProperty("blocks");

            // Build a set of already-registered block guids
            var registeredGuids = new HashSet<string>();
            for (int i = 0; i < blocksProp.arraySize; i++)
            {
                var element = blocksProp.GetArrayElementAtIndex(i);
                var obj = element.objectReferenceValue;
                if (obj != null)
                {
                    string path = AssetDatabase.GetAssetPath(obj);
                    registeredGuids.Add(AssetDatabase.AssetPathToGUID(path));
                }
            }

            int added = 0;
            foreach (var block in allBlocks)
            {
                string path = AssetDatabase.GetAssetPath(block);
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!registeredGuids.Contains(guid))
                {
                    int index = blocksProp.arraySize;
                    blocksProp.InsertArrayElementAtIndex(index);
                    blocksProp.GetArrayElementAtIndex(index).objectReferenceValue = block;
                    registeredGuids.Add(guid);
                    added++;
                }
            }

            if (added > 0)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(registry);
                Debug.Log($"[CreateDefaultBlocks] Added {added} block(s) to BlockRegistry.");
            }
        }
        else
        {
            Debug.LogWarning($"[CreateDefaultBlocks] BlockRegistry not found at {RegistryPath}. " +
                             "New block assets were created but not registered.");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (created > 0)
            Debug.Log($"[CreateDefaultBlocks] Done — created {created} new block asset(s).");
        else
            Debug.Log("[CreateDefaultBlocks] All default blocks already exist. Nothing to create.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void EnsureFolderExists(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string folder = Path.GetFileName(path);

        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolderExists(parent);

        AssetDatabase.CreateFolder(parent, folder);
    }
}
#endif
