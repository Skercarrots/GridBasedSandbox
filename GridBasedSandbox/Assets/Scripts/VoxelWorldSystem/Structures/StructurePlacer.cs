using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  StructurePlacer — Stateless, deterministic structure placement engine.
//
//  PLACEMENT ALGORITHM (Jittered Grid)
//  Instead of rolling random numbers per block, space is divided into a grid of
//  cells with cellSize ≈ 16 / sqrt(density).
//  Each grid cell deterministically hashes its cell coordinates and world seed:
//    1. Picks one candidate anchor point within that cell.
//    2. Queries the terrain surface via ISurfaceSampler.
//    3. Validates biome, ground block, slope, altitude, water, and air clearance.
//    4. If valid, generates or fetches the structure variant.
//    5. Rotates and mirrors the structure deterministically.
//    6. Writes ONLY the blocks that fall inside the target chunk's bounds.
//
//  KEY BENEFIT
//  Every chunk running in parallel queries the exact same cell coords and gets
//  the exact same candidate points. Cross-chunk trees seamlessly merge because
//  each chunk only writes its own volume.
// ─────────────────────────────────────────────────────────────────────────────

public static class StructurePlacer
{
    /// <summary>Places all features for one chunk column. Safe on background threads.</summary>
    public static void PlaceFeatures(
        VoxelChunkData chunkData,
        VoxelWorldSettings settings,
        int worldSeed,
        IReadOnlyList<WorldFeature> features,
        ISurfaceSampler surfaceSampler,
        byte bedrockId)
    {
        if (features == null || features.Count == 0 || surfaceSampler == null)
            return;

        int chunkWidth = chunkData.Width;
        int chunkX = chunkData.ChunkCoord.x;
        int chunkZ = chunkData.ChunkCoord.y;

        int chunkMinX = chunkX * chunkWidth;
        int chunkMaxX = chunkMinX + chunkWidth - 1;
        int chunkMinZ = chunkZ * chunkWidth;
        int chunkMaxZ = chunkMinZ + chunkWidth - 1;
        int chunkMinY = settings.minHeight;
        int chunkMaxY = settings.maxHeight;

        for (int fi = 0; fi < features.Count; fi++)
        {
            var feature = features[fi];
            if (feature == null || feature.density <= 0.001f) continue;

            int featureId = fi + 10007;
            int maxExtent = feature.MaxExtent;

            // Calculate grid cell size: average 1 structure per cell
            float targetCellSize = 16f / Mathf.Sqrt(feature.density);
            int cellSize = Mathf.Clamp(Mathf.RoundToInt(targetCellSize), 4, 64);

            // Bounding range of cells that could intersect this chunk
            int searchMinX = chunkMinX - maxExtent;
            int searchMaxX = chunkMaxX + maxExtent;
            int searchMinZ = chunkMinZ - maxExtent;
            int searchMaxZ = chunkMaxZ + maxExtent;

            int minCellX = Mathf.FloorToInt((float)searchMinX / cellSize);
            int maxCellX = Mathf.FloorToInt((float)searchMaxX / cellSize);
            int minCellZ = Mathf.FloorToInt((float)searchMinZ / cellSize);
            int maxCellZ = Mathf.FloorToInt((float)searchMaxZ / cellSize);

            for (int cellX = minCellX; cellX <= maxCellX; cellX++)
            for (int cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
            {
                int cellSeed = DeterministicRng.Hash(worldSeed, cellX, cellZ, featureId);
                var rng = new DeterministicRng(cellSeed);

                // Deterministic candidate coordinate within the cell
                int candidateWX = cellX * cellSize + rng.NextInt(0, cellSize);
                int candidateWZ = cellZ * cellSize + rng.NextInt(0, cellSize);

                // Quick extent check: can a structure at (candidateWX, candidateWZ) reach this chunk?
                if (candidateWX + maxExtent < chunkMinX || candidateWX - maxExtent > chunkMaxX) continue;
                if (candidateWZ + maxExtent < chunkMinZ || candidateWZ - maxExtent > chunkMaxZ) continue;

                // 1. Clustering noise check
                if (feature.clusterNoise != null && feature.clusterNoise.scale > 0.01f)
                {
                    float clusterVal = feature.clusterNoise.Sample(candidateWX, candidateWZ, worldSeed + 8888);
                    if (clusterVal < feature.clusterThreshold) continue;
                }

                // 2. Biome filter check
                BiomeDefinition biome = surfaceSampler.GetBiome(candidateWX, candidateWZ);
                if (!feature.IsBiomeAllowed(biome)) continue;

                // 3. Terrain surface query
                if (!surfaceSampler.TryGetSurface(candidateWX, candidateWZ, out int surfaceY, out byte groundBlockId, out float slope))
                    continue;

                // 4. Altitude check
                if (surfaceY < feature.minAltitude || surfaceY > feature.maxAltitude) continue;

                // 5. Water avoidance
                if (feature.avoidWater && surfaceY <= settings.seaLevel) continue;

                // 6. Slope limit
                if (slope > feature.maxSlope) continue;

                // 7. Ground block match
                if (!feature.IsGroundAllowed(groundBlockId)) continue;

                // 8. Air clearance check
                if (feature.requiredAirClearance > 0 && !surfaceSampler.IsAirColumn(candidateWX, surfaceY + 1, candidateWZ, feature.requiredAirClearance))
                    continue;

                // Anchor is directly on top of the ground block
                int anchorWX = candidateWX;
                int anchorWY = surfaceY + 1;
                int anchorWZ = candidateWZ;

                // Generate structure data
                int variantSeed = (int)rng.NextRaw();
                StructureData structure = feature.GetStructureData(variantSeed);
                if (structure == null || structure.blocks == null || structure.blocks.Length == 0) continue;

                // Apply random 90-degree rotations and X-mirror for variety
                int rotCount = rng.NextInt(0, 4);
                for (int r = 0; r < rotCount; r++) structure = structure.Rotate90();
                if (rng.NextBool()) structure = structure.MirrorX();

                // Place blocks within this chunk
                var blocks = structure.blocks;
                for (int bi = 0; bi < blocks.Length; bi++)
                {
                    var b = blocks[bi];
                    int bWX = anchorWX + b.offset.x;
                    int bWY = anchorWY + b.offset.y;
                    int bWZ = anchorWZ + b.offset.z;

                    // Bounds check for THIS chunk
                    if (bWX < chunkMinX || bWX > chunkMaxX) continue;
                    if (bWZ < chunkMinZ || bWZ > chunkMaxZ) continue;
                    if (bWY < chunkMinY || bWY > chunkMaxY) continue;

                    int lx = bWX - chunkMinX;
                    int lz = bWZ - chunkMinZ;
                    int ly = settings.WorldYToLocal(bWY);

                    if (!chunkData.IsInBounds(lx, ly, lz)) continue;

                    byte existing = chunkData.GetBlock(lx, ly, lz);
                    if (existing == bedrockId) continue; // Bedrock is protected

                    if (structure.replaceMode == ReplaceMode.ReplaceAirOnly && existing != 0) continue;
                    if (structure.replaceMode == ReplaceMode.ReplaceSolidOnly && existing == 0) continue;

                    chunkData.SetBlock(lx, ly, lz, b.blockId);
                }
            }
        }
    }
}
