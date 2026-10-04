// ─────────────────────────────────────────────────────────────────────────────
//  ISurfaceSampler — Interface providing world surface queries to StructurePlacer.
//  Implemented by OverworldGenerator so structures can query surface elevations,
//  ground blocks, slope, and biomes across chunk borders.
// ─────────────────────────────────────────────────────────────────────────────

public interface ISurfaceSampler
{
    /// <summary>Finds the surface Y and ground block at world (wx, wz). Returns false if no surface found.</summary>
    bool TryGetSurface(int wx, int wz, out int surfaceY, out byte surfaceBlockId);

    /// <summary>Finds the surface Y, ground block, and terrain slope gradient at world (wx, wz).</summary>
    bool TryGetSurface(int wx, int wz, out int surfaceY, out byte surfaceBlockId, out float slope);

    /// <summary>Checks whether a vertical column from startY up to startY + height is clear air.</summary>
    bool IsAirColumn(int wx, int startY, int wz, int height);

    /// <summary>Returns the biome definition at world (wx, wz).</summary>
    BiomeDefinition GetBiome(int wx, int wz);
}
