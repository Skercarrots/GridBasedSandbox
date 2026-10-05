using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UIButton = UnityEngine.UI.Button;

/// <summary>
/// Minimalist In-Game Python IDE Controller.
/// Inspired by "The Farmer Was Replaced":
/// • Distraction-free, clean dark slate design with warm amber RUN button.
/// • Single compact header containing RUN, STOP, script tabs (+), and controls.
/// • Pixel-perfect typography and line numbers gutter with matching font, size and padding.
/// • Smooth horizontal layout for tabs and buttons (no single-letter vertical wrapping).
/// • Minimalist bottom output bar that collapses into a single-line status or expands to full logs.
/// • Compact minimize dock mode (240x36px) so you can watch your robot at work in the voxel world.
/// • Auto-saves scripts as .py files between game sessions.
/// </summary>
public class InGameIDEController : MonoBehaviour
{
    public static InGameIDEController Instance { get; private set; }

    [System.Serializable]
    public class ScriptTab
    {
        public string fileName;
        public string content;
        public bool isDirty;
    }

    [Header("Core References")]
    [Tooltip("Active ScriptRunner executing the code")]
    public ScriptRunner runner;

    [Header("Legacy Inspector References (Optional)")]
    public GameObject idePanel;
    public TMP_InputField codeInputField;
    public UIButton runButton;
    public UIButton stopButton;
    public UIButton closeButton;

    [Header("UI Construction / Customization")]
    [SerializeField] private TMP_FontAsset customFont;

    // Window states
    public enum WindowMode { Normal, Maximized, Minimized }
    private WindowMode _windowMode = WindowMode.Normal;
    private bool _isOpen = false;

    // Tabs
    private readonly List<ScriptTab> _tabs = new();
    private int _activeTabIndex = 0;

    // Dynamic UI References
    private Canvas _rootCanvas;
    private RectTransform _windowRect;
    private RectTransform _editorBodyRect;
    private RectTransform _gutterRect;
    private RectTransform _inputFieldRect;
    private IDECodeEditor _codeEditor;
    private IDEConsole _console;

    // Header controls
    private Transform _tabBarContainer;
    private Image _runBtnImg;
    private Image _stopBtnImg;
    private TextMeshProUGUI _runBtnText;
    private TextMeshProUGUI _miniBarLabel;
    private GameObject _fileDropdownPanel;

    // Bottom Output Drawer
    private RectTransform _bottomDrawerRect;
    private GameObject _previewBarGo;
    private TextMeshProUGUI _bottomPreviewText;
    private TextMeshProUGUI _drawerChevronText;
    private GameObject _consoleContentPanel;
    private bool _isConsoleDrawerExpanded = false;

    // Dragging
    private bool _isDragging = false;
    private Vector2 _dragOffset;

    // Sizing
    private readonly Vector2 _normalSize = new Vector2(530f, 640f);
    private readonly Vector2 _minimizedSize = new Vector2(250f, 38f);
    private Vector2 _normalPos = new Vector2(320f, 0f);

    // Modern Minimalist Palette (The Farmer Was Replaced inspired)
    private static readonly Color BgWindow = new Color(0.07f, 0.08f, 0.11f, 0.98f);
    private static readonly Color BgHeader = new Color(0.05f, 0.06f, 0.08f, 1f);
    private static readonly Color AmberRun = new Color(0.96f, 0.60f, 0.12f, 1f);
    private static readonly Color DarkRunText = new Color(0.06f, 0.08f, 0.12f, 1f);
    private static readonly Color DarkBtn = new Color(0.14f, 0.17f, 0.23f, 1f);
    private static readonly Color RedRunning = new Color(0.85f, 0.25f, 0.25f, 1f);
    private static readonly Color TabActive = new Color(0.14f, 0.18f, 0.26f, 1f);
    private static readonly Color TabInactive = new Color(0.08f, 0.10f, 0.14f, 0.7f);
    private static readonly Color TextMuted = new Color(0.55f, 0.62f, 0.72f, 1f);
    private static readonly Color BorderSubtle = new Color(0.18f, 0.22f, 0.30f, 0.7f);

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        EnsureModernUIExists();
        InitializeScriptTabs();

        if (runButton != null) runButton.onClick.AddListener(RunCode);
        if (stopButton != null) stopButton.onClick.AddListener(StopCode);
        if (closeButton != null) closeButton.onClick.AddListener(ToggleIDE);

        if (idePanel != null)
            idePanel.SetActive(false);

        SetIDEOpen(false);
    }

    private void Update()
    {
        bool isTyping = _codeEditor != null && _codeEditor.InputField != null && _codeEditor.InputField.isFocused;

        if (!isTyping)
        {
            if (Input.GetKeyDown(KeyCode.BackQuote) || Input.GetKeyDown(KeyCode.Quote))
                ToggleIDE();
        }

        if (_isOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            if (_fileDropdownPanel != null && _fileDropdownPanel.activeSelf)
                _fileDropdownPanel.SetActive(false);
            else
                ToggleIDE();
        }

        UpdateWindowDragging();
        UpdateRunStateVisuals();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Public API (Backward Compatible)
    // ═══════════════════════════════════════════════════════════════════════════

    public void OpenFor(ScriptRunner targetRunner)
    {
        runner = targetRunner;

        if (!_isOpen)
            ToggleIDE();

        UpdateMiniBarLabel();
    }

    public void ToggleIDE()
    {
        SetIDEOpen(!_isOpen);
    }

    public void SetIDEOpen(bool open)
    {
        _isOpen = open;
        GameState.IsIDEOpen = _isOpen;

        if (_windowRect != null)
            _windowRect.gameObject.SetActive(_isOpen);

        if (idePanel != null)
            idePanel.SetActive(false);

        if (_isOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (_codeEditor != null && _codeEditor.InputField != null && _windowMode != WindowMode.Minimized)
            {
                EventSystem.current?.SetSelectedGameObject(_codeEditor.InputField.gameObject);
                _codeEditor.InputField.ActivateInputField();
            }
        }
        else
        {
            SaveCurrentTab();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        UpdateMiniBarLabel();
    }

    public void RunCode()
    {
        SaveCurrentTab();

        if (runner == null)
            runner = FindAnyObjectByType<ScriptRunner>();

        if (runner != null && _codeEditor != null)
        {
            string code = _codeEditor.GetText();
            runner.RunScript(code);
            _console?.AddLog($"Running '{GetActiveFileName()}'...", LogType.Log);
        }
        else
        {
            string msg = "No robot assigned.";
            Debug.LogWarning($"[InGameIDEController] {msg}");
            _console?.AddLog(msg, LogType.Error);
        }
    }

    public void StopCode()
    {
        if (runner != null)
        {
            runner.StopScript();
            _console?.AddLog("Stopped.", LogType.Warning);
        }
    }

    public void SanitizeText(string currentText) { }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Script Tabs Management & Auto-Save
    // ═══════════════════════════════════════════════════════════════════════════

    private void InitializeScriptTabs()
    {
        var scripts = ScriptStorageManager.GetAllScripts();
        if (scripts.Count == 0)
        {
            ScriptStorageManager.SaveScript("main.py", "# main.py\nprint('Robot ready')\nrobot.move(1)\n");
            scripts = ScriptStorageManager.GetAllScripts();
        }

        _tabs.Clear();
        foreach (var s in scripts)
        {
            string content = ScriptStorageManager.LoadScript(s);
            _tabs.Add(new ScriptTab { fileName = s, content = content, isDirty = false });
        }

        if (_tabs.Count == 0)
        {
            _tabs.Add(new ScriptTab { fileName = "main.py", content = "# Write code here\n", isDirty = false });
        }

        _activeTabIndex = 0;
        RefreshTabsUI();
        LoadTabContentIntoEditor(_activeTabIndex);
        UpdateMiniBarLabel();
    }

    public void OpenOrCreateTab(string fileName)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].fileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                SwitchToTab(i);
                return;
            }
        }

        string content = ScriptStorageManager.LoadScript(fileName);
        var tab = new ScriptTab { fileName = fileName, content = content, isDirty = false };
        _tabs.Add(tab);
        RefreshTabsUI();
        SwitchToTab(_tabs.Count - 1);
    }

    public void SwitchToTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;

        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count && _codeEditor != null)
        {
            _tabs[_activeTabIndex].content = _codeEditor.GetText();
        }

        _activeTabIndex = index;
        RefreshTabsUI();
        LoadTabContentIntoEditor(_activeTabIndex);
        UpdateMiniBarLabel();
    }

    public void CloseTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;

        if (_tabs[index].isDirty)
            ScriptStorageManager.SaveScript(_tabs[index].fileName, _tabs[index].content);

        _tabs.RemoveAt(index);

        if (_tabs.Count == 0)
        {
            string newName = ScriptStorageManager.GetNextAvailableScriptName();
            _tabs.Add(new ScriptTab { fileName = newName, content = "# New Script\n", isDirty = false });
        }

        _activeTabIndex = Mathf.Clamp(_activeTabIndex, 0, _tabs.Count - 1);
        RefreshTabsUI();
        LoadTabContentIntoEditor(_activeTabIndex);
        UpdateMiniBarLabel();
    }

    public void CreateNewTab()
    {
        string newName = ScriptStorageManager.GetNextAvailableScriptName();
        string initialContent = $"# {newName}\nprint('Ready')\n";
        ScriptStorageManager.SaveScript(newName, initialContent);

        var tab = new ScriptTab { fileName = newName, content = initialContent, isDirty = false };
        _tabs.Add(tab);
        RefreshTabsUI();
        SwitchToTab(_tabs.Count - 1);
    }

    public void SaveCurrentTab()
    {
        if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count || _codeEditor == null) return;

        var tab = _tabs[_activeTabIndex];
        tab.content = _codeEditor.GetText();
        bool ok = ScriptStorageManager.SaveScript(tab.fileName, tab.content);
        if (ok)
        {
            tab.isDirty = false;
            RefreshTabsUI();
        }
    }

    private void LoadTabContentIntoEditor(int index)
    {
        if (index < 0 || index >= _tabs.Count || _codeEditor == null) return;
        _codeEditor.SetText(_tabs[index].content);
    }

    private string GetActiveFileName() => (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count) ? _tabs[_activeTabIndex].fileName : "script.py";

    // ═══════════════════════════════════════════════════════════════════════════
    //  Window Modes: Minimize, Maximize, Dragging
    // ═══════════════════════════════════════════════════════════════════════════

    public void ToggleMinimize()
    {
        if (_windowMode == WindowMode.Minimized)
            SetWindowMode(WindowMode.Normal);
        else
            SetWindowMode(WindowMode.Minimized);
    }

    public void ToggleMaximize()
    {
        if (_windowMode == WindowMode.Maximized)
            SetWindowMode(WindowMode.Normal);
        else
            SetWindowMode(WindowMode.Maximized);
    }

    public void SetWindowMode(WindowMode mode)
    {
        _windowMode = mode;
        if (_windowRect == null) return;

        switch (_windowMode)
        {
            case WindowMode.Normal:
                _windowRect.sizeDelta = _normalSize;
                _windowRect.anchoredPosition = _normalPos;
                if (_editorBodyRect != null) _editorBodyRect.gameObject.SetActive(true);
                if (_bottomDrawerRect != null) _bottomDrawerRect.gameObject.SetActive(true);
                if (_tabBarContainer != null) _tabBarContainer.gameObject.SetActive(true);
                if (_miniBarLabel != null) _miniBarLabel.gameObject.SetActive(false);
                break;

            case WindowMode.Maximized:
                _normalPos = _windowRect.anchoredPosition;
                _windowRect.sizeDelta = new Vector2(1740f, 960f);
                _windowRect.anchoredPosition = Vector2.zero;
                if (_editorBodyRect != null) _editorBodyRect.gameObject.SetActive(true);
                if (_bottomDrawerRect != null) _bottomDrawerRect.gameObject.SetActive(true);
                if (_tabBarContainer != null) _tabBarContainer.gameObject.SetActive(true);
                if (_miniBarLabel != null) _miniBarLabel.gameObject.SetActive(false);
                break;

            case WindowMode.Minimized:
                _normalPos = _windowRect.anchoredPosition;
                _windowRect.sizeDelta = _minimizedSize;
                if (_editorBodyRect != null) _editorBodyRect.gameObject.SetActive(false);
                if (_bottomDrawerRect != null) _bottomDrawerRect.gameObject.SetActive(false);
                if (_tabBarContainer != null) _tabBarContainer.gameObject.SetActive(false);
                if (_miniBarLabel != null)
                {
                    _miniBarLabel.gameObject.SetActive(true);
                    UpdateMiniBarLabel();
                }
                break;
        }
    }

    public void OnTitleBarDragStart()
    {
        if (_windowMode == WindowMode.Maximized) return;
        _isDragging = true;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _rootCanvas.transform as RectTransform,
            Input.mousePosition,
            null,
            out Vector2 mousePos);
        _dragOffset = _windowRect.anchoredPosition - mousePos;
    }

    private void UpdateWindowDragging()
    {
        if (!_isDragging) return;

        if (Input.GetMouseButtonUp(0))
        {
            _isDragging = false;
            return;
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _rootCanvas.transform as RectTransform,
            Input.mousePosition,
            null,
            out Vector2 mousePos);

        _windowRect.anchoredPosition = mousePos + _dragOffset;
        if (_windowMode == WindowMode.Normal)
            _normalPos = _windowRect.anchoredPosition;
    }

    private void UpdateRunStateVisuals()
    {
        bool isRunning = runner != null && runner.IsScriptRunning;

        if (_runBtnImg != null)
        {
            _runBtnImg.color = isRunning ? new Color(1f, 0.72f, 0.2f, 1f) : AmberRun;
        }

        if (_stopBtnImg != null)
        {
            _stopBtnImg.color = isRunning ? RedRunning : DarkBtn;
        }
    }

    private void UpdateMiniBarLabel()
    {
        if (_miniBarLabel == null) return;
        string device = runner != null ? runner.ActiveDeviceName : "Robot";
        _miniBarLabel.text = $"{GetActiveFileName()} ({device})";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  UI Construction (The Farmer Was Replaced Clean Minimalist Style)
    // ═══════════════════════════════════════════════════════════════════════════

    private void EnsureModernUIExists()
    {
        if (_rootCanvas != null && _windowRect != null) return;

        // Clean up any stale canvas from prior hot-reload
        Transform stale = transform.Find("InGameIDE_Canvas");
        if (stale != null)
            Destroy(stale.gameObject);

        // 1. Root Canvas
        GameObject canvasGo = new GameObject("InGameIDE_Canvas");
        canvasGo.transform.SetParent(transform, false);

        _rootCanvas = canvasGo.AddComponent<Canvas>();
        _rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _rootCanvas.sortingOrder = 25000;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        // 2. Main Window Container
        GameObject winGo = CreatePanel(canvasGo.transform, "IDE_Window", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                       new Vector2(0.5f, 0.5f), _normalPos, _normalSize, BgWindow);
        _windowRect = winGo.GetComponent<RectTransform>();

        // Subtle 1px Outline / Border
        GameObject borderGo = CreatePanel(winGo.transform, "BorderOutline", Vector2.zero, Vector2.one,
                                          new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, BorderSubtle);
        Image borderImg = borderGo.GetComponent<Image>();
        borderImg.raycastTarget = false;
        Stretch(borderGo.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);

        // Inner background sits slightly inside to leave a 1px border
        GameObject innerBg = CreatePanel(winGo.transform, "InnerBg", Vector2.zero, Vector2.one,
                                         new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, BgWindow);
        innerBg.GetComponent<Image>().raycastTarget = false;
        Stretch(innerBg.GetComponent<RectTransform>(), 1f, 1f, 1f, 1f);

        // 3. Compact Header Bar (38px height)
        GameObject topBar = CreatePanel(winGo.transform, "TopBar", new Vector2(0f, 1f), new Vector2(1f, 1f),
                                        new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 38f), BgHeader);
        SetupDragTrigger(topBar);

        // Bottom border of top bar
        GameObject topBorder = CreatePanel(topBar.transform, "TopBarBorder", new Vector2(0f, 0f), new Vector2(1f, 0f),
                                           new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 1f), BorderSubtle);
        topBorder.GetComponent<Image>().raycastTarget = false;

        // --- Left Controls Group (RUN + STOP + Separator) ---
        // RUN Button
        GameObject runBtnGo = CreatePanel(topBar.transform, "Btn_Run", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                                          new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(44f, 26f), AmberRun);
        _runBtnImg = runBtnGo.GetComponent<Image>();
        UIButton runBtn = runBtnGo.AddComponent<UIButton>();
        runBtn.onClick.AddListener(RunCode);
        _runBtnText = CreateTMPText(runBtnGo.transform, "Label", 11f, TextAlignmentOptions.Center);
        _runBtnText.text = "<b>RUN</b>";
        _runBtnText.color = DarkRunText;
        Stretch(_runBtnText.rectTransform);

        // STOP Button
        GameObject stopBtnGo = CreatePanel(topBar.transform, "Btn_Stop", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                                           new Vector2(0f, 0.5f), new Vector2(56f, 0f), new Vector2(42f, 26f), DarkBtn);
        _stopBtnImg = stopBtnGo.GetComponent<Image>();
        UIButton stopBtn = stopBtnGo.AddComponent<UIButton>();
        stopBtn.onClick.AddListener(StopCode);
        var stopTxt = CreateTMPText(stopBtnGo.transform, "Label", 10f, TextAlignmentOptions.Center);
        stopTxt.text = "<b>STOP</b>";
        stopTxt.color = TextMuted;
        Stretch(stopTxt.rectTransform);

        // Separator
        CreatePanel(topBar.transform, "Sep", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(105f, 0f), new Vector2(1f, 18f), BorderSubtle);

        // --- Tab Bar Container (Horizontal Layout) ---
        GameObject tabBar = new GameObject("TabBar", typeof(RectTransform));
        tabBar.transform.SetParent(topBar.transform, false);
        RectTransform tabRt = tabBar.GetComponent<RectTransform>();
        tabRt.anchorMin = new Vector2(0f, 0f);
        tabRt.anchorMax = new Vector2(1f, 1f);
        tabRt.offsetMin = new Vector2(112f, 0f);
        tabRt.offsetMax = new Vector2(-128f, 0f);
        _tabBarContainer = tabBar.transform;

        HorizontalLayoutGroup hlg = tabBar.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 4f;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childAlignment = TextAnchor.MiddleLeft;

        // Minimized title label
        _miniBarLabel = CreateTMPText(topBar.transform, "MiniBarTitle", 11f, TextAlignmentOptions.MidlineLeft);
        _miniBarLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
        _miniBarLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
        _miniBarLabel.rectTransform.offsetMin = new Vector2(106f, 0f);
        _miniBarLabel.rectTransform.offsetMax = new Vector2(-54f, 0f);
        _miniBarLabel.color = Color.white;
        _miniBarLabel.gameObject.SetActive(false);

        // --- Right Controls Group (FILES, LOG, Min, Close) ---
        GameObject rightGrp = new GameObject("RightGroup", typeof(RectTransform));
        rightGrp.transform.SetParent(topBar.transform, false);
        RectTransform rRt = rightGrp.GetComponent<RectTransform>();
        rRt.anchorMin = new Vector2(1f, 0f);
        rRt.anchorMax = new Vector2(1f, 1f);
        rRt.pivot = new Vector2(1f, 0.5f);
        rRt.anchoredPosition = new Vector2(-6f, 0f);
        rRt.sizeDelta = new Vector2(120f, 0f);

        HorizontalLayoutGroup rHlg = rightGrp.AddComponent<HorizontalLayoutGroup>();
        rHlg.spacing = 3f;
        rHlg.childControlWidth = false;
        rHlg.childControlHeight = false;
        rHlg.childForceExpandWidth = false;
        rHlg.childForceExpandHeight = false;
        rHlg.childAlignment = TextAnchor.MiddleRight;

        CreatePillButton(rightGrp.transform, "FILES", 38f, 22f, ToggleFilesMenu);
        CreatePillButton(rightGrp.transform, "LOG", 32f, 22f, ToggleConsoleDrawer);
        CreatePillButton(rightGrp.transform, "_", 20f, 22f, ToggleMinimize);
        CreatePillButton(rightGrp.transform, "X", 20f, 22f, ToggleIDE);

        // 4. Main Editor Area (Takes full remaining space)
        GameObject editorBody = CreatePanel(winGo.transform, "EditorBody", new Vector2(0f, 0f), new Vector2(1f, 1f),
                                            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.clear);
        _editorBodyRect = editorBody.GetComponent<RectTransform>();
        _editorBodyRect.offsetMax = new Vector2(0f, -38f); // below top bar
        _editorBodyRect.offsetMin = new Vector2(0f, 26f);  // above bottom drawer

        // Line Numbers Gutter
        GameObject gutterGo = CreatePanel(editorBody.transform, "LineNumbersGutter", new Vector2(0f, 0f), new Vector2(0f, 1f),
                                          new Vector2(0f, 0.5f), Vector2.zero, new Vector2(38f, 0f), new Color(0.05f, 0.06f, 0.08f, 0.95f));
        _gutterRect = gutterGo.GetComponent<RectTransform>();

        // 1px divider line between gutter and code
        CreatePanel(gutterGo.transform, "GutterSep", new Vector2(1f, 0f), new Vector2(1f, 1f),
                    new Vector2(1f, 0.5f), Vector2.zero, new Vector2(1f, 0f), BorderSubtle);

        var lineNums = CreateTMPText(gutterGo.transform, "LineNumbersText", 13f, TextAlignmentOptions.TopRight);
        Stretch(lineNums.rectTransform, 0f, 6f, 6f, 6f);
        lineNums.color = new Color(0.35f, 0.42f, 0.52f, 0.75f);
        lineNums.lineSpacing = 0f;

        // Code Input Field
        GameObject codeInputGo = new GameObject("CodeInputField", typeof(RectTransform), typeof(TMP_InputField));
        codeInputGo.transform.SetParent(editorBody.transform, false);
        _inputFieldRect = codeInputGo.GetComponent<RectTransform>();
        Stretch(_inputFieldRect, 40f, 0f, 4f, 0f);

        // Viewport with RectMask2D (Crucial: prevents text overflowing!)
        GameObject vpGo = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        vpGo.transform.SetParent(codeInputGo.transform, false);
        RectTransform vpRt = vpGo.GetComponent<RectTransform>();
        Stretch(vpRt, 4f, 6f, 4f, 6f);

        // Text Component
        GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(vpGo.transform, false);
        RectTransform textRt = textGo.GetComponent<RectTransform>();
        Stretch(textRt, 0f, 0f, 0f, 0f);

        TextMeshProUGUI textTmp = textGo.GetComponent<TextMeshProUGUI>();
        textTmp.fontSize = 13f;
        textTmp.color = new Color(0.92f, 0.94f, 0.98f);
        textTmp.richText = true;
        textTmp.alignment = TextAlignmentOptions.TopLeft;
        textTmp.lineSpacing = 0f;
        textTmp.textWrappingMode = TextWrappingModes.NoWrap;

        TMP_InputField inputField = codeInputGo.GetComponent<TMP_InputField>();
        inputField.textViewport = vpRt;
        inputField.textComponent = textTmp;
        inputField.lineType = TMP_InputField.LineType.MultiLineNewline;
        inputField.caretColor = AmberRun;
        inputField.selectionColor = new Color(0.24f, 0.50f, 0.85f, 0.35f);
        codeInputField = inputField;

        _codeEditor = codeInputGo.AddComponent<IDECodeEditor>();
        _codeEditor.BindReferences(inputField, lineNums);

        // Wire Shortcuts
        _codeEditor.OnRunShortcut += RunCode;
        _codeEditor.OnStopShortcut += StopCode;
        _codeEditor.OnSaveShortcut += SaveCurrentTab;
        _codeEditor.OnNewShortcut += CreateNewTab;
        _codeEditor.OnCloseTabShortcut += () => CloseTab(_activeTabIndex);
        _codeEditor.OnToggleConsoleShortcut += ToggleConsoleDrawer;
        _codeEditor.OnContentChanged += (content) =>
        {
            if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
            {
                _tabs[_activeTabIndex].isDirty = true;
                _tabs[_activeTabIndex].content = content;
                RefreshTabsUI();
            }
        };

        // 5. Minimalist Bottom Output Bar / Drawer
        GameObject bottomDrawer = CreatePanel(winGo.transform, "BottomDrawer", new Vector2(0f, 0f), new Vector2(1f, 0f),
                                              new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 26f),
                                              new Color(0.05f, 0.06f, 0.08f, 0.98f));
        _bottomDrawerRect = bottomDrawer.GetComponent<RectTransform>();

        // Top border on bottom bar
        GameObject bottomBorder = CreatePanel(bottomDrawer.transform, "Border", new Vector2(0f, 1f), new Vector2(1f, 1f),
                                              new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 1f), BorderSubtle);
        bottomBorder.GetComponent<Image>().raycastTarget = false;

        // Collapsed status preview bar
        _previewBarGo = new GameObject("PreviewBar", typeof(RectTransform), typeof(UIButton));
        _previewBarGo.transform.SetParent(bottomDrawer.transform, false);
        Stretch(_previewBarGo.GetComponent<RectTransform>());
        UIButton previewBtn = _previewBarGo.GetComponent<UIButton>();
        previewBtn.onClick.AddListener(ToggleConsoleDrawer);

        _bottomPreviewText = CreateTMPText(_previewBarGo.transform, "PreviewText", 11f, TextAlignmentOptions.MidlineLeft);
        Stretch(_bottomPreviewText.rectTransform, 12f, 0f, 30f, 0f);
        _bottomPreviewText.text = "<color=#64748B>> </color><color=#94A3B8>Ready</color>";

        _drawerChevronText = CreateTMPText(_previewBarGo.transform, "Chevron", 10f, TextAlignmentOptions.Center);
        RectTransform chevRt = _drawerChevronText.rectTransform;
        chevRt.anchorMin = new Vector2(1f, 0.5f);
        chevRt.anchorMax = new Vector2(1f, 0.5f);
        chevRt.pivot = new Vector2(1f, 0.5f);
        chevRt.anchoredPosition = new Vector2(-8f, 0f);
        chevRt.sizeDelta = new Vector2(18f, 18f);
        _drawerChevronText.text = "<color=#64748B>^</color>";

        // Expanded Console Content Panel
        _consoleContentPanel = new GameObject("ConsoleContent", typeof(RectTransform));
        _consoleContentPanel.transform.SetParent(bottomDrawer.transform, false);
        Stretch(_consoleContentPanel.GetComponent<RectTransform>());
        _consoleContentPanel.SetActive(false);

        // Header inside expanded drawer: "OUTPUT" label + "clear" button
        var outLabel = CreateTMPText(_consoleContentPanel.transform, "Label", 10f, TextAlignmentOptions.MidlineLeft);
        outLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        outLabel.rectTransform.anchorMax = new Vector2(0f, 1f);
        outLabel.rectTransform.pivot = new Vector2(0f, 1f);
        outLabel.rectTransform.anchoredPosition = new Vector2(12f, -4f);
        outLabel.rectTransform.sizeDelta = new Vector2(100f, 18f);
        outLabel.text = "<color=#64748B><b>OUTPUT</b></color>";

        CreatePillButton(_consoleContentPanel.transform, "CLEAR", 40f, 16f, () => _console?.Clear());
        RectTransform clearRt = _consoleContentPanel.transform.GetChild(_consoleContentPanel.transform.childCount - 1).GetComponent<RectTransform>();
        clearRt.anchorMin = new Vector2(1f, 1f);
        clearRt.anchorMax = new Vector2(1f, 1f);
        clearRt.pivot = new Vector2(1f, 1f);
        clearRt.anchoredPosition = new Vector2(-10f, -4f);

        // Console Scroll Area
        GameObject scrollGo = new GameObject("ScrollRect", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(_consoleContentPanel.transform, false);
        var srt = scrollGo.GetComponent<RectTransform>();
        Stretch(srt, 12f, 22f, 12f, 4f);

        GameObject conTextGo = new GameObject("ConsoleText", typeof(RectTransform), typeof(TextMeshProUGUI));
        conTextGo.transform.SetParent(scrollGo.transform, false);
        var conTmp = conTextGo.GetComponent<TextMeshProUGUI>();
        conTmp.fontSize = 11f;
        conTmp.richText = true;
        conTmp.alignment = TextAlignmentOptions.TopLeft;
        conTmp.textWrappingMode = TextWrappingModes.NoWrap;
        Stretch(conTextGo.GetComponent<RectTransform>());

        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        sr.content = conTextGo.GetComponent<RectTransform>();
        sr.horizontal = false;
        sr.vertical = true;

        _console = bottomDrawer.AddComponent<IDEConsole>();
        _console.BindReferences(bottomDrawer, conTmp, sr);
        _console.OnNewMessage += (msg, isError) =>
        {
            if (_bottomPreviewText != null)
            {
                string col = isError ? "#F87171" : "#94A3B8";
                _bottomPreviewText.text = $"<color=#64748B>> </color><color={col}>{msg}</color>";
            }
        };

        // 6. Popover File Browser Drawer
        CreateFilesDropdownMenu(winGo.transform);
    }

    private void ToggleConsoleDrawer()
    {
        _isConsoleDrawerExpanded = !_isConsoleDrawerExpanded;

        float bottomHeight = _isConsoleDrawerExpanded ? 130f : 26f;
        if (_bottomDrawerRect != null)
            _bottomDrawerRect.sizeDelta = new Vector2(0f, bottomHeight);

        if (_previewBarGo != null)
            _previewBarGo.SetActive(!_isConsoleDrawerExpanded);

        if (_consoleContentPanel != null)
            _consoleContentPanel.SetActive(_isConsoleDrawerExpanded);

        if (_drawerChevronText != null)
            _drawerChevronText.text = _isConsoleDrawerExpanded ? "<color=#64748B>v</color>" : "<color=#64748B>^</color>";

        if (_editorBodyRect != null)
            _editorBodyRect.offsetMin = new Vector2(0f, bottomHeight);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Tabs UI Rendering (Horizontal)
    // ═══════════════════════════════════════════════════════════════════════════

    private void RefreshTabsUI()
    {
        if (_tabBarContainer == null) return;

        foreach (Transform child in _tabBarContainer)
            Destroy(child.gameObject);

        for (int i = 0; i < _tabs.Count; i++)
        {
            int tabIndex = i;
            var tab = _tabs[i];
            bool isActive = (i == _activeTabIndex);

            string label = tab.fileName + (tab.isDirty ? " *" : "");
            Color bgColor = isActive ? TabActive : TabInactive;
            Color txtColor = isActive ? Color.white : TextMuted;

            GameObject tabGo = new GameObject($"Tab_{i}", typeof(RectTransform), typeof(Image), typeof(UIButton));
            tabGo.transform.SetParent(_tabBarContainer, false);

            RectTransform rt = tabGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(94f, 26f);

            tabGo.GetComponent<Image>().color = bgColor;
            UIButton btn = tabGo.GetComponent<UIButton>();
            btn.onClick.AddListener(() => SwitchToTab(tabIndex));

            var tmp = CreateTMPText(tabGo.transform, "Label", 11f, TextAlignmentOptions.MidlineLeft);
            tmp.text = label;
            tmp.color = txtColor;
            Stretch(tmp.rectTransform, 8f, 0f, 20f, 0f);

            // Tab close 'X' button
            GameObject closeGo = new GameObject("CloseBtn", typeof(RectTransform), typeof(UIButton));
            closeGo.transform.SetParent(tabGo.transform, false);
            RectTransform closeRt = closeGo.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(1f, 0.5f);
            closeRt.anchorMax = new Vector2(1f, 0.5f);
            closeRt.pivot = new Vector2(1f, 0.5f);
            closeRt.anchoredPosition = new Vector2(-4f, 0f);
            closeRt.sizeDelta = new Vector2(14f, 14f);

            UIButton closeBtn = closeGo.GetComponent<UIButton>();
            closeBtn.onClick.AddListener(() => CloseTab(tabIndex));

            var closeTxt = CreateTMPText(closeGo.transform, "X", 10f, TextAlignmentOptions.Center);
            closeTxt.text = "<color=#64748B>x</color>";
            Stretch(closeTxt.rectTransform);
        }

        // [+] Add Tab button
        GameObject addBtnGo = new GameObject("Btn_Add", typeof(RectTransform), typeof(Image), typeof(UIButton));
        addBtnGo.transform.SetParent(_tabBarContainer, false);
        RectTransform addRt = addBtnGo.GetComponent<RectTransform>();
        addRt.sizeDelta = new Vector2(22f, 22f);

        addBtnGo.GetComponent<Image>().color = DarkBtn;
        UIButton addBtn = addBtnGo.GetComponent<UIButton>();
        addBtn.onClick.AddListener(CreateNewTab);

        var addTxt = CreateTMPText(addBtnGo.transform, "+", 12f, TextAlignmentOptions.Center);
        addTxt.text = "+";
        addTxt.color = TextMuted;
        Stretch(addTxt.rectTransform);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Popover File Browser
    // ═══════════════════════════════════════════════════════════════════════════

    private void CreateFilesDropdownMenu(Transform parent)
    {
        _fileDropdownPanel = CreatePanel(parent, "FilesDrawer", new Vector2(1f, 1f), new Vector2(1f, 1f),
                                         new Vector2(1f, 1f), new Vector2(-8f, -42f), new Vector2(160f, 200f),
                                         new Color(0.06f, 0.07f, 0.10f, 0.99f));

        // 1px border
        GameObject border = CreatePanel(_fileDropdownPanel.transform, "Border", Vector2.zero, Vector2.one,
                                        new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, BorderSubtle);
        border.GetComponent<Image>().raycastTarget = false;
        Stretch(border.GetComponent<RectTransform>());

        _fileDropdownPanel.SetActive(false);
    }

    private void ToggleFilesMenu()
    {
        if (_fileDropdownPanel == null) return;
        bool active = !_fileDropdownPanel.activeSelf;
        _fileDropdownPanel.SetActive(active);

        if (active)
            PopulateFilesDropdown();
    }

    private void PopulateFilesDropdown()
    {
        foreach (Transform child in _fileDropdownPanel.transform)
        {
            if (child.name != "Border")
                Destroy(child.gameObject);
        }

        var title = CreateTMPText(_fileDropdownPanel.transform, "Title", 10f, TextAlignmentOptions.MidlineLeft);
        title.rectTransform.anchorMin = new Vector2(0f, 1f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.pivot = new Vector2(0f, 1f);
        title.rectTransform.anchoredPosition = new Vector2(10f, -6f);
        title.rectTransform.sizeDelta = new Vector2(0f, 18f);
        title.text = "<color=#64748B>SAVED SCRIPTS</color>";

        var scripts = ScriptStorageManager.GetAllScripts();
        float yOffset = -26f;
        for (int i = 0; i < scripts.Count; i++)
        {
            string fileName = scripts[i];
            GameObject item = CreatePanel(_fileDropdownPanel.transform, $"FileItem_{i}", new Vector2(0f, 1f), new Vector2(1f, 1f),
                                          new Vector2(0.5f, 1f), new Vector2(0f, yOffset), new Vector2(-12f, 22f),
                                          new Color(0.11f, 0.14f, 0.19f, 0.8f));
            UIButton b = item.AddComponent<UIButton>();
            b.onClick.AddListener(() =>
            {
                OpenOrCreateTab(fileName);
                _fileDropdownPanel.SetActive(false);
            });

            var label = CreateTMPText(item.transform, "Label", 11f, TextAlignmentOptions.MidlineLeft);
            label.text = $"<color=#CBD5E1>{fileName}</color>";
            Stretch(label.rectTransform, 8f, 0f, 8f, 0f);

            yOffset -= 24f;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  UI Helper Factory Methods
    // ═══════════════════════════════════════════════════════════════════════════

    private GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                   Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta, Color color)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);

        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;

        Image img = panel.GetComponent<Image>();
        img.color = color;
        return panel;
    }

    private TextMeshProUGUI CreateTMPText(Transform parent, string name, float fontSize, TextAlignmentOptions alignment)
    {
        GameObject textGo = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.color = Color.white;
        tmp.richText = true;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;

        if (customFont != null)
            tmp.font = customFont;
        else
        {
            var libFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (libFont != null) tmp.font = libFont;
            else if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        }

        return tmp;
    }

    private void CreatePillButton(Transform parent, string text, float width, float height, Action onClick)
    {
        GameObject btnGo = new GameObject("Btn_" + text, typeof(RectTransform), typeof(Image), typeof(UIButton));
        btnGo.transform.SetParent(parent, false);

        RectTransform rt = btnGo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);

        btnGo.GetComponent<Image>().color = DarkBtn;
        UIButton btn = btnGo.GetComponent<UIButton>();
        btn.onClick.AddListener(() => onClick?.Invoke());

        var tmp = CreateTMPText(btnGo.transform, "Label", 10f, TextAlignmentOptions.Center);
        tmp.text = text;
        tmp.color = TextMuted;
        Stretch(tmp.rectTransform);
    }

    private void SetupDragTrigger(GameObject titleBar)
    {
        EventTrigger trigger = titleBar.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        entry.callback.AddListener((data) => OnTitleBarDragStart());
        trigger.triggers.Add(entry);
    }

    private static void Stretch(RectTransform rt, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }
}