using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Professional in-game debugging console for Python stdout and stderr logs.
/// Supports collapsible dock, log filtering, clear, copy to clipboard, and auto-scroll.
/// Styled to match the F3 debug HUD aesthetic.
/// </summary>
public class IDEConsole : MonoBehaviour
{
    public enum LogFilter { All, OutputOnly, ErrorsOnly }

    private struct ConsoleEntry
    {
        public string timestamp;
        public string message;
        public LogType type;
    }

    [Header("UI References (auto-created if not bound)")]
    [SerializeField] private GameObject consolePanel;
    [SerializeField] private TextMeshProUGUI logText;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private TextMeshProUGUI statusText;

    private readonly List<ConsoleEntry> _entries = new();
    private readonly StringBuilder _sb = new(2048);
    private LogFilter _currentFilter = LogFilter.All;
    private bool _autoScroll = true;
    private bool _isOpen = true;
    private const int MAX_LOGS = 300;

    public bool IsOpen => _isOpen;

    public void BindReferences(GameObject panel, TextMeshProUGUI text, ScrollRect scroll, TextMeshProUGUI status = null)
    {
        consolePanel = panel ?? gameObject;
        logText = text;
        scrollRect = scroll;
        statusText = status;
        RefreshLogView();
    }

    private void Awake()
    {
        ScriptRunner.OnAnyPythonOutput += HandleGlobalPythonOutput;
    }

    private void OnDestroy()
    {
        ScriptRunner.OnAnyPythonOutput -= HandleGlobalPythonOutput;
    }

    private void Start()
    {
        AddLog("[IDE] Debug Console ready. stdout & stderr stream here.", LogType.Log);
    }

    private void HandleGlobalPythonOutput(ScriptRunner runner, string message, LogType type)
    {
        AddLog(message, type);
    }

    public string LastMessage { get; private set; } = "Ready";
    public event Action<string, bool> OnNewMessage;

    public void AddLog(string message, LogType type)
    {
        if (string.IsNullOrEmpty(message)) return;

        var entry = new ConsoleEntry
        {
            timestamp = DateTime.Now.ToString("HH:mm:ss"),
            message = message,
            type = type
        };

        _entries.Add(entry);
        if (_entries.Count > MAX_LOGS)
            _entries.RemoveAt(0);

        bool isError = type == LogType.Error || type == LogType.Exception;
        LastMessage = message;
        OnNewMessage?.Invoke(message, isError);

        RefreshLogView();
    }

    public void Clear()
    {
        _entries.Clear();
        LastMessage = "Console cleared";
        OnNewMessage?.Invoke(LastMessage, false);
        RefreshLogView();
    }

    public void ToggleConsole()
    {
        SetConsoleOpen(!_isOpen);
    }

    public void SetConsoleOpen(bool open)
    {
        _isOpen = open;
        if (consolePanel != null)
            consolePanel.SetActive(_isOpen);
    }

    public void SetFilter(LogFilter filter)
    {
        _currentFilter = filter;
        RefreshLogView();
    }

    public void ToggleAutoScroll()
    {
        _autoScroll = !_autoScroll;
    }

    public void CopyAllToClipboard()
    {
        GUIUtility.systemCopyBuffer = _sb.ToString();
    }

    public void RefreshLogView()
    {
        if (logText == null) return;

        _sb.Clear();
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            bool isError = e.type == LogType.Error || e.type == LogType.Exception;
            bool isWarning = e.type == LogType.Warning;

            if (_currentFilter == LogFilter.OutputOnly && isError) continue;
            if (_currentFilter == LogFilter.ErrorsOnly && !isError) continue;

            string colorTag = isError ? "#F87171" : (isWarning ? "#FBBF24" : "#CBD5E1");
            _sb.Append("<color=#64748B>> </color><color=").Append(colorTag).Append(">")
               .Append(e.message)
               .Append("</color>")
               .AppendLine();
        }

        logText.text = _sb.ToString();

        if (_autoScroll && scrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }
}
