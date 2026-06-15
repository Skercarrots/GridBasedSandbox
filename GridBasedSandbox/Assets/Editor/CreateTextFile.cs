#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class CreateTextFile
{
    [MenuItem("Assets/Create/New Text File", priority = 100)]
    private static void CreateNewTextFile()
    {
        // Pega o caminho da pasta selecionada no Project Window
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        
        // Se nada estiver selecionado, usa a raiz da pasta Assets
        if (string.IsNullOrEmpty(path))
        {
            path = "Assets";
        }
        else if (!Directory.Exists(path))
        {
            path = Path.GetDirectoryName(path);
        }

        string fullPath = Path.Combine(path, "NewTextFile.txt");
        
        // Evita sobrescrever se o arquivo já existir
        int i = 0;
        string tempPath = fullPath;
        while (File.Exists(tempPath))
        {
            i++;
            tempPath = Path.Combine(path, $"NewTextFile {i}.txt");
        }
        fullPath = tempPath;

        File.WriteAllText(fullPath, "Novo arquivo de texto criado.");
        AssetDatabase.Refresh();
    }
}
#endif