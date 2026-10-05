using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Professional code editing handler for TMP_InputField.
/// Implements:
/// - Tab & Shift+Tab handling (4 spaces indentation / dedentation)
/// - Enter key with smart Python auto-indentation (+4 spaces after ':')
/// - Quad-space Backspace dedent
/// - Real-time line numbers synchronization
/// - Cursor Line & Column tracking
/// - IDE keyboard shortcuts (F5, Ctrl+Enter, Ctrl+S, etc.)
/// </summary>
public class IDECodeEditor : MonoBehaviour
{
    [Header("Component References")]
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private TextMeshProUGUI lineNumbersText;
    [SerializeField] private RectTransform textViewport;

    // Events for controller
    public event Action OnRunShortcut;
    public event Action OnStopShortcut;
    public event Action OnSaveShortcut;
    public event Action OnNewShortcut;
    public event Action OnCloseTabShortcut;
    public event Action OnToggleConsoleShortcut;
    public event Action<int, int> OnCursorChanged; // (line, col) 1-based
    public event Action<string> OnContentChanged;

    private const string INDENT = "    "; // 4 spaces
    private readonly StringBuilder _lineNumSb = new(512);
    private int _lastLineCount = -1;
    private int _lastCaretPos = -1;

    public TMP_InputField InputField => inputField;

    public void BindReferences(TMP_InputField input, TextMeshProUGUI lineNumbers)
    {
        inputField = input ?? GetComponent<TMP_InputField>();
        lineNumbersText = lineNumbers;
        if (inputField != null)
        {
            var nav = inputField.navigation;
            nav.mode = UnityEngine.UI.Navigation.Mode.None;
            inputField.navigation = nav;
            inputField.lineType = TMP_InputField.LineType.MultiLineNewline;
            inputField.onValueChanged.RemoveListener(HandleTextChanged);
            inputField.onValueChanged.AddListener(HandleTextChanged);

            if (inputField.textComponent != null && lineNumbersText != null)
            {
                lineNumbersText.font = inputField.textComponent.font;
                lineNumbersText.fontSize = inputField.textComponent.fontSize;
                lineNumbersText.lineSpacing = inputField.textComponent.lineSpacing;
            }
        }
        UpdateLineNumbers();
    }

    private void Awake()
    {
        if (inputField == null)
            inputField = GetComponent<TMP_InputField>();

        if (inputField != null)
        {
            // Disable tab navigation so Tab key stays within the code editor
            var nav = inputField.navigation;
            nav.mode = UnityEngine.UI.Navigation.Mode.None;
            inputField.navigation = nav;

            inputField.lineType = TMP_InputField.LineType.MultiLineNewline;
            inputField.onValueChanged.RemoveListener(HandleTextChanged);
            inputField.onValueChanged.AddListener(HandleTextChanged);
        }
    }

    private void Start()
    {
        UpdateLineNumbers();
    }

    private void Update()
    {
        if (inputField == null || !inputField.isFocused) return;

        UpdateCursorInfo();
    }

    private void LateUpdate()
    {
        // Keep line numbers scrolled in sync with the input field's text component
        if (lineNumbersText != null && inputField != null && inputField.textComponent != null)
        {
            var textRt = inputField.textComponent.rectTransform;
            var lineRt = lineNumbersText.rectTransform;
            lineRt.anchoredPosition = new Vector2(lineRt.anchoredPosition.x, textRt.anchoredPosition.y);
        }
    }

    private void OnGUI()
    {
        if (inputField == null || !inputField.isFocused) return;
        Event e = Event.current;
        if (e.type != EventType.KeyDown) return;

        bool ctrl = e.control;
        bool shift = e.shift;

        // ── Keyboard Shortcuts ──
        if (e.keyCode == KeyCode.F5 || (ctrl && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)))
        {
            e.Use();
            OnRunShortcut?.Invoke();
            return;
        }

        if (e.keyCode == KeyCode.F6 || (shift && e.keyCode == KeyCode.F5))
        {
            e.Use();
            OnStopShortcut?.Invoke();
            return;
        }

        if (ctrl && e.keyCode == KeyCode.S)
        {
            e.Use();
            OnSaveShortcut?.Invoke();
            return;
        }

        if (ctrl && e.keyCode == KeyCode.N)
        {
            e.Use();
            OnNewShortcut?.Invoke();
            return;
        }

        if (ctrl && e.keyCode == KeyCode.W)
        {
            e.Use();
            OnCloseTabShortcut?.Invoke();
            return;
        }

        if (ctrl && (e.keyCode == KeyCode.BackQuote || e.keyCode == KeyCode.Quote))
        {
            e.Use();
            OnToggleConsoleShortcut?.Invoke();
            return;
        }

        // ── Code Editing Keys (Tab, Shift+Tab, Enter, Backspace) ──
        if (e.keyCode == KeyCode.Tab)
        {
            e.Use();
            if (shift)
                DedentCurrentLineOrSelection();
            else
                IndentCurrentLineOrSelection();
            return;
        }

        if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
        {
            if (!ctrl && !e.alt)
            {
                e.Use();
                HandlePythonEnterIndent();
                return;
            }
        }

        if (e.keyCode == KeyCode.Backspace)
        {
            if (CanSmartBackspace())
            {
                e.Use();
                HandleSmartBackspace();
                return;
            }
        }
    }

    private void IndentCurrentLineOrSelection()
    {
        string text = inputField.text ?? string.Empty;
        int caret = inputField.caretPosition;
        int selAnchor = inputField.selectionAnchorPosition;
        int selFocus = inputField.selectionFocusPosition;

        // If text is selected, indent all touched lines
        if (selAnchor != selFocus && Math.Abs(selAnchor - selFocus) > 0)
        {
            int start = Math.Min(selAnchor, selFocus);
            int end = Math.Max(selAnchor, selFocus);

            int lineStart = FindLineStart(text, start);
            int lineEnd = FindLineEnd(text, end);

            string block = text.Substring(lineStart, lineEnd - lineStart);
            string[] lines = block.Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                sb.Append(INDENT).Append(lines[i]);
                if (i < lines.Length - 1) sb.Append('\n');
            }

            string newBlock = sb.ToString();
            text = text.Substring(0, lineStart) + newBlock + text.Substring(lineEnd);
            inputField.text = text;
            inputField.selectionAnchorPosition = lineStart;
            inputField.selectionFocusPosition = lineStart + newBlock.Length;
            inputField.caretPosition = lineStart + newBlock.Length;
        }
        else
        {
            // Insert 4 spaces at caret position
            text = text.Insert(caret, INDENT);
            inputField.text = text;
            inputField.caretPosition = caret + INDENT.Length;
            inputField.selectionAnchorPosition = inputField.caretPosition;
            inputField.selectionFocusPosition = inputField.caretPosition;
        }

        inputField.ActivateInputField();
    }

    private void DedentCurrentLineOrSelection()
    {
        string text = inputField.text ?? string.Empty;
        int selAnchor = inputField.selectionAnchorPosition;
        int selFocus = inputField.selectionFocusPosition;

        if (selAnchor != selFocus && Math.Abs(selAnchor - selFocus) > 0)
        {
            int start = Math.Min(selAnchor, selFocus);
            int end = Math.Max(selAnchor, selFocus);

            int lineStart = FindLineStart(text, start);
            int lineEnd = FindLineEnd(text, end);

            string block = text.Substring(lineStart, lineEnd - lineStart);
            string[] lines = block.Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int spacesToRemove = 0;
                while (spacesToRemove < line.Length && line[spacesToRemove] == ' ' && spacesToRemove < 4)
                {
                    spacesToRemove++;
                }
                sb.Append(line.Substring(spacesToRemove));
                if (i < lines.Length - 1) sb.Append('\n');
            }

            string newBlock = sb.ToString();
            text = text.Substring(0, lineStart) + newBlock + text.Substring(lineEnd);
            inputField.text = text;
            inputField.selectionAnchorPosition = lineStart;
            inputField.selectionFocusPosition = lineStart + newBlock.Length;
            inputField.caretPosition = lineStart + newBlock.Length;
        }
        else
        {
            int caret = inputField.caretPosition;
            int lineStart = FindLineStart(text, caret);

            int spaces = 0;
            while (lineStart + spaces < text.Length && text[lineStart + spaces] == ' ' && spaces < 4)
            {
                spaces++;
            }

            if (spaces > 0)
            {
                text = text.Remove(lineStart, spaces);
                inputField.text = text;
                inputField.caretPosition = Math.Max(lineStart, caret - spaces);
                inputField.selectionAnchorPosition = inputField.caretPosition;
                inputField.selectionFocusPosition = inputField.caretPosition;
            }
        }

        inputField.ActivateInputField();
    }

    private void HandlePythonEnterIndent()
    {
        string text = inputField.text ?? string.Empty;
        int caret = inputField.caretPosition;

        // If selection exists, delete selection first
        int selAnchor = inputField.selectionAnchorPosition;
        int selFocus = inputField.selectionFocusPosition;
        if (selAnchor != selFocus)
        {
            int start = Math.Min(selAnchor, selFocus);
            int length = Math.Abs(selAnchor - selFocus);
            text = text.Remove(start, length);
            caret = start;
        }

        int lineStart = FindLineStart(text, caret);
        string currentLineUpToCaret = caret > lineStart ? text.Substring(lineStart, caret - lineStart) : string.Empty;

        // Measure existing indentation on current line
        int indentSpaces = 0;
        while (indentSpaces < currentLineUpToCaret.Length && currentLineUpToCaret[indentSpaces] == ' ')
        {
            indentSpaces++;
        }

        string indentStr = new string(' ', indentSpaces);

        // Python syntax rule: If line ends with ':' (ignoring comments like #...), indent an extra 4 spaces!
        string trimmed = currentLineUpToCaret.TrimEnd();
        int commentIdx = trimmed.IndexOf('#');
        if (commentIdx >= 0)
            trimmed = trimmed.Substring(0, commentIdx).TrimEnd();

        if (trimmed.EndsWith(":"))
        {
            indentStr += INDENT;
        }

        // Insert newline + computed indent
        string insertion = "\n" + indentStr;
        text = text.Insert(caret, insertion);
        inputField.text = text;
        inputField.caretPosition = caret + insertion.Length;
        inputField.selectionAnchorPosition = inputField.caretPosition;
        inputField.selectionFocusPosition = inputField.caretPosition;
        inputField.ActivateInputField();
    }

    private bool CanSmartBackspace()
    {
        if (inputField == null) return false;
        if (inputField.selectionAnchorPosition != inputField.selectionFocusPosition)
            return false;

        string text = inputField.text ?? string.Empty;
        int caret = inputField.caretPosition;
        if (caret < 4) return false;

        int lineStart = FindLineStart(text, caret);
        int offsetFromLineStart = caret - lineStart;

        if (offsetFromLineStart >= 4 && offsetFromLineStart % 4 == 0)
        {
            for (int i = lineStart; i < caret; i++)
            {
                if (text[i] != ' ') return false;
            }
            return true;
        }
        return false;
    }

    private void HandleSmartBackspace()
    {
        string text = inputField.text ?? string.Empty;
        int caret = inputField.caretPosition;
        text = text.Remove(caret - 4, 4);
        inputField.text = text;
        inputField.caretPosition = caret - 4;
        inputField.selectionAnchorPosition = inputField.caretPosition;
        inputField.selectionFocusPosition = inputField.caretPosition;
        inputField.ActivateInputField();
    }

    // ── Line Numbers & Cursor Tracking ────────────────────────────────────────

    private void HandleTextChanged(string newText)
    {
        UpdateLineNumbers();
        OnContentChanged?.Invoke(newText);
    }

    private void UpdateLineNumbers()
    {
        if (lineNumbersText == null || inputField == null) return;

        string text = inputField.text ?? string.Empty;
        int lineCount = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') lineCount++;
        }

        if (lineCount == _lastLineCount) return;
        _lastLineCount = lineCount;

        _lineNumSb.Clear();
        for (int i = 1; i <= lineCount; i++)
        {
            _lineNumSb.Append(i).Append('\n');
        }

        lineNumbersText.text = _lineNumSb.ToString();
    }

    private void UpdateCursorInfo()
    {
        if (inputField == null) return;
        int caret = inputField.caretPosition;
        if (caret == _lastCaretPos) return;
        _lastCaretPos = caret;

        string text = inputField.text ?? string.Empty;
        if (caret > text.Length) caret = text.Length;

        int line = 1;
        int col = 1;
        int lineStart = 0;

        for (int i = 0; i < caret; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }
        col = (caret - lineStart) + 1;

        OnCursorChanged?.Invoke(line, col);
    }

    // ── Text Helpers ──────────────────────────────────────────────────────────

    private static int FindLineStart(string text, int index)
    {
        if (string.IsNullOrEmpty(text) || index <= 0) return 0;
        if (index > text.Length) index = text.Length;

        int pos = text.LastIndexOf('\n', index - 1);
        return pos == -1 ? 0 : pos + 1;
    }

    private static int FindLineEnd(string text, int index)
    {
        if (string.IsNullOrEmpty(text) || index < 0) return 0;
        if (index >= text.Length) return text.Length;

        int pos = text.IndexOf('\n', index);
        return pos == -1 ? text.Length : pos;
    }

    public void SetText(string text)
    {
        if (inputField != null)
        {
            inputField.text = text ?? string.Empty;
            inputField.caretPosition = 0;
            UpdateLineNumbers();
        }
    }

    public string GetText() => inputField != null ? inputField.text : string.Empty;
}
