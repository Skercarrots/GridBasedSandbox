// ═══════════════════════════════════════════════════════════════════════════════
//  IGenerationPass  —  A single composable step in the world-generation pipeline.
//
//  PURPOSE
//  Generators fill the chunk with base terrain.  Passes then modify that data
//  before the mesh is built:  structure placement, ore veins, decorations,
//  surface-detail scattering, etc.
//
//  PIPELINE ORDER (per chunk)
//    1. IWorldGenerator.Generate()      ← fills blank canvas
//    2. IGenerationPass[0].Apply()      ← e.g. room/corridor carver
//    3. IGenerationPass[1].Apply()      ← e.g. prop placer
//    4. …
//    N. VoxelMeshBuilder.Build()        ← convert to mesh
//
//  DESIGN NOTES
//  · Passes are stored in a List<IGenerationPass> on the DimensionProfile SO.
//    Add, remove, or reorder them without touching any generator code.
//  · Passes share the same thread-safety contract as IWorldGenerator:
//    no Unity API calls inside Apply().
// ═══════════════════════════════════════════════════════════════════════════════

public interface IGenerationPass
{
    /// <summary>Human-readable label shown in editor logs and profiler.</summary>
    string PassName { get; }

    /// <summary>
    /// Applies this pass to an already-filled <paramref name="data"/>.
    /// Called on the main thread after IWorldGenerator.Generate().
    /// </summary>
    void Apply(VoxelChunkData data, VoxelWorldSettings settings, int seed);
}
