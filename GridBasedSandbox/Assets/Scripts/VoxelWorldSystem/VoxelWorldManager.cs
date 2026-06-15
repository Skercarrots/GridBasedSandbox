using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  VoxelWorldManager  — The central brain of the voxel world system.
//
//  RESPONSIBILITIES
//  1. Tracks the player's chunk coordinate.
//  2. Loads (generates + meshes) chunks that enter view distance.
//  3. Unloads chunks that leave view distance, returning them to a pool.
//  4. Manages a rebuild queue — dirty chunks get remeshed in budget slices.
//  5. Implements IChunkNeighbourSampler so the mesh builder can query
//     cross-chunk block data without circular dependencies.
//
//  HOW TO SET UP IN UNITY
//  • Create a GameObject → Add VoxelWorldManager.
//  • Assign a VoxelWorldSettings SO in the Inspector.
//  • Assign your VoxelChunk prefab (has MeshFilter, MeshRenderer, MeshCollider).
//  • Assign the Transform of the player (or camera).
//  • Press Play — the world streams around the player.
//
//  INTEGRATION WITH EXISTING GridSystem
//  VoxelWorldManager does NOT touch your existing GridSystem.cs.
//  The two systems share a coordinate convention (1 unit = 1 cell) but are
//  otherwise independent. If you want to place GridSystem objects ON TOP of
//  the voxel terrain, query TryGetBlockWorld() or GetTopSolidY() to find the
//  surface height and then use your existing GridSystem.GridToWorldPosition().
// ─────────────────────────────────────────────────────────────────────────────

public class VoxelWorldManager : MonoBehaviour, IChunkNeighbourSampler
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Settings")]
    [SerializeField] private VoxelWorldSettings settings;

    [Header("Scene references")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private GameObject chunkPrefab;

    // ── State ─────────────────────────────────────────────────────────────────
    // Active chunks keyed by chunk coord (cx, cz)
    private readonly Dictionary<Vector2Int, VoxelChunk>     _activeChunks = new();
    // Chunk data keyed by chunk coord (kept alive even when chunk is pooled
    // so we don't re-generate terrain when the player comes back)
    private readonly Dictionary<Vector2Int, VoxelChunkData> _chunkDataCache = new();

    // Object pool for VoxelChunk GameObjects
    private readonly Queue<VoxelChunk>  _pool        = new();

    // Rebuild queue — dirty chunks waiting for a remesh
    private readonly Queue<Vector2Int>  _rebuildQueue = new();

    // Track last known chunk coordinate to avoid re-scanning every frame
    private Vector2Int _lastPlayerChunkCoord = new Vector2Int(int.MinValue, int.MinValue);

    // Resolved seed (may be randomised if settings.seed == 0)
    private int _seed;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        ValidateReferences();
        settings.blockRegistry.Initialize();
        _seed = settings.seed == 0 ? Random.Range(1, int.MaxValue) : settings.seed;
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

    // ── Streaming ─────────────────────────────────────────────────────────────

    private void UpdateStreamingFromPlayer(bool force)
    {
        Vector2Int playerChunkCoord = WorldToChunkCoord(playerTransform.position);

        if (!force && playerChunkCoord == _lastPlayerChunkCoord) return;
        _lastPlayerChunkCoord = playerChunkCoord;

        int range = settings.viewDistanceInChunks;

        // ── Collect which chunks SHOULD be loaded ─────────────────────────
        var desired = new HashSet<Vector2Int>();
        for (int cx = -range; cx <= range; cx++)
        for (int cz = -range; cz <= range; cz++)
            desired.Add(playerChunkCoord + new Vector2Int(cx, cz));

        // ── Unload chunks that are now out of range ────────────────────────
        var toUnload = new List<Vector2Int>();
        foreach (var coord in _activeChunks.Keys)
            if (!desired.Contains(coord)) toUnload.Add(coord);

        foreach (var coord in toUnload) UnloadChunk(coord);

        // ── Load/activate chunks that are now in range ─────────────────────
        foreach (var coord in desired)
            if (!_activeChunks.ContainsKey(coord)) LoadChunk(coord);
    }

    // ── Chunk load / unload ───────────────────────────────────────────────────

    private void LoadChunk(Vector2Int coord)
    {
        // Get or generate chunk data
        if (!_chunkDataCache.TryGetValue(coord, out VoxelChunkData data))
        {
            data = new VoxelChunkData(coord, settings.chunkWidth, settings.chunkHeight);
            VoxelTerrainGenerator.Generate(data, settings, _seed);
            _chunkDataCache[coord] = data;
        }

        // Get a pooled GameObject or instantiate a new one
        VoxelChunk chunk = GetOrCreateChunkObject();
        chunk.gameObject.SetActive(true);
        chunk.SetData(data, settings, settings.chunkMaterial);
        _activeChunks[coord] = chunk;

        // Queue for initial mesh build
        EnqueueRebuild(coord);
    }

    private void UnloadChunk(Vector2Int coord)
    {
        if (!_activeChunks.TryGetValue(coord, out VoxelChunk chunk)) return;

        chunk.Reset();
        chunk.gameObject.SetActive(false);
        _pool.Enqueue(chunk);
        _activeChunks.Remove(coord);
        // NOTE: _chunkDataCache keeps the data so re-entering doesn't re-generate
    }

    // ── Rebuild queue ─────────────────────────────────────────────────────────

    /// <summary>Adds coord to the rebuild queue (deduplicated at consume time).</summary>
    public void EnqueueRebuild(Vector2Int coord)
    {
        _rebuildQueue.Enqueue(coord);
    }

    private void ProcessRebuildQueue()
    {
        int budget = settings.maxChunkBuildsPerFrame;
        int processed = 0;

        while (_rebuildQueue.Count > 0 && processed < budget)
        {
            Vector2Int coord = _rebuildQueue.Dequeue();

            if (!_activeChunks.TryGetValue(coord, out VoxelChunk chunk)) continue;
            if (chunk.Data == null) continue;

            MeshData meshData = VoxelMeshBuilder.Build(chunk.Data, settings, this);
            chunk.ApplyMesh(meshData);
            processed++;
        }
    }

    // ── IChunkNeighbourSampler ────────────────────────────────────────────────

    /// <summary>
    /// Returns the block id at world-space block position (wx, wy, wz).
    /// Used by VoxelMeshBuilder to resolve cross-chunk border faces.
    /// </summary>
    public byte GetBlockAt(int wx, int wy, int wz)
    {
        // Convert world block position to chunk coord
        Vector2Int coord = BlockToChunkCoord(wx, wz);

        // If the chunk is active, query its data directly
        if (_activeChunks.TryGetValue(coord, out VoxelChunk chunk) && chunk.Data != null)
        {
            int ly = settings.WorldYToLocal(wy);
            int lx = wx - chunk.Data.WorldOriginX;
            int lz = wz - chunk.Data.WorldOriginZ;
            return chunk.Data.GetBlock(lx, ly, lz);
        }

        // If chunk data is cached but not active (e.g. just outside view)
        if (_chunkDataCache.TryGetValue(coord, out VoxelChunkData data))
        {
            int ly = settings.WorldYToLocal(wy);
            int lx = wx - data.WorldOriginX;
            int lz = wz - data.WorldOriginZ;
            return data.GetBlock(lx, ly, lz);
        }

        return 0; // unknown chunk → treat as air
    }

    // ── Public block editing API ──────────────────────────────────────────────

    /// <summary>
    /// Places or removes a block at world-space block position.
    /// Automatically flags the chunk (and affected neighbours) for rebuild.
    /// </summary>
    public bool TrySetBlock(int wx, int wy, int wz, byte id)
    {
        Vector2Int coord = BlockToChunkCoord(wx, wz);

        if (!_activeChunks.TryGetValue(coord, out VoxelChunk chunk)) return false;
        if (!chunk.TrySetBlockWorld(wx, wy, wz, id, settings)) return false;

        EnqueueRebuild(coord);

        // If the block is on a chunk border, flag the neighbour too
        int localX = wx - chunk.Data.WorldOriginX;
        int localZ = wz - chunk.Data.WorldOriginZ;
        int w = settings.chunkWidth - 1;

        if (localX == 0)              EnqueueRebuildIfActive(coord + new Vector2Int(-1, 0));
        if (localX == w)              EnqueueRebuildIfActive(coord + new Vector2Int( 1, 0));
        if (localZ == 0)              EnqueueRebuildIfActive(coord + new Vector2Int( 0,-1));
        if (localZ == w)              EnqueueRebuildIfActive(coord + new Vector2Int( 0, 1));

        return true;
    }

    /// <summary>Returns the highest solid block Y at world X/Z, or minHeight-1 if none.</summary>
    public int GetTopSolidY(int wx, int wz)
    {
        Vector2Int coord = BlockToChunkCoord(wx, wz);
        if (!_chunkDataCache.TryGetValue(coord, out VoxelChunkData data)) return settings.minHeight - 1;

        int lx = wx - data.WorldOriginX;
        int lz = wz - data.WorldOriginZ;

        for (int ly = data.Height - 1; ly >= 0; ly--)
        {
            byte id = data.GetBlock(lx, ly, lz);
            if (settings.blockRegistry.IsSolid(id))
                return settings.LocalYToWorld(ly);
        }
        return settings.minHeight - 1;
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────

    private Vector2Int WorldToChunkCoord(Vector3 worldPos)
    {
        int cx = Mathf.FloorToInt(worldPos.x / settings.chunkWidth);
        int cz = Mathf.FloorToInt(worldPos.z / settings.chunkWidth);
        return new Vector2Int(cx, cz);
    }

    private Vector2Int BlockToChunkCoord(int wx, int wz)
    {
        // Use FloorToInt to handle negative block coords correctly
        int cx = Mathf.FloorToInt((float)wx / settings.chunkWidth);
        int cz = Mathf.FloorToInt((float)wz / settings.chunkWidth);
        return new Vector2Int(cx, cz);
    }

    // ── Pool helpers ──────────────────────────────────────────────────────────

    private VoxelChunk GetOrCreateChunkObject()
    {
        if (_pool.Count > 0)
            return _pool.Dequeue();

        GameObject go = Instantiate(chunkPrefab, transform);
        return go.GetComponent<VoxelChunk>();
    }

    private void EnqueueRebuildIfActive(Vector2Int coord)
    {
        if (_activeChunks.ContainsKey(coord))
            EnqueueRebuild(coord);
    }

    // ── Validation ────────────────────────────────────────────────────────────

    private void ValidateReferences()
    {
        if (settings == null)
            Debug.LogError("[VoxelWorldManager] Missing VoxelWorldSettings!", this);
        if (playerTransform == null)
            Debug.LogError("[VoxelWorldManager] Missing player Transform!", this);
        if (chunkPrefab == null)
            Debug.LogError("[VoxelWorldManager] Missing chunk prefab!", this);
        if (settings != null && settings.chunkMaterial == null)
            Debug.LogWarning("[VoxelWorldManager] No chunk material assigned — chunks will be pink.", this);
    }
}
