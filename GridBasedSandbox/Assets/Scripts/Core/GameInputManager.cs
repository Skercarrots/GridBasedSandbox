using UnityEngine;

/// <summary>
/// Handles gameplay input for inventory hotbar selection, block/entity placement, and world interaction.
/// </summary>
public class GameInputManager : MonoBehaviour
{
    [SerializeField] private GridSystem gridSystem;
    [SerializeField] private InventoryManager inventoryManager;
    [SerializeField] private SimpleObjectPlacer objectPlacer;
    [SerializeField] private VoxelBlockPlacer voxelBlockPlacer;

    void Update()
    {
        // Suppress gameplay input while the in-game IDE or Dev Menu is open
        if (GameState.IsAnyUIOpen) return;

        InventoryInput();
        PlaceObjectsInput();
    }

    private void InventoryInput()
    {
        for (int i = 1; i <= 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + i))
                inventoryManager.ChangeCurrentSelectedSlot(i - 1);
        }

        if (Input.GetKeyDown(KeyCode.Alpha0))
            inventoryManager.ChangeCurrentSelectedSlot(9);

        // Mouse scroll wheel support for cycling hotbar slots
        float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
        if (scroll > 0.01f)
        {
            int next = (inventoryManager.CurrentSelectedSlot - 1 + inventoryManager.SlotCount) % inventoryManager.SlotCount;
            inventoryManager.ChangeCurrentSelectedSlot(next);
        }
        else if (scroll < -0.01f)
        {
            int next = (inventoryManager.CurrentSelectedSlot + 1) % inventoryManager.SlotCount;
            inventoryManager.ChangeCurrentSelectedSlot(next);
        }
    }

    private void PlaceObjectsInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            voxelBlockPlacer.RemoveBlock();
        }
        else if (Input.GetMouseButtonDown(1))
        {
            if (TryInteract())
            {
                Debug.Log("Interacted with object under cursor.");
                return;
            }

            ItemData selected = inventoryManager.GetSelectedItem();
            if (selected == null)
            {
                // Place default block when empty-handed
                voxelBlockPlacer.PlaceBlock(0);
                return;
            }

            if (!selected.isPlaceable) return;

            if (selected.isEntity)
                objectPlacer.PlaceObjectInCell();
            else
                voxelBlockPlacer.PlaceBlock(selected.voxelBlockId);
        }
    }

    private bool TryInteract()
    {
        if (!HasEmptyHands()) return false;

        PlacedItem item = objectPlacer.GetPlacedItemUnderCursor();
        if (item == null) return false;

        IInteractable interactable = item.GetInteractable();
        if (interactable == null) return false;

        interactable.Interact();
        return true;
    }

    private bool HasEmptyHands() => inventoryManager.GetSelectedItem() == null;
}