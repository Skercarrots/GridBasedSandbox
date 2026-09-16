/// <summary>
/// In-game programming interface controller.
/// Manages window toggling, reading player code, running/stopping scripts, and formatting sanitization.
/// </summary>
public class InGameIDEController : MonoBehaviour
{
    [Header("Core References")]
    [Tooltip("ScriptRunner component that will execute the code")]
    public ScriptRunner runner; 

    [Header("Interface Elements")]
    [Tooltip("Main background panel of the IDE")]
    public GameObject idePanel; 
    
    [Tooltip("Text input field where the player enters Python code")]
    public TMP_InputField codeInputField; 
    
    public UnityEngine.UI.Button runButton;
    public UnityEngine.UI.Button stopButton;
    public UnityEngine.UI.Button closeButton;

    private void Start()
    {
        if (runButton  != null) runButton.onClick.AddListener(RunCode);
        if (stopButton != null) stopButton.onClick.AddListener(StopCode);
        if (closeButton != null) closeButton.onClick.AddListener(ToggleIDE);

        if (codeInputField != null)
        {
            codeInputField.lineType = TMP_InputField.LineType.MultiLineNewline;
            codeInputField.onValueChanged.AddListener(SanitizeText);
        }

        if (idePanel != null)
            idePanel.SetActive(false);

        // Reset state on start / scene reload
        GameState.IsIDEOpen = false;
    }

    private void Update()
    {
        // Don't toggle IDE if the player is currently typing in the input field
        bool codeFieldFocused = codeInputField != null && codeInputField.isFocused;

        if (!codeFieldFocused && (Input.GetKeyDown(KeyCode.Quote) || Input.GetKeyDown(KeyCode.BackQuote)))
            ToggleIDE();

        // Escape closes the IDE while open
        if (GameState.IsIDEOpen && Input.GetKeyDown(KeyCode.Escape))
            ToggleIDE();
    }

    /// <summary>
    /// Sends the current text from the code editor to the assigned <see cref="ScriptRunner"/> for execution.
    /// </summary>
    public void RunCode()
    {
        if (runner != null && codeInputField != null)
        {
            runner.RunScript(codeInputField.text);
            Debug.Log("Code sent to ScriptRunner!");
        }
        else
        {
            Debug.LogError("Error: Missing ScriptRunner or InputField reference in Inspector.");
        }
    }

    /// <summary>
    /// Halts execution of the currently running script on the assigned <see cref="ScriptRunner"/>.
    /// </summary>
    public void StopCode()
    {
        if (runner != null)
        {
            runner.StopScript();
            Debug.Log("Script stopped by player.");
        }
    }

    /// <summary>
    /// Opens the IDE panel and targets a specific <see cref="ScriptRunner"/> instance (e.g. from an interacted robot).
    /// </summary>
    /// <param name="targetRunner">The ScriptRunner component of the device being edited.</param>
    public void OpenFor(ScriptRunner targetRunner)
    {
        runner = targetRunner;

        if (idePanel != null && !idePanel.activeSelf)
            ToggleIDE();
    }

    /// <summary>
    /// Toggles the visibility of the IDE panel, syncing <see cref="GameState.IsIDEOpen"/> and locking/unlocking the mouse cursor.
    /// </summary>
    public void ToggleIDE()
    {
        if (idePanel == null) return;

        bool willBeActive = !idePanel.activeSelf;

        idePanel.SetActive(willBeActive);
        GameState.IsIDEOpen = willBeActive;

        if (willBeActive)
        {
            // Opening: show the cursor so the player can type and click buttons.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }
        else
        {
            // Closing: re-lock the cursor so the player is back in first-person immediately.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }
    }

    // ── Text Sanitization ──────────────────────────────────────────────────

    /// <summary>
    /// Strips carriage returns (\r) from text input to prevent line-ending corruption when executing Python scripts.
    /// </summary>
    /// <param name="currentText">The raw text from the input field.</param>
    public void SanitizeText(string currentText)
    {
        if (currentText.Contains("\r"))
        {
            int caretPos = codeInputField.caretPosition;

            string cleanedText = currentText.Replace("\r", "");

            codeInputField.onValueChanged.RemoveListener(SanitizeText);
            codeInputField.text = cleanedText;
            codeInputField.caretPosition = caretPos;
            codeInputField.onValueChanged.AddListener(SanitizeText);
        }
    }
}