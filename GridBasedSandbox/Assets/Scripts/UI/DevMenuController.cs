using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UIButton = UnityEngine.UI.Button;

// ─────────────────────────────────────────────────────────────────────────────
//  DevMenuController — Developer & Creative Debug Menu.
//
//  Aesthetic & Architecture:
//  • Built in the identical minimalist dark-slate style as VoxelDebugHUD (F3)
//    and InGameIDEController (IDE).
//  • Runtime auto-initialization via [RuntimeInitializeOnLoadMethod] — works
//    in any scene immediately without manual Inspector configuration.
//  • Draggable header bar, collapsible floating dock mode, tabbed organization.
//  • Comprehensive Flight Mode system with velocity tuning, quick speed presets,
//    fine-step buttons, No-Clip toggle, 3D look flight toggle, and boost controls.
//  • World diagnostics, chunk remesh triggers, safe surface teleports, and quick
//    inventory item granting.
//
//  KEYBINDINGS:
//  • [F1] or [F4] : Toggle Dev Menu open / closed
//  • [Escape] : Close Dev Menu if open
// ─────────────────────────────────────────────────────────────────────────────

public class DevMenuController : MonoBehaviour
{
    public static DevMenuController Instance { get; private set; }

    [Header("Controls")]
    [Tooltip("Primary key used to toggle the Dev Menu on and off.")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F1;

    [Tooltip("Secondary key used to toggle the Dev Menu (convenient for keyboards with Fn lock).")]
    [SerializeField] private KeyCode secondaryToggleKey = KeyCode.F4;

    [Tooltip("Whether the Dev Menu is open at game start.")]
    [SerializeField] private bool startsOpen = false;

    [Header("Custom Font (Optional)")]
    [SerializeField] private TMP_FontAsset customFont;

    // Window States
    public enum WindowMode { Normal, Minimized }
    private WindowMode _windowMode = WindowMode.Normal;
    private bool _isOpen = false;

    // Tabs
    public enum DevTab { Flight, Teleport, World, Items }
    private DevTab _currentTab = DevTab.Flight;

    // Dynamic UI References
    private Canvas _rootCanvas;
    private RectTransform _windowRect;
    private RectTransform _bodyRect;
    private GameObject _tabsContainer;
    private GameObject _bottomStatusBar;

    // Minimized Mode Controls
    private TextMeshProUGUI _miniBarLabel;
    private GameObject _miniFlyToggleBtn;
    private TextMeshProUGUI _miniFlyToggleTxt;

    // Header Flying Badge
    private TextMeshProUGUI _headerFlyBadge;

    // Tab Panels
    private GameObject _flightPanel;
    private GameObject _teleportPanel;
    private GameObject _worldPanel;
    private GameObject _itemsPanel;
    private readonly List<UIButton> _tabButtons = new();
    private readonly List<TextMeshProUGUI> _tabButtonTexts = new();

    // Flight Tab Interactive Controls
    private Image _flightToggleBtnImg;
    private TextMeshProUGUI _flightToggleBtnTxt;
    private TextMeshProUGUI _flightStatusSubtext;
    private TextMeshProUGUI _velocityValueText;
    private Slider _flySpeedSlider;
    private Image _noClipBtnImg;
    private TextMeshProUGUI _noClipBtnTxt;
    private Image _camRelativeBtnImg;
    private TextMeshProUGUI _camRelativeBtnTxt;
    private TextMeshProUGUI _boostValText;

    // Teleport & Status Info
    private TextMeshProUGUI _posReadoutText;
    private TextMeshProUGUI _bottomStatusText;
    private TextMeshProUGUI _worldDiagnosticsText;

    // Customizable Item Palette
    private DevMenuItemPalette _palette;
    private string _activeItemCategory = "ALL";
    private int _selectedGrantAmount = 64;
    private Transform _itemsGridContainer;
    private TextMeshProUGUI _itemsFeedbackText;
    private Transform _categoryBtnContainer;
    private readonly List<string> _categoryButtonKeys = new();
    private readonly List<Image> _categoryButtonImgs = new();
    private readonly List<TextMeshProUGUI> _categoryButtonTexts = new();
    private readonly List<int> _qtyButtonAmounts = new();
    private readonly List<Image> _qtyButtonImgs = new();
    private readonly List<TextMeshProUGUI> _qtyButtonTexts = new();
    private readonly Dictionary<byte, ItemData> _runtimeItemDataCache = new();
    private Sprite _fallbackCubeIcon;

    // Dragging
    private bool _isDragging = false;
    private Vector2 _dragOffset;
    private readonly Vector2 _normalSize = new Vector2(560f, 520f);
    private readonly Vector2 _minimizedSize = new Vector2(320f, 38f);
    private Vector2 _normalPos = new Vector2(0f, 40f);

    // Modern Minimalist Palette (Exact match with IDE & F3)
    private static readonly Color BgWindow = new Color(0.07f, 0.08f, 0.11f, 0.96f);
    private static readonly Color BgHeader = new Color(0.05f, 0.06f, 0.08f, 1f);
    private static readonly Color BgCard = new Color(0.10f, 0.12f, 0.16f, 0.85f);
    private static readonly Color BorderSubtle = new Color(0.18f, 0.22f, 0.30f, 0.75f);
    private static readonly Color AmberAccent = new Color(0.96f, 0.60f, 0.12f, 1f);
    private static readonly Color DarkAmberText = new Color(0.06f, 0.08f, 0.12f, 1f);
    private static readonly Color EmeraldAccent = new Color(0.15f, 0.78f, 0.45f, 1f);
    private static readonly Color DarkBtn = new Color(0.14f, 0.17f, 0.23f, 1f);
    private static readonly Color TabActive = new Color(0.16f, 0.21f, 0.30f, 1f);
    private static readonly Color TabInactive = new Color(0.08f, 0.10f, 0.14f, 0.7f);
    private static readonly Color TextMuted = new Color(0.55f, 0.62f, 0.72f, 1f);

    // StringBuilder for zero-alloc per-frame updates
    private readonly StringBuilder _statusSb = new StringBuilder(256);
    private readonly StringBuilder _diagSb = new StringBuilder(512);

    // ── Auto-Spawn Hook ───────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        if (FindFirstObjectByType<DevMenuController>() == null)
        {
            GameObject devGo = new GameObject("DevMenuController");
            devGo.AddComponent<DevMenuController>();
            DontDestroyOnLoad(devGo);
            Debug.Log("[DevMenuController] Auto-initialized DevMenuController in scene.");
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
        _isOpen = startsOpen;

        EnsureUIExists();
        SetMenuOpen(_isOpen);
    }

    private void Start()
    {
        // Wire events from PlayerController if present
        var player = GetPlayerController();
        if (player != null)
        {
            player.OnFlightStateChanged += (flying) => RefreshFlightUI();
            player.OnFlySpeedChanged += (spd) => RefreshFlightUI();
            player.OnNoClipChanged += (nc) => RefreshFlightUI();
        }

        RefreshFlightUI();
        SwitchTab(DevTab.Flight);
    }

    private void Update()
    {
        // Toggle Dev Menu via F1 or F4
        if (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(secondaryToggleKey))
        {
            ToggleMenu();
        }

        // Close on Escape if open
        if (_isOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            SetMenuOpen(false);
        }

        UpdateWindowDragging();

        if (_isOpen)
        {
            UpdateLiveStats();
        }
    }

    private int _lastToggleFrame = -1;

    // ═══════════════════════════════════════════════════════════════════════════
    //  Public Menu API
    // ═══════════════════════════════════════════════════════════════════════════

    public void ToggleMenu()
    {
        if (Time.frameCount == _lastToggleFrame) return;
        _lastToggleFrame = Time.frameCount;
        SetMenuOpen(!_isOpen);
    }

    public void SetMenuOpen(bool open)
    {
        _isOpen = open;
        GameState.IsDevMenuOpen = _isOpen;

        EnsureUIExists();

        if (_windowRect != null)
            _windowRect.gameObject.SetActive(_isOpen);

        if (_isOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            RefreshFlightUI();
            UpdateLiveStats();
            if (_currentTab == DevTab.Items)
            {
                RebuildCategoryButtons();
                RebuildItemsGrid();
            }
        }
        else
        {
            // Only re-lock cursor if IDE is also closed
            if (!GameState.IsIDEOpen)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Flight Mode Actions
    // ═══════════════════════════════════════════════════════════════════════════

    public void ToggleFlightMode()
    {
        var player = GetPlayerController();
        if (player != null)
        {
            player.SetFlying(!player.IsFlying);
            RefreshFlightUI();
        }
    }

    public void SetFlightVelocity(float speed)
    {
        var player = GetPlayerController();
        if (player != null)
        {
            player.FlySpeed = speed;
            RefreshFlightUI();
        }
    }

    public void AdjustFlightVelocity(float delta)
    {
        var player = GetPlayerController();
        if (player != null)
        {
            player.FlySpeed = Mathf.Clamp(player.FlySpeed + delta, 1f, 100f);
            RefreshFlightUI();
        }
    }

    public void ToggleNoClip()
    {
        var player = GetPlayerController();
        if (player != null)
        {
            player.SetNoClip(!player.NoClip);
            RefreshFlightUI();
        }
    }

    public void ToggleCameraRelativeFly()
    {
        var player = GetPlayerController();
        if (player != null)
        {
            player.CameraRelativeFly = !player.CameraRelativeFly;
            RefreshFlightUI();
        }
    }

    public void SetSprintBoost(float multiplier)
    {
        var player = GetPlayerController();
        if (player != null)
        {
            player.FlySprintMultiplier = multiplier;
            RefreshFlightUI();
        }
    }

    private void RefreshFlightUI()
    {
        var player = GetPlayerController();
        bool isFlying = player != null && player.IsFlying;
        float speed = player != null ? player.FlySpeed : 16f;
        float boostMul = player != null ? player.FlySprintMultiplier : 2f;
        bool noClip = player != null && player.NoClip;
        bool camRelative = player != null && player.CameraRelativeFly;

        // Big Flight Toggle Button
        if (_flightToggleBtnImg != null)
            _flightToggleBtnImg.color = isFlying ? AmberAccent : DarkBtn;

        if (_flightToggleBtnTxt != null)
        {
            _flightToggleBtnTxt.text = isFlying ? "<b>FLY MODE: ENABLED (ON)</b>" : "<b>FLY MODE: DISABLED (OFF)</b>";
            _flightToggleBtnTxt.color = isFlying ? DarkAmberText : Color.white;
        }

        if (_flightStatusSubtext != null)
        {
            _flightStatusSubtext.text = isFlying
                ? "<color=#55FF55>• Flying Active</color> — Gravity suspended. [WASD] Move, [Space] Up, [Ctrl/C] Down, [Shift] Boost"
                : "<color=#94A3B8>• Normal Physics</color> — Walking & standard gravity active. Enable to fly freely.";
        }

        // Header Flying Badge
        if (_headerFlyBadge != null)
        {
            _headerFlyBadge.text = isFlying ? "<color=#55FF55><b>[FLY ON]</b></color>" : "<color=#64748B>[FLY OFF]</color>";
        }

        // Velocity readout & slider
        if (_velocityValueText != null)
        {
            _velocityValueText.text = $"<color=#AAAAAA>Fly Velocity:</color> <color=#55FF55><b>{speed:F1} m/s</b></color>  <color=#888888>(Sprint Boost: {(speed * boostMul):F1} m/s)</color>";
        }

        if (_flySpeedSlider != null && Mathf.Abs(_flySpeedSlider.value - speed) > 0.05f)
        {
            _flySpeedSlider.SetValueWithoutNotify(speed);
        }

        // No-Clip Button
        if (_noClipBtnImg != null)
            _noClipBtnImg.color = noClip ? EmeraldAccent : DarkBtn;
        if (_noClipBtnTxt != null)
        {
            _noClipBtnTxt.text = noClip ? "<b>NO-CLIP: ON</b>" : "<b>NO-CLIP: OFF</b>";
            _noClipBtnTxt.color = noClip ? Color.black : TextMuted;
        }

        // 3D Look Fly Button
        if (_camRelativeBtnImg != null)
            _camRelativeBtnImg.color = camRelative ? EmeraldAccent : DarkBtn;
        if (_camRelativeBtnTxt != null)
        {
            _camRelativeBtnTxt.text = camRelative ? "<b>3D LOOK FLY: ON</b>" : "<b>PLANAR FLY: ON</b>";
            _camRelativeBtnTxt.color = camRelative ? Color.black : TextMuted;
        }

        // Boost readout
        if (_boostValText != null)
        {
            _boostValText.text = $"<color=#AAAAAA>Sprint Boost:</color> <color=#FFFF55><b>{boostMul:F1}x</b></color>";
        }

        // Minimized Dock Bar Label & Toggle
        if (_miniBarLabel != null)
        {
            _miniBarLabel.text = $"<color=#55FFFF><b>DEV MENU</b></color> <color=#AAAAAA>|</color> {(isFlying ? "<color=#55FF55>FLY ON</color>" : "<color=#94A3B8>FLY OFF</color>")} <color=#FFFF55>{speed:F0}m/s</color>";
        }
        if (_miniFlyToggleTxt != null)
        {
            _miniFlyToggleTxt.text = isFlying ? "<color=#F59E0B><b>FLYING</b></color>" : "<color=#94A3B8>FLY</color>";
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Teleport & Player Actions
    // ═══════════════════════════════════════════════════════════════════════════

    public void TeleportToSafeSurface()
    {
        var player = GetPlayerController();
        if (player == null) return;

        Vector3 curPos = player.transform.position;
        int bx = Mathf.FloorToInt(curPos.x);
        int bz = Mathf.FloorToInt(curPos.z);

        Vector3 safePos = curPos + Vector3.up * 5f;
        var wm = VoxelWorldManager.Instance;
        if (wm != null)
        {
            safePos = wm.GetSpawnPosition(bx, bz);
        }

        player.Teleport(safePos);
    }

    public void TeleportNudgeUp(float deltaY)
    {
        var player = GetPlayerController();
        if (player == null) return;
        player.Teleport(player.transform.position + Vector3.up * deltaY);
    }

    public void TeleportToOrigin()
    {
        var player = GetPlayerController();
        if (player == null) return;

        Vector3 target = new Vector3(0.5f, 64f, 0.5f);
        var wm = VoxelWorldManager.Instance;
        if (wm != null)
        {
            target = wm.GetSpawnPosition(0, 0);
        }

        player.Teleport(target);
    }

    public void ResetPlayerVelocity()
    {
        var player = GetPlayerController();
        player?.ResetVelocity();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  World & Diagnostics Actions
    // ═══════════════════════════════════════════════════════════════════════════

    public void RebuildActiveChunks()
    {
        var wm = VoxelWorldManager.Instance;
        if (wm == null) return;

        var chunks = FindObjectsByType<VoxelChunk>(FindObjectsSortMode.None);
        foreach (var c in chunks)
        {
            if (c.gameObject.activeInHierarchy && c.Data != null)
            {
                wm.EnqueueRebuild(c.Data.ChunkCoord);
            }
        }
    }

    public void ToggleF3HUD()
    {
        var hud = VoxelDebugHUD.Instance;
        if (hud != null)
        {
            hud.enabled = !hud.enabled;
        }
    }

    public void ToggleIDE()
    {
        var ide = InGameIDEController.Instance;
        if (ide != null)
        {
            ide.ToggleIDE();
        }
    }

    public void ToggleDebugOres()
    {
        var wm = VoxelWorldManager.Instance;
        if (wm != null && wm.Settings != null)
        {
            wm.Settings.debugOresOnly = !wm.Settings.debugOresOnly;
            RebuildActiveChunks();
        }
    }

    public void AdjustViewDistance(int delta)
    {
        var wm = VoxelWorldManager.Instance;
        if (wm != null && wm.Settings != null)
        {
            wm.Settings.viewDistanceInChunks = Mathf.Clamp(wm.Settings.viewDistanceInChunks + delta, 1, 32);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Quick Item Granting (Palette Integration)
    // ═══════════════════════════════════════════════════════════════════════════

    public void EnsurePaletteLoaded()
    {
        if (_palette != null && _palette.items.Count > 0) return;

        _palette = DevMenuItemPalette.LoadPalette();

        if (_palette == null || _palette.items.Count == 0)
        {
            _palette = ScriptableObject.CreateInstance<DevMenuItemPalette>();
            BuildFallbackPalette(_palette);
        }

        if (_fallbackCubeIcon == null)
        {
            var allItems = Resources.FindObjectsOfTypeAll<ItemData>();
            foreach (var it in allItems)
            {
                if (it.icon != null)
                {
                    _fallbackCubeIcon = it.icon;
                    break;
                }
            }
        }
    }

    private void BuildFallbackPalette(DevMenuItemPalette targetPalette)
    {
        targetPalette.items.Clear();

        var wm = VoxelWorldManager.Instance;
        var registry = wm != null && wm.Settings != null ? wm.Settings.blockRegistry : null;
        if (registry == null)
        {
            var regs = Resources.FindObjectsOfTypeAll<VoxelBlockRegistry>();
            if (regs.Length > 0) registry = regs[0];
        }

        if (registry != null)
        {
            foreach (var b in registry.RegisteredBlocks)
            {
                if (b == null || b.blockId == 0) continue; // Skip Air
                string cat = "Terrain";
                string n = b.blockName.ToLowerInvariant();
                if (n.Contains("ore") || n.Contains("coal") || n.Contains("iron") || n.Contains("copper") || n.Contains("gold")) cat = "Ores";
                else if (n.Contains("log") || n.Contains("wood") || n.Contains("leaves")) cat = "Flora";
                else if (n.Contains("water") || n.Contains("fluid")) cat = "Fluids";

                targetPalette.items.Add(new DevMenuItemEntry
                {
                    displayName = b.blockName,
                    voxelBlockId = b.blockId,
                    isEntity = false,
                    category = cat,
                    defaultAmount = 64,
                    isEnabled = true
                });
            }
        }
        else
        {
            // Default real blocks
            targetPalette.items.Add(new DevMenuItemEntry { displayName = "Stone", voxelBlockId = 1, category = "Terrain", defaultAmount = 64, isEnabled = true });
            targetPalette.items.Add(new DevMenuItemEntry { displayName = "Dirt", voxelBlockId = 2, category = "Terrain", defaultAmount = 64, isEnabled = true });
            targetPalette.items.Add(new DevMenuItemEntry { displayName = "Grass", voxelBlockId = 3, category = "Terrain", defaultAmount = 64, isEnabled = true });
            targetPalette.items.Add(new DevMenuItemEntry { displayName = "Sand", voxelBlockId = 4, category = "Terrain", defaultAmount = 64, isEnabled = true });
            targetPalette.items.Add(new DevMenuItemEntry { displayName = "Water", voxelBlockId = 5, category = "Fluids", defaultAmount = 64, isEnabled = true });
            targetPalette.items.Add(new DevMenuItemEntry { displayName = "Bedrock", voxelBlockId = 6, category = "Terrain", defaultAmount = 64, isEnabled = true });
            targetPalette.items.Add(new DevMenuItemEntry { displayName = "Log", voxelBlockId = 7, category = "Flora", defaultAmount = 64, isEnabled = true });
            targetPalette.items.Add(new DevMenuItemEntry { displayName = "Leaves", voxelBlockId = 8, category = "Flora", defaultAmount = 64, isEnabled = true });
            targetPalette.items.Add(new DevMenuItemEntry { displayName = "Snow", voxelBlockId = 9, category = "Terrain", defaultAmount = 64, isEnabled = true });
            targetPalette.items.Add(new DevMenuItemEntry { displayName = "Gravel", voxelBlockId = 10, category = "Terrain", defaultAmount = 64, isEnabled = true });
            targetPalette.items.Add(new DevMenuItemEntry { displayName = "Coal Ore", voxelBlockId = 11, category = "Ores", defaultAmount = 64, isEnabled = true });
            targetPalette.items.Add(new DevMenuItemEntry { displayName = "Iron Ore", voxelBlockId = 12, category = "Ores", defaultAmount = 64, isEnabled = true });
            targetPalette.items.Add(new DevMenuItemEntry { displayName = "Copper Ore", voxelBlockId = 13, category = "Ores", defaultAmount = 64, isEnabled = true });
            targetPalette.items.Add(new DevMenuItemEntry { displayName = "Gold Ore", voxelBlockId = 14, category = "Ores", defaultAmount = 64, isEnabled = true });
        }

        targetPalette.items.Add(new DevMenuItemEntry { displayName = "Robot", voxelBlockId = 0, isEntity = true, category = "Entities", defaultAmount = 1, isEnabled = true });
    }

    public void GrantPaletteItem(DevMenuItemEntry entry, int amount = -1)
    {
        var invMgr = FindFirstObjectByType<InventoryManager>();
        if (invMgr == null) return;

        int finalAmount = amount > 0 ? amount : (entry.isEntity ? entry.defaultAmount : _selectedGrantAmount);

        // 1. Direct ItemData reference
        if (entry.itemData != null)
        {
            invMgr.TryAddItem(entry.itemData, finalAmount);
            invMgr.RefreshSelectedSlot();
            ShowItemGrantFeedback(entry.displayName, finalAmount);
            return;
        }

        // 2. Entity item (e.g. Robot)
        if (entry.isEntity)
        {
            GiveRobotItem(finalAmount);
            ShowItemGrantFeedback(entry.displayName, finalAmount);
            return;
        }

        // 3. Voxel Block
        GiveBlockItem(entry.voxelBlockId, entry.displayName, finalAmount);
        ShowItemGrantFeedback(entry.displayName, finalAmount);
    }

    public void GiveBlockItem(byte blockId, string blockName, int amount = 64)
    {
        var invMgr = FindFirstObjectByType<InventoryManager>();
        if (invMgr == null) return;

        if (!_runtimeItemDataCache.TryGetValue(blockId, out ItemData targetItem) || targetItem == null)
        {
            var allItems = Resources.FindObjectsOfTypeAll<ItemData>();
            foreach (var it in allItems)
            {
                if (!it.isEntity && it.voxelBlockId == blockId)
                {
                    targetItem = it;
                    break;
                }
            }

            if (targetItem == null)
            {
                var dyn = ScriptableObject.CreateInstance<ItemData>();
                dyn.id = $"voxel_{blockId}";
                dyn.itemName = blockName;
                dyn.voxelBlockId = blockId;
                dyn.isPlaceable = true;
                dyn.maxStackAmount = 64;
                dyn.icon = _fallbackCubeIcon;
                targetItem = dyn;
            }

            _runtimeItemDataCache[blockId] = targetItem;
        }

        invMgr.TryAddItem(targetItem, amount);
        invMgr.RefreshSelectedSlot();
    }

    public void GiveRobotItem(int amount = 1)
    {
        var invMgr = FindFirstObjectByType<InventoryManager>();
        if (invMgr == null) return;

        var allItems = Resources.FindObjectsOfTypeAll<ItemData>();
        ItemData robotItem = null;
        foreach (var it in allItems)
        {
            if (it.isEntity || it.itemName.IndexOf("Robot", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                robotItem = it;
                break;
            }
        }

        if (robotItem != null)
        {
            invMgr.TryAddItem(robotItem, amount);
            invMgr.RefreshSelectedSlot();
        }
    }

    private void ShowItemGrantFeedback(string itemName, int amount)
    {
        string msg = $"<color=#55FF55>Added {amount}x {itemName} to inventory!</color>";
        if (_itemsFeedbackText != null)
            _itemsFeedbackText.text = msg;
        if (_bottomStatusText != null)
            _bottomStatusText.text = msg;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Live Stats Update (Zero Garbage)
    // ═══════════════════════════════════════════════════════════════════════════

    private void UpdateLiveStats()
    {
        var player = GetPlayerController();
        Vector3 pos = player != null ? player.transform.position : Vector3.zero;
        int bx = Mathf.FloorToInt(pos.x);
        int by = Mathf.FloorToInt(pos.y);
        int bz = Mathf.FloorToInt(pos.z);

        var wm = VoxelWorldManager.Instance;
        int chunkW = wm != null && wm.Settings != null ? wm.Settings.chunkWidth : 16;
        int cx = Mathf.FloorToInt((float)bx / chunkW);
        int cz = Mathf.FloorToInt((float)bz / chunkW);

        // Status bar update
        _statusSb.Clear();
        _statusSb.Append("<color=#AAAAAA>Player:</color> <color=#FFFFFF>")
                 .Append(pos.x.ToString("F1")).Append(", ")
                 .Append(pos.y.ToString("F1")).Append(", ")
                 .Append(pos.z.ToString("F1")).Append("</color>  <color=#64748B>|</color>  ")
                 .Append("<color=#AAAAAA>Chunk:</color> <color=#55FF55>[").Append(cx).Append(", ").Append(cz).Append("]</color>  <color=#64748B>|</color>  ")
                 .Append("<color=#64748B>[F1/F4] Dev Menu  [F3] HUD</color>");

        if (_bottomStatusText != null)
            _bottomStatusText.text = _statusSb.ToString();

        // Position readout in Teleport tab
        if (_posReadoutText != null && _currentTab == DevTab.Teleport)
        {
            _posReadoutText.text = $"<color=#AAAAAA>World Coordinates:</color> <color=#FFFFFF>X: {pos.x:F2}  Y: {pos.y:F2}  Z: {pos.z:F2}</color>\n" +
                                   $"<color=#AAAAAA>Block Integer:</color> <color=#55FF55>{bx} {by} {bz}</color>  |  <color=#AAAAAA>Chunk:</color> [{cx}, {cz}]";
        }

        // Diagnostics readout in World tab
        if (_worldDiagnosticsText != null && _currentTab == DevTab.World)
        {
            _diagSb.Clear();
            _diagSb.Append("<color=#55FFFF><b>Voxel World Engine:</b></color>\n");
            if (wm != null)
            {
                _diagSb.Append("• <color=#AAAAAA>Seed:</color> <color=#FFFFFF>").Append(wm.Seed).Append("</color>\n");
                _diagSb.Append("• <color=#AAAAAA>Active Chunks:</color> <color=#55FF55>").Append(wm.ActiveChunkCount).Append("</color>\n");
                _diagSb.Append("• <color=#AAAAAA>Cached Chunks:</color> <color=#FFFFFF>").Append(wm.CachedChunkCount).Append("</color>\n");
                _diagSb.Append("• <color=#AAAAAA>Rebuild Queue:</color> <color=#FFFF55>").Append(wm.RebuildQueueCount).Append("</color>\n");
                if (wm.Settings != null)
                {
                    _diagSb.Append("• <color=#AAAAAA>View Distance:</color> <color=#55FF55>").Append(wm.Settings.viewDistanceInChunks).Append(" chunks</color>\n");
                    _diagSb.Append("• <color=#AAAAAA>Debug Ores Only:</color> ").Append(wm.Settings.debugOresOnly ? "<color=#55FF55>ON</color>" : "<color=#FF5555>OFF</color>").Append("\n");
                }
            }
            else
            {
                _diagSb.Append("<color=#FF5555>VoxelWorldManager not found in scene</color>\n");
            }
            _worldDiagnosticsText.text = _diagSb.ToString();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Tabs Management
    // ═══════════════════════════════════════════════════════════════════════════

    public void SwitchTab(DevTab tab)
    {
        _currentTab = tab;

        if (_flightPanel != null) _flightPanel.SetActive(_currentTab == DevTab.Flight);
        if (_teleportPanel != null) _teleportPanel.SetActive(_currentTab == DevTab.Teleport);
        if (_worldPanel != null) _worldPanel.SetActive(_currentTab == DevTab.World);
        if (_itemsPanel != null)
        {
            _itemsPanel.SetActive(_currentTab == DevTab.Items);
            if (_currentTab == DevTab.Items)
            {
                RebuildCategoryButtons();
                RebuildItemsGrid();
            }
        }

        // Refresh tab button visuals
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            bool isActive = (i == (int)_currentTab);
            var img = _tabButtons[i].GetComponent<Image>();
            if (img != null) img.color = isActive ? TabActive : TabInactive;
            if (i < _tabButtonTexts.Count && _tabButtonTexts[i] != null)
                _tabButtonTexts[i].color = isActive ? Color.white : TextMuted;
        }

        UpdateLiveStats();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Window Modes: Minimize, Dragging
    // ═══════════════════════════════════════════════════════════════════════════

    public void ToggleMinimize()
    {
        SetWindowMode(_windowMode == WindowMode.Minimized ? WindowMode.Normal : WindowMode.Minimized);
    }

    public void SetWindowMode(WindowMode mode)
    {
        _windowMode = mode;
        if (_windowRect == null) return;

        switch (_windowMode)
        {
            case WindowMode.Normal:
                _windowRect.sizeDelta = _normalSize;
                _windowRect.anchoredPosition = _normalPos;
                if (_bodyRect != null) _bodyRect.gameObject.SetActive(true);
                if (_tabsContainer != null) _tabsContainer.SetActive(true);
                if (_bottomStatusBar != null) _bottomStatusBar.SetActive(true);
                if (_miniBarLabel != null) _miniBarLabel.gameObject.SetActive(false);
                if (_miniFlyToggleBtn != null) _miniFlyToggleBtn.SetActive(false);
                break;

            case WindowMode.Minimized:
                _normalPos = _windowRect.anchoredPosition;
                _windowRect.sizeDelta = _minimizedSize;
                if (_bodyRect != null) _bodyRect.gameObject.SetActive(false);
                if (_tabsContainer != null) _tabsContainer.SetActive(false);
                if (_bottomStatusBar != null) _bottomStatusBar.SetActive(false);
                if (_miniBarLabel != null)
                {
                    _miniBarLabel.gameObject.SetActive(true);
                    RefreshFlightUI();
                }
                if (_miniFlyToggleBtn != null) _miniFlyToggleBtn.SetActive(true);
                break;
        }
    }

    public void OnTitleBarDragStart()
    {
        _isDragging = true;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _rootCanvas.transform as RectTransform,
            Input.mousePosition,
            null,
            out Vector2 mousePos);
        _dragOffset = _windowRect.anchoredPosition - mousePos;
    }

    private void UpdateWindowDragging()
    {
        if (!_isDragging) return;

        if (Input.GetMouseButtonUp(0))
        {
            _isDragging = false;
            return;
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _rootCanvas.transform as RectTransform,
            Input.mousePosition,
            null,
            out Vector2 mousePos);

        _windowRect.anchoredPosition = mousePos + _dragOffset;
        if (_windowMode == WindowMode.Normal)
            _normalPos = _windowRect.anchoredPosition;
    }

    private PlayerController GetPlayerController()
    {
        if (PlayerController.Instance != null)
            return PlayerController.Instance;
        return FindFirstObjectByType<PlayerController>();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  UI Construction (Pure Code / Zero-Prefab Runtime Setup)
    // ═══════════════════════════════════════════════════════════════════════════

    private void EnsureUIExists()
    {
        if (_rootCanvas != null && _windowRect != null) return;

        // Clean up any stale canvas from prior scene loads
        Transform stale = transform.Find("DevMenu_Canvas");
        if (stale != null)
            Destroy(stale.gameObject);

        // 1. Root Canvas
        GameObject canvasGo = new GameObject("DevMenu_Canvas");
        canvasGo.transform.SetParent(transform, false);

        _rootCanvas = canvasGo.AddComponent<Canvas>();
        _rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _rootCanvas.sortingOrder = 27000; // Above IDE (25000), below F3 HUD (30000)

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        // 2. Main Window Container
        GameObject winGo = CreatePanel(canvasGo.transform, "DevMenu_Window", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                       new Vector2(0.5f, 0.5f), _normalPos, _normalSize, BgWindow);
        _windowRect = winGo.GetComponent<RectTransform>();

        // Subtle 1px Border Outline
        GameObject borderGo = CreatePanel(winGo.transform, "BorderOutline", Vector2.zero, Vector2.one,
                                          new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, BorderSubtle);
        borderGo.GetComponent<Image>().raycastTarget = false;
        Stretch(borderGo.GetComponent<RectTransform>());

        // 3. Compact Header Bar (38px height)
        GameObject topBar = CreatePanel(winGo.transform, "TopBar", new Vector2(0f, 1f), new Vector2(1f, 1f),
                                        new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 38f), BgHeader);
        SetupDragTrigger(topBar);

        // Header Title
        var titleTmp = CreateTMPText(topBar.transform, "Title", 12f, TextAlignmentOptions.MidlineLeft);
        titleTmp.text = "<color=#55FFFF><b>DEV TOOLS</b></color> <color=#64748B>CONSOLE</color>";
        titleTmp.rectTransform.anchorMin = new Vector2(0f, 0f);
        titleTmp.rectTransform.anchorMax = new Vector2(0f, 1f);
        titleTmp.rectTransform.pivot = new Vector2(0f, 0.5f);
        titleTmp.rectTransform.anchoredPosition = new Vector2(12f, 0f);
        titleTmp.rectTransform.sizeDelta = new Vector2(150f, 0f);

        // Header Flying Badge
        _headerFlyBadge = CreateTMPText(topBar.transform, "HeaderFlyBadge", 11f, TextAlignmentOptions.MidlineLeft);
        _headerFlyBadge.rectTransform.anchorMin = new Vector2(0f, 0f);
        _headerFlyBadge.rectTransform.anchorMax = new Vector2(0f, 1f);
        _headerFlyBadge.rectTransform.pivot = new Vector2(0f, 0.5f);
        _headerFlyBadge.rectTransform.anchoredPosition = new Vector2(165f, 0f);
        _headerFlyBadge.rectTransform.sizeDelta = new Vector2(80f, 0f);
        _headerFlyBadge.text = "<color=#64748B>[FLY OFF]</color>";

        // Minimized Title Label
        _miniBarLabel = CreateTMPText(topBar.transform, "MiniBarTitle", 11f, TextAlignmentOptions.MidlineLeft);
        _miniBarLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
        _miniBarLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
        _miniBarLabel.rectTransform.offsetMin = new Vector2(12f, 0f);
        _miniBarLabel.rectTransform.offsetMax = new Vector2(-110f, 0f);
        _miniBarLabel.gameObject.SetActive(false);

        // Minimized Quick Flight Toggle
        _miniFlyToggleBtn = CreatePanel(topBar.transform, "MiniFlyBtn", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                                       new Vector2(1f, 0.5f), new Vector2(-54f, 0f), new Vector2(46f, 22f), DarkBtn);
        UIButton mBtn = _miniFlyToggleBtn.AddComponent<UIButton>();
        mBtn.onClick.AddListener(ToggleFlightMode);
        _miniFlyToggleTxt = CreateTMPText(_miniFlyToggleBtn.transform, "Txt", 10f, TextAlignmentOptions.Center);
        Stretch(_miniFlyToggleTxt.rectTransform);
        _miniFlyToggleTxt.text = "<color=#94A3B8>FLY</color>";
        _miniFlyToggleBtn.SetActive(false);

        // Top Right Controls (Min, Close)
        GameObject rightGrp = new GameObject("RightGroup", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        rightGrp.transform.SetParent(topBar.transform, false);
        RectTransform rRt = rightGrp.GetComponent<RectTransform>();
        rRt.anchorMin = new Vector2(1f, 0.5f);
        rRt.anchorMax = new Vector2(1f, 0.5f);
        rRt.pivot = new Vector2(1f, 0.5f);
        rRt.anchoredPosition = new Vector2(-8f, 0f);
        rRt.sizeDelta = new Vector2(50f, 26f);

        HorizontalLayoutGroup rHlg = rightGrp.GetComponent<HorizontalLayoutGroup>();
        rHlg.spacing = 4f;
        rHlg.childControlWidth = false;
        rHlg.childControlHeight = false;
        rHlg.childAlignment = TextAnchor.MiddleRight;

        CreatePillButton(rightGrp.transform, "_", 22f, 22f, ToggleMinimize);
        CreatePillButton(rightGrp.transform, "X", 22f, 22f, () => SetMenuOpen(false));

        // 4. Tab Bar Container (30px height, right below top bar)
        _tabsContainer = new GameObject("TabsBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        _tabsContainer.transform.SetParent(winGo.transform, false);
        RectTransform tabsRt = _tabsContainer.GetComponent<RectTransform>();
        tabsRt.anchorMin = new Vector2(0f, 1f);
        tabsRt.anchorMax = new Vector2(1f, 1f);
        tabsRt.pivot = new Vector2(0.5f, 1f);
        tabsRt.anchoredPosition = new Vector2(0f, -38f);
        tabsRt.sizeDelta = new Vector2(0f, 32f);

        HorizontalLayoutGroup tabsHlg = _tabsContainer.GetComponent<HorizontalLayoutGroup>();
        tabsHlg.padding = new RectOffset(8, 8, 4, 2);
        tabsHlg.spacing = 6f;
        tabsHlg.childControlWidth = false;
        tabsHlg.childControlHeight = false;
        tabsHlg.childAlignment = TextAnchor.MiddleLeft;

        _tabButtons.Clear();
        _tabButtonTexts.Clear();
        CreateTabButton(_tabsContainer.transform, "FLIGHT & PLAYER", DevTab.Flight);
        CreateTabButton(_tabsContainer.transform, "TELEPORT", DevTab.Teleport);
        CreateTabButton(_tabsContainer.transform, "WORLD & VOXELS", DevTab.World);
        CreateTabButton(_tabsContainer.transform, "ITEMS", DevTab.Items);

        // Divider below tabs
        GameObject tabDiv = CreatePanel(winGo.transform, "TabDivider", new Vector2(0f, 1f), new Vector2(1f, 1f),
                                        new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(0f, 1f), BorderSubtle);
        tabDiv.GetComponent<Image>().raycastTarget = false;

        // 5. Main Body Area
        GameObject bodyGo = CreatePanel(winGo.transform, "BodyArea", new Vector2(0f, 0f), new Vector2(1f, 1f),
                                        new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.clear);
        _bodyRect = bodyGo.GetComponent<RectTransform>();
        _bodyRect.offsetMax = new Vector2(0f, -71f); // below tab bar
        _bodyRect.offsetMin = new Vector2(0f, 26f);  // above bottom status bar

        // Build Panels
        _flightPanel = CreateScrollableTabPanel(_bodyRect.transform, "FlightPanel");
        BuildFlightTabContent(_flightPanel.transform);

        _teleportPanel = CreateScrollableTabPanel(_bodyRect.transform, "TeleportPanel");
        BuildTeleportTabContent(_teleportPanel.transform);

        _worldPanel = CreateScrollableTabPanel(_bodyRect.transform, "WorldPanel");
        BuildWorldTabContent(_worldPanel.transform);

        _itemsPanel = CreateScrollableTabPanel(_bodyRect.transform, "ItemsPanel");
        BuildItemsTabContent(_itemsPanel.transform);

        // 6. Bottom Status Bar (26px height)
        _bottomStatusBar = CreatePanel(winGo.transform, "BottomStatusBar", new Vector2(0f, 0f), new Vector2(1f, 0f),
                                       new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 26f), BgHeader);
        GameObject botBorder = CreatePanel(_bottomStatusBar.transform, "Border", new Vector2(0f, 1f), new Vector2(1f, 1f),
                                           new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 1f), BorderSubtle);
        botBorder.GetComponent<Image>().raycastTarget = false;

        _bottomStatusText = CreateTMPText(_bottomStatusBar.transform, "StatusTxt", 11f, TextAlignmentOptions.MidlineLeft);
        Stretch(_bottomStatusText.rectTransform, 12f, 0f, 12f, 0f);
        _bottomStatusText.text = "<color=#94A3B8>Ready</color>";
    }

    private GameObject CreateScrollableTabPanel(Transform parent, string name)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(parent, false);
        Stretch(panel.GetComponent<RectTransform>(), 12f, 10f, 12f, 8f);

        VerticalLayoutGroup vlg = panel.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 8f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        return panel;
    }

    private void CreateTabButton(Transform parent, string label, DevTab tab)
    {
        GameObject btnGo = new GameObject($"Tab_{tab}", typeof(RectTransform), typeof(Image), typeof(UIButton));
        btnGo.transform.SetParent(parent, false);

        RectTransform rt = btnGo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(125f, 24f);

        Image img = btnGo.GetComponent<Image>();
        img.color = TabInactive;

        UIButton btn = btnGo.GetComponent<UIButton>();
        btn.onClick.AddListener(() => SwitchTab(tab));
        _tabButtons.Add(btn);

        var txt = CreateTMPText(btnGo.transform, "Label", 11f, TextAlignmentOptions.Center);
        txt.text = $"<b>{label}</b>";
        txt.color = TextMuted;
        Stretch(txt.rectTransform);
        _tabButtonTexts.Add(txt);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TAB 1: Flight & Locomotion Content
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildFlightTabContent(Transform parent)
    {
        // ── Card 1: Master Flight Mode Toggle ───────────────────────────────
        GameObject card1 = CreateCard(parent, "FlightToggleCard", 86f);

        // Big Toggle Button
        GameObject toggleBtnGo = CreatePanel(card1.transform, "ToggleBtn", new Vector2(0f, 1f), new Vector2(1f, 1f),
                                             new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(-20f, 38f), DarkBtn);
        _flightToggleBtnImg = toggleBtnGo.GetComponent<Image>();
        UIButton tBtn = toggleBtnGo.AddComponent<UIButton>();
        tBtn.onClick.AddListener(ToggleFlightMode);

        _flightToggleBtnTxt = CreateTMPText(toggleBtnGo.transform, "Label", 13f, TextAlignmentOptions.Center);
        _flightToggleBtnTxt.text = "<b>FLY MODE: DISABLED (OFF)</b>";
        Stretch(_flightToggleBtnTxt.rectTransform);

        // Subtext status
        _flightStatusSubtext = CreateTMPText(card1.transform, "Subtext", 11f, TextAlignmentOptions.MidlineLeft);
        _flightStatusSubtext.rectTransform.anchorMin = new Vector2(0f, 0f);
        _flightStatusSubtext.rectTransform.anchorMax = new Vector2(1f, 0f);
        _flightStatusSubtext.rectTransform.pivot = new Vector2(0.5f, 0f);
        _flightStatusSubtext.rectTransform.anchoredPosition = new Vector2(0f, 8f);
        _flightStatusSubtext.rectTransform.sizeDelta = new Vector2(-24f, 26f);
        _flightStatusSubtext.text = "<color=#94A3B8>• Normal Physics active.</color>";

        // ── Card 2: Fly Velocity Slider & Presets ───────────────────────────
        GameObject card2 = CreateCard(parent, "VelocityCard", 126f);

        // Title / Current Value
        _velocityValueText = CreateTMPText(card2.transform, "VelValue", 12f, TextAlignmentOptions.MidlineLeft);
        _velocityValueText.rectTransform.anchorMin = new Vector2(0f, 1f);
        _velocityValueText.rectTransform.anchorMax = new Vector2(1f, 1f);
        _velocityValueText.rectTransform.pivot = new Vector2(0.5f, 1f);
        _velocityValueText.rectTransform.anchoredPosition = new Vector2(0f, -8f);
        _velocityValueText.rectTransform.sizeDelta = new Vector2(-24f, 20f);
        _velocityValueText.text = "<color=#AAAAAA>Fly Velocity:</color> <color=#55FF55><b>16.0 m/s</b></color>";

        // Slider Container
        GameObject sliderRow = new GameObject("SliderRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        sliderRow.transform.SetParent(card2.transform, false);
        RectTransform sRowRt = sliderRow.GetComponent<RectTransform>();
        sRowRt.anchorMin = new Vector2(0f, 1f);
        sRowRt.anchorMax = new Vector2(1f, 1f);
        sRowRt.pivot = new Vector2(0.5f, 1f);
        sRowRt.anchoredPosition = new Vector2(0f, -34f);
        sRowRt.sizeDelta = new Vector2(-24f, 24f);

        HorizontalLayoutGroup sHlg = sliderRow.GetComponent<HorizontalLayoutGroup>();
        sHlg.spacing = 6f;
        sHlg.childControlWidth = false;
        sHlg.childControlHeight = true;
        sHlg.childAlignment = TextAnchor.MiddleLeft;

        CreatePillButton(sliderRow.transform, "-5", 34f, 22f, () => AdjustFlightVelocity(-5f));
        CreatePillButton(sliderRow.transform, "-1", 30f, 22f, () => AdjustFlightVelocity(-1f));

        _flySpeedSlider = CreateSlider(sliderRow.transform, "VelocitySlider", 1f, 60f, 16f, (v) => SetFlightVelocity(v));
        _flySpeedSlider.GetComponent<RectTransform>().sizeDelta = new Vector2(300f, 20f);

        CreatePillButton(sliderRow.transform, "+1", 30f, 22f, () => AdjustFlightVelocity(+1f));
        CreatePillButton(sliderRow.transform, "+5", 34f, 22f, () => AdjustFlightVelocity(+5f));

        // Quick Preset Buttons Row
        GameObject presetRow = new GameObject("PresetRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        presetRow.transform.SetParent(card2.transform, false);
        RectTransform pRowRt = presetRow.GetComponent<RectTransform>();
        pRowRt.anchorMin = new Vector2(0f, 1f);
        pRowRt.anchorMax = new Vector2(1f, 1f);
        pRowRt.pivot = new Vector2(0.5f, 1f);
        pRowRt.anchoredPosition = new Vector2(0f, -66f);
        pRowRt.sizeDelta = new Vector2(-24f, 26f);

        HorizontalLayoutGroup pHlg = presetRow.GetComponent<HorizontalLayoutGroup>();
        pHlg.spacing = 8f;
        pHlg.childControlWidth = false;
        pHlg.childControlHeight = true;
        pHlg.childAlignment = TextAnchor.MiddleLeft;

        var pLabel = CreateTMPText(presetRow.transform, "Label", 11f, TextAlignmentOptions.MidlineLeft);
        pLabel.rectTransform.sizeDelta = new Vector2(50f, 22f);
        pLabel.text = "<color=#AAAAAA>Presets:</color>";

        CreatePillButton(presetRow.transform, "Walk (5)", 66f, 22f, () => SetFlightVelocity(5f));
        CreatePillButton(presetRow.transform, "Normal (15)", 74f, 22f, () => SetFlightVelocity(15f));
        CreatePillButton(presetRow.transform, "Fast (30)", 66f, 22f, () => SetFlightVelocity(30f));
        CreatePillButton(presetRow.transform, "Sonic (50)", 68f, 22f, () => SetFlightVelocity(50f));
        CreatePillButton(presetRow.transform, "Max (80)", 64f, 22f, () => SetFlightVelocity(80f));

        // Sprint Boost Multiplier Row
        GameObject boostRow = new GameObject("BoostRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        boostRow.transform.SetParent(card2.transform, false);
        RectTransform bRowRt = boostRow.GetComponent<RectTransform>();
        bRowRt.anchorMin = new Vector2(0f, 1f);
        bRowRt.anchorMax = new Vector2(1f, 1f);
        bRowRt.pivot = new Vector2(0.5f, 1f);
        bRowRt.anchoredPosition = new Vector2(0f, -96f);
        bRowRt.sizeDelta = new Vector2(-24f, 24f);

        HorizontalLayoutGroup bHlg = boostRow.GetComponent<HorizontalLayoutGroup>();
        bHlg.spacing = 8f;
        bHlg.childControlWidth = false;
        bHlg.childControlHeight = true;
        bHlg.childAlignment = TextAnchor.MiddleLeft;

        _boostValText = CreateTMPText(boostRow.transform, "BoostVal", 11f, TextAlignmentOptions.MidlineLeft);
        _boostValText.rectTransform.sizeDelta = new Vector2(140f, 22f);
        _boostValText.text = "<color=#AAAAAA>Sprint Boost:</color> <color=#FFFF55>2.0x</color>";

        CreatePillButton(boostRow.transform, "1.5x", 42f, 22f, () => SetSprintBoost(1.5f));
        CreatePillButton(boostRow.transform, "2.0x", 42f, 22f, () => SetSprintBoost(2.0f));
        CreatePillButton(boostRow.transform, "3.0x", 42f, 22f, () => SetSprintBoost(3.0f));
        CreatePillButton(boostRow.transform, "4.0x", 42f, 22f, () => SetSprintBoost(4.0f));

        // ── Card 3: Additional Flight Modes ─────────────────────────────────
        GameObject card3 = CreateCard(parent, "FlightModesCard", 46f);
        GameObject modeRow = new GameObject("ModeRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        modeRow.transform.SetParent(card3.transform, false);
        Stretch(modeRow.GetComponent<RectTransform>(), 12f, 8f, 12f, 8f);

        HorizontalLayoutGroup mHlg = modeRow.GetComponent<HorizontalLayoutGroup>();
        mHlg.spacing = 10f;
        mHlg.childControlWidth = true;
        mHlg.childControlHeight = true;

        // No-Clip Button
        GameObject ncBtn = CreatePanel(modeRow.transform, "NoClipBtn", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, DarkBtn);
        _noClipBtnImg = ncBtn.GetComponent<Image>();
        UIButton ncB = ncBtn.AddComponent<UIButton>();
        ncB.onClick.AddListener(ToggleNoClip);
        _noClipBtnTxt = CreateTMPText(ncBtn.transform, "Txt", 11f, TextAlignmentOptions.Center);
        Stretch(_noClipBtnTxt.rectTransform);
        _noClipBtnTxt.text = "<b>NO-CLIP: OFF</b>";

        // 3D Look Fly Button
        GameObject camBtn = CreatePanel(modeRow.transform, "CamRelBtn", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, DarkBtn);
        _camRelativeBtnImg = camBtn.GetComponent<Image>();
        UIButton camB = camBtn.AddComponent<UIButton>();
        camB.onClick.AddListener(ToggleCameraRelativeFly);
        _camRelativeBtnTxt = CreateTMPText(camBtn.transform, "Txt", 11f, TextAlignmentOptions.Center);
        Stretch(_camRelativeBtnTxt.rectTransform);
        _camRelativeBtnTxt.text = "<b>PLANAR FLY: ON</b>";

        // ── Card 4: Flight Controls Cheatsheet ──────────────────────────────
        GameObject card4 = CreateCard(parent, "ControlsCard", 96f);
        var cheatsheet = CreateTMPText(card4.transform, "Cheatsheet", 11f, TextAlignmentOptions.TopLeft);
        Stretch(cheatsheet.rectTransform, 12f, 8f, 12f, 8f);
        cheatsheet.text = "<color=#55FFFF><b>Locomotion Keybindings:</b></color>\n" +
                          "• <color=#E2E8F0><b>W / A / S / D</b> : Move horizontally or towards look direction</color>\n" +
                          "• <color=#E2E8F0><b>Space</b> : Ascend vertically (+Y)</color>  |  <color=#E2E8F0><b>Ctrl / C</b> : Descend vertically (-Y)</color>\n" +
                          "• <color=#E2E8F0><b>Left Shift</b> : Sprint Boost</color>  |  <color=#FFFF55><b>Double-Tap Space</b> : Toggle Flight</color>";
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TAB 2: Teleport & Position Tools
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildTeleportTabContent(Transform parent)
    {
        // ── Card 1: Live Coordinates ────────────────────────────────────────
        GameObject card1 = CreateCard(parent, "PosReadoutCard", 60f);
        _posReadoutText = CreateTMPText(card1.transform, "PosText", 11f, TextAlignmentOptions.MidlineLeft);
        Stretch(_posReadoutText.rectTransform, 12f, 8f, 12f, 8f);
        _posReadoutText.text = "<color=#AAAAAA>World Coordinates:</color> ...";

        // ── Card 2: Quick Teleport Actions ──────────────────────────────────
        GameObject card2 = CreateCard(parent, "TeleportActionsCard", 120f);
        var tLabel = CreateTMPText(card2.transform, "Label", 12f, TextAlignmentOptions.MidlineLeft);
        tLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        tLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
        tLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
        tLabel.rectTransform.anchoredPosition = new Vector2(0f, -8f);
        tLabel.rectTransform.sizeDelta = new Vector2(-24f, 20f);
        tLabel.text = "<color=#55FFFF><b>Teleport & Respawn:</b></color>";

        // Row 1: Surface & Origin
        GameObject row1 = new GameObject("Row1", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row1.transform.SetParent(card2.transform, false);
        RectTransform r1Rt = row1.GetComponent<RectTransform>();
        r1Rt.anchorMin = new Vector2(0f, 1f);
        r1Rt.anchorMax = new Vector2(1f, 1f);
        r1Rt.pivot = new Vector2(0.5f, 1f);
        r1Rt.anchoredPosition = new Vector2(0f, -34f);
        r1Rt.sizeDelta = new Vector2(-24f, 26f);

        HorizontalLayoutGroup h1 = row1.GetComponent<HorizontalLayoutGroup>();
        h1.spacing = 8f;
        h1.childControlWidth = true;
        h1.childControlHeight = true;

        CreatePillButton(row1.transform, "Teleport to Safe Surface / Spawn", 240f, 24f, TeleportToSafeSurface);
        CreatePillButton(row1.transform, "Teleport to Origin (0, 0)", 180f, 24f, TeleportToOrigin);

        // Row 2: Nudge Altitude & Zero Velocity
        GameObject row2 = new GameObject("Row2", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row2.transform.SetParent(card2.transform, false);
        RectTransform r2Rt = row2.GetComponent<RectTransform>();
        r2Rt.anchorMin = new Vector2(0f, 1f);
        r2Rt.anchorMax = new Vector2(1f, 1f);
        r2Rt.pivot = new Vector2(0.5f, 1f);
        r2Rt.anchoredPosition = new Vector2(0f, -68f);
        r2Rt.sizeDelta = new Vector2(-24f, 26f);

        HorizontalLayoutGroup h2 = row2.GetComponent<HorizontalLayoutGroup>();
        h2.spacing = 8f;
        h2.childControlWidth = true;
        h2.childControlHeight = true;

        CreatePillButton(row2.transform, "Nudge Up +10m", 110f, 24f, () => TeleportNudgeUp(10f));
        CreatePillButton(row2.transform, "Nudge Up +50m", 110f, 24f, () => TeleportNudgeUp(50f));
        CreatePillButton(row2.transform, "Reset Velocity (Stop)", 160f, 24f, ResetPlayerVelocity);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TAB 3: World & Voxel Diagnostics Content
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildWorldTabContent(Transform parent)
    {
        // ── Card 1: World Diagnostics ───────────────────────────────────────
        GameObject card1 = CreateCard(parent, "DiagnosticsCard", 130f);
        _worldDiagnosticsText = CreateTMPText(card1.transform, "DiagText", 11f, TextAlignmentOptions.TopLeft);
        Stretch(_worldDiagnosticsText.rectTransform, 12f, 8f, 12f, 8f);
        _worldDiagnosticsText.text = "<color=#AAAAAA>Loading world data...</color>";

        // ── Card 2: World Actions ───────────────────────────────────────────
        GameObject card2 = CreateCard(parent, "WorldActionsCard", 120f);
        var wLabel = CreateTMPText(card2.transform, "Label", 12f, TextAlignmentOptions.MidlineLeft);
        wLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        wLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
        wLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
        wLabel.rectTransform.anchoredPosition = new Vector2(0f, -8f);
        wLabel.rectTransform.sizeDelta = new Vector2(-24f, 20f);
        wLabel.text = "<color=#55FFFF><b>World & Rendering Commands:</b></color>";

        // Row 1: Rebuild chunks & debug ores
        GameObject row1 = new GameObject("Row1", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row1.transform.SetParent(card2.transform, false);
        RectTransform r1Rt = row1.GetComponent<RectTransform>();
        r1Rt.anchorMin = new Vector2(0f, 1f);
        r1Rt.anchorMax = new Vector2(1f, 1f);
        r1Rt.pivot = new Vector2(0.5f, 1f);
        r1Rt.anchoredPosition = new Vector2(0f, -34f);
        r1Rt.sizeDelta = new Vector2(-24f, 26f);

        HorizontalLayoutGroup h1 = row1.GetComponent<HorizontalLayoutGroup>();
        h1.spacing = 8f;
        h1.childControlWidth = true;
        h1.childControlHeight = true;

        CreatePillButton(row1.transform, "Rebuild Active Chunks", 180f, 24f, RebuildActiveChunks);
        CreatePillButton(row1.transform, "Toggle Debug Ores Only", 180f, 24f, ToggleDebugOres);

        // Row 2: View Distance & Overlay Toggles
        GameObject row2 = new GameObject("Row2", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row2.transform.SetParent(card2.transform, false);
        RectTransform r2Rt = row2.GetComponent<RectTransform>();
        r2Rt.anchorMin = new Vector2(0f, 1f);
        r2Rt.anchorMax = new Vector2(1f, 1f);
        r2Rt.pivot = new Vector2(0.5f, 1f);
        r2Rt.anchoredPosition = new Vector2(0f, -68f);
        r2Rt.sizeDelta = new Vector2(-24f, 26f);

        HorizontalLayoutGroup h2 = row2.GetComponent<HorizontalLayoutGroup>();
        h2.spacing = 8f;
        h2.childControlWidth = true;
        h2.childControlHeight = true;

        CreatePillButton(row2.transform, "View Dist -1", 90f, 24f, () => AdjustViewDistance(-1));
        CreatePillButton(row2.transform, "View Dist +1", 90f, 24f, () => AdjustViewDistance(+1));
        CreatePillButton(row2.transform, "Toggle F3 HUD", 110f, 24f, ToggleF3HUD);
        CreatePillButton(row2.transform, "Toggle Python IDE", 110f, 24f, ToggleIDE);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TAB 4: Items & Quick Give Content
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildItemsTabContent(Transform parent)
    {
        EnsurePaletteLoaded();

        // ── Card 1: Category Filter & Quantity Controls (86f height) ───────
        GameObject card1 = CreateCard(parent, "ItemsControlsCard", 86f);

        // Top Row: Title + Quantity Selector Buttons (x1, x16, x32, x64)
        GameObject topRow = new GameObject("TopRow", typeof(RectTransform));
        topRow.transform.SetParent(card1.transform, false);
        RectTransform trRt = topRow.GetComponent<RectTransform>();
        trRt.anchorMin = new Vector2(0f, 1f);
        trRt.anchorMax = new Vector2(1f, 1f);
        trRt.pivot = new Vector2(0.5f, 1f);
        trRt.anchoredPosition = new Vector2(0f, -8f);
        trRt.sizeDelta = new Vector2(-24f, 22f);

        var iLabel = CreateTMPText(topRow.transform, "Label", 12f, TextAlignmentOptions.MidlineLeft);
        iLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
        iLabel.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        iLabel.rectTransform.pivot = new Vector2(0f, 0.5f);
        iLabel.rectTransform.anchoredPosition = Vector2.zero;
        iLabel.rectTransform.sizeDelta = Vector2.zero;
        iLabel.text = "<color=#55FFFF><b>Item & Block Catalog</b></color>";

        // Quantity Selector Container
        GameObject qtyContainer = new GameObject("QtyContainer", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        qtyContainer.transform.SetParent(topRow.transform, false);
        RectTransform qtyRt = qtyContainer.GetComponent<RectTransform>();
        qtyRt.anchorMin = new Vector2(1f, 0.5f);
        qtyRt.anchorMax = new Vector2(1f, 0.5f);
        qtyRt.pivot = new Vector2(1f, 0.5f);
        qtyRt.anchoredPosition = Vector2.zero;
        qtyRt.sizeDelta = new Vector2(230f, 22f);

        HorizontalLayoutGroup qtyHlg = qtyContainer.GetComponent<HorizontalLayoutGroup>();
        qtyHlg.spacing = 4f;
        qtyHlg.childControlWidth = false;
        qtyHlg.childControlHeight = false;
        qtyHlg.childAlignment = TextAnchor.MiddleRight;

        var qtyLabel = CreateTMPText(qtyContainer.transform, "QtyLbl", 10f, TextAlignmentOptions.MidlineRight);
        qtyLabel.rectTransform.sizeDelta = new Vector2(32f, 20f);
        qtyLabel.text = "<color=#94A3B8>Qty:</color>";

        _qtyButtonAmounts.Clear();
        _qtyButtonImgs.Clear();
        _qtyButtonTexts.Clear();
        CreateQtyButton(qtyContainer.transform, "x1", 1);
        CreateQtyButton(qtyContainer.transform, "x16", 16);
        CreateQtyButton(qtyContainer.transform, "x32", 32);
        CreateQtyButton(qtyContainer.transform, "x64", 64);
        UpdateQtyButtonsVisuals();

        // Row 2: Category Filter Buttons Bar
        GameObject catRow = new GameObject("CategoryRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        catRow.transform.SetParent(card1.transform, false);
        RectTransform catRt = catRow.GetComponent<RectTransform>();
        catRt.anchorMin = new Vector2(0f, 1f);
        catRt.anchorMax = new Vector2(1f, 1f);
        catRt.pivot = new Vector2(0.5f, 1f);
        catRt.anchoredPosition = new Vector2(0f, -34f);
        catRt.sizeDelta = new Vector2(-24f, 22f);

        HorizontalLayoutGroup catHlg = catRow.GetComponent<HorizontalLayoutGroup>();
        catHlg.spacing = 6f;
        catHlg.childControlWidth = false;
        catHlg.childControlHeight = false;
        catHlg.childAlignment = TextAnchor.MiddleLeft;
        _categoryBtnContainer = catRow.transform;

        // Feedback / Subtitle note
        _itemsFeedbackText = CreateTMPText(card1.transform, "FeedbackText", 10f, TextAlignmentOptions.MidlineLeft);
        _itemsFeedbackText.rectTransform.anchorMin = new Vector2(0f, 0f);
        _itemsFeedbackText.rectTransform.anchorMax = new Vector2(1f, 0f);
        _itemsFeedbackText.rectTransform.pivot = new Vector2(0.5f, 0f);
        _itemsFeedbackText.rectTransform.anchoredPosition = new Vector2(0f, 6f);
        _itemsFeedbackText.rectTransform.sizeDelta = new Vector2(-24f, 18f);
        _itemsFeedbackText.text = "<color=#64748B>Click any item to grant to inventory. Edit in Unity: Tools > Dev Menu > Item Palette Editor</color>";

        // ── Card 2: Scrollable Items Grid (305f height) ──────────────────────
        GameObject card2 = CreateCard(parent, "ItemsGridCard", 305f);

        // ScrollRect
        GameObject scrollGo = new GameObject("ItemsScrollView", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(card2.transform, false);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        Stretch(scrollRt, 8f, 8f, 8f, 8f);

        ScrollRect scrollRect = scrollGo.GetComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 25f;

        // Viewport with RectMask2D
        GameObject viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        RectTransform vpRt = viewportGo.GetComponent<RectTransform>();
        Stretch(vpRt, 0f, 0f, 0f, 0f);
        scrollRect.viewport = vpRt;

        // Content
        GameObject contentGo = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewportGo.transform, false);
        RectTransform contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 0f);

        GridLayoutGroup grid = contentGo.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(166f, 28f);
        grid.spacing = new Vector2(8f, 6f);
        grid.padding = new RectOffset(2, 2, 4, 4);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;

        ContentSizeFitter csf = contentGo.GetComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        scrollRect.content = contentRt;
        _itemsGridContainer = contentGo.transform;

        RebuildCategoryButtons();
        RebuildItemsGrid();
    }

    private void RebuildCategoryButtons()
    {
        if (_categoryBtnContainer == null) return;

        // Clear previous buttons
        for (int i = _categoryBtnContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(_categoryBtnContainer.GetChild(i).gameObject);
        }

        _categoryButtonKeys.Clear();
        _categoryButtonImgs.Clear();
        _categoryButtonTexts.Clear();

        EnsurePaletteLoaded();
        var categories = _palette != null ? _palette.GetCategories() : new List<string> { "ALL" };

        foreach (var cat in categories)
        {
            string categoryName = cat;
            GameObject btnGo = new GameObject("Cat_" + categoryName, typeof(RectTransform), typeof(Image), typeof(UIButton));
            btnGo.transform.SetParent(_categoryBtnContainer, false);

            RectTransform rt = btnGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(68f, 22f);

            bool isActive = string.Equals(_activeItemCategory, categoryName, StringComparison.OrdinalIgnoreCase);
            Image img = btnGo.GetComponent<Image>();
            img.color = isActive ? AmberAccent : DarkBtn;

            UIButton btn = btnGo.GetComponent<UIButton>();
            btn.onClick.AddListener(() =>
            {
                _activeItemCategory = categoryName;
                UpdateCategoryButtonsVisuals();
                RebuildItemsGrid();
            });

            var txt = CreateTMPText(btnGo.transform, "Label", 10f, TextAlignmentOptions.Center);
            txt.text = $"<b>{categoryName}</b>";
            txt.color = isActive ? DarkAmberText : TextMuted;
            Stretch(txt.rectTransform);

            _categoryButtonKeys.Add(categoryName);
            _categoryButtonImgs.Add(img);
            _categoryButtonTexts.Add(txt);
        }
    }

    private void UpdateCategoryButtonsVisuals()
    {
        for (int i = 0; i < _categoryButtonKeys.Count; i++)
        {
            bool isActive = string.Equals(_activeItemCategory, _categoryButtonKeys[i], StringComparison.OrdinalIgnoreCase);
            if (i < _categoryButtonImgs.Count && _categoryButtonImgs[i] != null)
                _categoryButtonImgs[i].color = isActive ? AmberAccent : DarkBtn;
            if (i < _categoryButtonTexts.Count && _categoryButtonTexts[i] != null)
                _categoryButtonTexts[i].color = isActive ? DarkAmberText : TextMuted;
        }
    }

    private void RebuildItemsGrid()
    {
        if (_itemsGridContainer == null) return;

        for (int i = _itemsGridContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(_itemsGridContainer.GetChild(i).gameObject);
        }

        EnsurePaletteLoaded();
        var items = _palette != null ? _palette.GetItemsByCategory(_activeItemCategory) : new List<DevMenuItemEntry>();

        for (int i = 0; i < items.Count; i++)
        {
            var entry = items[i];
            if (entry == null) continue;

            CreatePaletteItemButton(_itemsGridContainer, entry);
        }
    }

    private void CreatePaletteItemButton(Transform parent, DevMenuItemEntry entry)
    {
        GameObject btnGo = new GameObject("Item_" + entry.displayName, typeof(RectTransform), typeof(Image), typeof(UIButton));
        btnGo.transform.SetParent(parent, false);

        RectTransform rt = btnGo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(166f, 28f);

        Image img = btnGo.GetComponent<Image>();
        img.color = DarkBtn;

        UIButton btn = btnGo.GetComponent<UIButton>();
        btn.onClick.AddListener(() => GrantPaletteItem(entry, -1));

        // Subtly colored border/accent based on category
        Color catColor = GetCategoryColor(entry.category);
        GameObject accentBar = CreatePanel(btnGo.transform, "Accent", new Vector2(0f, 0f), new Vector2(0f, 1f),
                                           new Vector2(0f, 0.5f), Vector2.zero, new Vector2(3f, 0f), catColor);
        accentBar.GetComponent<Image>().raycastTarget = false;

        // Label with name and blockId / type badge
        var lbl = CreateTMPText(btnGo.transform, "Label", 10.5f, TextAlignmentOptions.MidlineLeft);
        Stretch(lbl.rectTransform, 8f, 0f, 4f, 0f);

        string badge = entry.isEntity ? "<color=#A78BFA>[ENT]</color>" : $"<color=#64748B>#{entry.voxelBlockId}</color>";
        lbl.text = $"{badge} <color=#FFFFFF>{entry.displayName}</color>";
    }

    private void CreateQtyButton(Transform parent, string label, int amount)
    {
        GameObject btnGo = new GameObject("Qty_" + label, typeof(RectTransform), typeof(Image), typeof(UIButton));
        btnGo.transform.SetParent(parent, false);

        RectTransform rt = btnGo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(38f, 20f);

        Image img = btnGo.GetComponent<Image>();
        img.color = DarkBtn;

        UIButton btn = btnGo.GetComponent<UIButton>();
        btn.onClick.AddListener(() =>
        {
            _selectedGrantAmount = amount;
            UpdateQtyButtonsVisuals();
        });

        var txt = CreateTMPText(btnGo.transform, "Label", 10f, TextAlignmentOptions.Center);
        txt.text = $"<b>{label}</b>";
        txt.color = TextMuted;
        Stretch(txt.rectTransform);

        _qtyButtonAmounts.Add(amount);
        _qtyButtonImgs.Add(img);
        _qtyButtonTexts.Add(txt);
    }

    private void UpdateQtyButtonsVisuals()
    {
        for (int i = 0; i < _qtyButtonAmounts.Count; i++)
        {
            bool isSelected = (_selectedGrantAmount == _qtyButtonAmounts[i]);
            if (i < _qtyButtonImgs.Count && _qtyButtonImgs[i] != null)
                _qtyButtonImgs[i].color = isSelected ? AmberAccent : DarkBtn;
            if (i < _qtyButtonTexts.Count && _qtyButtonTexts[i] != null)
                _qtyButtonTexts[i].color = isSelected ? DarkAmberText : TextMuted;
        }
    }

    private Color GetCategoryColor(string category)
    {
        if (string.IsNullOrEmpty(category)) return BorderSubtle;
        string c = category.ToLowerInvariant();
        if (c.Contains("ore")) return AmberAccent;
        if (c.Contains("flora") || c.Contains("leaves") || c.Contains("wood") || c.Contains("log")) return EmeraldAccent;
        if (c.Contains("fluid") || c.Contains("water")) return new Color(0.22f, 0.74f, 0.98f, 1f);
        if (c.Contains("entit")) return new Color(0.65f, 0.55f, 0.98f, 1f);
        if (c.Contains("terrain") || c.Contains("stone") || c.Contains("dirt") || c.Contains("grass") || c.Contains("sand")) return new Color(0.78f, 0.70f, 0.58f, 1f);
        return BorderSubtle;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  UI Helper Factory Methods
    // ═══════════════════════════════════════════════════════════════════════════

    private GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                   Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta, Color color)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);

        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;

        Image img = panel.GetComponent<Image>();
        img.color = color;
        return panel;
    }

    private GameObject CreateCard(Transform parent, string name, float height)
    {
        GameObject card = CreatePanel(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, height), BgCard);
        RectTransform rt = card.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, height);

        // Subtle 1px card border
        GameObject border = CreatePanel(card.transform, "CardBorder", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, BorderSubtle);
        border.GetComponent<Image>().raycastTarget = false;
        Stretch(border.GetComponent<RectTransform>());

        // Inner background
        GameObject inner = CreatePanel(card.transform, "CardInner", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, BgCard);
        inner.GetComponent<Image>().raycastTarget = false;
        Stretch(inner.GetComponent<RectTransform>(), 1f, 1f, 1f, 1f);

        return card;
    }

    private TextMeshProUGUI CreateTMPText(Transform parent, string name, float fontSize, TextAlignmentOptions alignment)
    {
        GameObject textGo = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.color = Color.white;
        tmp.richText = true;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;

        if (customFont != null)
            tmp.font = customFont;
        else
        {
            var libFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (libFont != null) tmp.font = libFont;
            else if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        }

        return tmp;
    }

    private void CreatePillButton(Transform parent, string text, float width, float height, Action onClick)
    {
        GameObject btnGo = new GameObject("Btn_" + text, typeof(RectTransform), typeof(Image), typeof(UIButton));
        btnGo.transform.SetParent(parent, false);

        RectTransform rt = btnGo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);

        btnGo.GetComponent<Image>().color = DarkBtn;
        UIButton btn = btnGo.GetComponent<UIButton>();
        btn.onClick.AddListener(() => onClick?.Invoke());

        var tmp = CreateTMPText(btnGo.transform, "Label", 11f, TextAlignmentOptions.Center);
        tmp.text = text;
        tmp.color = TextMuted;
        Stretch(tmp.rectTransform);
    }

    private Slider CreateSlider(Transform parent, string name, float min, float max, float currentVal, Action<float> onValueChanged)
    {
        GameObject sliderGo = new GameObject(name, typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(parent, false);
        RectTransform sRt = sliderGo.GetComponent<RectTransform>();
        sRt.sizeDelta = new Vector2(240f, 20f);

        // Background track
        GameObject bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bgGo.transform.SetParent(sliderGo.transform, false);
        RectTransform bgRt = bgGo.GetComponent<RectTransform>();
        Stretch(bgRt, 0f, 6f, 0f, 6f);
        Image bgImg = bgGo.GetComponent<Image>();
        bgImg.color = new Color(0.04f, 0.05f, 0.07f, 1f);

        // Fill Area
        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        RectTransform faRt = fillArea.GetComponent<RectTransform>();
        Stretch(faRt, 0f, 6f, 10f, 6f);

        // Fill
        GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(fillArea.transform, false);
        RectTransform fRt = fillGo.GetComponent<RectTransform>();
        Stretch(fRt, 0f, 0f, 0f, 0f);
        Image fillImg = fillGo.GetComponent<Image>();
        fillImg.color = AmberAccent;

        // Handle Slide Area
        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderGo.transform, false);
        RectTransform haRt = handleArea.GetComponent<RectTransform>();
        Stretch(haRt, 6f, 0f, 6f, 0f);

        // Handle
        GameObject handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleGo.transform.SetParent(handleArea.transform, false);
        RectTransform hRt = handleGo.GetComponent<RectTransform>();
        hRt.sizeDelta = new Vector2(14f, 20f);
        Image hImg = handleGo.GetComponent<Image>();
        hImg.color = Color.white;

        Slider slider = sliderGo.GetComponent<Slider>();
        slider.fillRect = fRt;
        slider.handleRect = hRt;
        slider.targetGraphic = hImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = currentVal;
        slider.onValueChanged.AddListener((val) => onValueChanged?.Invoke(val));

        return slider;
    }

    private void SetupDragTrigger(GameObject titleBar)
    {
        EventTrigger trigger = titleBar.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        entry.callback.AddListener((data) => OnTitleBarDragStart());
        trigger.triggers.Add(entry);
    }

    private static void Stretch(RectTransform rt, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }
}
