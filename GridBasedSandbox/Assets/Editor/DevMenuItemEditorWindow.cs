#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  DevMenuItemEditorWindow — Visual Unity Editor Window to manage and customize
//  the voxel blocks and items available in the in-game [F1] Dev Menu.
//
//  Open via:
//  • GridBasedSandbox > Dev Menu Item Palette
//  • Tools > Dev Menu > Item Palette Editor
// ─────────────────────────────────────────────────────────────────────────────

public class DevMenuItemEditorWindow : EditorWindow
{
    private DevMenuItemPalette _palette;
    private Vector2 _scrollPos;
    private string _searchFilter = "";
    private string _selectedCategory = "ALL";

    [MenuItem("GridBasedSandbox/Dev Menu Item Palette")]
    [MenuItem("Tools/Dev Menu/Item Palette Editor")]
    [MenuItem("Window/Dev Menu Item Palette")]
    public static void OpenWindow()
    {
        var window = GetWindow<DevMenuItemEditorWindow>("Dev Menu Palette");
        window.minSize = new Vector2(650, 500);
        window.Show();
    }

    private void OnEnable()
    {
        LoadOrCreatePalette();
    }

    private void LoadOrCreatePalette()
    {
        _palette = DevMenuItemPalette.LoadPalette();

        if (_palette == null)
        {
            string resourcesDir = Path.Combine(Application.dataPath, "Resources");
            if (!Directory.Exists(resourcesDir))
            {
                Directory.CreateDirectory(resourcesDir);
                AssetDatabase.Refresh();
            }

            string assetPath = "Assets/Resources/DevMenuItemPalette.asset";
            _palette = AssetDatabase.LoadAssetAtPath<DevMenuItemPalette>(assetPath);

            if (_palette == null)
            {
                _palette = CreateInstance<DevMenuItemPalette>();
                AssetDatabase.CreateAsset(_palette, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[DevMenuItemEditorWindow] Created new DevMenuItemPalette at: {assetPath}");
            }
        }

        // If palette is currently empty, auto-populate from project assets
        if (_palette != null && _palette.items.Count == 0)
        {
            SyncFromBlockRegistry(showLog: false);
            ScanProjectForItemData(showLog: false);
            SavePalette();
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        DrawHeaderToolbar();
        EditorGUILayout.Space(6);

        if (_palette == null)
        {
            EditorGUILayout.HelpBox("No DevMenuItemPalette asset loaded. Click 'Create Palette Asset' to begin.", MessageType.Warning);
            if (GUILayout.Button("Create Palette Asset", GUILayout.Height(30)))
            {
                LoadOrCreatePalette();
            }
            return;
        }

        DrawSearchAndCategoryToolbar();
        EditorGUILayout.Space(4);

        DrawItemsList();
    }

    private void DrawHeaderToolbar()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.LabelField("<b>DEV MENU ITEM PALETTE</b>", new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, richText = true });

        // Palette Asset Field
        GUI.color = Color.white;
        _palette = (DevMenuItemPalette)EditorGUILayout.ObjectField(_palette, typeof(DevMenuItemPalette), false, GUILayout.Width(220));

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        // Action Buttons Row
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.2f, 0.7f, 0.3f, 1f);
        if (GUILayout.Button("Sync from Block Registry", GUILayout.Height(26)))
        {
            SyncFromBlockRegistry(showLog: true);
        }

        GUI.backgroundColor = new Color(0.2f, 0.5f, 0.85f, 1f);
        if (GUILayout.Button("Scan Project for ItemData", GUILayout.Height(26)))
        {
            ScanProjectForItemData(showLog: true);
        }

        GUI.backgroundColor = new Color(0.9f, 0.6f, 0.15f, 1f);
        if (GUILayout.Button("+ Add Custom Item", GUILayout.Height(26)))
        {
            AddNewItem();
        }

        GUI.backgroundColor = Color.white;
        if (GUILayout.Button("Save Palette", GUILayout.Height(26)))
        {
            SavePalette();
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawSearchAndCategoryToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        // Search Bar
        EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
        _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(200));

        if (!string.IsNullOrEmpty(_searchFilter) && GUILayout.Button("X", EditorStyles.toolbarButton, GUILayout.Width(22)))
        {
            _searchFilter = "";
            GUI.FocusControl(null);
        }

        GUILayout.FlexibleSpace();

        // Category Filter
        EditorGUILayout.LabelField("Category:", GUILayout.Width(60));
        var categories = new string[] { "ALL", "Terrain", "Flora", "Ores", "Entities", "Custom" };
        for (int i = 0; i < categories.Length; i++)
        {
            bool isSelected = string.Equals(_selectedCategory, categories[i], StringComparison.OrdinalIgnoreCase);
            if (GUILayout.Toggle(isSelected, categories[i], EditorStyles.toolbarButton))
            {
                _selectedCategory = categories[i];
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawItemsList()
    {
        int totalItems = _palette.items.Count;
        int activeCount = _palette.GetEnabledItems().Count;

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Palette Entries: <b>{totalItems}</b>  |  Active in Dev Menu: <color=#00AA00><b>{activeCount}</b></color>",
            new GUIStyle(EditorStyles.label) { richText = true });
        EditorGUILayout.EndHorizontal();

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        int drawnCount = 0;
        int itemToRemove = -1;
        int itemToMoveUp = -1;
        int itemToMoveDown = -1;

        for (int i = 0; i < _palette.items.Count; i++)
        {
            var item = _palette.items[i];
            if (item == null) continue;

            // Apply category filter
            if (!string.Equals(_selectedCategory, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(item.category, _selectedCategory, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            // Apply search filter
            if (!string.IsNullOrEmpty(_searchFilter))
            {
                bool matchesName = item.displayName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                bool matchesCat = item.category.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                bool matchesId = item.voxelBlockId.ToString().Equals(_searchFilter, StringComparison.OrdinalIgnoreCase);

                if (!matchesName && !matchesCat && !matchesId)
                    continue;
            }

            drawnCount++;
            DrawItemEntryCard(i, item, ref itemToRemove, ref itemToMoveUp, ref itemToMoveDown);
            EditorGUILayout.Space(3);
        }

        if (drawnCount == 0)
        {
            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox("No items matched the current filter. Click 'Sync from Block Registry' or '+ Add Custom Item' above.", MessageType.Info);
        }

        EditorGUILayout.EndScrollView();

        // Handle item list modifications
        if (itemToRemove >= 0 && itemToRemove < _palette.items.Count)
        {
            Undo.RecordObject(_palette, "Remove Palette Item");
            _palette.items.RemoveAt(itemToRemove);
            SavePalette();
        }

        if (itemToMoveUp > 0 && itemToMoveUp < _palette.items.Count)
        {
            Undo.RecordObject(_palette, "Move Palette Item Up");
            var temp = _palette.items[itemToMoveUp];
            _palette.items[itemToMoveUp] = _palette.items[itemToMoveUp - 1];
            _palette.items[itemToMoveUp - 1] = temp;
            SavePalette();
        }

        if (itemToMoveDown >= 0 && itemToMoveDown < _palette.items.Count - 1)
        {
            Undo.RecordObject(_palette, "Move Palette Item Down");
            var temp = _palette.items[itemToMoveDown];
            _palette.items[itemToMoveDown] = _palette.items[itemToMoveDown + 1];
            _palette.items[itemToMoveDown + 1] = temp;
            SavePalette();
        }
    }

    private void DrawItemEntryCard(int index, DevMenuItemEntry item, ref int itemToRemove, ref int itemToMoveUp, ref int itemToMoveDown)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // Header Row: Toggle, Display Name, Actions
        EditorGUILayout.BeginHorizontal();

        bool newEnabled = EditorGUILayout.Toggle(item.isEnabled, GUILayout.Width(22));
        if (newEnabled != item.isEnabled)
        {
            Undo.RecordObject(_palette, "Toggle Item Enabled");
            item.isEnabled = newEnabled;
        }

        GUI.color = item.isEnabled ? Color.white : new Color(0.7f, 0.7f, 0.7f, 0.7f);
        string newName = EditorGUILayout.TextField(item.displayName, EditorStyles.boldLabel, GUILayout.Width(170));
        if (newName != item.displayName)
        {
            Undo.RecordObject(_palette, "Edit Item Name");
            item.displayName = newName;
        }
        GUI.color = Color.white;

        // Category Tag
        EditorGUILayout.LabelField("Cat:", GUILayout.Width(30));
        string newCat = EditorGUILayout.TextField(item.category, GUILayout.Width(90));
        if (newCat != item.category)
        {
            Undo.RecordObject(_palette, "Edit Item Category");
            item.category = newCat;
        }

        GUILayout.FlexibleSpace();

        // Move Up / Down Buttons
        GUI.enabled = index > 0;
        if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(24)))
            itemToMoveUp = index;

        GUI.enabled = index < _palette.items.Count - 1;
        if (GUILayout.Button("▼", EditorStyles.miniButtonRight, GUILayout.Width(24)))
            itemToMoveDown = index;
        GUI.enabled = true;

        // Delete Button
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f, 1f);
        if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24)))
            itemToRemove = index;
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndHorizontal();

        // Details Row: Voxel ID, IsEntity, ItemData ref, Default Amount
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.LabelField("Block ID:", GUILayout.Width(55));
        int newId = EditorGUILayout.IntField(item.voxelBlockId, GUILayout.Width(45));
        if (newId != item.voxelBlockId)
        {
            Undo.RecordObject(_palette, "Edit Voxel Block ID");
            item.voxelBlockId = (byte)Mathf.Clamp(newId, 0, 255);
        }

        EditorGUILayout.Space(8);
        bool newIsEntity = EditorGUILayout.ToggleLeft("Entity?", item.isEntity, GUILayout.Width(65));
        if (newIsEntity != item.isEntity)
        {
            Undo.RecordObject(_palette, "Toggle IsEntity");
            item.isEntity = newIsEntity;
        }

        EditorGUILayout.LabelField("ItemData:", GUILayout.Width(60));
        var newItemData = (ItemData)EditorGUILayout.ObjectField(item.itemData, typeof(ItemData), false, GUILayout.Width(150));
        if (newItemData != item.itemData)
        {
            Undo.RecordObject(_palette, "Assign ItemData Asset");
            item.itemData = newItemData;
            if (item.itemData != null && string.IsNullOrEmpty(item.displayName))
                item.displayName = item.itemData.itemName;
        }

        EditorGUILayout.LabelField("Grant Qty:", GUILayout.Width(65));
        int newAmt = EditorGUILayout.IntField(item.defaultAmount, GUILayout.Width(40));
        if (newAmt != item.defaultAmount)
        {
            Undo.RecordObject(_palette, "Edit Default Amount");
            item.defaultAmount = Mathf.Clamp(newAmt, 1, 64);
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Sync & Discovery Helpers
    // ─────────────────────────────────────────────────────────────────────────

    public void SyncFromBlockRegistry(bool showLog = true)
    {
        if (_palette == null) return;

        // Locate VoxelBlockRegistry asset
        var registries = FindAssetsByType<VoxelBlockRegistry>();
        if (registries.Count == 0)
        {
            if (showLog) EditorUtility.DisplayDialog("Registry Not Found", "No VoxelBlockRegistry asset was found in the project.", "OK");
            return;
        }

        var registry = registries[0];
        Undo.RecordObject(_palette, "Sync Block Registry");

        int addedCount = 0;
        int updatedCount = 0;

        foreach (var block in registry.RegisteredBlocks)
        {
            if (block == null || block.blockId == 0) continue; // Skip Air

            // Check if entry already exists with this blockId
            var existing = _palette.items.Find(it => !it.isEntity && it.voxelBlockId == block.blockId);
            if (existing != null)
            {
                existing.displayName = block.blockName;
                existing.category = CategorizeBlock(block.blockId, block.blockName);
                updatedCount++;
            }
            else
            {
                _palette.items.Add(new DevMenuItemEntry
                {
                    displayName = block.blockName,
                    voxelBlockId = block.blockId,
                    isEntity = false,
                    category = CategorizeBlock(block.blockId, block.blockName),
                    defaultAmount = 64,
                    isEnabled = true
                });
                addedCount++;
            }
        }

        SavePalette();
        if (showLog)
        {
            EditorUtility.DisplayDialog("Sync Complete",
                $"Successfully synced from '{registry.name}'!\n• Added {addedCount} new blocks\n• Updated {updatedCount} existing blocks", "OK");
        }
    }

    public void ScanProjectForItemData(bool showLog = true)
    {
        if (_palette == null) return;

        var items = FindAssetsByType<ItemData>();
        Undo.RecordObject(_palette, "Scan ItemData Assets");

        int addedCount = 0;
        foreach (var it in items)
        {
            if (it == null) continue;

            // Check if already in palette
            bool exists = _palette.items.Exists(entry => entry.itemData == it ||
                (it.isEntity && entry.isEntity && string.Equals(entry.displayName, it.itemName, StringComparison.OrdinalIgnoreCase)));

            if (!exists)
            {
                _palette.items.Add(new DevMenuItemEntry
                {
                    displayName = it.itemName,
                    voxelBlockId = it.voxelBlockId,
                    isEntity = it.isEntity,
                    itemData = it,
                    category = it.isEntity ? "Entities" : "Items",
                    defaultAmount = it.isEntity ? 1 : Mathf.Min(64, it.maxStackAmount),
                    isEnabled = true
                });
                addedCount++;
            }
        }

        SavePalette();
        if (showLog)
        {
            EditorUtility.DisplayDialog("Scan Complete",
                $"Found {items.Count} ItemData assets in project.\nAdded {addedCount} new items to palette.", "OK");
        }
    }

    private void AddNewItem()
    {
        Undo.RecordObject(_palette, "Add Dev Menu Item");
        _palette.items.Add(new DevMenuItemEntry
        {
            displayName = "New Block",
            voxelBlockId = 1,
            category = "Custom",
            defaultAmount = 64,
            isEnabled = true
        });
        SavePalette();
    }

    private void SavePalette()
    {
        if (_palette == null) return;
        EditorUtility.SetDirty(_palette);
        AssetDatabase.SaveAssets();
    }

    private static string CategorizeBlock(byte id, string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("ore") || n.Contains("coal") || n.Contains("iron") || n.Contains("copper") || n.Contains("gold"))
            return "Ores";
        if (n.Contains("log") || n.Contains("wood") || n.Contains("leaves") || n.Contains("sapling"))
            return "Flora";
        if (n.Contains("water") || n.Contains("lava"))
            return "Fluids";
        return "Terrain";
    }

    private static List<T> FindAssetsByType<T>() where T : UnityEngine.Object
    {
        var list = new List<T>();
        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) list.Add(asset);
        }
        return list;
    }
}
#endif
