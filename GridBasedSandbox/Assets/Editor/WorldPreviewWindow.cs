using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  WorldPreviewWindow — Editor window for visualising terrain generation.
//  Open via: GridBasedSandbox > World Preview
//
//  FEATURES
//  • Top-down map rendering at configurable resolution (64–2048 blocks)
//  • Visualization modes: Height (grayscale), Biome (color), Climate axes,
//    Chaos mask, Surface Y (gradient)
//  • Pan (click+drag), Zoom (scroll wheel)
//  • Seed override field
//  • Background thread rendering with progress bar — no editor freeze
//  • X cross-section slice view to reveal terrain profile
//  • Double-click to log climate values at a point
//
//  IMPORTANT
//  This window calls OverworldGenerator and BiomeRegistry directly — it does
//  NOT enter Play Mode. To bake AnimationCurves it calls biomeRegistry.Initialize()
//  on the editor main thread before starting the background render.
// ─────────────────────────────────────────────────────────────────────────────

public class WorldPreviewWindow : EditorWindow
{
    // ── Config (shown in toolbar) ──────────────────────────────────────────────
    private OverworldGenerator _generator;
    private VoxelWorldSettings _settings;
    private int                _seed         = 12345;
    private int                _mapSize      = 512;  // blocks
    private VisualizationMode  _mode         = VisualizationMode.Height;

    // ── Cross-section slice ───────────────────────────────────────────────────
    private bool _showSlice;
    private int  _sliceX;

    // ── Navigation ────────────────────────────────────────────────────────────
    private Vector2 _panOffset   = Vector2.zero;
    private float   _zoom        = 1f;
    private bool    _dragging;
    private Vector2 _dragStart;
    private Vector2 _panAtDragStart;

    // ── Render state ──────────────────────────────────────────────────────────
    private Texture2D          _mapTex;
    private Texture2D          _sliceTex;
    private bool               _rendering;
    private float              _progress;
    private CancellationTokenSource _cts;

    // ── Biome color palette (index = position in biomeRegistry.Biomes) ────────
    private static readonly Color[] BiomePalette = new Color[]
    {
        new Color(0.35f, 0.75f, 0.35f, 1f), // 0 green  — plains
        new Color(0.20f, 0.55f, 0.20f, 1f), // 1 dark   — forest
        new Color(0.70f, 0.70f, 0.75f, 1f), // 2 grey   — mountains
        new Color(0.25f, 0.45f, 0.85f, 1f), // 3 blue   — ocean/beach
        new Color(0.60f, 0.40f, 0.80f, 1f), // 4 violet — fantasy highlands
        new Color(0.90f, 0.80f, 0.50f, 1f), // 5 sand   — dunes (original)
        new Color(0.80f, 0.80f, 0.80f, 1f), // 6 white
        new Color(0.60f, 0.30f, 0.10f, 1f), // 7 brown
        new Color(0.50f, 0.80f, 0.90f, 1f), // 8 cyan
        new Color(1.00f, 0.60f, 0.20f, 1f), // 9 orange
    };

    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("GridBasedSandbox/World Preview")]
    public static void ShowWindow()
    {
        var win = GetWindow<WorldPreviewWindow>("World Preview");
        win.minSize = new Vector2(640f, 480f);
    }

    private void OnDisable()
    {
        _cts?.Cancel();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GUI
    // ─────────────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        DrawToolbar();

        if (_generator == null || _settings == null)
        {
            EditorGUILayout.HelpBox("Assign an OverworldGenerator and VoxelWorldSettings above, " +
                                    "then click Render.", MessageType.Info);
            return;
        }

        // Progress bar while rendering
        if (_rendering)
        {
            var progRect = EditorGUILayout.GetControlRect(GUILayout.Height(20f));
            EditorGUI.ProgressBar(progRect, _progress, $"Rendering… {_progress * 100f:F0}%");
            if (GUILayout.Button("Cancel", GUILayout.Width(80f)))
                _cts?.Cancel();
            Repaint();
            return;
        }

        // Draw map / slice
        HandleNavInput();

        if (_showSlice && _sliceTex != null)
            DrawSlice();
        else if (_mapTex != null)
            DrawMap();
        else
            EditorGUILayout.HelpBox("Click Render to generate the preview.", MessageType.None);
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginVertical(EditorStyles.toolbar, GUILayout.ExpandWidth(true));

        // Row 1 — asset references
        EditorGUILayout.BeginHorizontal();
        _generator = (OverworldGenerator)EditorGUILayout.ObjectField(
            "Generator", _generator, typeof(OverworldGenerator), false, GUILayout.MinWidth(200f));
        _settings = (VoxelWorldSettings)EditorGUILayout.ObjectField(
            "Settings", _settings, typeof(VoxelWorldSettings), false, GUILayout.MinWidth(200f));
        EditorGUILayout.EndHorizontal();

        // Row 2 — controls
        EditorGUILayout.BeginHorizontal();
        _seed    = EditorGUILayout.IntField("Seed", _seed, GUILayout.Width(160f));
        _mapSize = EditorGUILayout.IntSlider("Size (blocks)", _mapSize, 64, 2048, GUILayout.MinWidth(200f));
        _mode    = (VisualizationMode)EditorGUILayout.EnumPopup("Mode", _mode, GUILayout.Width(200f));

        if (GUILayout.Button("Render", GUILayout.Width(80f)) && !_rendering)
            StartRender();

        if (GUILayout.Button("Reset View", GUILayout.Width(85f)))
        {
            _panOffset = Vector2.zero;
            _zoom      = 1f;
        }
        EditorGUILayout.EndHorizontal();

        // Row 3 — slice
        EditorGUILayout.BeginHorizontal();
        _showSlice = EditorGUILayout.Toggle("X Slice", _showSlice, GUILayout.Width(100f));
        if (_showSlice)
        {
            _sliceX = EditorGUILayout.IntSlider("X coordinate", _sliceX, 0, _mapSize - 1, GUILayout.MinWidth(200f));
            if (GUILayout.Button("Render Slice", GUILayout.Width(100f)) && !_rendering)
                StartRenderSlice();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawMap()
    {
        Rect drawRect = GetMapRect();
        GUI.DrawTexture(drawRect, _mapTex, ScaleMode.StretchToFill);

        // Hover info
        Event e = Event.current;
        if (drawRect.Contains(e.mousePosition))
        {
            Vector2 uv   = (e.mousePosition - drawRect.position) / drawRect.size;
            int     px   = Mathf.Clamp(Mathf.FloorToInt(uv.x * _mapSize), 0, _mapSize - 1);
            int     pz   = Mathf.Clamp(Mathf.FloorToInt(uv.y * _mapSize), 0, _mapSize - 1);
            int     wx   = px - _mapSize / 2;
            int     wz   = pz - _mapSize / 2;
            EditorGUI.LabelField(new Rect(e.mousePosition.x + 12, e.mousePosition.y, 200, 18),
                                 $"World ({wx}, {wz})");

            // Double-click: log climate
            if (e.type == EventType.MouseDown && e.clickCount == 2)
            {
                LogClimateAt(wx, wz);
                e.Use();
            }
        }
    }

    private void DrawSlice()
    {
        Rect sliceRect = new Rect(4, 100, position.width - 8, position.height - 104);
        GUI.DrawTexture(sliceRect, _sliceTex, ScaleMode.StretchToFill);
        EditorGUI.LabelField(new Rect(8, 102, 300, 18),
                              $"X-Slice at world X = {_sliceX - _mapSize / 2}");
    }

    private Rect GetMapRect()
    {
        float toolbarH = 95f; // estimated toolbar height (3 rows)
        float availW   = position.width - 8f;
        float availH   = position.height - toolbarH - 8f;
        float texSize  = Mathf.Min(availW, availH);

        // Apply pan and zoom
        float displaySize = texSize * _zoom;
        Vector2 center = new Vector2(4f + availW * 0.5f, toolbarH + availH * 0.5f);
        Vector2 topLeft = center - new Vector2(displaySize, displaySize) * 0.5f + _panOffset;
        return new Rect(topLeft, new Vector2(displaySize, displaySize));
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Navigation
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleNavInput()
    {
        Event e = Event.current;
        switch (e.type)
        {
            case EventType.ScrollWheel:
                _zoom = Mathf.Clamp(_zoom * (1f - e.delta.y * 0.05f), 0.1f, 8f);
                e.Use();
                Repaint();
                break;
            case EventType.MouseDown when e.button == 0:
                _dragging        = true;
                _dragStart       = e.mousePosition;
                _panAtDragStart  = _panOffset;
                e.Use();
                break;
            case EventType.MouseDrag when _dragging:
                _panOffset = _panAtDragStart + (e.mousePosition - _dragStart);
                e.Use();
                Repaint();
                break;
            case EventType.MouseUp:
                _dragging = false;
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Rendering
    // ─────────────────────────────────────────────────────────────────────────

    private void StartRender()
    {
        _cts?.Cancel();
        _cts      = new CancellationTokenSource();
        _rendering = true;
        _progress  = 0f;

        // Bake biome curves on main thread before going async
        _generator.Prepare();

        int   size      = _mapSize;
        int   seed      = _seed;
        var   gen       = _generator;
        var   settings  = _settings;
        var   mode      = _mode;
        var   token     = _cts.Token;

        // Capture noise configs for the closure — all read-only after Prepare()
        Task.Run(() =>
        {
            Color32[] pixels = RenderMap(gen, settings, seed, size, mode, token,
                                         p => { _progress = p; });
            if (token.IsCancellationRequested) return;

            // Switch to main thread to set texture
            EditorApplication.delayCall += () =>
            {
                if (pixels == null) { _rendering = false; return; }
                if (_mapTex == null || _mapTex.width != size)
                    _mapTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Point
                    };
                _mapTex.SetPixels32(pixels);
                _mapTex.Apply();
                _rendering = false;
                Repaint();
            };
        }, token);
    }

    private void StartRenderSlice()
    {
        _cts?.Cancel();
        _cts      = new CancellationTokenSource();
        _rendering = true;
        _progress  = 0f;

        _generator.Prepare();

        int   size      = _mapSize;
        int   seed      = _seed;
        int   sliceX    = _sliceX;
        var   gen       = _generator;
        var   settings  = _settings;
        var   token     = _cts.Token;

        Task.Run(() =>
        {
            Color32[] pixels = RenderSlice(gen, settings, seed, size, sliceX, token,
                                           p => { _progress = p; });
            if (token.IsCancellationRequested) return;

            int sliceW = size;
            int sliceH = settings.TotalWorldHeight;

            EditorApplication.delayCall += () =>
            {
                if (pixels == null) { _rendering = false; return; }
                if (_sliceTex == null || _sliceTex.width != sliceW || _sliceTex.height != sliceH)
                    _sliceTex = new Texture2D(sliceW, sliceH, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Point
                    };
                _sliceTex.SetPixels32(pixels);
                _sliceTex.Apply();
                _rendering = false;
                Repaint();
            };
        }, token);
    }

    // ── Static pixel-computation methods (background-thread safe) ─────────────

    private static Color32[] RenderMap(
        OverworldGenerator gen, VoxelWorldSettings settings,
        int seed, int size, VisualizationMode mode,
        CancellationToken token, Action<float> reportProgress)
    {
        // Access climate via reflection-free approach: the generator exposes
        // GetSurfaceY() and we rebuild climate sampling here using the same
        // ClimateConfig that lives on the generator. Since ClimateConfig is a
        // plain C# serializable, we read it directly.
        var climateField = typeof(OverworldGenerator).GetField("climate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var biomeRegField = typeof(OverworldGenerator).GetField("biomeRegistry",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (climateField == null || biomeRegField == null) return null;

        var climate      = (ClimateConfig)climateField.GetValue(gen);
        var biomeReg     = (BiomeRegistry)biomeRegField.GetValue(gen);

        int half = size / 2;

        Color32[] pixels = new Color32[size * size];
        int minY = settings.minHeight;
        int maxY = settings.maxHeight;

        for (int pz = 0; pz < size; pz++)
        {
            if (token.IsCancellationRequested) return null;
            reportProgress?.Invoke((float)pz / size);

            for (int px = 0; px < size; px++)
            {
                int wx = px - half;
                int wz = pz - half;

                ClimatePoint cp = climate.Sample(wx, wz, seed);

                Color c;
                switch (mode)
                {
                    case VisualizationMode.Biome:
                    {
                        var biome = biomeReg?.GetBiome(in cp);
                        int idx   = biome != null && biomeReg.Biomes != null
                            ? FindBiomeIndex(biomeReg, biome) : 0;
                        c = BiomePalette[idx % BiomePalette.Length];
                        break;
                    }
                    case VisualizationMode.Continentalness:
                        c = Color.Lerp(Color.black, Color.white, cp.Continentalness);
                        break;
                    case VisualizationMode.Erosion:
                        c = Color.Lerp(Color.black, Color.white, cp.Erosion);
                        break;
                    case VisualizationMode.Temperature:
                        c = Color.Lerp(Color.blue, Color.red, cp.Temperature);
                        break;
                    case VisualizationMode.Humidity:
                        c = Color.Lerp(new Color(0.9f, 0.85f, 0.6f), new Color(0.1f, 0.35f, 0.8f), cp.Humidity);
                        break;
                    case VisualizationMode.Weirdness:
                        c = Color.Lerp(Color.black, new Color(0.8f, 0.3f, 1f), cp.Weirdness);
                        break;
                    case VisualizationMode.ChaosMask:
                    {
                        var chaosField = typeof(OverworldGenerator).GetField("chaos",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var chaos = chaosField != null ? (ChaosConfig)chaosField.GetValue(gen) : null;
                        if (chaos != null)
                        {
                            float maskOx = 10000f + (seed * 127.1f + chaos.maskSeedOffset) % 9999f;
                            float maskOz = 10000f + (seed * 311.7f + chaos.maskSeedOffset) % 9999f;
                            float mask   = Mathf.PerlinNoise((wx + maskOx) / chaos.maskScale,
                                                             (wz + maskOz) / chaos.maskScale);
                            c = mask > chaos.maskThreshold
                                ? Color.Lerp(Color.red, Color.yellow, (mask - chaos.maskThreshold) / (1f - chaos.maskThreshold))
                                : Color.Lerp(Color.black, Color.blue, mask / chaos.maskThreshold);
                        }
                        else c = Color.magenta;
                        break;
                    }
                    case VisualizationMode.Height:
                    default:
                    {
                        int sy = gen.GetSurfaceY(wx, wz, seed, settings);
                        float t = Mathf.InverseLerp(minY, maxY, sy);
                        // Tinted height: deep blue < sea level, green/gray above
                        if (sy <= settings.seaLevel)
                            c = Color.Lerp(new Color(0.05f, 0.1f, 0.5f), new Color(0.3f, 0.5f, 0.8f),
                                           Mathf.InverseLerp(0f, settings.seaLevel - minY, sy - minY));
                        else
                            c = Color.Lerp(new Color(0.25f, 0.55f, 0.25f), Color.white, t);
                        break;
                    }
                }

                // Flip Z so +Z world = top of texture (more natural orientation)
                pixels[(size - 1 - pz) * size + px] = c;
            }
        }

        return pixels;
    }

    private static Color32[] RenderSlice(
        OverworldGenerator gen, VoxelWorldSettings settings,
        int seed, int mapSize, int sliceX,
        CancellationToken token, Action<float> reportProgress)
    {
        int half  = mapSize / 2;
        int totalH = settings.TotalWorldHeight;
        int minY   = settings.minHeight;
        int maxY   = settings.maxHeight;

        Color32[] pixels = new Color32[mapSize * totalH];

        for (int pz = 0; pz < mapSize; pz++)
        {
            if (token.IsCancellationRequested) return null;
            reportProgress?.Invoke((float)pz / mapSize);

            int wx = sliceX - half;
            int wz = pz - half;
            int sy = gen.GetSurfaceY(wx, wz, seed, settings);

            for (int ly = 0; ly < totalH; ly++)
            {
                int wy = minY + ly;
                Color c;

                if (wy > sy)
                    c = wy <= settings.seaLevel ? new Color(0.2f, 0.4f, 0.8f, 0.5f) : Color.clear;
                else if (wy == sy)
                    c = new Color(0.3f, 0.7f, 0.3f);
                else if (wy >= sy - 3)
                    c = new Color(0.55f, 0.38f, 0.22f);
                else
                    c = new Color(0.5f, 0.5f, 0.5f);

                // Sea level marker
                if (wy == settings.seaLevel)
                    c = Color.Lerp(c, Color.cyan, 0.4f);

                pixels[ly * mapSize + pz] = c;
            }
        }

        return pixels;
    }

    private static int FindBiomeIndex(BiomeRegistry reg, BiomeDefinition target)
    {
        var biomes = reg.Biomes;
        for (int i = 0; i < biomes.Count; i++)
            if (biomes[i] == target) return i;
        return 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Debug helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void LogClimateAt(int wx, int wz)
    {
        _generator.Prepare();

        var climateField = typeof(OverworldGenerator).GetField("climate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var biomeRegField = typeof(OverworldGenerator).GetField("biomeRegistry",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (climateField == null || biomeRegField == null) return;

        var climate  = (ClimateConfig)climateField.GetValue(_generator);
        var biomeReg = (BiomeRegistry)biomeRegField.GetValue(_generator);

        ClimatePoint cp    = climate.Sample(wx, wz, _seed);
        var          biome = biomeReg.GetBiome(in cp);
        int          sy    = _generator.GetSurfaceY(wx, wz, _seed, _settings);

        Debug.Log($"[WorldPreview] World ({wx}, {wz})\n" +
                  $"  Continentalness = {cp.Continentalness:F3}\n" +
                  $"  Erosion         = {cp.Erosion:F3}\n" +
                  $"  Temperature     = {cp.Temperature:F3}\n" +
                  $"  Humidity        = {cp.Humidity:F3}\n" +
                  $"  Weirdness       = {cp.Weirdness:F3}\n" +
                  $"  Biome           = {biome?.biomeName ?? "null"}\n" +
                  $"  Surface Y       = {sy}");
    }

    // ─────────────────────────────────────────────────────────────────────────

    private enum VisualizationMode
    {
        Height,
        Biome,
        Continentalness,
        Erosion,
        Temperature,
        Humidity,
        Weirdness,
        ChaosMask,
    }
}

