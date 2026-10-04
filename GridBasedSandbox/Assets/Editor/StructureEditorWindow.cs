using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  StructureEditorWindow — Interactive editor window for designing, previewing,
//  and baking procedural tree and structure recipes.
//  Open via: GridBasedSandbox > Structure Editor
// ─────────────────────────────────────────────────────────────────────────────

public class StructureEditorWindow : EditorWindow
{
    private TreeRecipe _selectedRecipe;
    private int _seed = 42;
    private bool _show3x3Grid = false;
    private bool _showWireframe = true;

    // Orbit camera controls for 3D preview
    private Vector2 _orbitAngles = new Vector2(30f, 45f);
    private float _zoom = 12f;
    private Vector2 _panOffset = Vector2.zero;

    // Cached generation data
    private StructureData _cachedData;
    private Mesh _previewMesh;
    private Material _previewMaterial;
    private PreviewRenderUtility _previewUtility;

    // 3x3 grid cache
    private readonly StructureData[] _gridData = new StructureData[9];
    private readonly Mesh[] _gridMeshes = new Mesh[9];

    // Scroll positions
    private Vector2 _sidebarScroll;
    private Vector2 _inspectorScroll;

    [MenuItem("GridBasedSandbox/Structure Editor")]
    public static void OpenWindow()
    {
        var window = GetWindow<StructureEditorWindow>("Structure Editor");
        window.minSize = new Vector2(800, 500);
        window.Show();
    }

    private void OnEnable()
    {
        InitPreviewUtility();
        RefreshActiveRecipe();
    }

    private void OnDisable()
    {
        CleanupPreviewUtility();
    }

    private void InitPreviewUtility()
    {
        if (_previewUtility == null)
        {
            _previewUtility = new PreviewRenderUtility();
            _previewUtility.cameraFieldOfView = 35f;
            _previewUtility.camera.nearClipPlane = 0.3f;
            _previewUtility.camera.farClipPlane = 100f;
            _previewUtility.ambientColor = new Color(0.45f, 0.45f, 0.48f);

            if (_previewUtility.lights != null && _previewUtility.lights.Length >= 2)
            {
                _previewUtility.lights[0].intensity = 1.3f;
                _previewUtility.lights[0].transform.rotation = Quaternion.Euler(45f, 40f, 0f);
                _previewUtility.lights[0].color = Color.white;

                _previewUtility.lights[1].intensity = 0.6f;
                _previewUtility.lights[1].transform.rotation = Quaternion.Euler(330f, 220f, 0f);
                _previewUtility.lights[1].color = new Color(0.75f, 0.85f, 1f);
            }
        }

        if (_previewMaterial == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _previewMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _previewMaterial.EnableKeyword("_VERTEX_COLOR");
            _previewMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
            _previewMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
            _previewMaterial.SetColor("_BaseColor", Color.white);
            _previewMaterial.SetColor("_Color", Color.white);
            _previewMaterial.SetFloat("_Smoothness", 0.05f);
            _previewMaterial.SetFloat("_Cull", 2f); // Back culling
        }
    }

    private void CleanupPreviewUtility()
    {
        _previewUtility?.Cleanup();
        _previewUtility = null;

        if (_previewMesh != null)
        {
            DestroyImmediate(_previewMesh);
            _previewMesh = null;
        }

        for (int i = 0; i < _gridMeshes.Length; i++)
        {
            if (_gridMeshes[i] != null)
            {
                DestroyImmediate(_gridMeshes[i]);
                _gridMeshes[i] = null;
            }
        }

        if (_previewMaterial != null)
        {
            DestroyImmediate(_previewMaterial);
            _previewMaterial = null;
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();

        // ── Left Column: Recipe Selector & Inspector ─────────────────────────
        EditorGUILayout.BeginVertical(GUILayout.Width(320));
        DrawRecipeList();
        EditorGUILayout.Space(8);
        if (_selectedRecipe != null)
        {
            DrawRecipeInspector();
        }
        else
        {
            EditorGUILayout.HelpBox("Select or create a TreeRecipe to edit.", MessageType.Info);
        }
        EditorGUILayout.EndVertical();

        // ── Right Column: 3D Preview & Tools ─────────────────────────────────
        EditorGUILayout.BeginVertical();
        DrawToolbar();

        if (_selectedRecipe != null)
        {
            if (_show3x3Grid)
                Draw3x3VariantGrid();
            else
                DrawSinglePreview();

            DrawValidationInfo();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Left Panel: Asset Browser & Inspector
    // ─────────────────────────────────────────────────────────────────────────

    private void DrawRecipeList()
    {
        EditorGUILayout.LabelField("Recipes", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("New Tree Recipe", EditorStyles.miniButtonLeft))
        {
            CreateNewRecipe();
        }
        if (GUILayout.Button("Duplicate", EditorStyles.miniButtonRight) && _selectedRecipe != null)
        {
            DuplicateRecipe();
        }
        EditorGUILayout.EndHorizontal();

        _sidebarScroll = EditorGUILayout.BeginScrollView(_sidebarScroll, GUILayout.Height(130));
        string[] guids = AssetDatabase.FindAssets("t:TreeRecipe");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var recipe = AssetDatabase.LoadAssetAtPath<TreeRecipe>(path);
            if (recipe == null) continue;

            bool isSelected = recipe == _selectedRecipe;
            if (GUILayout.Toggle(isSelected, recipe.name, EditorStyles.radioButton))
            {
                if (!isSelected)
                {
                    _selectedRecipe = recipe;
                    RefreshActiveRecipe();
                }
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawRecipeInspector()
    {
        EditorGUILayout.LabelField("Recipe Parameters", EditorStyles.boldLabel);

        _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);
        EditorGUI.BeginChangeCheck();

        Undo.RecordObject(_selectedRecipe, "Edit Tree Recipe");

        // Trunk
        EditorGUILayout.LabelField("Trunk", EditorStyles.boldLabel);
        _selectedRecipe.trunkMinHeight = EditorGUILayout.IntSlider("Min Height", _selectedRecipe.trunkMinHeight, 3, 30);
        _selectedRecipe.trunkMaxHeight = EditorGUILayout.IntSlider("Max Height", _selectedRecipe.trunkMaxHeight, _selectedRecipe.trunkMinHeight, 30);
        _selectedRecipe.trunkThickness = EditorGUILayout.IntSlider("Thickness", _selectedRecipe.trunkThickness, 1, 3);
        _selectedRecipe.trunkTaper = EditorGUILayout.Slider("Taper", _selectedRecipe.trunkTaper, 0f, 0.5f);
        _selectedRecipe.trunkLean = EditorGUILayout.Slider("Lean", _selectedRecipe.trunkLean, 0f, 0.4f);

        // Branches
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Branches", EditorStyles.boldLabel);
        _selectedRecipe.branchCount = EditorGUILayout.IntSlider("Branch Count", _selectedRecipe.branchCount, 0, 8);
        _selectedRecipe.branchAngle = EditorGUILayout.Slider("Branch Angle", _selectedRecipe.branchAngle, 15f, 75f);
        _selectedRecipe.branchLength = EditorGUILayout.IntSlider("Branch Length", _selectedRecipe.branchLength, 1, 8);
        _selectedRecipe.branchDepth = EditorGUILayout.IntSlider("Branch Depth", _selectedRecipe.branchDepth, 0, 2);

        // Root Flare
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Root Flare", EditorStyles.boldLabel);
        _selectedRecipe.rootFlareRadius = EditorGUILayout.IntSlider("Root Flare Radius", _selectedRecipe.rootFlareRadius, 0, 3);

        // Canopy
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Canopy", EditorStyles.boldLabel);
        _selectedRecipe.canopyShape = (TreeRecipe.CanopyShape)EditorGUILayout.EnumPopup("Canopy Shape", _selectedRecipe.canopyShape);
        _selectedRecipe.canopyRadiusX = EditorGUILayout.IntSlider("Radius X", _selectedRecipe.canopyRadiusX, 1, 8);
        _selectedRecipe.canopyRadiusY = EditorGUILayout.IntSlider("Radius Y", _selectedRecipe.canopyRadiusY, 1, 8);
        _selectedRecipe.canopyRadiusZ = EditorGUILayout.IntSlider("Radius Z", _selectedRecipe.canopyRadiusZ, 1, 8);
        _selectedRecipe.leafDensityErosion = EditorGUILayout.Slider("Leaf Erosion", _selectedRecipe.leafDensityErosion, 0f, 0.5f);

        // Blocks
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Blocks", EditorStyles.boldLabel);
        _selectedRecipe.logBlock = (VoxelBlockType)EditorGUILayout.ObjectField("Log Block", _selectedRecipe.logBlock, typeof(VoxelBlockType), false);
        _selectedRecipe.leafBlock = (VoxelBlockType)EditorGUILayout.ObjectField("Leaf Block", _selectedRecipe.leafBlock, typeof(VoxelBlockType), false);

        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(_selectedRecipe);
            RefreshActiveRecipe();
        }

        EditorGUILayout.EndScrollView();
    }

    private void CreateNewRecipe()
    {
        string dir = "Assets/ScriptableObjects/Voxel World/Structures";
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var recipe = CreateInstance<TreeRecipe>();
        recipe.logBlock = AssetDatabase.LoadAssetAtPath<VoxelBlockType>("Assets/ScriptableObjects/Voxel World/Blocks/Log.asset");
        recipe.leafBlock = AssetDatabase.LoadAssetAtPath<VoxelBlockType>("Assets/ScriptableObjects/Voxel World/Blocks/Leaves.asset");

        string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/TreeRecipe_New.asset");
        AssetDatabase.CreateAsset(recipe, path);
        AssetDatabase.SaveAssets();

        _selectedRecipe = recipe;
        RefreshActiveRecipe();
    }

    private void DuplicateRecipe()
    {
        if (_selectedRecipe == null) return;
        string path = AssetDatabase.GetAssetPath(_selectedRecipe);
        string newPath = AssetDatabase.GenerateUniqueAssetPath(path.Replace(".asset", "_Copy.asset"));
        AssetDatabase.CopyAsset(path, newPath);
        AssetDatabase.SaveAssets();

        _selectedRecipe = AssetDatabase.LoadAssetAtPath<TreeRecipe>(newPath);
        RefreshActiveRecipe();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Right Panel: Toolbar & 3D Preview
    // ─────────────────────────────────────────────────────────────────────────

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        EditorGUILayout.LabelField("Seed:", GUILayout.Width(40));
        EditorGUI.BeginChangeCheck();
        _seed = EditorGUILayout.IntSlider(_seed, 0, 999, GUILayout.Width(180));
        if (EditorGUI.EndChangeCheck())
        {
            RefreshActiveRecipe();
        }

        if (GUILayout.Button("Randomize", EditorStyles.toolbarButton, GUILayout.Width(75)))
        {
            _seed = UnityEngine.Random.Range(0, 1000);
            RefreshActiveRecipe();
        }

        _show3x3Grid = GUILayout.Toggle(_show3x3Grid, "3×3 Grid", EditorStyles.toolbarButton, GUILayout.Width(75));
        _showWireframe = GUILayout.Toggle(_showWireframe, "Wireframe", EditorStyles.toolbarButton, GUILayout.Width(75));

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Reset View", EditorStyles.toolbarButton, GUILayout.Width(80)))
        {
            _orbitAngles = new Vector2(30f, 45f);
            _zoom = 12f;
            _panOffset = Vector2.zero;
        }

        if (GUILayout.Button("Bake Template", EditorStyles.toolbarButton, GUILayout.Width(100)))
        {
            BakeCurrentToTemplate();
        }

        if (GUILayout.Button("Bake 5 Variants", EditorStyles.toolbarButton, GUILayout.Width(110)))
        {
            BakeMultipleVariants(5);
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawSinglePreview()
    {
        Rect previewRect = GUILayoutUtility.GetRect(200, 380, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        HandleOrbitInput(previewRect);

        if (_previewUtility != null && _previewMesh != null)
        {
            _previewUtility.BeginPreview(previewRect, GUIStyle.none);

            SetupCamera(_previewUtility.camera, _orbitAngles, _zoom, _panOffset);

            _previewMaterial.SetPass(0);
            Graphics.DrawMesh(_previewMesh, Matrix4x4.identity, _previewMaterial, 0, _previewUtility.camera);

            _previewUtility.camera.Render();
            Texture tex = _previewUtility.EndPreview();
            GUI.DrawTexture(previewRect, tex);
        }
    }

    private void Draw3x3VariantGrid()
    {
        Rect gridRect = GUILayoutUtility.GetRect(200, 380, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        float cellW = gridRect.width / 3f;
        float cellH = gridRect.height / 3f;

        for (int row = 0; row < 3; row++)
        for (int col = 0; col < 3; col++)
        {
            int idx = row * 3 + col;
            Rect cellRect = new Rect(gridRect.x + col * cellW, gridRect.y + row * cellH, cellW - 2, cellH - 2);

            if (_gridMeshes[idx] != null && _previewUtility != null)
            {
                _previewUtility.BeginPreview(cellRect, GUIStyle.none);
                SetupCamera(_previewUtility.camera, _orbitAngles, _zoom * 0.8f, Vector2.zero);
                _previewMaterial.SetPass(0);
                Graphics.DrawMesh(_gridMeshes[idx], Matrix4x4.identity, _previewMaterial, 0, _previewUtility.camera);
                _previewUtility.camera.Render();
                Texture tex = _previewUtility.EndPreview();
                GUI.DrawTexture(cellRect, tex);
            }

            GUI.Label(new Rect(cellRect.x + 4, cellRect.y + 4, 80, 20), $"Seed {_seed + idx}", EditorStyles.miniBoldLabel);
        }
    }

    private void DrawValidationInfo()
    {
        if (_cachedData == null) return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Bounds: {_cachedData.bounds.x} × {_cachedData.bounds.y} × {_cachedData.bounds.z}", GUILayout.Width(160));
        EditorGUILayout.LabelField($"Blocks: {_cachedData.blocks.Length}", GUILayout.Width(100));

        // Count logs and leaves
        int logs = 0, leaves = 0;
        byte logId = _selectedRecipe.logBlock != null ? _selectedRecipe.logBlock.blockId : (byte)7;
        byte leafId = _selectedRecipe.leafBlock != null ? _selectedRecipe.leafBlock.blockId : (byte)8;

        for (int i = 0; i < _cachedData.blocks.Length; i++)
        {
            if (_cachedData.blocks[i].blockId == logId) logs++;
            else if (_cachedData.blocks[i].blockId == leafId) leaves++;
        }
        EditorGUILayout.LabelField($"Trunk/Branches: {logs}", GUILayout.Width(140));
        EditorGUILayout.LabelField($"Foliage: {leaves}", GUILayout.Width(120));
        EditorGUILayout.EndHorizontal();

        // Validation checks
        if (logs == 0)
        {
            EditorGUILayout.HelpBox("Validation Warning: No trunk/branch blocks generated!", MessageType.Warning);
        }
        if (leaves == 0)
        {
            EditorGUILayout.HelpBox("Validation Notice: No foliage generated for this configuration.", MessageType.Info);
        }
        EditorGUILayout.EndVertical();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Preview Camera & Mesh Builders
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleOrbitInput(Rect rect)
    {
        Event e = Event.current;
        if (!rect.Contains(e.mousePosition)) return;

        if (e.type == EventType.MouseDrag && e.button == 0)
        {
            _orbitAngles.y += e.delta.x * 0.8f;
            _orbitAngles.x = Mathf.Clamp(_orbitAngles.x + e.delta.y * 0.8f, -80f, 80f);
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseDrag && e.button == 2)
        {
            _panOffset.x += e.delta.x * 0.05f;
            _panOffset.y -= e.delta.y * 0.05f;
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.ScrollWheel)
        {
            _zoom = Mathf.Clamp(_zoom + e.delta.y * 0.8f, 3f, 40f);
            e.Use();
            Repaint();
        }
    }

    // ── Voxel Face Tables (matching VoxelMeshBuilder winding and normals) ────
    // 0=Top(+Y)  1=Bottom(-Y)  2=Front(+Z)  3=Back(-Z)  4=Left(-X)  5=Right(+X)
    private static readonly Vector3Int[] FaceNormals =
    {
        Vector3Int.up,
        Vector3Int.down,
        Vector3Int.forward,
        new Vector3Int(0, 0, -1),
        new Vector3Int(-1, 0, 0),
        Vector3Int.right
    };

    private static readonly Vector3[,] FaceVertices =
    {
        // Top (+Y)
        { new(0,1,0), new(0,1,1), new(1,1,1), new(1,1,0) },
        // Bottom (−Y)
        { new(0,0,0), new(1,0,0), new(1,0,1), new(0,0,1) },
        // Front (+Z)
        { new(0,0,1), new(1,0,1), new(1,1,1), new(0,1,1) },
        // Back (−Z)
        { new(1,0,0), new(0,0,0), new(0,1,0), new(1,1,0) },
        // Left (−X)
        { new(0,0,0), new(0,0,1), new(0,1,1), new(0,1,0) },
        // Right (+X)
        { new(1,0,1), new(1,0,0), new(1,1,0), new(1,1,1) },
    };

    private static readonly Vector2[] FaceUVs =
    {
        new(0, 0), new(0, 1), new(1, 1), new(1, 0)
    };

    private static readonly int[] QuadTriangles = { 0, 1, 2, 0, 2, 3 };

    private void SetupCamera(Camera cam, Vector2 angles, float distance, Vector2 pan)
    {
        Vector3 target = Vector3.zero;
        if (_cachedData != null && _cachedData.bounds != Vector3Int.zero)
        {
            // Center camera at vertical middle of the tree
            target = new Vector3(0f, _cachedData.bounds.y * 0.45f, 0f);
        }

        Quaternion rot = Quaternion.Euler(angles.x, angles.y, 0f);
        Vector3 center = target + new Vector3(pan.x, pan.y, 0f);
        cam.transform.position = center - rot * (Vector3.forward * distance);
        cam.transform.rotation = rot;
    }

    private void RefreshActiveRecipe()
    {
        if (_selectedRecipe == null) return;

        _cachedData = _selectedRecipe.GenerateData(_seed);
        if (_previewMesh != null) DestroyImmediate(_previewMesh);
        _previewMesh = BuildStructureMesh(_cachedData, _selectedRecipe);

        // Also refresh 3x3 variants if enabled
        for (int i = 0; i < 9; i++)
        {
            _gridData[i] = _selectedRecipe.GenerateData(_seed + i);
            if (_gridMeshes[i] != null) DestroyImmediate(_gridMeshes[i]);
            _gridMeshes[i] = BuildStructureMesh(_gridData[i], _selectedRecipe);
        }

        Repaint();
    }

    private static Mesh BuildStructureMesh(StructureData data, TreeRecipe recipe)
    {
        var vertices  = new List<Vector3>();
        var normals   = new List<Vector3>();
        var triangles = new List<int>();
        var colors    = new List<Color>();
        var uvs       = new List<Vector2>();

        if (data == null || data.blocks == null || data.blocks.Length == 0)
            return new Mesh();

        byte logId = recipe.logBlock != null ? recipe.logBlock.blockId : (byte)7;
        Color logColor = (recipe.logBlock != null && recipe.logBlock.placeholderColor.a > 0.01f)
            ? recipe.logBlock.placeholderColor
            : new Color(0.55f, 0.35f, 0.15f, 1f);
        Color leafColor = (recipe.leafBlock != null && recipe.leafBlock.placeholderColor.a > 0.01f)
            ? recipe.leafBlock.placeholderColor
            : new Color(0.20f, 0.62f, 0.16f, 1f);

        // Put all blocks into a dictionary for neighbor checking (interior face culling)
        var blockLookup = new Dictionary<Vector3Int, byte>(data.blocks.Length);
        for (int i = 0; i < data.blocks.Length; i++)
        {
            blockLookup[data.blocks[i].offset] = data.blocks[i].blockId;
        }

        for (int i = 0; i < data.blocks.Length; i++)
        {
            var b = data.blocks[i];
            Vector3 origin = b.offset;
            Color col = (b.blockId == logId) ? logColor : leafColor;

            for (int face = 0; face < 6; face++)
            {
                Vector3Int normal = FaceNormals[face];
                Vector3Int neighborPos = b.offset + normal;

                // Interior face culling: do not emit face if neighbor block is solid
                if (blockLookup.ContainsKey(neighborPos))
                    continue;

                int baseV = vertices.Count;
                for (int v = 0; v < 4; v++)
                {
                    vertices.Add(origin + FaceVertices[face, v]);
                    normals.Add((Vector3)normal);
                    colors.Add(col);
                    uvs.Add(FaceUVs[v]);
                }

                for (int t = 0; t < QuadTriangles.Length; t++)
                {
                    triangles.Add(baseV + QuadTriangles[t]);
                }
            }
        }

        var mesh = new Mesh();
        mesh.indexFormat = vertices.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateBounds();
        return mesh;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Baking Methods
    // ─────────────────────────────────────────────────────────────────────────

    private void BakeCurrentToTemplate()
    {
        if (_selectedRecipe == null || _cachedData == null) return;

        string dir = "Assets/ScriptableObjects/Voxel World/Structures";
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var template = CreateInstance<VoxelStructureTemplate>();
        template.FromData(_cachedData);

        string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{_selectedRecipe.name}_Baked_{_seed}.asset");
        AssetDatabase.CreateAsset(template, path);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Bake Complete", $"Saved structure template asset to:\n{path}", "OK");
    }

    private void BakeMultipleVariants(int count)
    {
        if (_selectedRecipe == null) return;

        string dir = "Assets/ScriptableObjects/Voxel World/Structures";
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        for (int i = 0; i < count; i++)
        {
            int variantSeed = _seed + i * 37;
            var data = _selectedRecipe.GenerateData(variantSeed);
            var template = CreateInstance<VoxelStructureTemplate>();
            template.FromData(data);

            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{_selectedRecipe.name}_Variant_{i + 1}.asset");
            AssetDatabase.CreateAsset(template, path);
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Batch Bake Complete", $"Baked {count} variant templates to:\n{dir}", "OK");
    }
}
