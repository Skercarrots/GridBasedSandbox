using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;

[System.Serializable]
public class InventorySlot
{
    public ItemStack itemStack;
    public int slotID;
}

public class InventoryManager : MonoBehaviour
{
    [SerializeField] private InventoryUI inventoryUI;
    [SerializeField] private int inventorySize = 10;
    private int currentSelectedSlot = 0;
    [SerializeField] private ItemData selectedItem;
    private List<InventorySlot> slots;
    
    /// <summary>
    /// Initializes the inventory slots and sets up the corresponding UI bar.
    /// </summary>
    public void InitializeInventoryBar()
    {
        slots = new List<InventorySlot>();

        for (int i = 0; i < inventorySize; i++)
        {
            slots.Add(new InventorySlot { slotID = i, itemStack = null });
            inventoryUI.CreateBarSlotUI(i, null);
        }

        SelectSlot(currentSelectedSlot);
        // Pass -1 as old slot since there's nothing to deselect yet
        inventoryUI.UpdateSelectionVisual(currentSelectedSlot, -1);

        Debug.Log($"Inventory initialized with {slots.Count} slots.");
    }

    /// <summary>
    /// Changes the active hotbar slot index, updating the selected item and UI highlight.
    /// </summary>
    /// <param name="value">The desired target slot index (will be clamped to valid range).</param>
    public void ChangeCurrentSelectedSlot(int value)
    {
        int oldSelectedSlotID = currentSelectedSlot;
        currentSelectedSlot = Mathf.Clamp(value, 0, slots.Count - 1);

        if (currentSelectedSlot == oldSelectedSlotID) return;

        SelectSlot(currentSelectedSlot);
        inventoryUI.UpdateSelectionVisual(currentSelectedSlot, oldSelectedSlotID);
    }

    // Handles data update only — UI highlight is handled by the caller
    private void SelectSlot(int slotIndex)
    {
        selectedItem = slots[slotIndex].itemStack?.itemData;
    }

    /// <summary>
    /// Attempts to add a given amount of an item to the inventory, either stacking into an existing slot or filling an empty slot.
    /// </summary>
    /// <param name="item">The item data to add.</param>
    /// <param name="amount">The quantity to add.</param>
    /// <returns><c>true</c> if the item was successfully added; <c>false</c> if the inventory is full.</returns>
    public bool TryAddItem(ItemData item, int amount)
    {
        int slotIndex = FindAvailableSlotOfType(item);
    
        if (slotIndex == -1)
        {
            Debug.Log("Inventory full!");
            return false;
        }

        InventorySlot targetSlot = slots[slotIndex];

        if (targetSlot.itemStack == null)
        {
            targetSlot.itemStack = new ItemStack(item, amount);
        }
        else
        {
            targetSlot.itemStack.amount += amount;
        }
    
        inventoryUI.UpdateSlotUI(slotIndex, targetSlot.itemStack);
        return true;
    }

    private int FindAvailableSlotOfType(ItemData item)
    {
        foreach (var slot in slots)
        {
            var stack = slot.itemStack;
            bool canStack = stack != null && 
                            stack.itemData == item && 
                            stack.amount < item.maxStackAmount;

            if (canStack) return slot.slotID;
        }

        foreach (var slot in slots)
        {
            if (slot.itemStack == null) return slot.slotID;
        }

        return -1; 
    }

    /// <summary>
    /// Returns the <see cref="ItemData"/> currently selected in the active hotbar slot, or <c>null</c> if empty.
    /// </summary>
    public ItemData GetSelectedItem()
    {
        return selectedItem;
    }

    /// <summary>
    /// Refreshes the currently selected item data reference for the active slot.
    /// </summary>
    public void RefreshSelectedSlot()
    {
        SelectSlot(currentSelectedSlot);
    }

    /// <summary>
    /// Returns the complete list of inventory slots.
    /// </summary>
    public List<InventorySlot> GetSlots() => slots;
    
}
