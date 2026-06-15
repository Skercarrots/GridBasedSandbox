using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  VoxelChunk  — MonoBehaviour that represents one loaded chunk in the scene.
//
//  Responsibilities
//  • Owns the MeshFilter, MeshRenderer, and MeshCollider for this chunk.
//  • Holds a reference to the chunk's VoxelChunkData (pure data).
//  • Knows how to apply a pre-built MeshData to its mesh components.
//  • Does NOT generate terrain or build meshes — that belongs to other systems.
//
//  Lifecycle (managed by VoxelWorldManager)
//  1. Instantiated when a chunk enters view distance.
//  2. Receives SetData() to attach its VoxelChunkData.
//  3. ApplyMesh() is called after the mesh builder finishes.
//  4. Destroyed (or returned to pool) when it leaves view distance.
//
//  POOLING NOTE
//  The manager uses a simple pool of VoxelChunk GameObjects.
//  When recycled, call Reset() to clear the old data before SetData().
// ─────────────────────────────────────────────────────────────────────────────

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class VoxelChunk : MonoBehaviour
{
    // ── Components ────────────────────────────────────────────────────────────
    private MeshFilter   _filter;
    private MeshRenderer _renderer;
    private MeshCollider _collider;

    // ── Data ──────────────────────────────────────────────────────────────────
    public VoxelChunkData Data { get; private set; }

    // ── State ─────────────────────────────────────────────────────────────────
    public bool IsMeshDirty { get; set; } = false;

    // ── Unity setup ───────────────────────────────────────────────────────────
    private void Awake()
    {
        _filter   = GetComponent<MeshFilter>();
        _renderer = GetComponent<MeshRenderer>();
        _collider = GetComponent<MeshCollider>();

        _filter.mesh = new Mesh { name = "ChunkMesh" };
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Attaches chunk data and positions the GameObject in the world.</summary>
    public void SetData(VoxelChunkData data, VoxelWorldSettings settings, Material material)
    {
        Data = data;
        _renderer.material = material;
        IsMeshDirty = true;

        // Position the chunk: world origin X/Z, bottom of the world at minHeight.
        transform.position = new Vector3(
            data.WorldOriginX,
            settings.minHeight,
            data.WorldOriginZ);

        gameObject.name = $"Chunk_{data.ChunkCoord.x}_{data.ChunkCoord.y}";
    }

    /// <summary>Pushes new mesh data onto the chunk mesh and collider. Main thread only.</summary>
    public void ApplyMesh(in MeshData meshData)
    {
        Mesh mesh = _filter.mesh;
        meshData.ApplyToMesh(mesh);

        // Sync the collider to the new mesh
        _collider.sharedMesh = null;   // force refresh
        _collider.sharedMesh = mesh;

        IsMeshDirty = false;
    }

    /// <summary>Clears the chunk for reuse by the pool.</summary>
    public void Reset()
    {
        Data = null;
        _filter.mesh.Clear();
        _collider.sharedMesh = null;
        IsMeshDirty = false;
        gameObject.name = "Chunk_Pooled";
    }

    // ── Block editing (world-facing) ──────────────────────────────────────────

    /// <summary>
    /// Sets a block at WORLD-SPACE block position (wx, wy, wz).
    /// Returns false if the position is outside this chunk's bounds.
    /// Marks the chunk dirty — the world manager will schedule a rebuild.
    /// </summary>
    public bool TrySetBlockWorld(int wx, int wy, int wz, byte id, VoxelWorldSettings settings)
    {
        if (Data == null) return false;

        Vector3Int local = Data.WorldToLocal(wx, wy, wz, settings.minHeight);

        if (!Data.IsInBounds(local.x, local.y, local.z)) return false;

        Data.SetBlock(local.x, local.y, local.z, id);
        IsMeshDirty = true;
        return true;
    }

    /// <summary>
    /// Reads a block at WORLD-SPACE position. Returns 0 (Air) if out of bounds.
    /// </summary>
    public byte GetBlockWorld(int wx, int wy, int wz, VoxelWorldSettings settings)
    {
        if (Data == null) return 0;
        Vector3Int local = Data.WorldToLocal(wx, wy, wz, settings.minHeight);
        return Data.GetBlock(local.x, local.y, local.z);
    }
}
