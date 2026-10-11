using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  DimensionManager — Central orchestrator of the multidimensional system.
//  Manages active dimension state, per-dimension chunk persistence, return
//  anchors, and loading screen transitions.
// ─────────────────────────────────────────────────────────────────────────────

public class DimensionManager : MonoBehaviour
{
    public static DimensionManager Instance { get; private set; }

    [Header("Configuration")]
    [Tooltip("Registry containing all available dimensions.")]
    [SerializeField] private DimensionRegistry dimensionRegistry;

    [Tooltip("Dimension ID to start in (default: 'overworld').")]
    [SerializeField] private string startingDimensionId = "overworld";

    [Header("Scene References")]
    [SerializeField] private LoadingScreenController loadingScreen;
    [SerializeField] private DimensionEnvironmentController environmentController;

    [Header("Wall Rift Portal Prefab (Optional)")]
    [Tooltip("Prefab for the Kane Pixels wall seam return portal in Backrooms.")]
    [SerializeField] private GameObject wallRiftPrefab;

    // Master seed for the entire save/session — owned by VoxelWorldManager so both always agree.
    public int MasterSeed => VoxelWorldManager.Instance != null ? VoxelWorldManager.Instance.MasterSeed : _fallbackMasterSeed;
    private int _fallbackMasterSeed;

    // Seconds after finishing a transition during which portals ignore the player, so arriving
    // inside/next to a trigger can never bounce the player straight back.
    private const float ArrivalGraceSeconds = 2.5f;
    private float _arrivalGraceUntil;

    /// <summary>True briefly after a dimension switch completes. Portals must not fire while this is true.</summary>
    public bool IsInArrivalGrace => Time.time < _arrivalGraceUntil;

    // Rift portals spawned for the CURRENT dimension. Destroyed when leaving it.
    private readonly List<GameObject> _activePortals = new List<GameObject>();
    private Material _riftMaterial;

    // Active dimension state
    public DimensionProfile CurrentDimension { get; private set; }
    public string CurrentDimensionId => CurrentDimension != null ? CurrentDimension.dimensionId : startingDimensionId;
    public bool IsTransitioning { get; private set; } = false;

    // Player return base position in the Overworld
    public Vector3 LastOverworldBasePosition { get; set; } = new Vector3(0.5f, 20f, 0.5f);

    // Per-dimension chunk data caches: dimensionId -> (chunkCoord -> VoxelChunkData)
    private readonly Dictionary<string, Dictionary<Vector2Int, VoxelChunkData>> _dimensionCaches = new(StringComparer.OrdinalIgnoreCase);

    // Saved player positions per dimension
    private readonly Dictionary<string, Vector3> _savedDimensionPositions = new(StringComparer.OrdinalIgnoreCase);

    // Events
    public event Action<DimensionProfile> OnDimensionChanging;
    public event Action<DimensionProfile> OnDimensionChanged;

    // Auto-spawn hook so the DimensionManager works out-of-the-box in any scene without manual placement
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        if (FindFirstObjectByType<DimensionManager>() == null)
        {
            GameObject managerGo = new GameObject("DimensionManager");
            managerGo.AddComponent<DimensionManager>();
            managerGo.AddComponent<DimensionEnvironmentController>();
            DontDestroyOnLoad(managerGo);
            Debug.Log("[DimensionManager] Auto-initialized DimensionManager in scene.");
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

        if (dimensionRegistry == null)
            dimensionRegistry = Resources.Load<DimensionRegistry>("DimensionRegistry");

        if (dimensionRegistry != null)
            dimensionRegistry.Initialize();

        if (environmentController == null)
            environmentController = FindFirstObjectByType<DimensionEnvironmentController>();

        if (loadingScreen == null)
            loadingScreen = FindFirstObjectByType<LoadingScreenController>();

        _fallbackMasterSeed = UnityEngine.Random.Range(1, int.MaxValue);

        InitializeStartingDimension();
    }

    private void InitializeStartingDimension()
    {
        if (dimensionRegistry != null)
        {
            CurrentDimension = dimensionRegistry.GetDimension(startingDimensionId);
        }

        if (CurrentDimension != null && environmentController != null && CurrentDimension.environmentProfile != null)
        {
            environmentController.ApplyEnvironment(CurrentDimension.environmentProfile, immediate: true);
        }
    }

    /// <summary>
    /// Switches the active game dimension, persisting outgoing world state and streaming
    /// chunks around the target arrival position.
    /// </summary>
    public void SwitchDimension(string targetDimensionId, Vector3? targetPosition = null, Action onComplete = null)
    {
        if (IsTransitioning)
        {
            Debug.LogWarning("[DimensionManager] Dimension switch already in progress.");
            return;
        }

        if (string.Equals(CurrentDimensionId, targetDimensionId, StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"[DimensionManager] Already in dimension '{targetDimensionId}'.");
            return;
        }

        StartCoroutine(SwitchDimensionRoutine(targetDimensionId, targetPosition, onComplete));
    }

    /// <summary>Returns player back to their saved base position in the Overworld.</summary>
    public void ReturnToOverworldBase(Action onComplete = null)
    {
        SwitchDimension("overworld", LastOverworldBasePosition, onComplete);
    }

    private IEnumerator SwitchDimensionRoutine(string targetId, Vector3? explicitTargetPos, Action onComplete)
    {
        IsTransitioning = true;

        var nextProfile = dimensionRegistry != null ? dimensionRegistry.GetDimension(targetId) : null;
        if (nextProfile == null)
        {
            Debug.LogError($"[DimensionManager] Dimension '{targetId}' not found in DimensionRegistry!");
            IsTransitioning = false;
            yield break;
        }

        OnDimensionChanging?.Invoke(nextProfile);

        // 1. Show loading screen
        if (loadingScreen != null)
        {
            loadingScreen.Show();
            loadingScreen.SetProgress(0.05f);
        }

        yield return null;

        var worldMgr = VoxelWorldManager.Instance;
        var player = worldMgr != null ? worldMgr.PlayerTransform : null;

        // 2. Save current dimension player position
        if (player != null)
        {
            Vector3 currentPos = player.position;

            // Left through the void (noclip)? Never persist a below-world position: returning there
            // would immediately re-trigger the void. Store the dimension's safe spawn instead.
            if (worldMgr != null && worldMgr.Settings != null && CurrentDimension != null &&
                currentPos.y < worldMgr.Settings.minHeight)
            {
                currentPos = worldMgr.GetSpawnPosition(CurrentDimension.defaultSpawnColumn.x, CurrentDimension.defaultSpawnColumn.y);
            }

            // Left through a rift? The player is standing inside its trigger — don't persist that,
            // so re-entering uses the dimension's normal arrival point instead of bouncing back.
            if (IsNearActivePortal(currentPos, 3.5f))
                _savedDimensionPositions.Remove(CurrentDimensionId);
            else
                _savedDimensionPositions[CurrentDimensionId] = currentPos;

            if (CurrentDimensionId.Equals("overworld", StringComparison.OrdinalIgnoreCase))
            {
                LastOverworldBasePosition = currentPos;
            }
        }

        // Rifts belong to the dimension they were spawned in
        ClearActivePortals();

        // 3. Export & persist chunk data of the outgoing dimension
        if (worldMgr != null)
        {
            if (!_dimensionCaches.TryGetValue(CurrentDimensionId, out var outgoingCache))
            {
                outgoingCache = new Dictionary<Vector2Int, VoxelChunkData>();
                _dimensionCaches[CurrentDimensionId] = outgoingCache;
            }
            worldMgr.ExportChunkDataCache(outgoingCache);
        }

        if (loadingScreen != null) loadingScreen.SetProgress(0.2f);
        yield return null;

        // 4. Retrieve or create chunk data cache for incoming dimension
        if (!_dimensionCaches.TryGetValue(targetId, out var targetCache))
        {
            targetCache = new Dictionary<Vector2Int, VoxelChunkData>();
            _dimensionCaches[targetId] = targetCache;
        }

        // 5. Switch active dimension in VoxelWorldManager
        CurrentDimension = nextProfile;
        if (worldMgr != null)
        {
            worldMgr.SwitchDimension(nextProfile, targetCache);
        }

        // 6. Apply environment (fog, lighting, skybox, ambient audio)
        if (environmentController != null && nextProfile.environmentProfile != null)
        {
            environmentController.ApplyEnvironment(nextProfile.environmentProfile, immediate: true);
        }

        if (loadingScreen != null) loadingScreen.SetProgress(0.4f);
        yield return null;

        // 7. Determine arrival position.
        //    Generators with a reserved arrival clearing guarantee a safe spawn (plus a wall anchor
        //    for the return rift) — that beats scanning voxel columns and hoping.
        DimensionArrivalPoint arrivalPoint = default;
        bool hasArrivalPoint = worldMgr != null && worldMgr.TryGetArrivalPoint(out arrivalPoint);

        Vector3 arrivalPos;
        if (explicitTargetPos.HasValue)
        {
            arrivalPos = explicitTargetPos.Value;
        }
        else if (_savedDimensionPositions.TryGetValue(targetId, out Vector3 savedPos))
        {
            arrivalPos = savedPos;
        }
        else if (hasArrivalPoint)
        {
            arrivalPos = arrivalPoint.position;
        }
        else
        {
            arrivalPos = worldMgr != null
                ? worldMgr.GetSpawnPosition(nextProfile.defaultSpawnColumn.x, nextProfile.defaultSpawnColumn.y)
                : new Vector3(0.5f, 2f, 0.5f);
        }

        // 8. Pre-load initial chunks around arrival coordinate
        int blockX = Mathf.FloorToInt(arrivalPos.x);
        int blockZ = Mathf.FloorToInt(arrivalPos.z);
        const int preLoadRadius = 2;

        if (worldMgr != null)
        {
            worldMgr.BeginAreaLoad(blockX, blockZ, preLoadRadius);

            float timeout = Time.realtimeSinceStartup + 10f;
            while (!worldMgr.IsAreaReady(blockX, blockZ, preLoadRadius, out float areaProgress))
            {
                if (Time.realtimeSinceStartup > timeout)
                {
                    Debug.LogWarning("[DimensionManager] Area pre-load timed out; proceeding with spawn.");
                    break;
                }

                if (loadingScreen != null)
                    loadingScreen.SetProgress(0.4f + areaProgress * 0.55f);

                yield return null;
            }
        }

        if (loadingScreen != null) loadingScreen.SetProgress(1.0f);
        yield return null;

        // 9. Relocate player controller cleanly
        if (player != null)
        {
            var pc = player.GetComponent<PlayerController>();
            if (pc != null)
            {
                pc.Teleport(arrivalPos);
            }
            else
            {
                player.position = arrivalPos;
                var rb = player.GetComponent<Rigidbody>();
                if (rb != null) rb.linearVelocity = Vector3.zero;
            }

            if (worldMgr != null)
            {
                worldMgr.AttachPlayer(player);
            }
        }

        // 10. Spawn the return rift on the wall anchor provided by the generator.
        //     Which dimensions get one is data (DimensionProfile.spawnReturnPortal), not an id check.
        if (nextProfile.spawnReturnPortal)
        {
            if (hasArrivalPoint && arrivalPoint.hasReturnAnchor)
                SpawnReturnPortal(nextProfile, arrivalPoint);
            else
                Debug.LogWarning($"[DimensionManager] '{nextProfile.dimensionId}' requests a return portal but its generator provides no wall anchor (TryGetArrivalPoint).");
        }

        yield return new WaitForSeconds(0.15f);

        // 11. Hide loading screen and finish
        if (loadingScreen != null)
            loadingScreen.Hide();

        _arrivalGraceUntil = Time.time + ArrivalGraceSeconds;

        OnDimensionChanged?.Invoke(nextProfile);
        IsTransitioning = false;
        onComplete?.Invoke();
    }

    private void SpawnReturnPortal(DimensionProfile profile, DimensionArrivalPoint arrival)
    {
        Quaternion portalRot = Quaternion.Euler(0f, arrival.returnAnchorYaw, 0f);

        GameObject portalObj;
        WallRiftPortal portal;

        if (wallRiftPrefab != null)
        {
            portalObj = Instantiate(wallRiftPrefab, arrival.returnAnchorPosition, portalRot);
            portal = portalObj.GetComponent<WallRiftPortal>();
            if (portal == null) portal = portalObj.AddComponent<WallRiftPortal>();
        }
        else
        {
            portalObj = new GameObject("WallRiftPortal_KanePixels");
            portalObj.transform.position = arrival.returnAnchorPosition;
            portalObj.transform.rotation = portalRot;
            portal = portalObj.AddComponent<WallRiftPortal>();

            // Visual vertical glowing slit cube
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "RiftVisualBeam";
            visual.transform.SetParent(portalObj.transform, false);
            visual.transform.localScale = new Vector3(0.2f, 2.6f, 0.08f);
            visual.transform.localPosition = new Vector3(0f, 1.3f, 0f);

            var col = visual.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var renderer = visual.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = GetRiftMaterial();
        }

        portal.SetTarget(profile.returnDimensionId);
        _activePortals.Add(portalObj);
    }

    private Material GetRiftMaterial()
    {
        if (_riftMaterial == null)
        {
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _riftMaterial = new Material(unlitShader) { name = "RiftBeam_Runtime" };
            _riftMaterial.color = new Color(0.9f, 0.96f, 1f, 1f);
        }
        return _riftMaterial;
    }

    private bool IsNearActivePortal(Vector3 position, float radius)
    {
        float sqrRadius = radius * radius;
        for (int i = 0; i < _activePortals.Count; i++)
        {
            if (_activePortals[i] == null) continue;

            Vector3 delta = position - _activePortals[i].transform.position;
            if (Mathf.Abs(delta.y) < 4f && (delta.x * delta.x + delta.z * delta.z) < sqrRadius)
                return true;
        }
        return false;
    }

    private void ClearActivePortals()
    {
        for (int i = 0; i < _activePortals.Count; i++)
        {
            if (_activePortals[i] != null) Destroy(_activePortals[i]);
        }
        _activePortals.Clear();
    }
}
