using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour
{
    [SerializeField] private GameObject InventoryBarUI;
    [SerializeField] private GameObject slotUIPrefab;
    [SerializeField] private Transform slotsParent;

    [Header("Background Sprites")]
    [SerializeField] private Sprite defaultSlotSprite; // Sprite for the default slot background
    [SerializeField] private Sprite selectedSlotSprite; // Sprite for the selected slot background
    
    // List to hold the slot GameObjects displayed on screen
    private List<GameObject> slotUIObjects = new List<GameObject>();
    
    private void Awake()
    {
        InventoryBarUI.SetActive(true);
    }

    /// <summary>
    /// Instantiates and configures a UI slot game object on the inventory bar.
    /// </summary>
    /// <param name="slotID">The unique slot index.</param>
    /// <param name="itemStack">The initial item stack to display, or <c>null</c> if empty.</param>
    public void CreateBarSlotUI(int slotID, ItemStack itemStack)
    {
        if (slotUIObjects.Count >= 10)
        {
            Debug.LogWarning($"Cannot create more than 10 slots. SlotID {slotID} was not created.");
            return;
        }

        GameObject slotUIObj = Instantiate(slotUIPrefab, slotsParent);
        slotUIObj.name = $"Slot_{slotID}";
        
        if (slotUIObj.TryGetComponent<Image>(out Image slotBackground))
        {
            slotBackground.sprite = defaultSlotSprite;
        }

        slotUIObjects.Add(slotUIObj);

        UpdateSlotUI(slotID, itemStack);
    }

    /// <summary>
    /// Updates the icon and visibility of a specific slot on the inventory bar.
    /// </summary>
    /// <param name="slotID">The slot index to update.</param>
    /// <param name="itemStack">The current item stack to display, or <c>null</c> to clear the icon.</param>
    public void UpdateSlotUI(int slotID, ItemStack itemStack)
    {
        if (slotID < 0 || slotID >= slotUIObjects.Count) return;

        GameObject slotUIObj = slotUIObjects[slotID];
        Image iconImage = slotUIObj.transform.GetChild(0).GetComponent<Image>();

        if (itemStack == null || itemStack.itemData == null)
        {
            iconImage.sprite = null;
            iconImage.gameObject.SetActive(false);
        }
        else
        {
            iconImage.sprite = itemStack.itemData.icon;
            iconImage.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Updates the background sprite for both the newly selected slot and the deselected slot.
    /// </summary>
    /// <param name="newSelectedSlotID">The newly active slot index.</param>
    /// <param name="oldSelectedSlotID">The previously active slot index, or -1 if none.</param>
    public void UpdateSelectionVisual(int newSelectedSlotID, int oldSelectedSlotID)
    {
        if (oldSelectedSlotID >= 0 && oldSelectedSlotID < slotUIObjects.Count)
        {
            GameObject oldSlot = slotUIObjects[oldSelectedSlotID];
            if (oldSlot.TryGetComponent<Image>(out Image oldBackground))
            {
                oldBackground.sprite = defaultSlotSprite;
            }
        }

        if (newSelectedSlotID >= 0 && newSelectedSlotID < slotUIObjects.Count)
        {
            GameObject newSlot = slotUIObjects[newSelectedSlotID];
            if (newSlot.TryGetComponent<Image>(out Image newBackground))
            {
                newBackground.sprite = selectedSlotSprite;
            }
        }
    }

    /// <summary>
    /// Shows or hides the complete inventory bar UI.
    /// </summary>
    /// <param name="value"><c>true</c> to show; <c>false</c> to hide.</param>
    public void ToggleInventoryBar(bool value)
    {
        InventoryBarUI.SetActive(value);
    }
}