using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Handles persistent storage of user Python scripts between game sessions.
/// Scripts are saved as plain .py files in Application.persistentDataPath/UserScripts.
/// </summary>
public static class ScriptStorageManager
{
    private static string _scriptsDirectory;

    public static string ScriptsDirectory
    {
        get
        {
            if (string.IsNullOrEmpty(_scriptsDirectory))
            {
                _scriptsDirectory = Path.Combine(Application.persistentDataPath, "UserScripts");
                if (!Directory.Exists(_scriptsDirectory))
                {
                    Directory.CreateDirectory(_scriptsDirectory);
                    SeedStarterScripts();
                }
            }
            return _scriptsDirectory;
        }
    }

    /// <summary>
    /// Returns a list of all saved Python script filenames (e.g. ["main.py", "patrol.py"]).
    /// </summary>
    public static List<string> GetAllScripts()
    {
        var result = new List<string>();
        try
        {
            var dir = ScriptsDirectory;
            var files = Directory.GetFiles(dir, "*.py");
            foreach (var f in files)
            {
                result.Add(Path.GetFileName(f));
            }

            if (result.Count == 0)
            {
                SeedStarterScripts();
                files = Directory.GetFiles(dir, "*.py");
                foreach (var f in files)
                    result.Add(Path.GetFileName(f));
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ScriptStorageManager] Failed to list scripts: {e.Message}");
        }
        return result;
    }

    /// <summary>
    /// Loads the text content of a saved script.
    /// </summary>
    public static string LoadScript(string fileName)
    {
        try
        {
            if (!fileName.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                fileName += ".py";

            string path = Path.Combine(ScriptsDirectory, fileName);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ScriptStorageManager] Failed to load script '{fileName}': {e.Message}");
        }
        return string.Empty;
    }

    /// <summary>
    /// Saves script text content to disk.
    /// </summary>
    public static bool SaveScript(string fileName, string content)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "main.py";

            if (!fileName.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                fileName += ".py";

            string path = Path.Combine(ScriptsDirectory, fileName);
            File.WriteAllText(path, content ?? string.Empty);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ScriptStorageManager] Failed to save script '{fileName}': {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Deletes a script file from disk.
    /// </summary>
    public static bool DeleteScript(string fileName)
    {
        try
        {
            if (!fileName.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                fileName += ".py";

            string path = Path.Combine(ScriptsDirectory, fileName);
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ScriptStorageManager] Failed to delete script '{fileName}': {e.Message}");
        }
        return false;
    }

    /// <summary>
    /// Creates a unique new script filename (e.g. script_1.py, script_2.py).
    /// </summary>
    public static string GetNextAvailableScriptName(string baseName = "script")
    {
        string dir = ScriptsDirectory;
        int index = 1;
        while (true)
        {
            string candidate = $"{baseName}_{index}.py";
            if (!File.Exists(Path.Combine(dir, candidate)))
                return candidate;
            index++;
        }
    }

    private static void SeedStarterScripts()
    {
        try
        {
            string mainPath = Path.Combine(_scriptsDirectory, "main.py");
            if (!File.Exists(mainPath))
            {
                File.WriteAllText(mainPath,
@"# main.py — Basic Robot Controller
# Interacts with the active robot or device in the sandbox.

print('Initializing robot...')
robot.report('state', 'ready')

# Basic movement pattern
for i in range(4):
    robot.move(2)
    robot.turn_left()
    print(f'Completed segment {i + 1}/4')

robot.report('state', 'idle')
print('Mission finished!')
");
            }

            string patrolPath = Path.Combine(_scriptsDirectory, "patrol.py");
            if (!File.Exists(patrolPath))
            {
                File.WriteAllText(patrolPath,
@"# patrol.py — Continuous Perimeter Patrol
import random

print('Starting perimeter patrol...')
robot.report('status', 'patrolling')

steps = 0
while steps < 12:
    robot.move(1)
    steps += 1
    if steps % 3 == 0:
        robot.turn_right()
        print(f'Turned corner. Step: {steps}')

print('Patrol cycle complete.')
");
            }

            string miningPath = Path.Combine(_scriptsDirectory, "mining_robot.py");
            if (!File.Exists(miningPath))
            {
                File.WriteAllText(miningPath,
@"# mining_robot.py — Forward Excavation Routine
print('Scanning terrain for mining...')
robot.report('mode', 'mining')

# Dig forward
for distance in range(5):
    robot.move(1)
    print(f'Advanced forward to block {distance + 1}')

robot.report('mode', 'done')
print('Mining corridor complete.')
");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ScriptStorageManager] Could not seed starter scripts: {e.Message}");
        }
    }
}
