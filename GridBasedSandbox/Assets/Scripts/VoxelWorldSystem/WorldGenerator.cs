using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  WorldGenerator — Abstract ScriptableObject base for anything that can fill
//  a VoxelChunkData. Making this an SO (rather than a plain interface) lets
//  you assign a concrete generator as an asset in the Inspector, and swap it
//  per-dimension via DimensionProfile without any code changes.
//
//  IMPLEMENTING A NEW GENERATOR
//  Subclass this, implement Generate(), add [CreateAssetMenu]. See
//  OverworldGenerator for the reference implementation.
//
//  THREADING
//  Generate() must not call any Unity API that requires the main thread
//  (Instantiate, GetComponent, Physics, etc). Reading plain serialized fields
//  off ScriptableObject references (block IDs, floats, AnimationCurve) is
//  commonly done off-thread but isn't officially guaranteed thread-safe by
//  Unity — see the migration guide's "Async generation" section for a safer
//  alternative if you hit issues.
// ─────────────────────────────────────────────────────────────────────────────

public abstract class WorldGenerator : ScriptableObject
{
    public abstract void Generate(VoxelChunkData data, VoxelWorldSettings settings, int seed);
}