using UnityEngine;
using IronPython.Hosting;
using Microsoft.Scripting.Hosting; // ← add this for ExceptionOperations
using System.Collections;
using System.Collections.Concurrent;
using System.Threading;

public class ScriptRunner : MonoBehaviour
{
    [Header("Device")]
    [Tooltip("Drag any GameObject that has an IScriptableDevice component here.")]
    [SerializeField] private MonoBehaviour device;

    [Header("Security")]
    [SerializeField] private float scriptTimeout = 10f;

    [Header("Debug")]
    [SerializeField] private bool verbose = true; // toggle in Inspector

    private ConcurrentQueue<System.Action> _actionQueue = new();
    private ManualResetEventSlim _stepDone = new(false);
    private Thread _pythonThread;
    private float  _scriptStartTime;
    private bool   _scriptRunning;

    private const string SANDBOX_SETUP = @"
import sys
import math
import random

_allowed = {'math': math, 'random': random}

import builtins as _builtins
def _safe_import(name, *args, **kwargs):
    if name in _allowed:
        return _allowed[name]
    raise ImportError(f""import of '{name}' is not allowed"")
_builtins.__import__ = _safe_import

del _builtins.open
del _builtins.eval
del _builtins.exec
del _builtins.compile

sys.modules['clr'] = None

del sys, _builtins, _allowed
";

    public void RunScript(string code)
    {
        StopScript();
        _scriptStartTime = Time.time;
        _scriptRunning   = true;

        // Capture API on main thread — Unity API calls not allowed in thread
        string varName    = null;
        BaseDeviceAPI api = null;
        if (device is IScriptableDevice sd)
        {
            varName = sd.VariableName;
            api     = sd.CreateAPI(this);

            if (verbose)
                Debug.Log($"[ScriptRunner] Starting script on device '{sd.DeviceName}' (var: {varName})");
        }
        else if (verbose)
        {
            Debug.LogWarning("[ScriptRunner] No IScriptableDevice assigned — no device API will be injected.");
        }

        _pythonThread = new Thread(() =>
        {
            ScriptEngine engine = null;
            try
            {
                engine = Python.CreateEngine();
                var scope = engine.CreateScope();

                var outputStream = new OutputCaptureStream(
                    text => Debug.Log($"[Python] {text}"));
                var errorStream = new OutputCaptureStream(
                    text => Debug.LogError($"[Python Error] {text}"));
                engine.Runtime.IO.SetOutput(outputStream, System.Text.Encoding.UTF8);
                engine.Runtime.IO.SetErrorOutput(errorStream, System.Text.Encoding.UTF8);

                var libPath = System.IO.Path.Combine(
                    Application.streamingAssetsPath, "IronPython/Lib");
                if (System.IO.Directory.Exists(libPath))
                {
                    engine.SetSearchPaths(new[] { libPath });
                    if (verbose) Debug.Log($"[ScriptRunner] Stdlib path set: {libPath}");
                }
                else if (verbose)
                {
                    Debug.LogWarning($"[ScriptRunner] Stdlib not found at: {libPath}");
                }

                if (api != null)
                {
                    scope.SetVariable(varName, api);
                    if (verbose) Debug.Log($"[ScriptRunner] Injected '{varName}' = {api.GetType().Name} (name={api.name})");
                }

                engine.Execute(Dedent(SANDBOX_SETUP), scope);

                if (verbose) Debug.Log("[ScriptRunner] Sandbox applied — executing player code...");

                engine.Execute(Dedent(code), scope);

                if (verbose) Debug.Log("[ScriptRunner] Script finished successfully.");
            }
            catch (ThreadAbortException) 
            {
                if (verbose) Debug.Log("[ScriptRunner] Script stopped by user.");
            }
            catch (System.Exception e)
            {
                // Get full Python traceback with line numbers if possible
                if (engine != null)
                {
                    try
                    {
                        var ops = engine.GetService<ExceptionOperations>();
                        Debug.LogError($"[Python Error]\n{ops.FormatException(e)}");
                    }
                    catch
                    {
                        // Fallback if ExceptionOperations fails
                        Debug.LogError($"[Python Error] {e.Message}\n{e.StackTrace}");
                    }
                }
                else
                {
                    Debug.LogError($"[Python Error] {e.Message}");
                }
            }
            finally
            {
                _scriptRunning = false;
            }
        });

        _pythonThread.IsBackground = true;
        _pythonThread.Start();
    }

    public void StopScript()
    {
        _pythonThread?.Abort();
        _actionQueue.Clear();
        _scriptRunning = false;
    }

    private void Update()
    {
        if (_scriptRunning && Time.time - _scriptStartTime > scriptTimeout)
        {
            Debug.LogWarning($"[ScriptRunner] Script timed out after {scriptTimeout}s — stopped.");
            StopScript();
        }

        if (_actionQueue.TryDequeue(out var action))
            StartCoroutine(ExecuteStep(action));
    }

    public void EnqueueAndWait(System.Action action)
    {
        _stepDone.Reset();
        _actionQueue.Enqueue(action);
        _stepDone.Wait();
    }

    private IEnumerator ExecuteStep(System.Action action)
    {
        action.Invoke();
        if (device is IScriptableDevice sd)
            yield return new WaitUntil(() => !sd.IsAnimating);
        _stepDone.Set();
    }

    private static string Dedent(string code)
    {
        var lines = code.Split('\n');
        int minIndent = int.MaxValue;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            int spaces = line.Length - line.TrimStart(' ', '\t').Length;
            if (spaces < minIndent) minIndent = spaces;
        }
        if (minIndent == int.MaxValue || minIndent == 0) return code;
        var sb = new System.Text.StringBuilder();
        foreach (var line in lines)
            sb.AppendLine(line.Length > minIndent
                ? line.Substring(minIndent)
                : line.TrimStart());
        return sb.ToString();
    }

    private class OutputCaptureStream : System.IO.Stream
    {
        private readonly System.Action<string> _onWrite;
        public OutputCaptureStream(System.Action<string> onWrite) => _onWrite = onWrite;

        public override void Write(byte[] buffer, int offset, int count)
        {
            string text = System.Text.Encoding.UTF8
                .GetString(buffer, offset, count)
                .TrimEnd('\n', '\r'); // trim trailing newline only
            if (!string.IsNullOrEmpty(text)) _onWrite?.Invoke(text);
        }

        public override bool CanRead  => false;
        public override bool CanSeek  => false;
        public override bool CanWrite => true;
        public override long Length   => 0;
        public override long Position { get; set; }
        public override void Flush()  { }
        public override int  Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, System.IO.SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
    }
}