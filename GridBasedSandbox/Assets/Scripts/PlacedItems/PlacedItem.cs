using UnityEngine;

public class PlacedItem : MonoBehaviour
{
    [SerializeField] private ItemData itemData;
    public bool IsInteractable => GetComponent<IInteractable>() != null;
    

    private DebugLabel debugLabel;

    private void Awake()
    {
        debugLabel = GetComponent<DebugLabel>() ?? gameObject.AddComponent<DebugLabel>();
    }

    private void Start()
    {
        if (itemData == null)
        {
            Debug.LogError("PlacedItem: This Placed item has no ItemData reference assigned, assign one when placing an item.");
        }
    }

    public void SetItemData(ItemData data)
    {
        itemData = data;
    }

    private void PerformSimpleDebug()
    {
        debugLabel.Flash($"Item: {itemData.itemName}");
    }

    public IInteractable GetInteractable() => GetComponent<IInteractable>();
}
