using UnityEngine;

public class GameInpurManager : MonoBehaviour
{
    [SerializeField] private GridSystem gridSystem;
    [SerializeField] private InventoryManager inventoryManager;
    [SerializeField] private SimpleObjectPlacer objectPlacer;

    void Update()
    {
        InventoryInput();

        PlaceObjectsInput();
    }

    private void InventoryInput()
    {
        for (int i = 1; i <= 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + i))
            {
                inventoryManager.ChangeCurrentSelectedSlot(i - 1);
            }
        }

        if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            inventoryManager.ChangeCurrentSelectedSlot(9);
        }
    }

    private void PlaceObjectsInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            
            objectPlacer.RemoveObjectFromCell();
        }
        else if (Input.GetMouseButtonDown(1))
        {
            // Try interaction first, fall back to removal
            if (TryInteract())
            {
                Debug.Log("Interacted with object under cursor.");
                return;
            }
            // Only place if hands are NOT empty — check inventory
            if (inventoryManager.GetSelectedItem() != null)
                objectPlacer.PlaceObjectInCell();
                
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
