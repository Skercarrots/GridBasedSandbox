using UnityEngine;

// RobotInteraction.cs
// Lets the player right-click a robot with empty hands to open its code
// editor — the exact same IInteractable pattern Button.cs uses, just wired
// to InGameIDEController instead of a DebugLabel flash.
//
// SETUP
// Add this to the same GameObject as RobotController + ScriptRunner (your
// robot prefab). Also make sure that GameObject (and its colliders) sit on
// whatever layer GameInputManager's interaction raycast checks — the same
// layer PlacedItem-based decorative objects use. If the robot was spawned
// via SimpleObjectPlacer.PlaceObjectInCell() (the "spawn egg" item flow)
// this is already handled for you.
[RequireComponent(typeof(ScriptRunner))]
public class RobotInteraction : MonoBehaviour, IInteractable
{
    [Tooltip("Drag the scene's InGameIDEController here. If left empty, the first one found in the scene is used — fine as long as there's only one IDE window.")]
    [SerializeField] private InGameIDEController ideController;

    private ScriptRunner _runner;

    private void Awake()
    {
        _runner = GetComponent<ScriptRunner>();

        if (ideController == null)
            ideController = FindAnyObjectByType<InGameIDEController>();
    }

    public string GetInteractionLabel() => "Program Robot";

    public void Interact()
    {
        if (ideController == null)
        {
            Debug.LogWarning("[RobotInteraction] No InGameIDEController found in scene — can't open the editor.");
            return;
        }

        ideController.OpenFor(_runner);
    }
}
