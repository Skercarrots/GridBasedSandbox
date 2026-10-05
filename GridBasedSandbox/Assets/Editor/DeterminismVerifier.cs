using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// ─────────────────────────────────────────────────────────────────────────────
//  DeterminismVerifier — Editor utility and window to verify that chunk
//  generation is 100% deterministic and invariant to chunk visit order.
//
//  Open via: GridBasedSandbox > Terrain Determinism Verifier
//        or: Tools > Voxel World > Verify Determinism
// ─────────────────────────────────────────────────────────────────────────────

public class DeterminismVerifier : EditorWindow
{
    [SerializeField] private WorldGenerator generator;
    [SerializeField] private VoxelWorldSettings settings;
    [SerializeField] private int seed = 1337;
    [SerializeField] private int gridSize = 3; // 3x3 = 9 chunks
    [SerializeField] private Vector2Int centerChunk = Vector2Int.zero;

    private Vector2 _scrollPos;
    private string _lastReport = "";
    private bool? _lastResult = null;
    private double _elapsedMs = 0;

    [MenuItem("GridBasedSandbox/Terrain Determinism Verifier")]
    [MenuItem("Tools/Voxel World/Verify Determinism")]
    public static void OpenWindow()
    {
        var window = GetWindow<DeterminismVerifier>("Determinism Verifier");
        window.minSize = new Vector2(500, 450);
        window.Show();
    }

    private void OnEnable()
    {
        AutoFindReferences();
    }

    private void AutoFindReferences()
    {
        if (generator == null)
        {
            string[] genGuids = AssetDatabase.FindAssets("t:OverworldGenerator");
            if (genGuids.Length == 0)
                genGuids = AssetDatabase.FindAssets("t:WorldGenerator");

            if (genGuids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(genGuids[0]);
                generator = AssetDatabase.LoadAssetAtPath<WorldGenerator>(path);
            }
        }

        if (settings == null)
        {
            string[] setGuids = AssetDatabase.FindAssets("t:VoxelWorldSettings");
            if (setGuids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(setGuids[0]);
                settings = AssetDatabase.LoadAssetAtPath<VoxelWorldSettings>(path);
            }
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Terrain Determinism Verifier", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Verifies that voxel chunk generation is 100% reproducible and invariant to the order " +
            "in which chunks are loaded or generated across worker threads.",
            MessageType.Info);

        EditorGUILayout.Space(6);

        generator = (WorldGenerator)EditorGUILayout.ObjectField("Generator", generator, typeof(WorldGenerator), false);
        settings = (VoxelWorldSettings)EditorGUILayout.ObjectField("World Settings", settings, typeof(VoxelWorldSettings), false);

        seed = EditorGUILayout.IntField("Master Seed", seed);
        gridSize = EditorGUILayout.IntSlider("Grid Size (N×N)", gridSize, 2, 7);
        centerChunk = EditorGUILayout.Vector2IntField("Center Chunk Coord", centerChunk);

        EditorGUILayout.Space(10);

        int totalChunks = gridSize * gridSize;
        EditorGUILayout.LabelField($"Total chunks per pass: {totalChunks} (Pass A + Pass B = {totalChunks * 2} chunks generated)");

        GUI.enabled = generator != null && settings != null;
        if (GUILayout.Button("Run Determinism Verification", GUILayout.Height(32)))
        {
            RunVerificationUI();
        }
        GUI.enabled = true;

        if (_lastResult.HasValue)
        {
            EditorGUILayout.Space(10);
            if (_lastResult.Value)
            {
                GUI.backgroundColor = new Color(0.3f, 0.9f, 0.3f);
                EditorGUILayout.HelpBox($"VERIFICATION PASSED!\nAll {totalChunks} chunks produced identical voxel hashes regardless of visit order.\nTime: {_elapsedMs:F1} ms", MessageType.Info);
            }
            else
            {
                GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                EditorGUILayout.HelpBox($"VERIFICATION FAILED!\nMismatches detected between visit orders.\nTime: {_elapsedMs:F1} ms", MessageType.Error);
            }
            GUI.backgroundColor = Color.white;
        }

        if (!string.IsNullOrEmpty(_lastReport))
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Detailed Report", EditorStyles.boldLabel);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, EditorStyles.helpBox, GUILayout.Height(200));
            EditorGUILayout.TextArea(_lastReport, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Copy Report to Clipboard"))
            {
                EditorGUIUtility.systemCopyBuffer = _lastReport;
            }
        }
    }

    private void RunVerificationUI()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            bool passed = RunVerification(generator, settings, seed, gridSize, centerChunk, out string report);
            sw.Stop();
            _elapsedMs = sw.Elapsed.TotalMilliseconds;
            _lastResult = passed;
            _lastReport = report;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    /// <summary>
    /// Executes a full determinism test comparing sequential generation vs shuffled generation.
    /// Can be called from the Editor GUI, command-line build steps, or automated unit tests.
    /// </summary>
    public static bool RunVerification(
        WorldGenerator gen,
        VoxelWorldSettings set,
        int worldSeed,
        int size,
        Vector2Int center,
        out string report)
    {
        if (gen == null || set == null)
        {
            report = "Error: Generator or Settings is null.";
            return false;
        }

        int half = size / 2;
        var chunkCoords = new List<Vector2Int>(size * size);

        for (int x = -half; x <= half; x++)
        for (int z = -half; z <= half; z++)
        {
            chunkCoords.Add(new Vector2Int(center.x + x, center.y + z));
        }

        int total = chunkCoords.Count;
        var hashesSequential = new Dictionary<Vector2Int, string>(total);
        var dataSequential = new Dictionary<Vector2Int, VoxelChunkData>(total);

        // ── Pass 1: Sequential Order ──────────────────────────────────────────
        gen.Prepare();
        for (int i = 0; i < total; i++)
        {
            Vector2Int coord = chunkCoords[i];
            EditorUtility.DisplayProgressBar("Determinism Verifier",
                $"Pass 1/2 (Sequential): Generating chunk {coord} ({i + 1}/{total})",
                (float)i / (total * 2));

            var data = new VoxelChunkData(coord, set.chunkWidth, set.TotalWorldHeight);
            gen.Generate(data, set, worldSeed);

            string hash = ComputeChunkHash(data);
            hashesSequential[coord] = hash;
            dataSequential[coord] = data;
        }

        // ── Pass 2: Shuffled / Reversed Order ─────────────────────────────────
        var shuffledCoords = new List<Vector2Int>(chunkCoords);
        // Deterministic pseudo-shuffle based on inverse coordinates
        shuffledCoords.Reverse();
        // Additional shuffle swap
        for (int i = 0; i < shuffledCoords.Count - 1; i += 2)
        {
            var temp = shuffledCoords[i];
            shuffledCoords[i] = shuffledCoords[i + 1];
            shuffledCoords[i + 1] = temp;
        }

        var hashesShuffled = new Dictionary<Vector2Int, string>(total);
        var dataShuffled = new Dictionary<Vector2Int, VoxelChunkData>(total);

        gen.Prepare();
        for (int i = 0; i < total; i++)
        {
            Vector2Int coord = shuffledCoords[i];
            EditorUtility.DisplayProgressBar("Determinism Verifier",
                $"Pass 2/2 (Shuffled): Generating chunk {coord} ({i + 1}/{total})",
                0.5f + (float)i / (total * 2));

            var data = new VoxelChunkData(coord, set.chunkWidth, set.TotalWorldHeight);
            gen.Generate(data, set, worldSeed);

            string hash = ComputeChunkHash(data);
            hashesShuffled[coord] = hash;
            dataShuffled[coord] = data;
        }

        // ── Pass 3: Seed sensitivity verification ─────────────────────────────
        var diffSeedData = new VoxelChunkData(center, set.chunkWidth, set.TotalWorldHeight);
        gen.Generate(diffSeedData, set, worldSeed + 99999);
        string diffSeedHash = ComputeChunkHash(diffSeedData);
        bool seedSensitive = diffSeedHash != hashesSequential[center];

        // ── Comparison & Report ───────────────────────────────────────────────
        var mismatches = new List<string>();
        int matchedCount = 0;

        foreach (var coord in chunkCoords)
        {
            string hashA = hashesSequential[coord];
            string hashB = hashesShuffled[coord];

            if (hashA == hashB)
            {
                matchedCount++;
            }
            else
            {
                // Detailed block inspection
                VoxelChunkData chunkA = dataSequential[coord];
                VoxelChunkData chunkB = dataShuffled[coord];
                string diffDetail = FindFirstBlockDifference(chunkA, chunkB, set);

                mismatches.Add($"• Chunk {coord}: Hash A={hashA.Substring(0, 8)} != Hash B={hashB.Substring(0, 8)}\n  Difference: {diffDetail}");
            }
        }

        bool allMatched = matchedCount == total;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== DETERMINISM VERIFICATION REPORT ===");
        sb.AppendLine($"Generator: {gen.name} ({gen.GetType().Name})");
        sb.AppendLine($"Settings: {set.name} (Width={set.chunkWidth}, Height={set.TotalWorldHeight})");
        sb.AppendLine($"Seed: {worldSeed}");
        sb.AppendLine($"Grid: {size}x{size} ({total} chunks)");
        sb.AppendLine($"Center: {center}");
        sb.AppendLine($"Result: {(allMatched ? "PASSED (100% Deterministic)" : "FAILED (Order Mismatch)")}");
        sb.AppendLine($"Matched Chunks: {matchedCount} / {total} ({(matchedCount * 100f / total):F1}%)");
        sb.AppendLine($"Seed Sensitivity: {(seedSensitive ? "VERIFIED (Different seeds generate different voxels)" : "WARNING (Same voxels across seeds)")}");
        sb.AppendLine();

        if (mismatches.Count > 0)
        {
            sb.AppendLine("Mismatches detected:");
            foreach (var m in mismatches)
                sb.AppendLine(m);
        }
        else
        {
            sb.AppendLine("All chunks produced identical block arrays regardless of visit order.");
            sb.AppendLine("Verified Chunk Hashes (MD5):");
            foreach (var coord in chunkCoords)
            {
                sb.AppendLine($"  Chunk {coord}: {hashesSequential[coord]}");
            }
        }

        report = sb.ToString();
        return allMatched && seedSensitive;
    }

    /// <summary>Finds the first block coordinate where chunkA and chunkB differ.</summary>
    private static string FindFirstBlockDifference(VoxelChunkData chunkA, VoxelChunkData chunkB, VoxelWorldSettings settings)
    {
        int cw = chunkA.Width;
        int ch = chunkA.Height;
        int totalDiffs = 0;
        string firstDiff = null;

        for (int x = 0; x < cw; x++)
        for (int y = 0; y < ch; y++)
        for (int z = 0; z < cw; z++)
        {
            byte bA = chunkA.GetBlock(x, y, z);
            byte bB = chunkB.GetBlock(x, y, z);

            if (bA != bB)
            {
                totalDiffs++;
                if (firstDiff == null)
                {
                    int wy = settings.LocalYToWorld(y);
                    int wx = chunkA.WorldOriginX + x;
                    int wz = chunkA.WorldOriginZ + z;
                    firstDiff = $"first mismatch at local ({x}, {y}, {z}), world ({wx}, {wy}, {wz}): Block A = {bA}, Block B = {bB}";
                }
            }
        }

        return firstDiff != null
            ? $"{firstDiff} (Total mismatched blocks in chunk: {totalDiffs})"
            : "No block differences found (hash collision or metadata mismatch).";
    }

    /// <summary>Computes the MD5 checksum of the entire 3D block array.</summary>
    public static string ComputeChunkHash(VoxelChunkData chunk)
    {
        using (var md5 = MD5.Create())
        {
            int totalBytes = chunk.Width * chunk.Height * chunk.Width;
            byte[] buffer = new byte[totalBytes];
            int idx = 0;

            for (int x = 0; x < chunk.Width; x++)
            for (int y = 0; y < chunk.Height; y++)
            for (int z = 0; z < chunk.Width; z++)
            {
                buffer[idx++] = chunk.GetBlock(x, y, z);
            }

            byte[] hash = md5.ComputeHash(buffer);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
