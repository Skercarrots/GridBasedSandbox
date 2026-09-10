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
//  • EITHER assign a player Transform directly in the Inspector (simple case,
//    world streams around whatever is already in the scene) OR leave it empty
//    and call AttachPlayer() at runtime once you've spawned your player prefab
//    — see WorldBootstrapper for the recommended spawn-with-loading-screen flow.
//
//  ASYNC CHUNK GENERATION
//  Chunk DATA generation (WorldGenerator.Generate()) now runs on background
//  threads via ChunkGenerationScheduler — see that class for details. Mesh
//  building stays on the main thread, budgeted by maxChunkBuildsPerFrame, same
//  as before. This applies uniformly: normal streaming as the player walks,
//  AND the initial pre-spawn load via BeginAreaLoad(), share one pipeline.
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
    // Convenience singleton — lets device components (e.g. VoxelBodySensor)
    // query the world without every prefab needing a manual Inspector
    // reference. Assumes a single VoxelWorldManager in the scene, which
    // matches how the rest of this system (chunk streaming, spawn point)
    // already assumes one world.
    public static VoxelWorldManager Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Settings")]
    [SerializeField] private VoxelWorldSettings settings;
    [SerializeField] private WorldGenerator worldGenerator;

    [Header("Scene references")]
    [Tooltip("Leave Player Transform field empty in the Inspector if you're using the bootstrapper — it'll wait for AttachPlayer() instead of streaming around nothing at Start().")]
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

    // Which chunk coords SHOULD currently be loaded — persisted across frames
    // (not just a local variable) so DrainGeneratedChunks knows, when a
    // background generation finishes, whether it's still wanted or the player
    // has since moved away.
    private HashSet<Vector2Int> _desiredChunks = new();

    // Background generation. Data creation happens on worker threads; this is
    // where finished results land until the main thread picks them up.
    private ChunkGenerationScheduler _scheduler;
    private readonly List<(Vector2Int coord, VoxelChunkData data)> _drainBuffer = new();

    // Track last known chunk coordinate to avoid re-scanning every frame
    private Vector2Int _lastPlayerChunkCoord = new Vector2Int(int.MinValue, int.MinValue);

    // Resolved seed (may be randomised if settings.seed == 0)
    private int _seed;

    // Guards EnsureInitialized() so it only ever does its work once, no matter
    // whether it's triggered by Awake() or by an early GetSpawnPosition() call.
    private bool _initialized = false;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;
        EnsureInitialized();
    }

    /// <summary>
    /// One-time setup: validates references, initializes the block registry, and
    /// resolves the world seed. Normally this just runs from Awake(). It's pulled
    /// out into its own method — and called again from GetSpawnPosition() — so a
    /// spawn script can safely ask for a spawn point from ITS OWN Awake(), without
    /// relying on Unity having already run VoxelWorldManager.Awake() first (Unity
    /// doesn't guarantee Awake() order between different components).
    /// </summary>
    private void EnsureInitialized()
    {
        if (_initialized) return;

        ValidateReferences();
        settings.blockRegistry.Initialize();
        _seed = settings.seed == 0 ? Random.Range(1, int.MaxValue) : settings.seed;

        // Let the generator bake anything it'll need off-thread (e.g. an
        // AnimationCurve → LUT) BEFORE we ever dispatch a background Generate().
        worldGenerator.Prepare();

        _scheduler = new ChunkGenerationScheduler(worldGenerator, settings, _seed, settings.maxConcurrentChunkGenerations);

        _initialized = true;
    }

    private void Start()
    {
        // If no player was assigned in the Inspector, someone (e.g.
        // WorldBootstrapper) is going to call AttachPlayer() later once it's
        // spawned the player prefab — streaming simply doesn't start until then.
        if (playerTransform != null)
            UpdateStreamingFromPlayer(force: true);
    }

    private void Update()
    {
        if (playerTransform != null)
            UpdateStreamingFromPlayer(force: false);

        DrainGeneratedChunks();
        ProcessRebuildQueue();
    }

    // ── Streaming ─────────────────────────────────────────────────────────────

    private void UpdateStreamingFromPlayer(bool force)
    {
        Vector2Int playerChunkCoord = WorldToChunkCoord(playerTransform.position);

        if (!force && playerChunkCoord == _lastPlayerChunkCoord) return;
        _lastPlayerChunkCoord = playerChunkCoord;

        UpdateStreamingAround(playerChunkCoord, settings.viewDistanceInChunks);
    }

    /// <summary>
    /// Core streaming step: unloads active chunks outside range of
    /// centerChunkCoord, and requests (possibly async) loading for every chunk
    /// inside it that isn't active yet. Shared by per-frame player streaming and
    /// by BeginAreaLoad()'s pre-spawn load, so there's exactly one code path for
    /// "make this area exist" — no risk of the two drifting apart.
    /// </summary>
    private void UpdateStreamingAround(Vector2Int centerChunkCoord, int range)
    {
        // ── Collect which chunks SHOULD be loaded ─────────────────────────
        var desired = new HashSet<Vector2Int>();
        for (int cx = -range; cx <= range; cx++)
        for (int cz = -range; cz <= range; cz++)
            desired.Add(centerChunkCoord + new Vector2Int(cx, cz));

        // ── Unload chunks that are now out of range ────────────────────────
        var toUnload = new List<Vector2Int>();
        foreach (var coord in _activeChunks.Keys)
            if (!desired.Contains(coord)) toUnload.Add(coord);

        foreach (var coord in toUnload) UnloadChunk(coord);

        _desiredChunks = desired;

        // ── Request load/activate for chunks that are now in range ─────────
        foreach (var coord in desired)
            if (!_activeChunks.ContainsKey(coord)) RequestChunkLoad(coord);
    }

    // ── Chunk load / unload ───────────────────────────────────────────────────

    /// <summary>Loads coord's data immediately if it's already cached (e.g. the
    /// player revisiting a chunk), otherwise queues background generation —
    /// ActivateChunk() runs later, from DrainGeneratedChunks(), once it's ready.</summary>
    private void RequestChunkLoad(Vector2Int coord)
    {
        if (_chunkDataCache.TryGetValue(coord, out VoxelChunkData data))
        {
            ActivateChunk(coord, data);
            return;
        }

        _scheduler.Enqueue(coord);
    }

    /// <summary>Attaches data to a (possibly pooled) VoxelChunk GameObject and
    /// queues it for a mesh rebuild. Main thread only.</summary>
    private void ActivateChunk(Vector2Int coord, VoxelChunkData data)
    {
        VoxelChunk chunk = GetOrCreateChunkObject();
        chunk.gameObject.SetActive(true);
        chunk.SetData(data, settings, settings.chunkMaterial);
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
        // NOTE: _chunkDataCache keeps the data so re-entering doesn't re-generate
    }

    /// <summary>Returns coord's chunk data from cache, generating it SYNCHRONOUSLY
    /// (blocking, main thread) if it isn't cached yet. Deliberately not async —
    /// used only by GetSpawnPosition(), which needs a single chunk's answer
    /// immediately, once, at startup. Everything else goes through the async
    /// RequestChunkLoad()/ChunkGenerationScheduler path instead.</summary>
    private VoxelChunkData GetOrGenerateChunkData(Vector2Int coord)
    {
        if (!_chunkDataCache.TryGetValue(coord, out VoxelChunkData data))
        {
            data = new VoxelChunkData(coord, settings.chunkWidth, settings.TotalWorldHeight);
            worldGenerator.Generate(data, settings, _seed);
            _chunkDataCache[coord] = data;
        }
        return data;
    }

    /// <summary>Main thread. Picks up every chunk the background scheduler has
    /// finished generating since last frame, caches it, and activates it if it's
    /// still desired (the player may have moved away while it was generating).</summary>
    private void DrainGeneratedChunks()
    {
        _drainBuffer.Clear();
        _scheduler.DrainCompleted(_drainBuffer, int.MaxValue); // cheap — just data handoff, no mesh work here

        foreach (var (coord, data) in _drainBuffer)
        {
            _chunkDataCache[coord] = data;

            if (_desiredChunks.Contains(coord) && !_activeChunks.ContainsKey(coord))
                ActivateChunk(coord, data);
        }
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

    /// <summary>
    /// True if the block at world-space block position (wx, wy, wz) is solid.
    /// Convenience wrapper around GetBlockAt() + the block registry, meant for
    /// device sensors (see VoxelBodySensor) that just need a yes/no answer.
    /// Unloaded chunks read as air, same convention as GetBlockAt().
    /// </summary>
    public bool IsSolidBlock(int wx, int wy, int wz)
        => settings.blockRegistry.IsSolid(GetBlockAt(wx, wy, wz));

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

    /// <summary>
    /// Finds a safe spawn point at world block X/Z: the space directly above the
    /// highest solid block in that column (e.g. GetSpawnPosition(0, 0) for the
    /// world's center column). Unlike GetTopSolidY(), this GENERATES the chunk on
    /// demand if it isn't cached yet — which is the usual case at startup, since
    /// chunk streaming only loads chunks around wherever the player already is.
    /// That's what makes it safe to call before the player has been placed.
    ///
    /// Caves are never carved within caveMinDepthBelowSurface blocks of the
    /// surface (see OverworldGenerator), so the highest solid block in a column
    /// is always the true ground surface, never the roof of a cave — the player
    /// will never spawn underground.
    /// </summary>
    /// <param name="wx">World block X (0 = world center column).</param>
    /// <param name="wz">World block Z (0 = world center column).</param>
    /// <returns>World-space position centered on the block, one block above the
    /// surface. Nudge Y up further if your character controller needs more headroom.</returns>
    public Vector3 GetSpawnPosition(int wx, int wz)
    {
        EnsureInitialized();

        Vector2Int coord = BlockToChunkCoord(wx, wz);
        VoxelChunkData data = GetOrGenerateChunkData(coord);

        int lx = wx - data.WorldOriginX;
        int lz = wz - data.WorldOriginZ;

        for (int ly = data.Height - 1; ly >= 0; ly--)
        {
            byte id = data.GetBlock(lx, ly, lz);
            if (settings.blockRegistry.IsSolid(id))
            {
                int surfaceY = settings.LocalYToWorld(ly);
                return new Vector3(wx + 0.5f, surfaceY + 1f, wz + 0.5f);
            }
        }

        // Column came back fully air (shouldn't normally happen) — fall back to
        // just above the world floor instead of leaving the caller with garbage.
        return new Vector3(wx + 0.5f, settings.minHeight + 1f, wz + 0.5f);
    }

    // ── Pre-spawn bootstrapping (see WorldBootstrapper) ─────────────────────────

    /// <summary>
    /// Requests every chunk within radiusInChunks of the chunk containing world
    /// block (wx, wz) — no player Transform required. Generation happens in the
    /// background same as normal streaming; poll IsAreaReady() to know when it's
    /// safe to spawn something on that ground. Typically called once, at startup,
    /// with a small radius (1–2), just to guarantee solid ground under the spawn
    /// point before the player prefab is instantiated.
    /// </summary>
    public void BeginAreaLoad(int wx, int wz, int radiusInChunks)
    {
        EnsureInitialized();
        UpdateStreamingAround(BlockToChunkCoord(wx, wz), radiusInChunks);
    }

    /// <summary>
    /// True once every chunk within radiusInChunks of world block (wx, wz) is
    /// both generated AND meshed (i.e. has real collision — safe to stand on).
    /// progress is readyCount/totalCount, for driving a loading bar.
    /// </summary>
    public bool IsAreaReady(int wx, int wz, int radiusInChunks, out float progress)
    {
        Vector2Int center = BlockToChunkCoord(wx, wz);
        int total = 0, ready = 0;

        for (int cx = -radiusInChunks; cx <= radiusInChunks; cx++)
        for (int cz = -radiusInChunks; cz <= radiusInChunks; cz++)
        {
            total++;
            Vector2Int coord = center + new Vector2Int(cx, cz);
            if (_activeChunks.TryGetValue(coord, out VoxelChunk chunk) && !chunk.IsMeshDirty)
                ready++;
        }

        progress = total == 0 ? 1f : ready / (float)total;
        return ready == total;
    }

    /// <summary>
    /// Hands the manager a live player Transform and immediately starts normal
    /// distance-based streaming around it. Call this once, right after
    /// instantiating the player prefab — WorldBootstrapper does this itself once
    /// IsAreaReady() confirms the spawn point has solid, meshed ground.
    /// </summary>
    public void AttachPlayer(Transform player)
    {
        playerTransform = player;
        _lastPlayerChunkCoord = new Vector2Int(int.MinValue, int.MinValue); // force a full recompute
        UpdateStreamingFromPlayer(force: true);
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
        if (worldGenerator == null)
            Debug.LogError("[VoxelWorldManager] Missing WorldGenerator! Assign an OverworldGenerator asset (or other WorldGenerator) in the Inspector.", this);
        if (chunkPrefab == null)
            Debug.LogError("[VoxelWorldManager] Missing chunk prefab!", this);
        if (settings != null && settings.chunkMaterial == null)
            Debug.LogWarning("[VoxelWorldManager] No chunk material assigned — chunks will be pink.", this);
        // playerTransform is intentionally NOT validated here — it's fine to be
        // unset at startup if you're using WorldBootstrapper / AttachPlayer() to
        // spawn the player once the world is ready, instead of hand-placing one
        // in the scene ahead of time.
    }
}