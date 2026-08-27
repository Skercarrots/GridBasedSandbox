using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// ═══════════════════════════════════════════════════════════════════════════════
//  VoxelWorldManagerExtended  —  Drop-in upgrade of VoxelWorldManager that adds
//  dimension-aware, modular world generation.
//
//  RELATIONSHIP TO VoxelWorldManager
//  This class REPLACES VoxelWorldManager in the scene.  All chunk streaming,
//  pooling, rebuild queuing, and IChunkNeighbourSampler logic is reproduced
//  here verbatim so the original file stays untouched (per spec).
//
//  NEW FEATURES vs. VoxelWorldManager
//  ┌─────────────────────────────────────────────────────────────────────────┐
//  │ 1. Pluggable generator    — any IWorldGenerator (from DimensionRegistry)│
//  │ 2. Ordered pass pipeline  — IGenerationPass[] run after base generation │
//  │ 3. Dimension switching    — SetActiveDimension() flushes & reloads world│
//  │ 4. GridSystem integration — NotifyGridSystem() marks cells occupied     │
//  │ 5. UnityEvent hooks       — OnDimensionWillChange / OnDimensionChanged  │
//  └─────────────────────────────────────────────────────────────────────────┘
//
//  HOW TO SET UP
//  1. Remove (or disable) any existing VoxelWorldManager component.
//  2. Add VoxelWorldManagerExtended to a GameObject.
//  3. Assign dimensionRegistry, playerTransform, chunkPrefab, and optionally
//     gridSystem in the Inspector.
//  4. Create DimensionProfile + DimensionRegistry assets and populate them.
//  5. Press Play.
//
//  SWITCHING DIMENSIONS AT RUNTIME
//  Call  worldManagerExtended.SwitchDimension("backrooms_level0");
//  or    dimensionRegistry.SetActiveDimension("backrooms_level0");  ← same effect
// ═══════════════════════════════════════════════════════════════════════════════

public class VoxelWorldManagerExtended : MonoBehaviour, IChunkNeighbourSampler
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Dimension System")]
    [SerializeField] private DimensionRegistry dimensionRegistry;

    [Header("Scene References")]
    [SerializeField] private Transform  playerTransform;
    [SerializeField] private GameObject chunkPrefab;

    [Header("GridSystem Integration (optional)")]
    [Tooltip("Assign the GridSystem MonoBehaviour if you want generated solid " +
             "voxel blocks to be automatically marked as occupied cells.")]
    [SerializeField] private GridSystem gridSystem;

    [Header("Unity Events")]
    [Tooltip("Fired just before a dimension switch flushes all chunks.")]
    public UnityEvent<string> OnDimensionWillChange;
    [Tooltip("Fired after the new dimension is active and chunks start loading.")]
    public UnityEvent<string> OnDimensionChanged;

    // ── Runtime state (mirrors VoxelWorldManager) ─────────────────────────────

    private readonly Dictionary<Vector2Int, VoxelChunk>     _activeChunks  = new();
    private readonly Dictionary<Vector2Int, VoxelChunkData> _chunkDataCache = new();
    private readonly Queue<VoxelChunk>   _pool         = new();
    private readonly Queue<Vector2Int>   _rebuildQueue = new();

    private Vector2Int _lastPlayerChunkCoord = new Vector2Int(int.MinValue, int.MinValue);
    private int        _seed;

    // Cached for the active dimension
    private IWorldGenerator    _generator;
    private List<IGenerationPass> _passes = new();
    private VoxelWorldSettings _settings;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (dimensionRegistry == null)
        {
            Debug.LogError("[VoxelWorldManagerExtended] DimensionRegistry is not assigned!", this);
            enabled = false;
            return;
        }

        dimensionRegistry.Initialize();
        dimensionRegistry.OnDimensionChanged += HandleDimensionChanged;

        RefreshActiveDimension();

        _seed = _settings != null && _settings.seed != 0
            ? _settings.seed
            : Random.Range(1, int.MaxValue);
    }

    private void OnDestroy()
    {
        if (dimensionRegistry != null)
            dimensionRegistry.OnDimensionChanged -= HandleDimensionChanged;
    }

    private void Start()
    {
        UpdateStreamingFromPlayer(force: true);
    }

    private void Update()
    {
        UpdateStreamingFromPlayer(force: false);
        ProcessRebuildQueue();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Switches to the dimension with the given <paramref name="id"/>.
    /// Flushes ALL cached chunk data and reloads the world around the player.
    /// </summary>
    public void SwitchDimension(string id)
    {
        if (!dimensionRegistry.SetActiveDimension(id))
            Debug.LogWarning($"[VoxelWorldManagerExtended] Dimension '{id}' not found.");
        // HandleDimensionChanged fires via the event
    }

    /// <summary>Returns the ID of the currently active dimension.</summary>
    public string ActiveDimensionId => dimensionRegistry.ActiveProfile?.dimensionId ?? "none";

    // Proxies matching VoxelWorldManager's public surface so existing code compiles:

    /// <summary>Places or removes a block at world-space block position.</summary>
    public bool TrySetBlock(int wx, int wy, int wz, byte id)
    {
        Vector2Int coord = BlockToChunkCoord(wx, wz);
        if (!_activeChunks.TryGetValue(coord, out VoxelChunk chunk)) return false;
        if (!chunk.TrySetBlockWorld(wx, wy, wz, id, _settings)) return false;

        EnqueueRebuild(coord);

        int localX = wx - chunk.Data.WorldOriginX;
        int localZ = wz - chunk.Data.WorldOriginZ;
        int w = _settings.chunkWidth - 1;

        if (localX == 0) EnqueueRebuildIfActive(coord + new Vector2Int(-1,  0));
        if (localX == w) EnqueueRebuildIfActive(coord + new Vector2Int( 1,  0));
        if (localZ == 0) EnqueueRebuildIfActive(coord + new Vector2Int( 0, -1));
        if (localZ == w) EnqueueRebuildIfActive(coord + new Vector2Int( 0,  1));

        // Sync GridSystem occupancy for the edited cell
        if (gridSystem != null)
        {
            if (id == 0)
                gridSystem.RemoveObjectFromCell(new Vector3Int(wx, wy, wz));
            else
                gridSystem.PlaceObjectInCell(new Vector3Int(wx, wy, wz), null);
        }

        return true;
    }

    /// <summary>Returns the highest solid block Y at world X/Z, or minHeight-1 if none.</summary>
    public int GetTopSolidY(int wx, int wz)
    {
        if (_settings == null) return -1;
        Vector2Int coord = BlockToChunkCoord(wx, wz);
        if (!_chunkDataCache.TryGetValue(coord, out VoxelChunkData data))
            return _settings.minHeight - 1;

        int lx = wx - data.WorldOriginX;
        int lz = wz - data.WorldOriginZ;

        for (int ly = data.Height - 1; ly >= 0; ly--)
        {
            byte bid = data.GetBlock(lx, ly, lz);
            if (_settings.blockRegistry.IsSolid(bid))
                return _settings.LocalYToWorld(ly);
        }
        return _settings.minHeight - 1;
    }

    /// <summary>Adds a chunk coordinate to the rebuild queue.</summary>
    public void EnqueueRebuild(Vector2Int coord) => _rebuildQueue.Enqueue(coord);

    // ── IChunkNeighbourSampler ────────────────────────────────────────────────

    public byte GetBlockAt(int wx, int wy, int wz)
    {
        if (_settings == null) return 0;
        Vector2Int coord = BlockToChunkCoord(wx, wz);

        if (_activeChunks.TryGetValue(coord, out VoxelChunk chunk) && chunk.Data != null)
        {
            int ly = _settings.WorldYToLocal(wy);
            int lx = wx - chunk.Data.WorldOriginX;
            int lz = wz - chunk.Data.WorldOriginZ;
            return chunk.Data.GetBlock(lx, ly, lz);
        }
        if (_chunkDataCache.TryGetValue(coord, out VoxelChunkData data))
        {
            int ly = _settings.WorldYToLocal(wy);
            int lx = wx - data.WorldOriginX;
            int lz = wz - data.WorldOriginZ;
            return data.GetBlock(lx, ly, lz);
        }
        return 0;
    }

    // ── Dimension change handler ──────────────────────────────────────────────

    private void HandleDimensionChanged(DimensionProfile profile)
    {
        string newId = profile.dimensionId;
        OnDimensionWillChange?.Invoke(newId);

        // Unload all active chunks to pool
        var coords = new List<Vector2Int>(_activeChunks.Keys);
        foreach (var c in coords) UnloadChunk(c);

        // Clear cached data (new dimension = new world)
        _chunkDataCache.Clear();

        // Reset position tracker so streaming restarts
        _lastPlayerChunkCoord = new Vector2Int(int.MinValue, int.MinValue);

        // Refresh generator + passes + settings
        RefreshActiveDimension();

        // Re-seed (each dimension can carry its own seed via its VoxelWorldSettings)
        _seed = _settings != null && _settings.seed != 0
            ? _settings.seed
            : _seed; // keep global seed if dimension uses 0

        // Teleport player to dimension spawn
        if (playerTransform != null)
            playerTransform.position = profile.defaultSpawnPosition;

        // Force streaming restart next Update
        UpdateStreamingFromPlayer(force: true);

        OnDimensionChanged?.Invoke(newId);
    }

    private void RefreshActiveDimension()
    {
        _generator = dimensionRegistry.ActiveGenerator;
        _passes    = dimensionRegistry.ActivePasses ?? new List<IGenerationPass>();
        _settings  = dimensionRegistry.ActiveProfile?.worldSettings;

        if (_settings == null)
            Debug.LogWarning("[VoxelWorldManagerExtended] Active dimension has no VoxelWorldSettings!");
        else
            _settings.blockRegistry?.Initialize();
    }

    // ── Streaming (identical logic to VoxelWorldManager) ─────────────────────

    private void UpdateStreamingFromPlayer(bool force)
    {
        if (_settings == null || playerTransform == null) return;

        Vector2Int playerChunkCoord = WorldToChunkCoord(playerTransform.position);
        if (!force && playerChunkCoord == _lastPlayerChunkCoord) return;
        _lastPlayerChunkCoord = playerChunkCoord;

        int range = _settings.viewDistanceInChunks;
        var desired = new HashSet<Vector2Int>();

        for (int cx = -range; cx <= range; cx++)
        for (int cz = -range; cz <= range; cz++)
            desired.Add(playerChunkCoord + new Vector2Int(cx, cz));

        var toUnload = new List<Vector2Int>();
        foreach (var coord in _activeChunks.Keys)
            if (!desired.Contains(coord)) toUnload.Add(coord);
        foreach (var coord in toUnload) UnloadChunk(coord);

        foreach (var coord in desired)
            if (!_activeChunks.ContainsKey(coord)) LoadChunk(coord);
    }

    // ── Chunk load / unload ───────────────────────────────────────────────────

    private void LoadChunk(Vector2Int coord)
    {
        if (!_chunkDataCache.TryGetValue(coord, out VoxelChunkData data))
        {
            data = new VoxelChunkData(coord, _settings.chunkWidth, _settings.chunkHeight);

            // 1. Base generation
            if (_generator != null)
                _generator.Generate(data, _settings, _seed);

            // 2. Post-process passes (in order)
            foreach (var pass in _passes)
            {
                pass.Apply(data, _settings, _seed);
            }

            _chunkDataCache[coord] = data;

            // Notify GridSystem about occupied cells in this chunk
            if (gridSystem != null)
                NotifyGridSystemForChunk(data);
        }

        VoxelChunk chunk = GetOrCreateChunkObject();
        chunk.gameObject.SetActive(true);
        chunk.SetData(data, _settings, _settings.chunkMaterial);
        _activeChunks[coord] = chunk;
        EnqueueRebuild(coord);
    }

    private void UnloadChunk(Vector2Int coord)
    {
        if (!_activeChunks.TryGetValue(coord, out VoxelChunk chunk)) return;
        chunk.Reset();
        chunk.gameObject.SetActive(false);
        _pool.Enqueue(chunk);
        _activeChunks.Remove(coord);
    }

    // ── GridSystem bridge ─────────────────────────────────────────────────────

    /// <summary>
    /// After generating a chunk, marks every solid block as an occupied
    /// GridCell so the GridSystem stays in sync with voxel world state.
    /// Only runs if gridSystem is assigned.
    /// </summary>
    private void NotifyGridSystemForChunk(VoxelChunkData data)
    {
        for (int lx = 0; lx < data.Width;  lx++)
        for (int ly = 0; ly < data.Height; ly++)
        for (int lz = 0; lz < data.Width;  lz++)
        {
            byte id = data.GetBlock(lx, ly, lz);
            if (_settings.blockRegistry.IsSolid(id))
            {
                int wx = data.WorldOriginX + lx;
                int wy = _settings.LocalYToWorld(ly);
                int wz = data.WorldOriginZ + lz;
                gridSystem.PlaceObjectInCell(new Vector3Int(wx, wy, wz), null);
            }
        }
    }

    // ── Rebuild queue ─────────────────────────────────────────────────────────

    private void ProcessRebuildQueue()
    {
        if (_settings == null) return;
        int budget    = _settings.maxChunkBuildsPerFrame;
        int processed = 0;

        while (_rebuildQueue.Count > 0 && processed < budget)
        {
            Vector2Int coord = _rebuildQueue.Dequeue();
            if (!_activeChunks.TryGetValue(coord, out VoxelChunk chunk)) continue;
            if (chunk.Data == null) continue;

            MeshData meshData = VoxelMeshBuilder.Build(chunk.Data, _settings, this);
            chunk.ApplyMesh(meshData);
            processed++;
        }
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────

    private Vector2Int WorldToChunkCoord(Vector3 worldPos)
    {
        int cx = Mathf.FloorToInt(worldPos.x / _settings.chunkWidth);
        int cz = Mathf.FloorToInt(worldPos.z / _settings.chunkWidth);
        return new Vector2Int(cx, cz);
    }

    private Vector2Int BlockToChunkCoord(int wx, int wz)
    {
        int cx = Mathf.FloorToInt((float)wx / _settings.chunkWidth);
        int cz = Mathf.FloorToInt((float)wz / _settings.chunkWidth);
        return new Vector2Int(cx, cz);
    }

    private VoxelChunk GetOrCreateChunkObject()
    {
        if (_pool.Count > 0) return _pool.Dequeue();
        GameObject go = Instantiate(chunkPrefab, transform);
        return go.GetComponent<VoxelChunk>();
    }

    private void EnqueueRebuildIfActive(Vector2Int coord)
    {
        if (_activeChunks.ContainsKey(coord)) EnqueueRebuild(coord);
    }
}
