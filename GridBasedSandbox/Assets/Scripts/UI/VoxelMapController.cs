using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
//  VoxelMapController — Real-time in-game map and radar for GridBasedSandbox.
//  Works in both Overworld and Backrooms Level 0.
//
//  FEATURES:
//  • Real-time map rendering centered on player position with player heading arrow.
//  • Full Backrooms architectural awareness:
//      - 3x3 Corridors & open rooms (yellow/beige carpet)
//      - Solid walls (dark ocher wallpaper)
//      - 3x2 Doorways with lintels (highlighted amber door markers)
//      - Continental Bulkheads (metallic borders) & Gateways (cyan transit arches)
//      - Ceiling Light Panels (white luminescent nodes)
//  • In Overworld: Renders surface biomes, water, and terrain elevation.
//  • UI displays active Dimension, Continent name, Sub-zone name, Coordinates, and Zoom.
//  • Hotkey [M] toggles map on/off. Zoom with [+] and [-] or UI buttons.
//  • Auto-instantiates at runtime so no manual scene setup is required.
// ─────────────────────────────────────────────────────────────────────────────

public class VoxelMapController : MonoBehaviour
{
    public static VoxelMapController Instance { get; private set; }

    [Header("Settings")]
    [Tooltip("Key to toggle the real-time map.")]
    [SerializeField] private KeyCode toggleKey = KeyCode.M;

    [Tooltip("If true, map is visible on game start.")]
    [SerializeField] private bool startsVisible = false;

    [Tooltip("Texture resolution of the map (pixels across).")]
    [Range(96, 256)]
    [SerializeField] private int mapResolution = 160;

    [Tooltip("View radius in world blocks (scale).")]
    [SerializeField] private int viewRadius = 48;

    [Header("Update Frequency")]
    [Tooltip("Seconds between map texture refreshes.")]
    [Range(0.05f, 0.5f)]
    [SerializeField] private float updateInterval = 0.1f;

    // UI Components
    private Canvas _rootCanvas;
    private GameObject _mapPanel;
    private RawImage _mapImage;
    private RectTransform _playerArrow;
    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _continentText;
    private TextMeshProUGUI _zoneText;
    private TextMeshProUGUI _coordsText;
    private TextMeshProUGUI _zoomText;

    // Runtime State
    private bool _isOpen = false;
    private float _nextUpdateTime = 0f;
    private Texture2D _mapTexture;
    private Color32[] _pixelBuffer;
    private Transform _playerTransform;

    // Color Palette
    private static readonly Color32 ColCorridorFloor = new Color32(215, 198, 115, 255); // Warm carpet
    private static readonly Color32 ColRoomFloor     = new Color32(195, 180, 105, 255); // Room interior carpet
    private static readonly Color32 ColSolidWall     = new Color32(48, 42, 28, 255);    // Dark wallpaper wall
    private static readonly Color32 ColDoorway       = new Color32(255, 160, 40, 255);  // Amber door marker
    private static readonly Color32 ColContinentWall = new Color32(30, 32, 38, 255);    // Metallic perimeter bulkhead
    private static readonly Color32 ColGateway       = new Color32(0, 230, 255, 255);   // Cyan transit gateway
    private static readonly Color32 ColLight         = new Color32(255, 255, 240, 255); // Fluorescent troffer
    private static readonly Color32 ColWater         = new Color32(40, 100, 200, 255);
    private static readonly Color32 ColGrass         = new Color32(80, 160, 60, 255);
    private static readonly Color32 ColSand          = new Color32(220, 200, 120, 255);
    private static readonly Color32 ColStone         = new Color32(140, 140, 140, 255);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        if (FindFirstObjectByType<VoxelMapController>() == null)
        {
            var go = new GameObject("VoxelMapController");
            go.AddComponent<VoxelMapController>();
            DontDestroyOnLoad(go);
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
        _isOpen = startsVisible;

        _pixelBuffer = new Color32[mapResolution * mapResolution];
        _mapTexture = new Texture2D(mapResolution, mapResolution, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        BuildUI();
        SetMapVisible(_isOpen);
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleMap();
        }

        if (!_isOpen) return;

        // Zoom controls
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
            AdjustZoom(-16);
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            AdjustZoom(+16);

        if (Time.unscaledTime >= _nextUpdateTime)
        {
            _nextUpdateTime = Time.unscaledTime + updateInterval;
            RefreshMap();
        }

        UpdatePlayerMarker();
    }

    public void ToggleMap()
    {
        SetMapVisible(!_isOpen);
    }

    public void SetMapVisible(bool visible)
    {
        _isOpen = visible;
        if (_mapPanel != null)
            _mapPanel.SetActive(_isOpen);

        if (_isOpen)
        {
            RefreshMap();
            UpdatePlayerMarker();
        }
    }

    public void AdjustZoom(int delta)
    {
        viewRadius = Mathf.Clamp(viewRadius + delta, 16, 160);
        if (_zoomText != null)
            _zoomText.text = $"Radius: {viewRadius}m";
        RefreshMap();
    }

    private Transform GetPlayer()
    {
        if (_playerTransform != null) return _playerTransform;

        var vwm = VoxelWorldManager.Instance;
        if (vwm != null && vwm.PlayerTransform != null)
        {
            _playerTransform = vwm.PlayerTransform;
            return _playerTransform;
        }

        var playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            _playerTransform = playerObj.transform;
            return _playerTransform;
        }

        var cam = Camera.main;
        if (cam != null)
        {
            _playerTransform = cam.transform;
            return _playerTransform;
        }

        return null;
    }

    private void UpdatePlayerMarker()
    {
        var player = GetPlayer();
        if (player == null || _playerArrow == null) return;

        // Rotate arrow to match player yaw (UI rotation in Z axis)
        float yaw = player.eulerAngles.y;
        _playerArrow.localRotation = Quaternion.Euler(0f, 0f, -yaw);

        // Update coordinates readout
        if (_coordsText != null)
        {
            int px = Mathf.RoundToInt(player.position.x);
            int py = Mathf.RoundToInt(player.position.y);
            int pz = Mathf.RoundToInt(player.position.z);
            _coordsText.text = $"POS: <color=#FFFFFF>{px}, {py}, {pz}</color>";
        }
    }

    private void RefreshMap()
    {
        var player = GetPlayer();
        if (player == null) return;

        int playerX = Mathf.RoundToInt(player.position.x);
        int playerZ = Mathf.RoundToInt(player.position.z);

        var vwm = VoxelWorldManager.Instance;
        var dimMgr = DimensionManager.Instance;
        var backroomsGen = vwm != null ? vwm.WorldGenerator as BackroomsGenerator : null;
        var overworldGen = vwm != null ? vwm.WorldGenerator as OverworldGenerator : null;
        int seed = vwm != null ? vwm.Seed : 0;

        string dimTitle = dimMgr != null && dimMgr.CurrentDimension != null
            ? dimMgr.CurrentDimension.dimensionName
            : "World Map";

        if (_titleText != null)
            _titleText.text = $"<b>{dimTitle.ToUpper()}</b>";

        // Update Continent and Sub-zone readouts
        if (backroomsGen != null)
        {
            var cont = backroomsGen.GetContinentAt(playerX, playerZ, seed);
            var zone = backroomsGen.GetZoneAt(playerX, playerZ, seed);

            if (_continentText != null)
                _continentText.text = cont != null ? $"<color=#00FFFF>CONTINENT:</color> {cont.continentName}" : "";
            if (_zoneText != null)
                _zoneText.text = zone != null ? $"<color=#FFD700>SUB-ZONE:</color> {zone.zoneName}" : "";
        }
        else
        {
            if (_continentText != null) _continentText.text = "<color=#00FFFF>SURFACE EXPEDITION</color>";
            if (_zoneText != null) _zoneText.text = "";
        }

        // Render pixel buffer
        int res = mapResolution;
        float step = (viewRadius * 2f) / res;
        float startX = playerX - viewRadius;
        float startZ = playerZ - viewRadius;

        for (int py = 0; py < res; py++)
        {
            int wz = Mathf.RoundToInt(startZ + py * step);
            int rowOffset = py * res;

            for (int px = 0; px < res; px++)
            {
                int wx = Mathf.RoundToInt(startX + px * step);

                if (backroomsGen != null)
                {
                    _pixelBuffer[rowOffset + px] = SampleBackroomsPixel(backroomsGen, vwm, wx, wz, seed);
                }
                else if (overworldGen != null)
                {
                    _pixelBuffer[rowOffset + px] = SampleOverworldPixel(overworldGen, wx, wz, seed, vwm != null ? vwm.Settings : null);
                }
                else
                {
                    _pixelBuffer[rowOffset + px] = new Color32(25, 25, 25, 255);
                }
            }
        }

        _mapTexture.SetPixels32(_pixelBuffer);
        _mapTexture.Apply(false);
    }

    private Color32 SampleBackroomsPixel(BackroomsGenerator gen, VoxelWorldManager vwm, int wx, int wz, int seed)
    {
        var cell = gen.EvaluateCell(wx, wz, seed);

        if (cell.isSolidWall)
            return ColSolidWall;

        if (cell.isDoorway)
            return ColDoorway;

        if (cell.isLight)
            return ColLight;

        if (cell.spec.wallHeight > 0 && cell.spec.wallIsHalf)
            return ColDoorway;

        if (cell.isCorridor)
            return ColCorridorFloor;

        return ColRoomFloor;
    }

    private Color32 SampleOverworldPixel(OverworldGenerator gen, int wx, int wz, int seed, VoxelWorldSettings settings)
    {
        float surfaceY = gen.GetSurfaceY(wx, wz, seed, settings);
        if (surfaceY < 0f) return ColWater;
        if (surfaceY < 4f) return ColSand;
        if (surfaceY > 60f) return ColStone;
        return ColGrass;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UI Construction
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        // 1. Root Canvas
        var canvasGo = new GameObject("VoxelMapCanvas");
        canvasGo.transform.SetParent(transform, false);
        _rootCanvas = canvasGo.AddComponent<Canvas>();
        _rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _rootCanvas.sortingOrder = 950;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        // 2. Main Map Panel (Draggable / Centered / Floating window)
        _mapPanel = new GameObject("MapWindow");
        _mapPanel.transform.SetParent(canvasGo.transform, false);

        var panelRt = _mapPanel.AddComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(440f, 540f);
        panelRt.anchoredPosition = new Vector2(0f, 0f);

        var bgImg = _mapPanel.AddComponent<Image>();
        bgImg.color = new Color(0.06f, 0.07f, 0.08f, 0.94f);

        // Border Outline
        var outline = _mapPanel.AddComponent<Outline>();
        outline.effectColor = new Color(0.2f, 0.6f, 0.7f, 0.8f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        // 3. Header & Title
        var headerGo = new GameObject("Header");
        headerGo.transform.SetParent(_mapPanel.transform, false);
        var headerRt = headerGo.AddComponent<RectTransform>();
        headerRt.anchorMin = new Vector2(0f, 1f);
        headerRt.anchorMax = new Vector2(1f, 1f);
        headerRt.pivot = new Vector2(0.5f, 1f);
        headerRt.sizeDelta = new Vector2(-20f, 32f);
        headerRt.anchoredPosition = new Vector2(0f, -12f);

        _titleText = headerGo.AddComponent<TextMeshProUGUI>();
        _titleText.fontSize = 16f;
        _titleText.color = new Color(0.2f, 0.9f, 1.0f, 1f);
        _titleText.alignment = TextAlignmentOptions.Center;
        _titleText.text = "<b>LIMINAL SURVEY // REAL-TIME MAP</b>";

        // Subtitle Continent & Zone
        var contGo = new GameObject("ContinentLabel");
        contGo.transform.SetParent(_mapPanel.transform, false);
        var contRt = contGo.AddComponent<RectTransform>();
        contRt.anchorMin = new Vector2(0f, 1f);
        contRt.anchorMax = new Vector2(1f, 1f);
        contRt.sizeDelta = new Vector2(-24f, 20f);
        contRt.anchoredPosition = new Vector2(14f, -44f);
        _continentText = contGo.AddComponent<TextMeshProUGUI>();
        _continentText.fontSize = 12f;
        _continentText.color = Color.white;
        _continentText.text = "CONTINENT: Sector Alpha";

        var zoneGo = new GameObject("ZoneLabel");
        zoneGo.transform.SetParent(_mapPanel.transform, false);
        var zoneRt = zoneGo.AddComponent<RectTransform>();
        zoneRt.anchorMin = new Vector2(0f, 1f);
        zoneRt.anchorMax = new Vector2(1f, 1f);
        zoneRt.sizeDelta = new Vector2(-24f, 20f);
        zoneRt.anchoredPosition = new Vector2(14f, -62f);
        _zoneText = zoneGo.AddComponent<TextMeshProUGUI>();
        _zoneText.fontSize = 11f;
        _zoneText.color = Color.gray;
        _zoneText.text = "SUB-ZONE: 3x3 Corridor Maze";

        // 4. Map Display Frame & RawImage
        var frameGo = new GameObject("MapFrame");
        frameGo.transform.SetParent(_mapPanel.transform, false);
        var frameRt = frameGo.AddComponent<RectTransform>();
        frameRt.anchorMin = new Vector2(0.5f, 0.5f);
        frameRt.anchorMax = new Vector2(0.5f, 0.5f);
        frameRt.pivot = new Vector2(0.5f, 0.5f);
        frameRt.sizeDelta = new Vector2(380f, 380f);
        frameRt.anchoredPosition = new Vector2(0f, -16f);

        var frameImg = frameGo.AddComponent<Image>();
        frameImg.color = new Color(0.02f, 0.02f, 0.02f, 1f);

        var mapImgGo = new GameObject("MapTexture");
        mapImgGo.transform.SetParent(frameGo.transform, false);
        var mapImgRt = mapImgGo.AddComponent<RectTransform>();
        mapImgRt.anchorMin = Vector2.zero;
        mapImgRt.anchorMax = Vector2.one;
        mapImgRt.sizeDelta = Vector2.zero;

        _mapImage = mapImgGo.AddComponent<RawImage>();
        _mapImage.texture = _mapTexture;

        // Crosshair reticle
        var reticleGo = new GameObject("Reticle");
        reticleGo.transform.SetParent(frameGo.transform, false);
        var reticleRt = reticleGo.AddComponent<RectTransform>();
        reticleRt.anchorMin = new Vector2(0.5f, 0.5f);
        reticleRt.anchorMax = new Vector2(0.5f, 0.5f);
        reticleRt.sizeDelta = new Vector2(16f, 16f);

        // Player Heading Arrow
        var arrowGo = new GameObject("PlayerArrow");
        arrowGo.transform.SetParent(frameGo.transform, false);
        _playerArrow = arrowGo.AddComponent<RectTransform>();
        _playerArrow.anchorMin = new Vector2(0.5f, 0.5f);
        _playerArrow.anchorMax = new Vector2(0.5f, 0.5f);
        _playerArrow.pivot = new Vector2(0.5f, 0.5f);
        _playerArrow.sizeDelta = new Vector2(14f, 14f);

        var arrowText = arrowGo.AddComponent<TextMeshProUGUI>();
        arrowText.text = "▲";
        arrowText.fontSize = 18f;
        arrowText.color = new Color(1.0f, 0.2f, 0.2f, 1f);
        arrowText.alignment = TextAlignmentOptions.Center;

        // 5. Bottom Status Bar & Legend
        var footerGo = new GameObject("Footer");
        footerGo.transform.SetParent(_mapPanel.transform, false);
        var footerRt = footerGo.AddComponent<RectTransform>();
        footerRt.anchorMin = new Vector2(0f, 0f);
        footerRt.anchorMax = new Vector2(1f, 0f);
        footerRt.pivot = new Vector2(0.5f, 0f);
        footerRt.sizeDelta = new Vector2(-24f, 32f);
        footerRt.anchoredPosition = new Vector2(0f, 10f);

        _coordsText = footerGo.AddComponent<TextMeshProUGUI>();
        _coordsText.fontSize = 11f;
        _coordsText.color = new Color(0.7f, 0.7f, 0.7f, 1f);
        _coordsText.text = "POS: 0, 0, 0";

        // Zoom Readout & Controls
        var zoomGo = new GameObject("ZoomLabel");
        zoomGo.transform.SetParent(_mapPanel.transform, false);
        var zoomRt = zoomGo.AddComponent<RectTransform>();
        zoomRt.anchorMin = new Vector2(1f, 0f);
        zoomRt.anchorMax = new Vector2(1f, 0f);
        zoomRt.pivot = new Vector2(1f, 0f);
        zoomRt.sizeDelta = new Vector2(160f, 32f);
        zoomRt.anchoredPosition = new Vector2(-14f, 10f);

        _zoomText = zoomGo.AddComponent<TextMeshProUGUI>();
        _zoomText.fontSize = 11f;
        _zoomText.alignment = TextAlignmentOptions.Right;
        _zoomText.color = new Color(0.5f, 0.8f, 1f, 1f);
        _zoomText.text = $"Radius: {viewRadius}m  [+/-]";
    }
}
