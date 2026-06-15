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

        Debug.Log($"Inventário inicializado com {slots.Count} slots.");
    }

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

        //Debug.Log($"Slot {slotIndex} selecionado. Item: {(selectedItem != null ? selectedItem.name : "Vazio")}");
    }

    public bool TryAddItem(ItemData item, int amount)
    {
        int slotIndex = FindAvailableSlotOfType(item);
    
        if (slotIndex == -1)
        {
            Debug.Log("Inventário cheio!");
            return false;
        }

        InventorySlot targetSlot = slots[slotIndex];

        if (targetSlot.itemStack == null)
        {
            // Cria um novo stack no slot vazio
            targetSlot.itemStack = new ItemStack(item, amount);
        }
        else
        {
            // Aumenta a quantidade do item existente
            targetSlot.itemStack.amount += amount;
        }
    
        // NOVO: Avisa a UI para atualizar o desenho do slot na tela
        inventoryUI.UpdateSlotUI(slotIndex, targetSlot.itemStack);

        //Debug.Log($"Item {item.name} adicionado ao slot {slotIndex}. Quantidade atual: {targetSlot.itemStack.amount}");
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

    public ItemData GetSelectedItem()
    {
        return selectedItem;
    }

    public void RefreshSelectedSlot()
    {
        SelectSlot(currentSelectedSlot);
    }

    public List<InventorySlot> GetSlots() => slots;
    
}
