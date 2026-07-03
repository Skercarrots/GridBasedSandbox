using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;

public class ScriptsBundlerWindow : EditorWindow
{
    // Each entry can be either a folder (DefaultAsset) or an individual script (MonoScript / TextAsset .cs)
    private List<Object> selectedItems = new List<Object>();
    private string bundleResult = "";
    private Vector2 scrollPos;
    private Vector2 listScrollPos;

    [MenuItem("Tools/Sker/Script Bundler")]
    public static void ShowWindow()
    {
        GetWindow<ScriptsBundlerWindow>("Script Bundler");
    }

    private void OnGUI()
    {
        GUILayout.Label("Bundle Scripts Utility", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        DrawDropArea();
        DrawSelectedList();

        EditorGUILayout.Space();

        EditorGUI.BeginDisabledGroup(selectedItems.Count == 0);
        if (GUILayout.Button("Process and Bundle Scripts", GUILayout.Height(30)))
        {
            ProcessScripts();
        }
        EditorGUI.EndDisabledGroup();

        if (!string.IsNullOrEmpty(bundleResult))
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Copy to Clipboard"))
            {
                EditorGUIUtility.systemCopyBuffer = bundleResult;
                Debug.Log("Bundle copied to clipboard!");
            }

            if (GUILayout.Button("Save as .txt in Assets"))
            {
                SaveToFile();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            GUILayout.Label("Preview:", EditorStyles.miniLabel);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(bundleResult, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
    }

    // --------------------------------------------------------------
    // Selection UI
    // --------------------------------------------------------------

    private void DrawDropArea()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("1. Drag scripts and/or folders from the Project tab here (multi-select works):", EditorStyles.label);

        Rect dropRect = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
        GUI.Box(dropRect, "Drop .cs files or folders here", EditorStyles.helpBox);

        Event evt = Event.current;
        switch (evt.type)
        {
            case EventType.DragUpdated:
                if (dropRect.Contains(evt.mousePosition))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                }
                break;

            case EventType.DragPerform:
                if (dropRect.Contains(evt.mousePosition))
                {
                    DragAndDrop.AcceptDrag();
                    foreach (Object dragged in DragAndDrop.objectReferences)
                    {
                        AddItem(dragged);
                    }
                    evt.Use();
                }
                break;
        }

        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Selection From Project Window"))
        {
            foreach (Object obj in Selection.objects)
            {
                AddItem(obj);
            }
        }
        if (GUILayout.Button("Clear List"))
        {
            selectedItems.Clear();
            bundleResult = "";
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void AddItem(Object obj)
    {
        if (obj == null) return;

        string path = AssetDatabase.GetAssetPath(obj);
        if (string.IsNullOrEmpty(path)) return;

        bool isFolder = AssetDatabase.IsValidFolder(path);
        bool isScript = path.EndsWith(".cs");

        if (!isFolder && !isScript) return; // ignore irrelevant asset types

        if (!selectedItems.Contains(obj))
        {
            selectedItems.Add(obj);
        }
    }

    private void DrawSelectedList()
    {
        if (selectedItems.Count == 0)
        {
            EditorGUILayout.HelpBox("No scripts or folders selected yet.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField($"2. Selected items ({selectedItems.Count}):", EditorStyles.boldLabel);

        listScrollPos = EditorGUILayout.BeginScrollView(listScrollPos, GUILayout.MaxHeight(150));

        int removeIndex = -1;
        for (int i = 0; i < selectedItems.Count; i++)
        {
            Object item = selectedItems[i];
            if (item == null) { removeIndex = i; continue; }

            string path = AssetDatabase.GetAssetPath(item);
            bool isFolder = AssetDatabase.IsValidFolder(path);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(isFolder ? "[Folder]" : "[Script]", GUILayout.Width(55));
            EditorGUILayout.ObjectField(item, typeof(Object), false);
            if (GUILayout.Button("X", GUILayout.Width(24)))
            {
                removeIndex = i;
            }
            EditorGUILayout.EndHorizontal();
        }

        if (removeIndex >= 0)
        {
            selectedItems.RemoveAt(removeIndex);
        }

        EditorGUILayout.EndScrollView();
    }

    // --------------------------------------------------------------
    // Processing
    // --------------------------------------------------------------

    private void ProcessScripts()
    {
        // Collect unique absolute paths for every .cs file implied by the selection
        List<string> scriptFiles = GatherScriptPaths();

        if (scriptFiles.Count == 0)
        {
            bundleResult = "No .cs scripts found in the current selection.";
            return;
        }

        StringBuilder sb = new StringBuilder();

        // Directory structure, built from the relative Assets-based paths of the collected files
        sb.AppendLine("==========================================");
        sb.AppendLine("DIRECTORY STRUCTURE");
        sb.AppendLine("==========================================");
        AppendStructure(scriptFiles, sb);
        sb.AppendLine("\n");

        // Script contents
        foreach (string filePath in scriptFiles)
        {
            string fileName = Path.GetFileName(filePath);
            string content = File.ReadAllText(filePath);

            sb.AppendLine("==========================================");
            sb.AppendLine($"SCRIPT: {fileName}");
            sb.AppendLine("==========================================");
            sb.AppendLine(content);
            sb.AppendLine("\n");
        }

        bundleResult = sb.ToString();
    }

    private List<string> GatherScriptPaths()
    {
        HashSet<string> absolutePaths = new HashSet<string>();

        foreach (Object item in selectedItems)
        {
            if (item == null) continue;

            string assetPath = AssetDatabase.GetAssetPath(item);
            if (string.IsNullOrEmpty(assetPath)) continue;

            if (AssetDatabase.IsValidFolder(assetPath))
            {
                string absoluteFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
                if (Directory.Exists(absoluteFolder))
                {
                    foreach (string file in Directory.GetFiles(absoluteFolder, "*.cs", SearchOption.AllDirectories))
                    {
                        absolutePaths.Add(Path.GetFullPath(file));
                    }
                }
            }
            else if (assetPath.EndsWith(".cs"))
            {
                string absoluteFile = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
                if (File.Exists(absoluteFile))
                {
                    absolutePaths.Add(absoluteFile);
                }
            }
        }

        List<string> result = absolutePaths.ToList();
        result.Sort();
        return result;
    }

    private void AppendStructure(List<string> absoluteScriptPaths, StringBuilder sb)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        // Group files by their containing folder (relative to project root) for a readable tree
        var byFolder = absoluteScriptPaths
            .GroupBy(p => Path.GetDirectoryName(p))
            .OrderBy(g => g.Key);

        foreach (var group in byFolder)
        {
            string relativeFolder = group.Key.Replace(projectRoot, "").TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            sb.AppendLine($"[Dir] {relativeFolder}");

            foreach (string file in group.OrderBy(f => f))
            {
                sb.AppendLine($"    - {Path.GetFileName(file)}");
            }
        }
    }

    private void SaveToFile()
    {
        string fileName = "ScriptsBundle_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
        string path = EditorUtility.SaveFilePanelInProject("Save Script Bundle", fileName, "txt", "Please enter a file name to save the bundle to");

        if (!string.IsNullOrEmpty(path))
        {
            File.WriteAllText(path, bundleResult);
            AssetDatabase.Refresh();
            Debug.Log($"Bundle saved to: {path}");
        }
    }
}