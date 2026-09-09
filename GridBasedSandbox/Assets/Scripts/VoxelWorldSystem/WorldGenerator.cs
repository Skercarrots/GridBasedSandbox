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
//
//  ASYNC GENERATION
//  VoxelWorldManager now runs Generate() on background threads via
//  ChunkGenerationScheduler. Prepare() is your one-time, MAIN-THREAD hook to
//  bake anything Generate() will need to read off-thread but that isn't
//  actually safe in its original Unity-object form — AnimationCurve.Evaluate()
//  being the concrete example fixed in BiomeDefinition (bakes to a float[]
//  LUT). It's called exactly once, before the first background Generate() call.
// ─────────────────────────────────────────────────────────────────────────────

public abstract class WorldGenerator : ScriptableObject
{
    /// <summary>Runs once on the main thread before any Generate() call — including
    /// the first background-thread one. Default no-op; override to bake anything
    /// Generate() needs that isn't safe to touch off-thread in its raw form.</summary>
    public virtual void Prepare() { }

    public abstract void Generate(VoxelChunkData data, VoxelWorldSettings settings, int seed);
}