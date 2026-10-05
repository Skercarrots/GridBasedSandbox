using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  DevMenuItemPalette — ScriptableObject database defining the blocks and
//  items accessible through the in-game Dev Menu's [ITEMS] panel.
//
//  Can be edited directly via the Unity Editor Window:
//  Tools > Dev Menu > Item Palette Editor
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class DevMenuItemEntry
{
    [Tooltip("Label displayed on the button in the in-game Dev Menu.")]
    public string displayName = "New Block";

    [Tooltip("Voxel block ID (matches VoxelBlockType.blockId in VoxelBlockRegistry, e.g. 1=Stone, 2=Dirt, 3=Grass).")]
    public byte voxelBlockId = 1;

    [Tooltip("True if this is an entity (like Robot) placed via SimpleObjectPlacer instead of terrain.")]
    public bool isEntity = false;

    [Tooltip("Optional reference to an existing ItemData asset. If assigned, this item data is granted directly.")]
    public ItemData itemData;

    [Tooltip("Category for tab filtering in the Dev Menu (e.g. Terrain, Flora, Ores, Entities).")]
    public string category = "Terrain";

    [Tooltip("Default quantity granted when clicked (e.g. 64 for blocks, 1 for entities).")]
    [Range(1, 64)] public int defaultAmount = 64;

    [Tooltip("Optional custom icon displayed on the hotbar.")]
    public Sprite customIcon;

    [Tooltip("Toggle to include or hide this item from the in-game Dev Menu.")]
    public bool isEnabled = true;
}

[CreateAssetMenu(fileName = "DevMenuItemPalette", menuName = "VoxelWorld/Dev Menu Item Palette")]
public class DevMenuItemPalette : ScriptableObject
{
    public const string ResourcesPath = "DevMenuItemPalette";

    [Header("Item Palette")]
    [Tooltip("List of items and blocks available in the in-game Dev Menu.")]
    public List<DevMenuItemEntry> items = new();

    /// <summary>
    /// Loads the palette asset from Resources/DevMenuItemPalette.
    /// </summary>
    public static DevMenuItemPalette LoadPalette()
    {
        return Resources.Load<DevMenuItemPalette>(ResourcesPath);
    }

    /// <summary>
    /// Returns all items marked as enabled.
    /// </summary>
    public List<DevMenuItemEntry> GetEnabledItems()
    {
        var result = new List<DevMenuItemEntry>();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null && items[i].isEnabled)
                result.Add(items[i]);
        }
        return result;
    }

    /// <summary>
    /// Returns distinct categories present among enabled items.
    /// </summary>
    public List<string> GetCategories()
    {
        var categories = new List<string> { "ALL" };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item == null || !item.isEnabled) continue;

            string cat = string.IsNullOrWhiteSpace(item.category) ? "General" : item.category.Trim();
            if (seen.Add(cat))
                categories.Add(cat.ToUpperInvariant());
        }

        return categories;
    }

    /// <summary>
    /// Returns enabled items matching the specified category (or all if category is "ALL").
    /// </summary>
    public List<DevMenuItemEntry> GetItemsByCategory(string category)
    {
        if (string.Equals(category, "ALL", StringComparison.OrdinalIgnoreCase))
            return GetEnabledItems();

        var result = new List<DevMenuItemEntry>();
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item == null || !item.isEnabled) continue;

            string cat = string.IsNullOrWhiteSpace(item.category) ? "General" : item.category.Trim();
            if (string.Equals(cat, category, StringComparison.OrdinalIgnoreCase))
                result.Add(item);
        }
        return result;
    }
}
