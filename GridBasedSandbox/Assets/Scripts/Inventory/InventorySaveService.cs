using System.Collections.Generic;
using UnityEngine;

public class InventorySaveService : MonoBehaviour, IItemGrantSource
{
    private const string SaveKey = "inventory_save";

    public int Priority => 0; // Highest priority — save data wins

    /// <summary>
    /// Checks whether saved inventory data exists in <see cref="PlayerPrefs"/>.
    /// </summary>
    public bool IsAvailable() => PlayerPrefs.HasKey(SaveKey);

    /// <summary>
    /// Deserializes and returns the list of saved item grants from <see cref="PlayerPrefs"/>.
    /// </summary>
    public IEnumerable<ItemGrant> GetItemGrants()
    {
        string json = PlayerPrefs.GetString(SaveKey);
        var saveData = JsonUtility.FromJson<InventorySaveData>(json);
        return saveData?.grants ?? new List<ItemGrant>();
    }

    /// <summary>
    /// Serializes the non-empty slots to JSON and saves them to <see cref="PlayerPrefs"/>.
    /// </summary>
    /// <param name="slots">The current inventory slots to persist.</param>
    public void Save(List<InventorySlot> slots)
    {
        var grants = new List<ItemGrant>();
        foreach (var slot in slots)
        {
            if (slot.itemStack != null)
                grants.Add(new ItemGrant { item = slot.itemStack.itemData, amount = slot.itemStack.amount });
        }

        string json = JsonUtility.ToJson(new InventorySaveData { grants = grants });
        PlayerPrefs.SetString(SaveKey, json);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Deletes the saved inventory data from <see cref="PlayerPrefs"/>.
    /// </summary>
    public void ClearSave() => PlayerPrefs.DeleteKey(SaveKey);

    [System.Serializable]
    private class InventorySaveData { public List<ItemGrant> grants; }
}