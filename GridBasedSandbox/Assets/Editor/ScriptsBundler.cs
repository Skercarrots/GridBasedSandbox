using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System.Collections.Generic;

public class ScriptsBundlerWindow : EditorWindow
{
    private DefaultAsset targetFolder;
    private string bundleResult = "";
    private Vector2 scrollPos;

    [MenuItem("Tools/Sker/Script Bundler")]
    public static void ShowWindow()
    {
        GetWindow<ScriptsBundlerWindow>("Script Bundler");
    }

    private void OnGUI()
    {
        GUILayout.Label("Bundle Scripts Utility", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Área de Seleção da Pasta
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("1. Drag a folder from Project tab here:", EditorStyles.label);
        
        targetFolder = (DefaultAsset)EditorGUILayout.ObjectField("Target Folder", targetFolder, typeof(DefaultAsset), false);
        
        if (targetFolder != null)
        {
            string path = AssetDatabase.GetAssetPath(targetFolder);
            if (!AssetDatabase.IsValidFolder(path))
            {
                EditorGUILayout.HelpBox("Please select a valid folder, not a file.", MessageType.Warning);
                targetFolder = null;
            }
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();

        // Botões de Ação
        EditorGUI.BeginDisabledGroup(targetFolder == null);
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

    private void ProcessScripts()
    {
        string folderPath = AssetDatabase.GetAssetPath(targetFolder);
        // Converte o path relativo da Unity para path absoluto do sistema
        string absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", folderPath));
        
        string[] scriptFiles = Directory.GetFiles(absolutePath, "*.cs", SearchOption.AllDirectories);

        if (scriptFiles.Length == 0)
        {
            bundleResult = "No .cs scripts found in the selected folder.";
            return;
        }

        StringBuilder sb = new StringBuilder();

        // Estrutura de Diretórios
        sb.AppendLine("==========================================");
        sb.AppendLine("DIRECTORY STRUCTURE");
        sb.AppendLine("==========================================");
        GenerateStructure(absolutePath, sb, 0);
        sb.AppendLine("\n");

        // Conteúdo dos Scripts
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

    private void GenerateStructure(string currentDir, StringBuilder sb, int indent)
    {
        string indentString = new string(' ', indent * 4);
        sb.AppendLine($"{indentString}[Dir] {Path.GetFileName(currentDir)}");

        foreach (string dir in Directory.GetDirectories(currentDir))
        {
            GenerateStructure(dir, sb, indent + 1);
        }

        foreach (string file in Directory.GetFiles(currentDir, "*.cs"))
        {
            sb.AppendLine($"{new string(' ', (indent + 1) * 4)}- {Path.GetFileName(file)}");
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