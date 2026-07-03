using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class ScriptInjectorDynamic : EditorWindow
{
    [System.Serializable]
    public class FolderNode {
        public string name;
        public List<string> scriptsInside = new List<string>();
        public List<FolderNode> subFolders = new List<FolderNode>();
        public bool isExpanded = true;
        public FolderNode(string n) { name = n; }
    }

    // Folder names ignored when importing a directory from disk (build artifacts / VCS / editor junk).
    private static readonly HashSet<string> IgnoredFolderNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".plastic", "bin", "obj", "Library", "Temp", "Logs", "node_modules", "PackageCache"
    };

    private DefaultAsset targetUnityFolder;
    private List<string> unassignedScripts = new List<string>();
    private List<FolderNode> unassignedFolders = new List<FolderNode>();
    private FolderNode rootFolder = new FolderNode("No Folder Selected");
    private Vector2 scrollPos;

    [MenuItem("Tools/Sker/Script Injector")]
    public static void ShowWindow() => GetWindow<ScriptInjectorDynamic>("Interactive Injector");

    private void OnGUI()
    {
        UpdateRootName();

        // 1. Header
        EditorGUILayout.BeginVertical(EditorStyles.inspectorFullWidthMargins);
        EditorGUILayout.Space(5);
        EditorGUILayout.HelpBox(
            "Instructions: Drag script files or whole folders here from your OS file explorer (multi-select supported — folders keep their internal structure), or use the buttons below. Then drag items into the hierarchy and click Finalize.",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        targetUnityFolder = (DefaultAsset)EditorGUILayout.ObjectField("Unity Target Folder:", targetUnityFolder, typeof(DefaultAsset), false);
        if (EditorGUI.EndChangeCheck()) UpdateRootName();
        EditorGUILayout.EndVertical();

        // 2. Scrollable Area
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        DrawInbox();

        GUILayout.Space(10);
        Rect lineRect = GUILayoutUtility.GetRect(position.width, 1);
        EditorGUI.DrawRect(lineRect, new Color(0.15f, 0.15f, 0.15f));
        GUILayout.Space(10);

        // Draw hierarchy starting with 0 indentation
        DrawHierarchy(rootFolder, 0);

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndScrollView();

        // 3. Footer
        EditorGUILayout.BeginVertical(EditorStyles.inspectorFullWidthMargins);
        if (GUILayout.Button("FINALIZE AND CREATE IN PROJECT", GUILayout.Height(35)))
        {
            ProcessInjection();
        }
        EditorGUILayout.Space(5);
        EditorGUILayout.EndVertical();
    }

    private void UpdateRootName()
    {
        if (targetUnityFolder != null)
            rootFolder.name = targetUnityFolder.name;
        else
            rootFolder.name = "No Folder Selected";
    }

    private void DrawInbox()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("AVAILABLE SCRIPTS & FOLDERS", EditorStyles.miniBoldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Add Script...", EditorStyles.miniButton, GUILayout.Width(90)))
        {
            string p = EditorUtility.OpenFilePanel("Select C# Script", "", "cs");
            if (!string.IsNullOrEmpty(p) && !unassignedScripts.Contains(p)) unassignedScripts.Add(p);
        }
        if (GUILayout.Button("Add Folder...", EditorStyles.miniButton, GUILayout.Width(90)))
        {
            string p = EditorUtility.OpenFolderPanel("Select Folder Containing Scripts", "", "");
            if (!string.IsNullOrEmpty(p)) unassignedFolders.Add(BuildFolderNodeFromDisk(p));
        }
        if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(45)))
        {
            unassignedScripts.Clear();
            unassignedFolders.Clear();
        }
        EditorGUILayout.EndHorizontal();

        if (unassignedScripts.Count == 0 && unassignedFolders.Count == 0)
            GUILayout.Label("Drag scripts or folders here from your file explorer (Explorer / Finder)...", EditorStyles.centeredGreyMiniLabel);

        // Folders first
        for (int i = 0; i < unassignedFolders.Count; i++)
        {
            FolderNode f = unassignedFolders[i];
            Rect rect = EditorGUILayout.GetControlRect(false, 18);
            int scriptCount = CountScripts(f);
            GUIContent content = new GUIContent(
                $"{f.name}  ({scriptCount} script{(scriptCount == 1 ? "" : "s")})",
                EditorGUIUtility.IconContent("Folder Icon").image);

            Rect labelRect = new Rect(rect.x, rect.y, rect.width - 25, rect.height);
            Rect btnRect = new Rect(rect.x + rect.width - 20, rect.y, 20, rect.height);

            GUI.Label(labelRect, content);
            if (GUI.Button(btnRect, "x", EditorStyles.miniLabel)) {
                unassignedFolders.RemoveAt(i);
                break;
            }
            HandleFolderDragStart(labelRect, f);
        }

        // Loose scripts
        for (int i = 0; i < unassignedScripts.Count; i++)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 18);
            GUIContent content = new GUIContent(Path.GetFileName(unassignedScripts[i]), EditorGUIUtility.IconContent("cs Script Icon").image);

            Rect labelRect = new Rect(rect.x, rect.y, rect.width - 25, rect.height);
            Rect btnRect = new Rect(rect.x + rect.width - 20, rect.y, 20, rect.height);

            GUI.Label(labelRect, content);
            if (GUI.Button(btnRect, "x", EditorStyles.miniLabel)) {
                unassignedScripts.RemoveAt(i);
                break;
            }
            HandleScriptDragStart(labelRect, unassignedScripts[i], true);
        }

        EditorGUILayout.EndVertical();

        // The whole inbox box is a valid drop target for files/folders dragged in from the OS.
        Rect inboxRect = GUILayoutUtility.GetLastRect();
        HandleExternalDrop(inboxRect, null);
    }

    // Returns this node's own header rect, so callers can position remove buttons correctly
    // even when the node has nested children drawn beneath it.
    private Rect DrawHierarchy(FolderNode node, int indentLevel)
    {
        float indentSpace = indentLevel * 16f;
        Rect folderRect = EditorGUILayout.GetControlRect(false, 18);

        // 1. Foldout Arrow (Manually placed based on indent)
        node.isExpanded = EditorGUI.Foldout(new Rect(folderRect.x + indentSpace, folderRect.y, 20, folderRect.height), node.isExpanded, "", true);

        // 2. Folder Icon
        GUI.DrawTexture(new Rect(folderRect.x + indentSpace + 15, folderRect.y, 16, 16), EditorGUIUtility.IconContent("Folder Icon").image);

        // 3. Name Field
        Rect nameRect = new Rect(folderRect.x + indentSpace + 35, folderRect.y, folderRect.width - indentSpace - 85, folderRect.height);
        if (node == rootFolder)
        {
            GUIStyle rootStyle = new GUIStyle(EditorStyles.boldLabel);
            if (targetUnityFolder == null) rootStyle.normal.textColor = Color.red;
            GUI.Label(nameRect, node.name, rootStyle);
        }
        else
        {
            node.name = EditorGUI.TextField(nameRect, node.name, EditorStyles.label);
        }

        // 4. Action Buttons (+ and -)
        if (GUI.Button(new Rect(folderRect.x + folderRect.width - 40, folderRect.y, 20, folderRect.height), EditorGUIUtility.IconContent("Toolbar Plus"), EditorStyles.label))
            node.subFolders.Add(new FolderNode("New Folder"));

        if (node.isExpanded)
        {
            // Draw Scripts
            for (int i = 0; i < node.scriptsInside.Count; i++)
            {
                Rect sRect = EditorGUILayout.GetControlRect(false, 18);
                float scriptIndent = indentSpace + 30f;
                Rect labelRect = new Rect(sRect.x + scriptIndent, sRect.y, sRect.width - scriptIndent - 25, sRect.height);

                GUIContent sContent = new GUIContent(Path.GetFileName(node.scriptsInside[i]), EditorGUIUtility.IconContent("cs Script Icon").image);
                GUI.Label(labelRect, sContent, EditorStyles.miniLabel);

                if (GUI.Button(new Rect(sRect.x + sRect.width - 20, sRect.y, 20, sRect.height), "x", EditorStyles.miniLabel)) {
                    node.scriptsInside.RemoveAt(i);
                    break;
                }
                HandleScriptDragStart(new Rect(sRect.x + scriptIndent, sRect.y, sRect.width, sRect.height), node.scriptsInside[i], false, node);
            }

            // Draw Subfolders
            for (int i = 0; i < node.subFolders.Count; i++)
            {
                FolderNode sub = node.subFolders[i];
                Rect subHeaderRect = DrawHierarchy(sub, indentLevel + 1);

                if (GUI.Button(new Rect(subHeaderRect.x + subHeaderRect.width - 18, subHeaderRect.y, 20, 18), EditorGUIUtility.IconContent("Toolbar Minus"), EditorStyles.label))
                {
                    node.subFolders.RemoveAt(i);
                    break;
                }
            }

            HandleInternalDrop(folderRect, node);
            HandleExternalDrop(folderRect, node);
        }

        return folderRect;
    }

    // --- Internal Drag & Drop (reordering scripts/folders already inside this tool) ---
    private void HandleScriptDragStart(Rect rect, string path, bool fromInbox, FolderNode source = null)
    {
        Event evt = Event.current;
        if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition))
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData("internal", true);
            DragAndDrop.SetGenericData("kind", "script");
            DragAndDrop.SetGenericData("path", path);
            DragAndDrop.SetGenericData("fromInbox", fromInbox);
            if (source != null) DragAndDrop.SetGenericData("source", source);
            DragAndDrop.StartDrag(Path.GetFileName(path));
            evt.Use();
        }
    }

    private void HandleFolderDragStart(Rect rect, FolderNode folder)
    {
        Event evt = Event.current;
        if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition))
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData("internal", true);
            DragAndDrop.SetGenericData("kind", "folder");
            DragAndDrop.SetGenericData("folder", folder);
            DragAndDrop.StartDrag(folder.name);
            evt.Use();
        }
    }

    private void HandleInternalDrop(Rect rect, FolderNode target)
    {
        Event evt = Event.current;
        if (!rect.Contains(evt.mousePosition)) return;
        if (DragAndDrop.GetGenericData("internal") == null) return; // not one of our own drags

        if (evt.type == EventType.DragUpdated)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            evt.Use();
        }
        else if (evt.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            string kind = DragAndDrop.GetGenericData("kind") as string;

            if (kind == "folder")
            {
                FolderNode dragged = DragAndDrop.GetGenericData("folder") as FolderNode;
                if (dragged != null && dragged != target)
                {
                    unassignedFolders.Remove(dragged);
                    if (!target.subFolders.Contains(dragged)) target.subFolders.Add(dragged);
                }
            }
            else
            {
                string path = DragAndDrop.GetGenericData("path") as string;
                if (!string.IsNullOrEmpty(path))
                {
                    bool fromInbox = DragAndDrop.GetGenericData("fromInbox") is bool b && b;
                    if (fromInbox) unassignedScripts.Remove(path);
                    FolderNode source = DragAndDrop.GetGenericData("source") as FolderNode;
                    if (source != null) source.scriptsInside.Remove(path);
                    if (!target.scriptsInside.Contains(path)) target.scriptsInside.Add(path);
                }
            }
            evt.Use();
        }
    }

    // --- External Drag & Drop (files/folders dragged in from the OS file explorer) ---
    private void HandleExternalDrop(Rect rect, FolderNode targetFolder)
    {
        Event evt = Event.current;
        if (!rect.Contains(evt.mousePosition)) return;
        if (DragAndDrop.GetGenericData("internal") != null) return; // let HandleInternalDrop own this
        if (DragAndDrop.paths == null || DragAndDrop.paths.Length == 0) return;

        if (evt.type == EventType.DragUpdated)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            evt.Use();
        }
        else if (evt.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();

            foreach (string rawPath in DragAndDrop.paths)
            {
                if (Directory.Exists(rawPath))
                {
                    FolderNode imported = BuildFolderNodeFromDisk(rawPath);
                    if (targetFolder != null) targetFolder.subFolders.Add(imported);
                    else unassignedFolders.Add(imported);
                }
                else if (File.Exists(rawPath) && string.Equals(Path.GetExtension(rawPath), ".cs", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (targetFolder != null)
                    {
                        if (!targetFolder.scriptsInside.Contains(rawPath)) targetFolder.scriptsInside.Add(rawPath);
                    }
                    else if (!unassignedScripts.Contains(rawPath))
                    {
                        unassignedScripts.Add(rawPath);
                    }
                }
            }
            evt.Use();
            Repaint();
        }
    }

    // Recursively rebuilds a FolderNode tree from a real directory, preserving its structure.
    // Skips common non-source directories (.git, bin, obj, Library, etc.).
    private FolderNode BuildFolderNodeFromDisk(string folderPath)
    {
        string cleanPath = folderPath.TrimEnd('/', '\\');
        FolderNode node = new FolderNode(new DirectoryInfo(cleanPath).Name);

        try
        {
            foreach (string file in Directory.GetFiles(cleanPath, "*.cs"))
                node.scriptsInside.Add(file);

            foreach (string dir in Directory.GetDirectories(cleanPath))
            {
                string dirName = new DirectoryInfo(dir).Name;
                if (IgnoredFolderNames.Contains(dirName)) continue;
                node.subFolders.Add(BuildFolderNodeFromDisk(dir));
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Script Injector] Couldn't fully read folder '{cleanPath}': {e.Message}");
        }

        return node;
    }

    // --- Injection Methods (Unchanged logic — works the same for imported or manually-built trees) ---
    private void ProcessInjection() {
        if (targetUnityFolder == null) { EditorUtility.DisplayDialog("Injection Failed", "Select a target folder!", "OK"); return; }
        if (CountScripts(rootFolder) == 0) { EditorUtility.DisplayDialog("Injection Failed", "No scripts assigned to folders!", "OK"); return; }
        string basePath = AssetDatabase.GetAssetPath(targetUnityFolder);
        InjectRecursive(rootFolder, basePath);
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Success", "Scripts injected!", "Awesome");
    }

    private int CountScripts(FolderNode node) {
        int count = node.scriptsInside.Count;
        foreach (var sub in node.subFolders) count += CountScripts(sub);
        return count;
    }

    private void InjectRecursive(FolderNode node, string currentPath) {
        string path = (node == rootFolder) ? currentPath : Path.Combine(currentPath, node.name);
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        foreach (string s in node.scriptsInside) File.Copy(s, Path.Combine(path, Path.GetFileName(s)), true);
        foreach (var sub in node.subFolders) InjectRecursive(sub, path);
    }
}