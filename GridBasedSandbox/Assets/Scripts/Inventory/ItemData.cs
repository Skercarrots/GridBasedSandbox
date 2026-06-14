using UnityEngine;

[CreateAssetMenu(fileName = "ItemData", menuName = "Inventory/ItemData")]
public class ItemData : ScriptableObject
{
    public string id; // ID único para salvar/carregar
    public string itemName;

    public Sprite icon;

    public int maxStackAmount = 64; // Quantidade máxima por stack
    public GameObject itemPrefab; // O modelo 3D para construir
    public bool isPlaceable;
    public bool isUsable;
}

[System.Serializable]
public class ItemStack
{
    public ItemData itemData;
    public int amount;

    public ItemStack(ItemData data, int initialAmount)
    {
        itemData = data;
        amount = initialAmount;
    }
}