using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
//  VoxelDebugHUD — Minecraft-style (F3) in-game debug screen.
//
//  LEFT PANEL:
//  • Engine & Game: Unity version, smoothed FPS & frame time (ms), master seed.
//  • World Streaming: Active chunks, cached chunks, dirty remesh queue length.
//  • Player Position: XYZ float world coords, integer Block coords,
//    Chunk coords [cx, cz] and local coords within chunk [lx, ly, lz].
//  • Movement: Player speed (linear velocity) and grounded/falling state.
//  • Facing: Cardinal direction (North/East/South/West) + Yaw and Pitch angles.
//  • Biome & Climate: Active BiomeDefinition name, all 5 climate noise axes
//    (Continentalness, Erosion, Temperature, Humidity, Weirdness).
//  • Surface Height: Estimated surface Y at player column + delta from feet.
//
//  RIGHT PANEL:
//  • Targeted Block: Raycast target name, block ID, integer coords, local chunk coords,
//    hit face normal, distance, breakability, target column biome.
//  • Targeted Entity: If looking at a PlacedItem (robot, machine, etc.), shows
//    entity name, type, world position, distance, interactable state.
//  • System / Hardware: Screen resolution, allocated and reserved heap memory.
//
//  USAGE:
//  • Press [F3] at any time during play mode to toggle the debug screen.
//  • Fully autonomous: auto-instantiates at runtime on scene load if not
//    already in the scene hierarchy.
// ─────────────────────────────────────────────────────────────────────────────

public class VoxelDebugHUD : MonoBehaviour
{
    public static VoxelDebugHUD Instance { get; private set; }

    [Header("Controls")]
    [Tooltip("Key used to toggle the debug HUD on and off.")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F3;

    [Tooltip("Whether the HUD is visible immediately on game start.")]
    [SerializeField] private bool startsVisible = true;

    [Header("UI References (Optional - auto-created if unassigned)")]
    [SerializeField] private Canvas hudCanvas;
    [SerializeField] private TextMeshProUGUI leftText;
    [SerializeField] private TextMeshProUGUI rightText;
    [SerializeField] private TMP_FontAsset customFont;

    [Header("Appearance")]
    [SerializeField] private float fontSize = 14f;
    [SerializeField] private Color panelBackgroundColor = new Color(0.04f, 0.04f, 0.04f, 0.65f);

    [Header("Fallback")]
    [Tooltip("If true, also draws an immediate OnGUI overlay when Canvas/TMP is unavailable.")]
    [SerializeField] private bool enableOnGUIFallback = false;

    // State
    private bool _isVisible = true;
    private Transform _playerTransform;
    private Camera _playerCamera;
    private Rigidbody _playerRb;

    // Smoothed FPS
    private float _fpsAccumulator = 0f;
    private int _fpsFrames = 0;
    private float _fpsTimer = 0.2f;
    private float _currentFps = 60f;
    private float _currentMs = 16.6f;

    // String builders (zero GC allocation per frame)
    private readonly StringBuilder _leftSb = new StringBuilder(1024);
    private readonly StringBuilder _rightSb = new StringBuilder(1024);

    // Auto-spawn hook so the HUD works out-of-the-box in any scene without manual placement
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        if (FindFirstObjectByType<VoxelDebugHUD>() == null)
        {
            GameObject hudGo = new GameObject("VoxelDebugHUD");
            hudGo.AddComponent<VoxelDebugHUD>();
            DontDestroyOnLoad(hudGo);
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _isVisible = startsVisible;

        EnsureUIExists();
        UpdateUIVisibility();
    }

    private void Update()
    {
        // Toggle HUD on key press
        if (Input.GetKeyDown(toggleKey))
        {
            _isVisible = !_isVisible;
            UpdateUIVisibility();
        }

        // Toggle Dev Menu on F1 or F4
        if (Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.F4))
        {
            if (DevMenuController.Instance != null)
            {
                DevMenuController.Instance.ToggleMenu();
            }
            else
            {
                var existing = FindFirstObjectByType<DevMenuController>();
                if (existing != null)
                {
                    existing.ToggleMenu();
                }
                else
                {
                    var devGo = new GameObject("DevMenuController");
                    var dev = devGo.AddComponent<DevMenuController>();
                    dev.SetMenuOpen(true);
                }
            }
        }

        if (!_isVisible) return;

        UpdateFpsCounters();
        ResolvePlayerReferences();

        RefreshHUDText();
    }

    private void UpdateFpsCounters()
    {
        float dt = Time.unscaledDeltaTime;
        _fpsAccumulator += dt;
        _fpsFrames++;
        _fpsTimer -= dt;

        if (_fpsTimer <= 0f)
        {
            _currentFps = _fpsFrames / Mathf.Max(_fpsAccumulator, 0.0001f);
            _currentMs = (_fpsAccumulator / Mathf.Max(_fpsFrames, 1)) * 1000f;
            _fpsAccumulator = 0f;
            _fpsFrames = 0;
            _fpsTimer = 0.2f;
        }
    }

    private void ResolvePlayerReferences()
    {
        var worldManager = VoxelWorldManager.Instance;

        // Try getting player transform
        if (_playerTransform == null)
        {
            if (worldManager != null && worldManager.PlayerTransform != null)
                _playerTransform = worldManager.PlayerTransform;
            else if (Camera.main != null)
                _playerTransform = Camera.main.transform;
            else
            {
                var playerObj = GameObject.FindWithTag("Player");
                if (playerObj != null) _playerTransform = playerObj.transform;
            }

            if (_playerTransform != null)
                _playerRb = _playerTransform.GetComponent<Rigidbody>();
        }

        // Try getting player camera
        if (_playerCamera == null)
        {
            var raycaster = WorldRaycaster.Instance;
            if (raycaster != null && raycaster.PlayerCamera != null)
                _playerCamera = raycaster.PlayerCamera;
            else if (Camera.main != null)
                _playerCamera = Camera.main;
            else if (_playerTransform != null)
                _playerCamera = _playerTransform.GetComponentInChildren<Camera>();
        }
    }

    private void RefreshHUDText()
    {
        var worldManager = VoxelWorldManager.Instance;
        var settings = worldManager != null ? worldManager.Settings : null;
        var overworldGen = worldManager != null ? worldManager.WorldGenerator as OverworldGenerator : null;
        int seed = worldManager != null ? worldManager.Seed : 0;
        int chunkWidth = settings != null ? settings.chunkWidth : 16;

        // ═════════════════════════════════════════════════════════════════════
        //  LEFT PANEL: Engine, Position, Direction, Biome, Climate
        // ═════════════════════════════════════════════════════════════════════
        _leftSb.Clear();

        // 1. Header & Engine Info
        _leftSb.Append("<color=#55FFFF><b>Voxel World Debug (F3)</b></color> <color=#888888>(Unity ").Append(Application.unityVersion).AppendLine(")</color>");
        _leftSb.Append("<color=#AAAAAA>FPS:</color> <color=#55FF55>").Append(_currentFps.ToString("F0")).Append("</color>")
               .Append(" <color=#AAAAAA>(").Append(_currentMs.ToString("F1")).AppendLine(" ms)</color>");

        if (worldManager != null)
        {
            _leftSb.Append("<color=#AAAAAA>Seed:</color> <color=#FFFFFF>").Append(seed).AppendLine("</color>");
            _leftSb.Append("<color=#AAAAAA>Chunks:</color> <color=#55FF55>").Append(worldManager.ActiveChunkCount).Append("</color> active, ")
                   .Append("<color=#FFFFFF>").Append(worldManager.CachedChunkCount).Append("</color> cached, ")
                   .Append("<color=#FFFF55>").Append(worldManager.RebuildQueueCount).AppendLine("</color> queued");
        }
        else
        {
            _leftSb.AppendLine("<color=#888888>WorldManager: Not loaded in scene</color>");
        }

        _leftSb.AppendLine();

        // 2. Player Coordinates & Direction
        if (_playerTransform != null)
        {
            Vector3 pos = _playerTransform.position;
            int bx = Mathf.FloorToInt(pos.x);
            int by = Mathf.FloorToInt(pos.y);
            int bz = Mathf.FloorToInt(pos.z);

            int cx = Mathf.FloorToInt((float)bx / chunkWidth);
            int cz = Mathf.FloorToInt((float)bz / chunkWidth);
            int lx = bx - cx * chunkWidth;
            int ly = settings != null ? settings.WorldYToLocal(by) : by;
            int lz = bz - cz * chunkWidth;

            _leftSb.Append("<color=#AAAAAA>XYZ:</color> <color=#FFFFFF>")
                   .Append(pos.x.ToString("F3")).Append(" / ")
                   .Append(pos.y.ToString("F3")).Append(" / ")
                   .Append(pos.z.ToString("F3")).AppendLine("</color>");

            _leftSb.Append("<color=#AAAAAA>Block:</color> <color=#55FF55>")
                   .Append(bx).Append(" ").Append(by).Append(" ").Append(bz).AppendLine("</color>");

            _leftSb.Append("<color=#AAAAAA>Chunk:</color> [").Append(cx).Append(", ").Append(cz).Append("] <color=#AAAAAA>local:</color> [")
                   .Append(lx).Append(", ").Append(ly).Append(", ").Append(lz).AppendLine("]");

            // Speed if available
            if (_playerRb != null)
            {
                Vector3 vel = _playerRb.linearVelocity;
                float hSpeed = new Vector2(vel.x, vel.z).magnitude;
                _leftSb.Append("<color=#AAAAAA>Speed:</color> <color=#FFFFFF>")
                       .Append(hSpeed.ToString("F1")).Append(" m/s</color> <color=#888888>(vert: ")
                       .Append(vel.y.ToString("F1")).AppendLine(" m/s)</color>");
            }

            // Facing direction
            Transform lookTr = _playerCamera != null ? _playerCamera.transform : _playerTransform;
            float yaw = (lookTr.eulerAngles.y % 360f + 360f) % 360f;
            float pitch = lookTr.eulerAngles.x;
            if (pitch > 180f) pitch -= 360f;

            string cardinal = GetCardinalFacing(yaw);
            _leftSb.Append("<color=#AAAAAA>Facing:</color> <color=#FFFFFF>").Append(cardinal).Append("</color>")
                   .Append(" <color=#888888>(Yaw: ").Append(yaw.ToString("F1")).Append(" / Pitch: ").Append(pitch.ToString("F1")).AppendLine(")</color>");

            _leftSb.AppendLine();

            // 3. Biome & Climate
            if (overworldGen != null)
            {
                var biome = overworldGen.GetBiome(bx, bz, seed);
                string biomeName = biome != null ? biome.biomeName : "Default";

                var cp = overworldGen.SampleClimate(bx, bz, seed);
                int surfaceY = overworldGen.GetSurfaceY(bx, bz, seed, settings);
                int yOffset = surfaceY - by;

                _leftSb.Append("<color=#AAAAAA>Biome:</color> <color=#FFFF55><b>").Append(biomeName).AppendLine("</b></color>");
                _leftSb.Append("<color=#AAAAAA>Climate:</color> ")
                       .Append("<color=#888888>C:</color><b>").Append(cp.Continentalness.ToString("F2")).Append("</b> ")
                       .Append("<color=#888888>E:</color><b>").Append(cp.Erosion.ToString("F2")).Append("</b> ")
                       .Append("<color=#888888>T:</color><b>").Append(cp.Temperature.ToString("F2")).Append("</b> ")
                       .Append("<color=#888888>H:</color><b>").Append(cp.Humidity.ToString("F2")).Append("</b> ")
                       .Append("<color=#888888>W:</color><b>").Append(cp.Weirdness.ToString("F2")).AppendLine("</b>");

                _leftSb.Append("<color=#AAAAAA>Surface Y (Est):</color> <color=#55FF55>").Append(surfaceY).Append("</color>")
                       .Append(" <color=#888888>(delta: ").Append(yOffset >= 0 ? "+" + yOffset : yOffset.ToString()).AppendLine(")</color>");
            }
        }
        else
        {
            _leftSb.AppendLine("<color=#FF5555>Player: Spawning / Not found</color>");
        }

        _leftSb.AppendLine();
        _leftSb.Append("<color=#666666>[F3] Toggle HUD  |  [F1] Dev Menu</color>");

        if (leftText != null)
            leftText.text = _leftSb.ToString();

        // ═════════════════════════════════════════════════════════════════════
        //  RIGHT PANEL: Targeted Block / Entity, System Info
        // ═════════════════════════════════════════════════════════════════════
        _rightSb.Clear();

        PopulateTargetedBlockInfo(_rightSb, worldManager, settings, overworldGen, seed, chunkWidth);

        // System memory info
        _rightSb.AppendLine();
        _rightSb.Append("<color=#55FFFF><b>System / Hardware:</b></color>").AppendLine();
        long allocatedMem = Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);
        long reservedMem = Profiler.GetTotalReservedMemoryLong() / (1024 * 1024);
        _rightSb.Append("<color=#AAAAAA>Resolution:</color> ").Append(Screen.width).Append("x").Append(Screen.height).AppendLine();
        _rightSb.Append("<color=#AAAAAA>Memory:</color> ").Append(allocatedMem).Append("MB alloc / ").Append(reservedMem).AppendLine("MB reserved");

        if (rightText != null)
            rightText.text = _rightSb.ToString();
    }

    private void PopulateTargetedBlockInfo(StringBuilder sb, VoxelWorldManager worldManager, VoxelWorldSettings settings, OverworldGenerator overworldGen, int seed, int chunkWidth)
    {
        var raycaster = WorldRaycaster.Instance;
        bool hasHit = false;
        Vector3 hitPoint = Vector3.zero;
        Vector3 hitNormal = Vector3.up;
        Collider hitCollider = null;
        PlacedItem hoveredEntity = null;

        if (raycaster != null && raycaster.HasHit)
        {
            hasHit = true;
            hitPoint = raycaster.Point;
            hitNormal = raycaster.Normal;
            hitCollider = raycaster.Collider;
            hoveredEntity = raycaster.HoveredEntity;
        }
        else if (_playerCamera != null)
        {
            // Fallback raycast if WorldRaycaster has no hit or is absent
            Vector3 fallbackOrigin = _playerCamera.transform.position + _playerCamera.transform.forward * 0.35f;
            Ray ray = new Ray(fallbackOrigin, _playerCamera.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore))
            {
                hasHit = true;
                hitPoint = hit.point;
                hitNormal = hit.normal;
                hitCollider = hit.collider;
                hoveredEntity = hitCollider.GetComponentInParent<PlacedItem>();
            }
        }

        if (!hasHit)
        {
            sb.Append("<color=#888888><b>Targeted Block:</b></color> <color=#666666>None (Air / Out of range)</color>").AppendLine();
            return;
        }

        // 1. Is it a PlacedItem / entity?
        if (hoveredEntity != null)
        {
            string entityName = hoveredEntity.ItemData != null ? hoveredEntity.ItemData.itemName : hoveredEntity.gameObject.name;
            float dist = _playerCamera != null ? Vector3.Distance(_playerCamera.transform.position, hitPoint) : 0f;

            sb.Append("<color=#55FFFF><b>Targeted Entity:</b></color>").AppendLine();
            sb.Append("  <color=#AAAAAA>Name:</color> <color=#FFFF55><b>").Append(entityName).AppendLine("</b></color>");
            sb.Append("  <color=#AAAAAA>Type:</color> <color=#FFFFFF>").Append(hoveredEntity.GetType().Name).AppendLine("</color>");
            sb.Append("  <color=#AAAAAA>Distance:</color> <color=#FFFFFF>").Append(dist.ToString("F2")).AppendLine("m</color>");
            sb.Append("  <color=#AAAAAA>Interactable:</color> <color=#FFFFFF>").Append(hoveredEntity.IsInteractable).AppendLine("</color>");
            return;
        }

        // 2. Otherwise it's a voxel block
        Vector3 blockCenter = hitPoint - hitNormal * WorldRaycaster.HitBias;
        Vector3Int targetPos = VoxelBlockPlacer.WorldPointToBlockCoord(blockCenter);
        float distance = _playerCamera != null ? Vector3.Distance(_playerCamera.transform.position, hitPoint) : 0f;

        byte blockId = 0;
        string blockName = "Air";
        bool isBreakable = false;

        if (worldManager != null)
        {
            blockId = worldManager.GetBlockAt(targetPos.x, targetPos.y, targetPos.z);
            // Secondary sample if grazing edge hit Air
            if (blockId == 0)
            {
                Vector3 deeperCenter = hitPoint - hitNormal * (WorldRaycaster.HitBias * 1.5f);
                Vector3Int deeperPos = VoxelBlockPlacer.WorldPointToBlockCoord(deeperCenter);
                byte deeperId = worldManager.GetBlockAt(deeperPos.x, deeperPos.y, deeperPos.z);
                if (deeperId != 0)
                {
                    targetPos = deeperPos;
                    blockId = deeperId;
                }
            }

            if (settings != null && settings.blockRegistry != null)
            {
                var blockDef = settings.blockRegistry.GetBlock(blockId);
                if (blockDef != null)
                {
                    blockName = blockDef.blockName;
                    isBreakable = !blockDef.isUnbreakable;
                }
                else if (blockId == 0)
                {
                    blockName = "Air";
                }
                else
                {
                    blockName = $"Unknown Block ({blockId})";
                }
            }
        }

        int tcx = Mathf.FloorToInt((float)targetPos.x / chunkWidth);
        int tcz = Mathf.FloorToInt((float)targetPos.z / chunkWidth);
        int tlx = targetPos.x - tcx * chunkWidth;
        int tly = settings != null ? settings.WorldYToLocal(targetPos.y) : targetPos.y;
        int tlz = targetPos.z - tcz * chunkWidth;

        sb.Append("<color=#55FFFF><b>Targeted Block:</b></color>").AppendLine();
        sb.Append("  <color=#AAAAAA>Name:</color> <color=#FFFF55><b>").Append(blockName).Append("</b></color>")
          .Append(" <color=#888888>(ID: ").Append(blockId).AppendLine(")</color>");
        sb.Append("  <color=#AAAAAA>Pos:</color> <color=#FFFFFF>").Append(targetPos.x).Append(", ").Append(targetPos.y).Append(", ").Append(targetPos.z).AppendLine("</color>");
        sb.Append("  <color=#AAAAAA>Chunk:</color> [").Append(tcx).Append(", ").Append(tcz).Append("] <color=#AAAAAA>local:</color> [")
          .Append(tlx).Append(", ").Append(tly).Append(", ").Append(tlz).AppendLine("]");
        sb.Append("  <color=#AAAAAA>Face:</color> <color=#55FF55>").Append(GetFaceName(hitNormal)).AppendLine("</color>");
        sb.Append("  <color=#AAAAAA>Distance:</color> <color=#FFFFFF>").Append(distance.ToString("F2")).AppendLine("m</color>");
        sb.Append("  <color=#AAAAAA>Breakable:</color> ")
          .Append(isBreakable ? "<color=#55FF55>True</color>" : "<color=#FF5555>False (Unbreakable)</color>").AppendLine();

        if (overworldGen != null)
        {
            var targetBiome = overworldGen.GetBiome(targetPos.x, targetPos.z, seed);
            if (targetBiome != null)
            {
                sb.Append("  <color=#AAAAAA>Target Biome:</color> <color=#FFFFFF>").Append(targetBiome.biomeName).AppendLine("</color>");
            }
        }
    }

    private string GetCardinalFacing(float yaw)
    {
        if (yaw >= 315f || yaw < 45f) return "North (Towards +Z)";
        if (yaw >= 45f && yaw < 135f) return "East (Towards +X)";
        if (yaw >= 135f && yaw < 225f) return "South (Towards -Z)";
        return "West (Towards -X)";
    }

    private string GetFaceName(Vector3 normal)
    {
        if (normal.y > 0.5f) return "Top (+Y)";
        if (normal.y < -0.5f) return "Bottom (-Y)";
        if (normal.z > 0.5f) return "North (+Z)";
        if (normal.z < -0.5f) return "South (-Z)";
        if (normal.x > 0.5f) return "East (+X)";
        if (normal.x < -0.5f) return "West (-X)";
        return $"({normal.x:F1}, {normal.y:F1}, {normal.z:F1})";
    }

    private void UpdateUIVisibility()
    {
        if (hudCanvas != null)
            hudCanvas.gameObject.SetActive(_isVisible);
    }

    // ── Auto-construction of Canvas & TMP UI ──────────────────────────────────

    private void EnsureUIExists()
    {
        if (hudCanvas != null && leftText != null && rightText != null) return;

        // 1. Root Canvas
        GameObject canvasGo = new GameObject("VoxelDebugHUD_Canvas");
        canvasGo.transform.SetParent(transform, false);

        hudCanvas = canvasGo.AddComponent<Canvas>();
        hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hudCanvas.sortingOrder = 30000; // Above all normal game UI

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        // 2. Left Panel
        if (leftText == null)
        {
            GameObject leftPanel = CreatePanel(canvasGo.transform, "LeftDebugPanel", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -12f));
            leftText = CreateTMPText(leftPanel.transform, "LeftText", TextAlignmentOptions.TopLeft);
        }

        // 3. Right Panel
        if (rightText == null)
        {
            GameObject rightPanel = CreatePanel(canvasGo.transform, "RightDebugPanel", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -12f));
            rightText = CreateTMPText(rightPanel.transform, "RightText", TextAlignmentOptions.TopLeft);
        }
    }

    private GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel.transform.SetParent(parent, false);

        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;

        Image img = panel.GetComponent<Image>();
        img.color = panelBackgroundColor;

        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 14, 10, 10);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = panel.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return panel;
    }

    private TextMeshProUGUI CreateTMPText(Transform parent, string name, TextAlignmentOptions alignment)
    {
        GameObject textGo = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.color = Color.white;
        tmp.richText = true;
        tmp.alignment = alignment;

        if (customFont != null)
            tmp.font = customFont;
        else if (tmp.font == null && TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;

        return tmp;
    }

    // ── OnGUI Fallback ────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (!_isVisible || !enableOnGUIFallback) return;
        if (hudCanvas != null && hudCanvas.gameObject.activeInHierarchy && leftText != null && rightText != null) return;

        GUI.color = Color.white;
        GUILayout.BeginArea(new Rect(10, 10, 450, 400), GUI.skin.box);
        GUILayout.Label(_leftSb.ToString());
        GUILayout.EndArea();

        GUILayout.BeginArea(new Rect(Screen.width - 460, 10, 450, 400), GUI.skin.box);
        GUILayout.Label(_rightSb.ToString());
        GUILayout.EndArea();
    }
}
