using System.Collections.Generic;

public interface IItemGrantSource
{
    // Lower number = higher priority (save data should be 0, defaults last)
    int Priority { get; }
    bool IsAvailable();
    IEnumerable<ItemGrant> GetItemGrants();
}

[System.Serializable]
public struct ItemGrant
{
    public ItemData item;
    public int amount;
}