using UnityEngine;

/// <summary>
/// Definition ScriptableObject for an item type, defining display data, stack limits, and placement behavior.
/// </summary>
[CreateAssetMenu(fileName = "ItemData", menuName = "Inventory/ItemData")]
public class ItemData : ScriptableObject
{
    public string id; // Unique ID for saving/loading
    public string itemName;

    public Sprite icon;

    public int maxStackAmount = 64;
    public GameObject itemPrefab; // 3D prefab to construct or spawn
    public bool isPlaceable;
    public bool isUsable;

    [Tooltip("If true (and isPlaceable is also true), placing this item spawns a live entity — like a Minecraft spawn egg — instead of a static decorative object. itemPrefab should be the entity's full prefab (RobotController, colliders, ScriptRunner, etc. already set up). GameInputManager checks this flag to decide whether a placement click goes to SimpleObjectPlacer (entity) or VoxelBlockPlacer (block).")]
    public bool isEntity;

    [Tooltip("Voxel block id placed into the world when this item is used and isEntity is false — must match a VoxelBlockType.blockId registered in your VoxelBlockRegistry. Leave at 0 to fall back to VoxelBlockPlacer's own defaultBlockId. Ignored entirely for entity items.")]
    public byte voxelBlockId;
}

/// <summary>
/// Represents a concrete quantity of an item within an inventory slot.
/// </summary>
[System.Serializable]
public class ItemStack
{
    public ItemData itemData;
    public int amount;

    public ItemStack(ItemData data, int initialAmount)
    {
        itemData = data;
        amount = initialAmount;
    }
}