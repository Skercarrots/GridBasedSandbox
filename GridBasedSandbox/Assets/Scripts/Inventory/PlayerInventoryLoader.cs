using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PlayerInventoryLoader : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private InventoryManager inventoryManager;
    [SerializeField] private DefaultLoadoutSource defaultLoadout;

    [Header("Optional Sources (auto-discovered if null)")]
    [SerializeField] private InventorySaveService saveService;
    [SerializeField] private LevelGrantSource levelGrants;

    private void Awake()
    {
        // Inventory initializes here as before
        inventoryManager.InitializeInventoryBar();
    }

    private void Start()
    {
        // All granting happens here, after Awake — execution order is now explicit
        LoadInventory();
    }

    private void LoadInventory()
    {
        var sources = GatherSources();
        var chosen = PickSource(sources);

        if (chosen == null)
        {
            Debug.LogWarning("No item grant source available. Inventory will be empty.");
            return;
        }

        foreach (var grant in chosen.GetItemGrants())
            inventoryManager.TryAddItem(grant.item, grant.amount);

        inventoryManager.RefreshSelectedSlot();
        
        Debug.Log($"Inventory loaded from: {chosen.GetType().Name}");
    }

    private List<IItemGrantSource> GatherSources()
    {
        var sources = new List<IItemGrantSource>();

        // Auto-discover LevelGrantSource in scene if not assigned
        if (levelGrants == null)
            levelGrants = Object.FindAnyObjectByType<LevelGrantSource>();

        if (saveService != null) sources.Add(saveService);
        if (levelGrants != null) sources.Add(levelGrants);
        if (defaultLoadout != null) sources.Add(defaultLoadout);

        return sources;
    }

    // Pick the highest-priority available source
    // To MERGE sources instead (e.g. save data + level grants), change this method
    private IItemGrantSource PickSource(List<IItemGrantSource> sources)
    {
        return sources
            .Where(s => s.IsAvailable())
            .OrderBy(s => s.Priority)
            .FirstOrDefault();
    }

    public void SaveInventory()
    {
        saveService?.Save(inventoryManager.GetSlots());
    }
}