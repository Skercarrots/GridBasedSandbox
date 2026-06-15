using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "DefaultLoadout", menuName = "Inventory/Default Loadout Source")]
public class DefaultLoadoutSource : ScriptableObject, IItemGrantSource
{
    [SerializeField] private List<ItemGrant> defaultItems;

    public int Priority => 100; // Lowest priority — fallback only
    public bool IsAvailable() => defaultItems != null && defaultItems.Count > 0;
    public IEnumerable<ItemGrant> GetItemGrants() => defaultItems;
}