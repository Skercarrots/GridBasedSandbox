using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  WorldPreviewWindow — Editor window for visualising terrain and dimension generation.
//  Open via: GridBasedSandbox > World Preview
//
//  FEATURES:
//  • Multi-Dimension Support:
//      - Overworld: Height (grayscale), Biome (color), Climate axes, Chaos mask, Surface Y, X-slice.
//      - Backrooms (Level 0):
//          * Floor Plan (3x3 corridors, 3x2 doors, rooms, walls, landmarks, continental gates).
//          * Continents & Gates (macro-continents, organic borders, gate locations).
//          * Sub-Zones / Biomes (macro-sector distribution).
//          * Lighting & Penumbra (fluorescent troffers vs collective failure dark pockets).
//  • Configurable resolution: 64 to 2048 blocks.
//  • Pan (drag) & Zoom (scroll wheel).
//  • Hover coordinate & architectural inspection.
//  • Double-click to log full details to console.
//  • Background thread rendering with progress bar — zero editor freeze.
// ─────────────────────────────────────────────────────────────────────────────

public class WorldPreviewWindow : EditorWindow
{
    public enum PreviewDimension
    {
        BackroomsLevel0,
        Overworld
    }

    public enum BackroomsVisMode
    {
        FloorPlan,
        Continents,
        SubZones,
        Lighting
    }

    public enum OverworldVisMode
    {
        Height,
        Biome,
        Continentalness,
        Erosion,
        Temperature,
        Humidity,
        Weirdness,
        ChaosMask
    }

    // ── Dimension Selection ───────────────────────────────────────────────────
    private PreviewDimension _dimension = PreviewDimension.BackroomsLevel0;

    // ── Asset References ──────────────────────────────────────────────────────
    private OverworldGenerator _overworldGenerator;
    private BackroomsGenerator _backroomsGenerator;
    private VoxelWorldSettings _settings;

    // ── Common Controls ───────────────────────────────────────────────────────
    private int _seed = 12345;
    private int _mapSize = 512; // blocks
    private BackroomsVisMode _backroomsMode = BackroomsVisMode.FloorPlan;
    private OverworldVisMode _overworldMode = OverworldVisMode.Biome;

    // ── Cross-section slice (Overworld only) ───────────────────────────────────
    private bool _showSlice;
    private int _sliceX;

    // ── Navigation ────────────────────────────────────────────────────────────
    private Vector2 _panOffset = Vector2.zero;
    private float _zoom = 1f;
    private bool _dragging;
    private Vector2 _dragStart;
    private Vector2 _panAtDragStart;

    // ── Render state ──────────────────────────────────────────────────────────
    private Texture2D _mapTex;
    private Texture2D _sliceTex;
    private bool _rendering;
    private float _progress;
    private CancellationTokenSource _cts;

    // ── Overworld Biome Palette ───────────────────────────────────────────────
    private static readonly Color[] OverworldPalette = new Color[]
    {
        new Color(0.35f, 0.75f, 0.35f, 1f), // plains
        new Color(0.20f, 0.55f, 0.20f, 1f), // forest
        new Color(0.70f, 0.70f, 0.75f, 1f), // mountains
        new Color(0.25f, 0.45f, 0.85f, 1f), // ocean/beach
        new Color(0.60f, 0.40f, 0.80f, 1f), // highlands
        new Color(0.90f, 0.80f, 0.50f, 1f), // dunes
        new Color(0.80f, 0.80f, 0.80f, 1f),
        new Color(0.60f, 0.30f, 0.10f, 1f),
        new Color(0.50f, 0.80f, 0.90f, 1f),
        new Color(1.00f, 0.60f, 0.20f, 1f),
    };

    // ── Backrooms Continent Palette ───────────────────────────────────────────
    private static readonly Color32[] ContinentPalette = new Color32[]
    {
        new Color32(230, 200, 80, 255),  // Continent A: Warm Golden Yellow
        new Color32(70, 140, 190, 255),  // Continent B: Industrial Steel Blue
        new Color32(190, 90, 70, 255),   // Continent C: Rust Red / Amber
        new Color32(110, 175, 110, 255), // Continent D: Muted Mint Green
        new Color32(170, 110, 200, 255), // Continent E: Deep Violet
    };

    // ── Backrooms Sub-Zone Palette ───────────────────────────────────────────
    private static readonly Color32[] ZonePalette = new Color32[]
    {
        new Color32(235, 215, 120, 255), // 0: Standard Corridor Maze
        new Color32(255, 175, 70, 255),  // 1: Colossal Open Mega-Halls
        new Color32(200, 120, 90, 255),  // 2: Dense Office Chambers
        new Color32(245, 235, 160, 255), // 3: Long Labyrinth Hallways
        new Color32(150, 130, 180, 255), // 4: Column Hypostyle Hall
    };

    [MenuItem("GridBasedSandbox/World Preview")]
    public static void ShowWindow()
    {
        var win = GetWindow<WorldPreviewWindow>("World Preview");
        win.minSize = new Vector2(680f, 520f);
        win.AutoFindAssets();
    }

    private void OnEnable()
    {
        AutoFindAssets();
    }

    private void OnDisable()
    {
        _cts?.Cancel();
    }

    private void AutoFindAssets()
    {
        if (_backroomsGenerator == null)
            _backroomsGenerator = AssetDatabase.LoadAssetAtPath<BackroomsGenerator>(
                "Assets/ScriptableObjects/Voxel World/Generators/BackroomsGenerator.asset");

        if (_overworldGenerator == null)
            _overworldGenerator = AssetDatabase.LoadAssetAtPath<OverworldGenerator>(
                "Assets/ScriptableObjects/Voxel World/Generators/OverworldGenerator.asset");

        if (_settings == null)
        {
            if (_dimension == PreviewDimension.BackroomsLevel0)
                _settings = AssetDatabase.LoadAssetAtPath<VoxelWorldSettings>(
                    "Assets/ScriptableObjects/Voxel World/World Settings/BackroomsWorldSettings.asset");
            else
                _settings = AssetDatabase.LoadAssetAtPath<VoxelWorldSettings>(
                    "Assets/ScriptableObjects/Voxel World/World Settings/VoxelWorldSettings.asset");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GUI
    // ─────────────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        DrawToolbar();

        bool hasGen = (_dimension == PreviewDimension.BackroomsLevel0 && _backroomsGenerator != null) ||
                      (_dimension == PreviewDimension.Overworld && _overworldGenerator != null);

        if (!hasGen || _settings == null)
        {
            EditorGUILayout.HelpBox("Assign the generator and VoxelWorldSettings above, then click Render.", MessageType.Info);
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

        if (_dimension == PreviewDimension.Overworld && _showSlice && _sliceTex != null)
            DrawSlice();
        else if (_mapTex != null)
            DrawMap();
        else
            EditorGUILayout.HelpBox("Click Render to generate the world preview map.", MessageType.None);
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginVertical(EditorStyles.toolbar, GUILayout.ExpandWidth(true));

        // Row 1 — Dimension & Generator
        EditorGUILayout.BeginHorizontal();

        var prevDim = _dimension;
        _dimension = (PreviewDimension)EditorGUILayout.EnumPopup("Dimension", _dimension, GUILayout.Width(220f));
        if (prevDim != _dimension)
        {
            AutoFindAssets();
            _mapTex = null;
        }

        if (_dimension == PreviewDimension.BackroomsLevel0)
        {
            _backroomsGenerator = (BackroomsGenerator)EditorGUILayout.ObjectField(
                "Backrooms Gen", _backroomsGenerator, typeof(BackroomsGenerator), false, GUILayout.MinWidth(180f));
        }
        else
        {
            _overworldGenerator = (OverworldGenerator)EditorGUILayout.ObjectField(
                "Overworld Gen", _overworldGenerator, typeof(OverworldGenerator), false, GUILayout.MinWidth(180f));
        }

        _settings = (VoxelWorldSettings)EditorGUILayout.ObjectField(
            "Settings", _settings, typeof(VoxelWorldSettings), false, GUILayout.MinWidth(160f));

        if (GUILayout.Button("Auto-Find", GUILayout.Width(75f)))
            AutoFindAssets();

        EditorGUILayout.EndHorizontal();

        // Row 2 — Controls & Modes
        EditorGUILayout.BeginHorizontal();
        _seed    = EditorGUILayout.IntField("Seed", _seed, GUILayout.Width(140f));
        _mapSize = EditorGUILayout.IntSlider("Size (blocks)", _mapSize, 64, 2048, GUILayout.MinWidth(180f));

        if (_dimension == PreviewDimension.BackroomsLevel0)
        {
            _backroomsMode = (BackroomsVisMode)EditorGUILayout.EnumPopup("Mode", _backroomsMode, GUILayout.Width(180f));
        }
        else
        {
            _overworldMode = (OverworldVisMode)EditorGUILayout.EnumPopup("Mode", _overworldMode, GUILayout.Width(180f));
        }

        if (GUILayout.Button("Render", GUILayout.Width(75f)) && !_rendering)
            StartRender();

        if (GUILayout.Button("Reset View", GUILayout.Width(80f)))
        {
            _panOffset = Vector2.zero;
            _zoom      = 1f;
        }
        EditorGUILayout.EndHorizontal();

        // Row 3 — Overworld Cross Section Slice
        if (_dimension == PreviewDimension.Overworld)
        {
            EditorGUILayout.BeginHorizontal();
            _showSlice = EditorGUILayout.Toggle("X Slice", _showSlice, GUILayout.Width(100f));
            if (_showSlice)
            {
                _sliceX = EditorGUILayout.IntSlider("X coordinate", _sliceX, 0, _mapSize - 1, GUILayout.MinWidth(200f));
                if (GUILayout.Button("Render Slice", GUILayout.Width(100f)) && !_rendering)
                    StartRenderSlice();
            }
            EditorGUILayout.EndHorizontal();
        }

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
            Vector2 uv = (e.mousePosition - drawRect.position) / drawRect.size;
            int px = Mathf.Clamp(Mathf.FloorToInt(uv.x * _mapSize), 0, _mapSize - 1);
            int pz = Mathf.Clamp(Mathf.FloorToInt(uv.y * _mapSize), 0, _mapSize - 1);
            int wx = px - _mapSize / 2;
            int wz = pz - _mapSize / 2;

            if (_dimension == PreviewDimension.BackroomsLevel0 && _backroomsGenerator != null)
            {
                var cont = _backroomsGenerator.GetContinentAt(wx, wz, _seed);
                var zone = _backroomsGenerator.GetZoneAt(wx, wz, _seed);
                string contName = cont != null ? cont.continentName : "Starting Sector";
                string zoneName = zone != null ? zone.zoneName : "Standard Lobby";

                EditorGUI.LabelField(new Rect(e.mousePosition.x + 14, e.mousePosition.y, 420, 20),
                    $"World ({wx}, {wz}) | {contName} | {zoneName}");

                if (e.type == EventType.MouseDown && e.clickCount == 2)
                {
                    Debug.Log($"[WorldPreview Backrooms] ({wx}, {wz})\n" +
                              $"  Continent: {contName}\n" +
                              $"  Sub-Zone:  {zoneName}\n" +
                              $"  Seed:      {_seed}");
                    e.Use();
                }
            }
            else if (_overworldGenerator != null)
            {
                EditorGUI.LabelField(new Rect(e.mousePosition.x + 14, e.mousePosition.y, 240, 20),
                    $"World ({wx}, {wz})");

                if (e.type == EventType.MouseDown && e.clickCount == 2)
                {
                    LogClimateAt(wx, wz);
                    e.Use();
                }
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
        float toolbarH = (_dimension == PreviewDimension.Overworld) ? 95f : 75f;
        float availW   = position.width - 8f;
        float availH   = position.height - toolbarH - 8f;
        float texSize  = Mathf.Min(availW, availH);

        float displaySize = texSize * _zoom;
        Vector2 center = new Vector2(4f + availW * 0.5f, toolbarH + availH * 0.5f);
        Vector2 topLeft = center - new Vector2(displaySize, displaySize) * 0.5f + _panOffset;
        return new Rect(topLeft, new Vector2(displaySize, displaySize));
    }

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
                _dragging       = true;
                _dragStart      = e.mousePosition;
                _panAtDragStart = _panOffset;
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
    //  Rendering Dispatch
    // ─────────────────────────────────────────────────────────────────────────

    private void StartRender()
    {
        _cts?.Cancel();
        _cts       = new CancellationTokenSource();
        _rendering = true;
        _progress  = 0f;

        int  size  = _mapSize;
        int  seed  = _seed;
        var  token = _cts.Token;

        if (_dimension == PreviewDimension.BackroomsLevel0)
        {
            _backroomsGenerator.Prepare();
            var gen  = _backroomsGenerator;
            var mode = _backroomsMode;

            Task.Run(() =>
            {
                Color32[] pixels = RenderBackroomsMap(gen, seed, size, mode, token, p => { _progress = p; });
                if (token.IsCancellationRequested) return;

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
        else
        {
            _overworldGenerator.Prepare();
            var gen      = _overworldGenerator;
            var settings = _settings;
            var mode     = _overworldMode;

            Task.Run(() =>
            {
                Color32[] pixels = RenderOverworldMap(gen, settings, seed, size, mode, token, p => { _progress = p; });
                if (token.IsCancellationRequested) return;

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
    }

    private void StartRenderSlice()
    {
        if (_overworldGenerator == null || _settings == null) return;

        _cts?.Cancel();
        _cts       = new CancellationTokenSource();
        _rendering = true;
        _progress  = 0f;

        _overworldGenerator.Prepare();

        int  size     = _mapSize;
        int  seed     = _seed;
        int  sliceX   = _sliceX;
        var  gen      = _overworldGenerator;
        var  settings = _settings;
        var  token    = _cts.Token;

        Task.Run(() =>
        {
            Color32[] pixels = RenderSlice(gen, settings, seed, size, sliceX, token, p => { _progress = p; });
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

    // ─────────────────────────────────────────────────────────────────────────
    //  Backrooms Pixel Renderer
    // ─────────────────────────────────────────────────────────────────────────

    private static Color32[] RenderBackroomsMap(
        BackroomsGenerator gen, int seed, int size, BackroomsVisMode mode,
        CancellationToken token, Action<float> reportProgress)
    {
        Color32[] pixels = new Color32[size * size];
        int half = size / 2;

        int contSize = 256;
        float warpScale = 0.006f;

        for (int pz = 0; pz < size; pz++)
        {
            if (token.IsCancellationRequested) return null;
            if (pz % 16 == 0) reportProgress((float)pz / size);

            int wz = pz - half;
            int rowOffset = pz * size;

            for (int px = 0; px < size; px++)
            {
                int wx = px - half;

                // 1. Continents Mode
                if (mode == BackroomsVisMode.Continents)
                {
                    int warpX = (int)((Mathf.PerlinNoise((wx + seed * 11) * warpScale + 123.4f, (wz + seed * 17) * warpScale + 567.8f) - 0.5f) * 44f);
                    int warpZ = (int)((Mathf.PerlinNoise((wx - seed * 23) * warpScale + 789.1f, (wz - seed * 29) * warpScale + 234.5f) - 0.5f) * 44f);
                    int cCoordX = wx + warpX;
                    int cCoordZ = wz + warpZ;

                    int cX = cCoordX >= 0 ? cCoordX / contSize : (cCoordX - contSize + 1) / contSize;
                    int cZ = cCoordZ >= 0 ? cCoordZ / contSize : (cCoordZ - contSize + 1) / contSize;
                    int lx = cCoordX % contSize; if (lx < 0) lx += contSize;
                    int lz = cCoordZ % contSize; if (lz < 0) lz += contSize;

                    bool onBorder = (lx < 3 || lx >= contSize - 3 || lz < 3 || lz >= contSize - 3);
                    if (onBorder)
                    {
                        // Check gate
                        int gateZ = 32 + (DeterministicRng.Hash(seed, cX, cZ, 7000) & 0x7FFFFFFF) % (contSize - 64);
                        int gateX = 32 + (DeterministicRng.Hash(seed, cX, cZ, 8000) & 0x7FFFFFFF) % (contSize - 64);
                        if (Mathf.Abs(lz - gateZ) <= 2 || Mathf.Abs(lx - gateX) <= 2)
                        {
                            pixels[rowOffset + px] = new Color32(0, 240, 255, 255); // Cyan Gateway
                            continue;
                        }

                        pixels[rowOffset + px] = new Color32(20, 22, 26, 255); // Dark Bulkhead
                        continue;
                    }

                    int contHash = Mathf.Abs(DeterministicRng.Hash(seed, cX, cZ, 44444));
                    pixels[rowOffset + px] = ContinentPalette[contHash % ContinentPalette.Length];
                    continue;
                }

                // 2. Sub-Zones Mode
                if (mode == BackroomsVisMode.SubZones)
                {
                    int secSize = 48;
                    int sX = wx >= 0 ? wx / secSize : (wx - secSize + 1) / secSize;
                    int sZ = wz >= 0 ? wz / secSize : (wz - secSize + 1) / secSize;
                    int pickHash = Mathf.Abs(DeterministicRng.Hash(seed, sX, sZ, 55555));
                    pixels[rowOffset + px] = ZonePalette[pickHash % ZonePalette.Length];
                    continue;
                }

                // 3. Lighting Mode
                if (mode == BackroomsVisMode.Lighting)
                {
                    int penCellX = wx >= 0 ? wx / 12 : (wx - 11) / 12;
                    int penCellZ = wz >= 0 ? wz / 12 : (wz - 11) / 12;
                    float penRoll = (DeterministicRng.Hash(seed, penCellX, penCellZ, 606) & 0xFFFF) / 65536f;
                    bool inDarkPocket = penRoll < 0.12f;

                    if (inDarkPocket)
                    {
                        pixels[rowOffset + px] = new Color32(22, 18, 14, 255); // Dark unlit pocket
                    }
                    else
                    {
                        bool isLight = (wx % 6 == 0 && wz % 6 == 0);
                        pixels[rowOffset + px] = isLight
                            ? new Color32(255, 255, 240, 255)
                            : new Color32(180, 160, 90, 255);
                    }
                    continue;
                }

                // 4. Floor Plan Mode (Architecture)
                pixels[rowOffset + px] = SampleBackroomsFloorPlan(gen, seed, wx, wz, contSize, warpScale);
            }
        }

        reportProgress(1f);
        return pixels;
    }

    private static Color32 SampleBackroomsFloorPlan(BackroomsGenerator gen, int seed, int wx, int wz, int contSize, float warpScale)
    {
        var cell = gen.EvaluateCell(wx, wz, seed);

        if (cell.isSolidWall)
            return new Color32(48, 42, 28, 255);

        if (cell.isDoorway)
            return new Color32(255, 160, 40, 255);

        if (cell.isLight)
            return new Color32(255, 255, 240, 255);

        if (cell.spec.wallHeight > 0 && cell.spec.wallIsHalf)
            return new Color32(255, 160, 40, 255);

        if (cell.isCorridor)
            return new Color32(215, 198, 115, 255);

        return new Color32(195, 180, 105, 255);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Overworld Pixel Renderer (Preserved)
    // ─────────────────────────────────────────────────────────────────────────

    private static Color32[] RenderOverworldMap(
        OverworldGenerator gen, VoxelWorldSettings settings,
        int seed, int size, OverworldVisMode mode,
        CancellationToken token, Action<float> reportProgress)
    {
        var climateField = typeof(OverworldGenerator).GetField("climate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var biomeRegField = typeof(OverworldGenerator).GetField("biomeRegistry",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (climateField == null || biomeRegField == null) return null;

        var climate  = (ClimateConfig)climateField.GetValue(gen);
        var biomeReg = (BiomeRegistry)biomeRegField.GetValue(gen);

        int half = size / 2;
        Color32[] pixels = new Color32[size * size];

        for (int pz = 0; pz < size; pz++)
        {
            if (token.IsCancellationRequested) return null;
            if (pz % 16 == 0) reportProgress((float)pz / size);

            int wz = pz - half;
            int rowOffset = pz * size;

            for (int px = 0; px < size; px++)
            {
                int wx = px - half;
                pixels[rowOffset + px] = EvalOverworldPixel(gen, settings, climate, biomeReg, seed, wx, wz, mode);
            }
        }

        reportProgress(1f);
        return pixels;
    }

    private static Color32 EvalOverworldPixel(
        OverworldGenerator gen, VoxelWorldSettings settings,
        ClimateConfig climate, BiomeRegistry biomeReg,
        int seed, int wx, int wz, OverworldVisMode mode)
    {
        switch (mode)
        {
            case OverworldVisMode.Height:
            {
                int y = gen.GetSurfaceY(wx, wz, seed, settings);
                float norm = Mathf.InverseLerp(settings.minHeight, settings.maxHeight, y);
                byte b = (byte)(Mathf.Clamp01(norm) * 255f);
                return new Color32(b, b, b, 255);
            }
            case OverworldVisMode.Biome:
            {
                ClimatePoint cp = climate.Sample(wx, wz, seed);
                var biome = biomeReg.GetBiome(in cp);
                int idx = 0;
                if (biome != null && biomeReg.Biomes != null)
                {
                    for (int i = 0; i < biomeReg.Biomes.Count; i++)
                    {
                        if (biomeReg.Biomes[i] == biome) { idx = i; break; }
                    }
                }
                Color c = OverworldPalette[Mathf.Clamp(idx, 0, OverworldPalette.Length - 1)];
                return (Color32)c;
            }
            case OverworldVisMode.Continentalness:
            {
                float val = climate.Sample(wx, wz, seed).Continentalness;
                byte b = (byte)(Mathf.Clamp01((val + 1f) * 0.5f) * 255f);
                return new Color32(b, b, b, 255);
            }
            case OverworldVisMode.Erosion:
            {
                float val = climate.Sample(wx, wz, seed).Erosion;
                byte b = (byte)(Mathf.Clamp01((val + 1f) * 0.5f) * 255f);
                return new Color32(b, b, b, 255);
            }
            case OverworldVisMode.Temperature:
            {
                float val = climate.Sample(wx, wz, seed).Temperature;
                byte r = (byte)(Mathf.Clamp01((val + 1f) * 0.5f) * 255f);
                return new Color32(r, 60, (byte)(255 - r), 255);
            }
            case OverworldVisMode.Humidity:
            {
                float val = climate.Sample(wx, wz, seed).Humidity;
                byte b = (byte)(Mathf.Clamp01((val + 1f) * 0.5f) * 255f);
                return new Color32(40, (byte)(b * 0.7f), b, 255);
            }
            case OverworldVisMode.Weirdness:
            {
                float val = climate.Sample(wx, wz, seed).Weirdness;
                byte b = (byte)(Mathf.Clamp01((val + 1f) * 0.5f) * 255f);
                return new Color32(b, (byte)(255 - b), b, 255);
            }
            default:
                return new Color32(128, 128, 128, 255);
        }
    }

    private static Color32[] RenderSlice(
        OverworldGenerator gen, VoxelWorldSettings settings,
        int seed, int size, int sliceX,
        CancellationToken token, Action<float> reportProgress)
    {
        int totalH = settings.TotalWorldHeight;
        Color32[] pixels = new Color32[size * totalH];
        int wx = sliceX - size / 2;

        for (int pz = 0; pz < size; pz++)
        {
            if (token.IsCancellationRequested) return null;
            if (pz % 16 == 0) reportProgress((float)pz / size);

            int wz = pz - size / 2;
            int surfaceY = gen.GetSurfaceY(wx, wz, seed, settings);
            int localSurface = settings.WorldYToLocal(surfaceY);

            for (int ly = 0; ly < totalH; ly++)
            {
                Color32 col;
                if (ly <= localSurface)
                    col = new Color32(100, 100, 100, 255);
                else
                    col = new Color32(30, 30, 40, 255);

                pixels[ly * size + pz] = col;
            }
        }

        reportProgress(1f);
        return pixels;
    }

    private void LogClimateAt(int wx, int wz)
    {
        if (_overworldGenerator == null || _settings == null) return;
        _overworldGenerator.Prepare();

        var climateField = typeof(OverworldGenerator).GetField("climate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var biomeRegField = typeof(OverworldGenerator).GetField("biomeRegistry",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (climateField == null || biomeRegField == null) return;

        var climate  = (ClimateConfig)climateField.GetValue(_overworldGenerator);
        var biomeReg = (BiomeRegistry)biomeRegField.GetValue(_overworldGenerator);

        ClimatePoint cp = climate.Sample(wx, wz, _seed);
        var biome       = biomeReg.GetBiome(in cp);
        int sy          = _overworldGenerator.GetSurfaceY(wx, wz, _seed, _settings);

        Debug.Log($"[WorldPreview Overworld] ({wx}, {wz})\n" +
                  $"  Continentalness = {cp.Continentalness:F3}\n" +
                  $"  Erosion         = {cp.Erosion:F3}\n" +
                  $"  Temperature     = {cp.Temperature:F3}\n" +
                  $"  Humidity        = {cp.Humidity:F3}\n" +
                  $"  Weirdness       = {cp.Weirdness:F3}\n" +
                  $"  Biome           = {biome?.biomeName ?? "null"}\n" +
                  $"  Surface Y       = {sy}");
    }
}
