// CodeEditorUI.cs
// Inspector test panel + optional runtime UI for running Python scripts.
// Unchanged from the previous version — works with the updated ScriptRunner.

using UnityEngine;
using TMPro;
using Sirenix.OdinInspector;

public class CodeEditorUI : MonoBehaviour
{
    [Title("References")]
    [SerializeField] ScriptRunner runner;
    [SerializeField] TMP_InputField codeInput; // optional — for a runtime UI button

    [Title("Inspector Test")]
    [MultiLineProperty(10)]
    public string testCode = "print('hello from python')";

    [Button("▶ Run Script", ButtonSizes.Large), GUIColor(0.4f, 0.9f, 0.4f)]
    private void RunFromInspector()
    {
        if (runner == null) { Debug.LogError("ScriptRunner not assigned."); return; }
        runner.RunScript(testCode);
    }

    [Button("■ Stop Script", ButtonSizes.Medium), GUIColor(0.9f, 0.4f, 0.4f)]
    private void StopFromInspector() => runner?.StopScript();

    // Called by a UI button at runtime
    public void OnRunPressed()
    {
        string code = codeInput != null ? codeInput.text : testCode;
        runner.RunScript(code);
    }
}
