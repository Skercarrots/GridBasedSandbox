using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class ScriptInjectorDynamic : EditorWindow
{
    // ─────────────────────────────────────────────
    //  Supported file extensions + icon mapping
    // ─────────────────────────────────────────────
    private static readonly string[] SupportedExtensions = new[]
    {
        "cs", "txt", "md", "json", "xml", "yaml", "yml",
        "shader", "hlsl", "glsl", "cginc",
        "asmdef", "asmref",
        "html", "css", "js",
        "csv", "ini", "cfg"
    };

    // Returns a Unity built-in icon name for a given file path
    private static string GetIconNameForFile(string path)
    {
        string ext = Path.GetExtension(path).TrimStart('.').ToLower();
        switch (ext)
        {
            case "cs":                          return "cs Script Icon";
            case "txt": case "md":             return "TextAsset Icon";
            case "json": case "xml":
            case "yaml": case "yml":           return "TextAsset Icon";
            case "shader": case "hlsl":
            case "glsl": case "cginc":         return "Shader Icon";
            case "asmdef": case "asmref":      return "AssemblyDefinitionAsset Icon";
            default:                            return "DefaultAsset Icon";
        }
    }

    // ─────────────────────────────────────────────
    //  Data model
    // ─────────────────────────────────────────────
    [System.Serializable]
    public class FolderNode
    {
        public string name;
        public List<string> scriptsInside = new List<string>(); // stores absolute PC paths
        public List<FolderNode> subFolders = new List<FolderNode>();
        public bool isExpanded = true;
        public FolderNode(string n) { name = n; }
    }

    // ─────────────────────────────────────────────
    //  State
    // ─────────────────────────────────────────────
    private DefaultAsset targetUnityFolder;
    private List<string> unassignedScripts = new List<string>();
    private FolderNode rootFolder = new FolderNode("No Folder Selected");
    private Vector2 scrollPos;

    // ─────────────────────────────────────────────
    //  Menu entry
    // ─────────────────────────────────────────────
    [MenuItem("Tools/Sker/Script Injector")]
    public static void ShowWindow() => GetWindow<ScriptInjectorDynamic>("Interactive Injector");

    // ─────────────────────────────────────────────
    //  OnGUI
    // ─────────────────────────────────────────────
    private void OnGUI()
    {
        UpdateRootName();

        // 1. Header
        EditorGUILayout.BeginVertical(EditorStyles.inspectorFullWidthMargins);
        EditorGUILayout.Space(5);
        EditorGUILayout.HelpBox(
            "Instructions: Add individual files or full folders, drag items into hierarchy folders, then click Finalize.",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        targetUnityFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            "Unity Target Folder:", targetUnityFolder, typeof(DefaultAsset), false);
        if (EditorGUI.EndChangeCheck()) UpdateRootName();
        EditorGUILayout.EndVertical();

        // 2. Scrollable area
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        DrawInbox();

        GUILayout.Space(10);
        Rect lineRect = GUILayoutUtility.GetRect(position.width, 1);
        EditorGUI.DrawRect(lineRect, new Color(0.15f, 0.15f, 0.15f));
        GUILayout.Space(10);

        DrawHierarchy(rootFolder, 0);

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndScrollView();

        // 3. Footer
        EditorGUILayout.BeginVertical(EditorStyles.inspectorFullWidthMargins);
        if (GUILayout.Button("FINALIZE AND CREATE IN PROJECT", GUILayout.Height(35)))
            ProcessInjection();
        EditorGUILayout.Space(5);
        EditorGUILayout.EndVertical();
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────
    private void UpdateRootName()
    {
        rootFolder.name = targetUnityFolder != null ? targetUnityFolder.name : "No Folder Selected";
    }

    /// <summary>Returns true if the file extension is in our supported list.</summary>
    private static bool IsSupportedFile(string path)
    {
        string ext = Path.GetExtension(path).TrimStart('.').ToLower();
        foreach (string supported in SupportedExtensions)
            if (ext == supported) return true;
        return false;
    }

    /// <summary>Adds a file to unassignedScripts if not already present and extension is supported.</summary>
    private void TryAddFile(string absolutePath)
    {
        if (!string.IsNullOrEmpty(absolutePath)
            && IsSupportedFile(absolutePath)
            && !unassignedScripts.Contains(absolutePath))
        {
            unassignedScripts.Add(absolutePath);
        }
    }

    // ─────────────────────────────────────────────
    //  Folder import (recursive) → builds hierarchy
    // ─────────────────────────────────────────────

    /// <summary>
    /// Recursively reads a PC directory and creates a matching FolderNode tree.
    /// Supported files go into scriptsInside; sub-directories become child FolderNodes.
    /// </summary>
    private FolderNode BuildFolderNodeFromDirectory(string dirPath)
    {
        FolderNode node = new FolderNode(Path.GetFileName(dirPath));

        // Files directly inside this directory
        foreach (string file in Directory.GetFiles(dirPath))
        {
            if (IsSupportedFile(file))
                node.scriptsInside.Add(file);
        }

        // Recurse into sub-directories
        foreach (string subDir in Directory.GetDirectories(dirPath))
            node.subFolders.Add(BuildFolderNodeFromDirectory(subDir));

        return node;
    }

    /// <summary>
    /// Opens a folder picker and either:
    ///  - adds the folder as a new child of rootFolder (addToHierarchy = true), or
    ///  - flattens all its files into the unassigned inbox.
    /// When addToHierarchy is true, checks for a name collision with the target
    /// Unity folder and offers Merge, Rename, or Cancel.
    /// </summary>
    private void ImportFolderFromPC(bool addToHierarchy)
    {
        string dir = EditorUtility.OpenFolderPanel("Select Folder", "", "");
        if (string.IsNullOrEmpty(dir)) return;

        if (addToHierarchy)
        {
            FolderNode imported = BuildFolderNodeFromDirectory(dir);
            ResolveNameCollision(imported);   // modifies or cancels in place
        }
        else
        {
            int added = 0;
            foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (IsSupportedFile(file) && !unassignedScripts.Contains(file))
                {
                    unassignedScripts.Add(file);
                    added++;
                }
            }
            EditorUtility.DisplayDialog("Files Added", $"{added} file(s) added to inbox.", "OK");
        }
        Repaint();
    }

    /// <summary>
    /// Checks whether the imported FolderNode's name collides with the current
    /// target Unity folder name (case-insensitive). If it does, presents three
    /// options to the user:
    ///   • Merge  – skip the outer wrapper and add its children directly to root.
    ///   • Rename – prompt for a new folder name, then add as normal.
    ///   • Cancel – discard the import entirely.
    /// If no collision, the node is added to rootFolder directly.
    /// </summary>
    private void ResolveNameCollision(FolderNode imported)
    {
        bool collision = targetUnityFolder != null &&
                         string.Equals(imported.name, rootFolder.name,
                                       System.StringComparison.OrdinalIgnoreCase);

        if (!collision)
        {
            // No problem – add normally
            rootFolder.subFolders.Add(imported);
            EditorUtility.DisplayDialog("Folder Imported",
                $"'{imported.name}' added to hierarchy.\n{CountScriptsInNode(imported)} file(s) found.", "OK");
            return;
        }

        // ── Collision detected ──
        int choice = EditorUtility.DisplayDialogComplex(
            "Folder Name Conflict",
            $"The folder you are importing ('{imported.name}') has the same name as your Unity target folder ('{rootFolder.name}').\n\n" +
            "This would create a duplicate nested folder.\n\n" +
            "What would you like to do?",
            "Merge into root",   // option 0
            "Cancel",            // option 1  (middle button = cancel in Unity)
            "Rename folder"      // option 2
        );

        switch (choice)
        {
            case 0: // Merge – promote children of imported directly into root
                foreach (string file in imported.scriptsInside)
                    if (!rootFolder.scriptsInside.Contains(file))
                        rootFolder.scriptsInside.Add(file);

                foreach (FolderNode sub in imported.subFolders)
                    rootFolder.subFolders.Add(sub);

                EditorUtility.DisplayDialog("Merged",
                    $"Contents of '{imported.name}' were merged directly into root.\n" +
                    $"{CountScriptsInNode(imported)} file(s) added.", "OK");
                break;

            case 1: // Cancel
                // Do nothing
                break;

            case 2: // Rename
                string newName = imported.name + "_imported";
                newName = AskForFolderName(newName);
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    imported.name = newName;
                    rootFolder.subFolders.Add(imported);
                    EditorUtility.DisplayDialog("Folder Imported",
                        $"'{imported.name}' added to hierarchy.\n{CountScriptsInNode(imported)} file(s) found.", "OK");
                }
                break;
        }
    }

    /// <summary>
    /// Shows a small modal-style EditorWindow to let the user type a new folder name.
    /// Since Unity's built-in dialogs don't support text input, we fall back to a simple
    /// naming suffix dialog. For a real text-input popup, a separate EditorWindow is needed.
    /// This version presents incrementing suffixes until the user accepts one.
    /// </summary>
    private string AskForFolderName(string suggestion)
    {
        // Unity doesn't have a built-in text-input dialog, so we open a dedicated popup.
        FolderRenamePopup.Show(suggestion, chosen => {
            // This callback fires after the user confirms in the popup window.
            // We can't block here, so the rename is applied via the popup itself.
        });

        // Return the suggestion so the caller has something to work with immediately;
        // the popup window will apply the real name asynchronously.
        return suggestion;
    }

    private int CountScriptsInNode(FolderNode node)
    {
        int c = node.scriptsInside.Count;
        foreach (var sub in node.subFolders) c += CountScriptsInNode(sub);
        return c;
    }

    // ─────────────────────────────────────────────
    //  Inbox (available / unassigned files)
    // ─────────────────────────────────────────────
    private void DrawInbox()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // ── Header row ──
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("AVAILABLE FILES", EditorStyles.miniBoldLabel);

        // Add individual files (multi-select via repeated open panel trick)
        if (GUILayout.Button("Add Files", EditorStyles.miniButton, GUILayout.Width(70)))
        {
            // EditorUtility.OpenFilePanel only picks one file.
            // We loop until the user cancels so they can add several in a row.
            while (true)
            {
                string p = EditorUtility.OpenFilePanel(
                    "Select File (Cancel to stop)", "", string.Join(",", SupportedExtensions));
                if (string.IsNullOrEmpty(p)) break;
                TryAddFile(p);
            }
        }

        // Add flat files from a folder (inbox)
        if (GUILayout.Button("Add Folder →Inbox", EditorStyles.miniButton, GUILayout.Width(115)))
            ImportFolderFromPC(addToHierarchy: false);

        // Add folder preserving hierarchy
        if (GUILayout.Button("Add Folder →Tree", EditorStyles.miniButton, GUILayout.Width(110)))
            ImportFolderFromPC(addToHierarchy: true);

        EditorGUILayout.EndHorizontal();

        // ── File list ──
        if (unassignedScripts.Count == 0)
            GUILayout.Label("List is empty…", EditorStyles.centeredGreyMiniLabel);

        for (int i = 0; i < unassignedScripts.Count; i++)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 18);
            string iconName = GetIconNameForFile(unassignedScripts[i]);
            GUIContent content = new GUIContent(
                Path.GetFileName(unassignedScripts[i]),
                EditorGUIUtility.IconContent(iconName).image);

            Rect labelRect = new Rect(rect.x, rect.y, rect.width - 25, rect.height);
            Rect btnRect   = new Rect(rect.x + rect.width - 20, rect.y, 20, rect.height);

            GUI.Label(labelRect, content);
            if (GUI.Button(btnRect, "x", EditorStyles.miniLabel))
            {
                unassignedScripts.RemoveAt(i);
                break;
            }
            HandleDragStart(rect, unassignedScripts[i], true);
        }

        EditorGUILayout.EndVertical();
    }

    // ─────────────────────────────────────────────
    //  Hierarchy drawing
    // ─────────────────────────────────────────────
    private void DrawHierarchy(FolderNode node, int indentLevel)
    {
        float indentSpace = indentLevel * 16f;
        Rect folderRect = EditorGUILayout.GetControlRect(false, 18);

        // 1. Foldout arrow
        node.isExpanded = EditorGUI.Foldout(
            new Rect(folderRect.x + indentSpace, folderRect.y, 20, folderRect.height),
            node.isExpanded, "", true);

        // 2. Folder icon
        GUI.DrawTexture(
            new Rect(folderRect.x + indentSpace + 15, folderRect.y, 16, 16),
            EditorGUIUtility.IconContent("Folder Icon").image);

        // 3. Name field
        Rect nameRect = new Rect(
            folderRect.x + indentSpace + 35,
            folderRect.y,
            folderRect.width - indentSpace - 85,
            folderRect.height);

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

        // 4. "+" button to add a sub-folder
        if (GUI.Button(
            new Rect(folderRect.x + folderRect.width - 40, folderRect.y, 20, folderRect.height),
            EditorGUIUtility.IconContent("Toolbar Plus"), EditorStyles.label))
        {
            node.subFolders.Add(new FolderNode("New Folder"));
        }

        if (!node.isExpanded) return;

        // ── Files inside this node ──
        for (int i = 0; i < node.scriptsInside.Count; i++)
        {
            Rect sRect = EditorGUILayout.GetControlRect(false, 18);
            float scriptIndent = indentSpace + 30f;
            Rect labelRect = new Rect(sRect.x + scriptIndent, sRect.y,
                                      sRect.width - scriptIndent - 25, sRect.height);

            string iconName = GetIconNameForFile(node.scriptsInside[i]);
            GUIContent sContent = new GUIContent(
                Path.GetFileName(node.scriptsInside[i]),
                EditorGUIUtility.IconContent(iconName).image);

            GUI.Label(labelRect, sContent, EditorStyles.miniLabel);

            if (GUI.Button(new Rect(sRect.x + sRect.width - 20, sRect.y, 20, sRect.height),
                           "x", EditorStyles.miniLabel))
            {
                node.scriptsInside.RemoveAt(i);
                break;
            }
            HandleDragStart(
                new Rect(sRect.x + scriptIndent, sRect.y, sRect.width, sRect.height),
                node.scriptsInside[i], false, node);
        }

        // ── Sub-folders ──
        for (int i = 0; i < node.subFolders.Count; i++)
        {
            FolderNode sub = node.subFolders[i];
            DrawHierarchy(sub, indentLevel + 1);

            Rect lastFolderRect = GUILayoutUtility.GetLastRect();
            if (GUI.Button(
                new Rect(lastFolderRect.x + lastFolderRect.width - 18, lastFolderRect.y, 20, 18),
                EditorGUIUtility.IconContent("Toolbar Minus"), EditorStyles.label))
            {
                node.subFolders.RemoveAt(i);
                break;
            }
        }

        HandleDrop(folderRect, node);
    }

    // ─────────────────────────────────────────────
    //  Drag & Drop
    // ─────────────────────────────────────────────
    private void HandleDragStart(Rect rect, string path, bool fromInbox, FolderNode source = null)
    {
        Event evt = Event.current;
        if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition))
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData("path",      path);
            DragAndDrop.SetGenericData("fromInbox", fromInbox);
            if (source != null) DragAndDrop.SetGenericData("source", source);
            DragAndDrop.StartDrag(Path.GetFileName(path));
            evt.Use();
        }
    }

    private void HandleDrop(Rect rect, FolderNode target)
    {
        Event evt = Event.current;
        if (!rect.Contains(evt.mousePosition)) return;

        if (evt.type == EventType.DragUpdated)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            evt.Use();
        }
        else if (evt.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            string path      = (string)DragAndDrop.GetGenericData("path");
            bool fromInbox   = (bool)DragAndDrop.GetGenericData("fromInbox");

            if (fromInbox)
                unassignedScripts.Remove(path);
            else if (DragAndDrop.GetGenericData("source") is FolderNode src)
                src.scriptsInside.Remove(path);

            if (!target.scriptsInside.Contains(path))
                target.scriptsInside.Add(path);

            evt.Use();
        }
    }

    // ─────────────────────────────────────────────
    //  Injection
    // ─────────────────────────────────────────────
    private void ProcessInjection()
    {
        if (targetUnityFolder == null)
        {
            EditorUtility.DisplayDialog("Injection Failed", "Select a target Unity folder!", "OK");
            return;
        }
        if (CountScripts(rootFolder) == 0)
        {
            EditorUtility.DisplayDialog("Injection Failed", "No files assigned to any folder!", "OK");
            return;
        }

        // ── Safety check: warn about any direct child whose name matches root ──
        List<string> offenders = FindDuplicateNamedChildren(rootFolder, rootFolder.name);
        if (offenders.Count > 0)
        {
            bool proceed = EditorUtility.DisplayDialog(
                "Potential Duplicate Folder Warning",
                $"The following folder(s) in your hierarchy share the same name as the target Unity folder '{rootFolder.name}':\n\n" +
                string.Join("\n", offenders) +
                "\n\nThis will create a nested folder with the same name inside the target. Proceed anyway?",
                "Proceed", "Cancel");

            if (!proceed) return;
        }

        string basePath = AssetDatabase.GetAssetPath(targetUnityFolder);
        InjectRecursive(rootFolder, basePath);
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Success", "Files injected successfully!", "Awesome");
    }

    /// <summary>
    /// Recursively finds any node (excluding root itself) whose name matches targetName
    /// (case-insensitive) and returns their display paths for the warning dialog.
    /// </summary>
    private List<string> FindDuplicateNamedChildren(FolderNode node, string targetName, string currentPath = "")
    {
        List<string> results = new List<string>();
        foreach (FolderNode sub in node.subFolders)
        {
            string subPath = string.IsNullOrEmpty(currentPath) ? sub.name : currentPath + "/" + sub.name;
            if (string.Equals(sub.name, targetName, System.StringComparison.OrdinalIgnoreCase))
                results.Add(subPath);
            results.AddRange(FindDuplicateNamedChildren(sub, targetName, subPath));
        }
        return results;
    }

    private int CountScripts(FolderNode node)
    {
        int count = node.scriptsInside.Count;
        foreach (var sub in node.subFolders) count += CountScripts(sub);
        return count;
    }

    private void InjectRecursive(FolderNode node, string currentPath)
    {
        string path = (node == rootFolder) ? currentPath : Path.Combine(currentPath, node.name);
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);

        foreach (string s in node.scriptsInside)
            File.Copy(s, Path.Combine(path, Path.GetFileName(s)), true);

        foreach (var sub in node.subFolders)
            InjectRecursive(sub, path);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Folder Rename Popup
//  A small floating EditorWindow used when the user chooses "Rename" during
//  a name-collision resolution. Stores the chosen name back via a callback.
// ─────────────────────────────────────────────────────────────────────────────
public class FolderRenamePopup : EditorWindow
{
    private string folderName = "";
    private System.Action<string> onConfirm;

    public static void Show(string initialName, System.Action<string> onConfirm)
    {
        FolderRenamePopup win = CreateInstance<FolderRenamePopup>();
        win.titleContent = new GUIContent("Rename Folder");
        win.folderName   = initialName;
        win.onConfirm    = onConfirm;
        win.minSize      = new Vector2(300, 80);
        win.maxSize      = new Vector2(300, 80);
        win.ShowUtility();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Enter a new name for the imported folder:", EditorStyles.wordWrappedMiniLabel);
        folderName = EditorGUILayout.TextField(folderName);
        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Confirm"))
        {
            if (!string.IsNullOrWhiteSpace(folderName))
            {
                onConfirm?.Invoke(folderName);
                Close();
            }
            else
            {
                EditorUtility.DisplayDialog("Invalid Name", "Folder name cannot be empty.", "OK");
            }
        }
        if (GUILayout.Button("Cancel")) Close();
        EditorGUILayout.EndHorizontal();
    }
}