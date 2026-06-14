using UnityEngine;

public class StartItems : MonoBehaviour
{
    [SerializeField] private InventoryManager inventoryManager;

    [SerializeField] private ItemData[] startingItems;

    private void Start()
    {
        foreach (var item in startingItems)
        {
            inventoryManager.TryAddItem(item, item.maxStackAmount);
            
        }
    }
}
