using System.Collections.Generic;
using UnityEngine;

public class InventorySaveService : MonoBehaviour, IItemGrantSource
{
    private const string SaveKey = "inventory_save";

    public int Priority => 0; // Highest priority — save data wins
    public bool IsAvailable() => PlayerPrefs.HasKey(SaveKey);

    public IEnumerable<ItemGrant> GetItemGrants()
    {
        string json = PlayerPrefs.GetString(SaveKey);
        var saveData = JsonUtility.FromJson<InventorySaveData>(json);
        return saveData?.grants ?? new List<ItemGrant>();
    }

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

    public void ClearSave() => PlayerPrefs.DeleteKey(SaveKey);

    [System.Serializable]
    private class InventorySaveData { public List<ItemGrant> grants; }
}