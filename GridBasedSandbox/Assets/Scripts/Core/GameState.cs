// GameState.cs
// Global flag store for cross-cutting state that multiple systems need to
// read without being wired to each other in the Inspector.
//
// Rule: only put things here that are genuinely cross-cutting — state that
// two or more unrelated systems both read AND that changes frequently enough
// that an event/delegate would be overkill. Everything specific to one
// system stays on that system's own component.
//
// Currently just IsIDEOpen, which is read by three separate systems
// (PlayerController, GameInputManager, potentially others) and written only
// by InGameIDEController. Using Inspector refs for this would mean those
// three systems all have a dep on InGameIDEController, which doesn't make
// sense architecturally.

public static class GameState
{
    /// <summary>
    /// True while the in-game IDE panel is open.
    /// Written exclusively by InGameIDEController.ToggleIDE().
    /// Read by:
    ///   • PlayerController  — suppresses movement and jump input
    ///   • GameInputManager  — suppresses block/entity placement and hotbar
    /// </summary>
    public static bool IsIDEOpen { get; set; } = false;

    /// <summary>
    /// True while the in-game Dev Menu is open.
    /// Written exclusively by DevMenuController.SetMenuOpen().
    /// Read by:
    ///   • PlayerController  — suppresses movement and jump input
    ///   • GameInputManager  — suppresses block/entity placement and hotbar
    /// </summary>
    public static bool IsDevMenuOpen { get; set; } = false;

    /// <summary>
    /// True if either the IDE or Dev Menu is currently open.
    /// </summary>
    public static bool IsAnyUIOpen => IsIDEOpen || IsDevMenuOpen;
}
