using UnityEngine;

public interface IInteractable
{
    /// <summary>
    /// Returns the user-facing text prompt displayed when hovering this interactable (e.g. "Open Chest", "Program Robot").
    /// </summary>
    string GetInteractionLabel();

    /// <summary>
    /// Executes the primary interaction behavior for this object.
    /// </summary>
    void Interact();
}

/*
How specific behaviours implement it independently:

//ChestInteraction.cs
public class ChestInteraction : MonoBehaviour, IInteractable
{
    public string GetInteractionLabel() => "Open Chest";
    public void Interact() { // open chest UI // }
}

// FurnaceInteraction.cs
public class FurnaceInteraction : MonoBehaviour, IInteractable
{
    public string GetInteractionLabel() => "Use Furnace";
    public void Interact() { // open smelting UI // }
}
*/


/*
Distinguishing Interactable vs Non-Interactable items

// PlacedItem.cs — add this
public bool IsInteractable => GetComponent<IInteractable>() != null;
public IInteractable GetInteractable() => GetComponent<IInteractable>();

The Minecraft way — the chest prefab already has ChestInteraction on it.
No runtime AddComponent juggling. The itemPrefab in ItemData simply has
the right components baked in.

*/