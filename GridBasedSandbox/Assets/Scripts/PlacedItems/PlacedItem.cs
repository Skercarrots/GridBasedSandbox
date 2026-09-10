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
        // No longer an error: PlacedItem now doubles as the generic "this is
        // an interactable/placed world object" marker — hand-placed things
        // like Button or a pre-placed robot legitimately have no ItemData,
        // since nothing was ever taken out of an inventory to create them.
        // Only objects spawned via SimpleObjectPlacer.PlaceObjectInCell()
        // (decorative items or "spawn egg" entities) get one via SetItemData().
        if (itemData == null)
            Debug.Log($"PlacedItem on '{gameObject.name}': no ItemData assigned (expected for hand-placed scene objects).");
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