using System.Collections.Generic;
using UnityEngine;

public class LevelGrantSource : MonoBehaviour, IItemGrantSource
{
    [SerializeField] private List<ItemGrant> levelStartItems;

    public int Priority => 50;
    public bool IsAvailable() => levelStartItems != null && levelStartItems.Count > 0;
    public IEnumerable<ItemGrant> GetItemGrants() => levelStartItems;
}