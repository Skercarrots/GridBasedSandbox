// InGameIDEController.cs
// Controlador da interface de programação in-game.
// Gerencia a abertura da janela, leitura do código do jogador e limpeza de bugs de formatação.
//
// CHANGED:
//   • ToggleIDE() now syncs GameState.IsIDEOpen and re-locks the cursor when
//     the panel closes, so the player snaps back into first-person control
//     without having to click.
//   • Update() now guards the backtick/quote toggle against the input field
//     being focused — previously typing a ' in your code would close the IDE.
//   • Update() handles Escape to close the IDE while it's open, since
//     PlayerController.HandleCursorToggle() is suppressed during IDE use.
//   • Start() resets GameState.IsIDEOpen = false so a scene reload can never
//     inherit a stale "open" state from a previous session.

using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class InGameIDEController : MonoBehaviour
{
    [Header("Referências Core")]
    [Tooltip("Arraste o componente ScriptRunner que executará o código")]
    public ScriptRunner runner; 

    [Header("Elementos da Interface")]
    [Tooltip("O painel principal (fundo) da sua IDE")]
    public GameObject idePanel; 
    
    [Tooltip("O campo de texto grande onde o jogador digita o Python")]
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

        // ── CHANGED: reset in case of scene reload ──────────────────────────
        GameState.IsIDEOpen = false;
    }

    private void Update()
    {
        // ── CHANGED: don't close the IDE if the player is typing in it ──────
        // Previously `'` in code would immediately close the panel.
        bool codeFieldFocused = codeInputField != null && codeInputField.isFocused;

        if (!codeFieldFocused && (Input.GetKeyDown(KeyCode.Quote) || Input.GetKeyDown(KeyCode.BackQuote)))
            ToggleIDE();

        // ── CHANGED: Escape closes the IDE ──────────────────────────────────
        // PlayerController.HandleCursorToggle() is suppressed while the IDE is
        // open (see GameState.IsIDEOpen guard in PlayerController.Update()), so
        // we take over Escape here to give it a natural "close the menu" feel.
        if (GameState.IsIDEOpen && Input.GetKeyDown(KeyCode.Escape))
            ToggleIDE();
    }

    public void RunCode()
    {
        if (runner != null && codeInputField != null)
        {
            runner.RunScript(codeInputField.text);
            Debug.Log("Código enviado para o ScriptRunner!");
        }
        else
        {
            Debug.LogError("Erro: Faltam referências do ScriptRunner ou do InputField no Inspector.");
        }
    }

    public void StopCode()
    {
        if (runner != null)
        {
            runner.StopScript();
            Debug.Log("O script foi parado pelo jogador.");
        }
    }

    // Opens the IDE already pointing at a specific ScriptRunner.
    // Called by RobotInteraction — clicking different robots opens the
    // "right" editor for that robot rather than staying on whatever runner
    // was wired in the Inspector.
    public void OpenFor(ScriptRunner targetRunner)
    {
        runner = targetRunner;

        if (idePanel != null && !idePanel.activeSelf)
            ToggleIDE();
        // If already open (player clicked a second robot while IDE was open),
        // we just swapped runners above — no second toggle needed.
        // GameState.IsIDEOpen stays true. ✓
    }

    // ── CHANGED: syncs GameState and cursor on both open and close ──────────
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
            // Closing: re-lock the cursor so the player is back in first-person
            // immediately. Without this they'd need to click to re-lock, which
            // was jarring and easy to accidentally place a block at.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }
    }

    // ── Sanitização de Texto ──────────────────────────────────────────────────

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