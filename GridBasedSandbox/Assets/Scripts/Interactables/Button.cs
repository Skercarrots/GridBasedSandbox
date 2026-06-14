using UnityEngine;

public class Button : MonoBehaviour, IInteractable
{
    private DebugLabel debugLabel;

    private void Start()
    {
        debugLabel = GetComponent<DebugLabel>() ?? gameObject.AddComponent<DebugLabel>();
    }

    public string GetInteractionLabel()
    {
        return "Press Button";
    }

    public void Interact()
    {
        debugLabel.Flash("Boop!");
    }
}
