using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
//  IWorldGenerator  —  Contract every world generator must satisfy.
//
//  PURPOSE
//  Defines the single entry-point called by VoxelWorldManager when a chunk
//  needs terrain data.  Swap generators per-dimension without touching any
//  engine code — the manager only cares about this interface.
//
//  IMPLEMENTORS
//  · OverworldGenerator      (existing fBm noise terrain)
//  · BackroomsGenerator      (procedural liminal-space rooms & corridors)
//  · Any future generator    (caves, sky islands, …)
//
//  THREAD SAFETY
//  Implementations MUST be safe to call from a worker thread.
//  → No MonoBehaviour / SceneManager / Unity API calls.
//  → Only pure C#, System.Math, and Unity.Mathematics / Mathf.PerlinNoise.
// ═══════════════════════════════════════════════════════════════════════════════

public interface IWorldGenerator
{
    /// <summary>
    /// Populates <paramref name="data"/> with block IDs.
    /// Called once per new chunk; result is cached in VoxelWorldManager.
    /// </summary>
    /// <param name="data">Empty chunk data container to fill.</param>
    /// <param name="settings">Shared world settings (chunk dims, height range…).</param>
    /// <param name="seed">World seed, already resolved (never 0).</param>
    void Generate(VoxelChunkData data, VoxelWorldSettings settings, int seed);
}
