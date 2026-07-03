using System.Collections.Generic;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
//  BackroomsLayout  —  Pure-logic layout engine for Backrooms chunk generation.
//
//  RESPONSIBILITY
//  Given a list of BackroomsStructureConfig SOs and the chunk coordinates,
//  this class:
//    1. Picks a primary structure for the chunk (weighted random).
//    2. Attempts to place 0–N secondary structures in remaining space.
//    3. Writes block IDs into a VoxelChunkData, carving out interiors and
//       laying floors, walls, and ceilings.
//
//  COORDINATE CONVENTION
//  All positions are in LOCAL chunk space (lx, ly, lz).
//  The caller is responsible for world↔local translation.
//
//  INFINITE WORLD CONSISTENCY
//  Every placement decision is derived deterministically from (chunkX, chunkZ,
//  seed) using WorldGenNoiseUtils.HashInts — the same chunk always generates
//  identically regardless of load order.
//
//  THREAD SAFETY
//  Pure static / no Unity API.  Can be called from a background thread.
// ═══════════════════════════════════════════════════════════════════════════════

public static class BackroomsLayout
{
    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>
    /// Fills <paramref name="data"/> with a Backrooms layout chosen from
    /// <paramref name="configs"/>.
    /// </summary>
    /// <param name="data">Chunk data to write into (must already be sized).</param>
    /// <param name="settings">World settings (height range, etc.).</param>
    /// <param name="configs">Available structure configs with their weights.</param>
    /// <param name="seed">Resolved world seed.</param>
    /// <param name="solidFillId">Block used to fill space that is NOT part of any structure
    /// (solid backrooms "void" between rooms).</param>
    public static void Generate(
        VoxelChunkData                   data,
        VoxelWorldSettings               settings,
        List<BackroomsStructureConfig>   configs,
        int                              seed,
        byte                             solidFillId = 12)
    {
        int cx = data.ChunkCoord.x;
        int cz = data.ChunkCoord.y;
        int w  = data.Width;
        int h  = data.Height;

        // 1. Flood-fill with solid material (wall mass)
        data.Fill(solidFillId);

        if (configs == null || configs.Count == 0) return;

        // 2. Build weighted lookup
        float totalWeight = 0f;
        foreach (var cfg in configs)
            if (cfg != null) totalWeight += cfg.spawnWeight;

        if (totalWeight <= 0f) return;

        // 3. Pick & carve primary structure (always at chunk centre)
        BackroomsStructureConfig primary = PickWeightedRandom(configs, totalWeight, cx, cz, seed, channel: 0);
        if (primary != null)
            CarveStructure(data, settings, primary, cx, cz, seed, isSecondary: false);

        // 4. Attempt to carve a secondary structure at an offset position
        //    (simple approach: try once at a hash-determined corner quadrant)
        int hashSec = WorldGenNoiseUtils.HashInts(cx * 7, cz * 13, seed + 1);
        if ((hashSec & 0x3) != 0) // 75% chance of a second structure
        {
            BackroomsStructureConfig secondary = PickWeightedRandom(
                configs, totalWeight, cx + 31, cz + 17, seed, channel: 1);
            if (secondary != null)
                CarveStructure(data, settings, secondary, cx + 31, cz + 17, seed, isSecondary: true);
        }
    }

    // ── Structure carving ─────────────────────────────────────────────────────

    private static void CarveStructure(
        VoxelChunkData          data,
        VoxelWorldSettings      settings,
        BackroomsStructureConfig cfg,
        int hx, int hz,         // hash inputs for this placement
        int seed,
        bool isSecondary)
    {
        int w  = data.Width;
        int h  = data.Height;

        // Resolve concrete dimensions from ranges
        int length = WorldGenNoiseUtils.HashToInt(hx, hz, seed + 10, cfg.lengthRange.x, cfg.lengthRange.y);
        int width  = WorldGenNoiseUtils.HashToInt(hx, hz, seed + 11, cfg.widthRange.x,  cfg.widthRange.y);
        int height = WorldGenNoiseUtils.HashToInt(hx, hz, seed + 12, cfg.heightRange.x, cfg.heightRange.y);

        // Clamp to chunk bounds (minus 1 border for walls)
        length = Mathf.Min(length, w - 2);
        width  = Mathf.Min(width,  w - 2);
        height = Mathf.Min(height, h - 2);
        if (length < 2 || width < 2 || height < 1) return;

        // Determine orientation: rotate 90° with hash
        bool rotated = cfg.allowRotation &&
                       (WorldGenNoiseUtils.HashInts(hx + 5, hz + 5, seed + 13) & 1) == 1;

        int dimX = rotated ? width  : length;
        int dimZ = rotated ? length : width;

        // Place at chunk centre (secondary offset by quadrant)
        int baseX = (w - dimX) / 2;
        int baseZ = (w - dimZ) / 2;

        if (isSecondary)
        {
            int qx = (WorldGenNoiseUtils.HashInts(hx, hz, seed + 20) & 1) == 0 ? 1 : -1;
            int qz = (WorldGenNoiseUtils.HashInts(hx, hz, seed + 21) & 1) == 0 ? 1 : -1;
            baseX = Mathf.Clamp(baseX + qx * (w / 4), 1, w - dimX - 1);
            baseZ = Mathf.Clamp(baseZ + qz * (w / 4), 1, w - dimZ - 1);
        }

        // Floor Y: place structures at the bottom of the valid height range
        int floorLocalY = 0;   // local Y = 0 → world minHeight
        int ceilLocalY  = floorLocalY + height + 1; // +1 for ceiling slab
        if (ceilLocalY >= h) ceilLocalY = h - 1;

        switch (cfg.structureType)
        {
            case BackroomsStructureType.Corridor:
            case BackroomsStructureType.CrawlSpace:
                CarveCorridor(data, cfg, baseX, baseZ, dimX, dimZ, floorLocalY, ceilLocalY,
                              hx, hz, seed);
                break;

            case BackroomsStructureType.Room:
                CarveRoom(data, cfg, baseX, baseZ, dimX, dimZ, floorLocalY, ceilLocalY,
                          hx, hz, seed);
                break;

            case BackroomsStructureType.PillarRoom:
                CarveRoom(data, cfg, baseX, baseZ, dimX, dimZ, floorLocalY, ceilLocalY,
                          hx, hz, seed);
                CarvePillars(data, cfg, baseX, baseZ, dimX, dimZ, floorLocalY, ceilLocalY);
                break;
        }
    }

    // ── Corridor ──────────────────────────────────────────────────────────────

    private static void CarveCorridor(
        VoxelChunkData data, BackroomsStructureConfig cfg,
        int bx, int bz, int dx, int dz,
        int floorY, int ceilY,
        int hx, int hz, int seed)
    {
        for (int lx = bx; lx < bx + dx; lx++)
        for (int lz = bz; lz < bz + dz; lz++)
        {
            bool isEdgeX = (lx == bx || lx == bx + dx - 1);
            bool isEdgeZ = (lz == bz || lz == bz + dz - 1);
            bool isWall  = isEdgeX || isEdgeZ;

            // Ceiling
            data.SetBlock(lx, ceilY, lz, cfg.ceilingBlockId);

            // Floor
            data.SetBlock(lx, floorY, lz, cfg.floorBlockId);

            // Interior column
            for (int ly = floorY + 1; ly < ceilY; ly++)
            {
                if (isWall)
                {
                    byte wallId = cfg.wallBlockId;

                    // Alcove chance: recess wall 1 block on non-corner edges
                    if (cfg.wallAlcoveChance > 0f && !isEdgeX && !isEdgeZ == false)
                    {
                        float alcoveNoise = WorldGenNoiseUtils.HashToFloat(lx * 31 + lz, ly, seed + 99);
                        if (alcoveNoise < cfg.wallAlcoveChance)
                            wallId = cfg.airBlockId; // carve alcove
                    }

                    data.SetBlock(lx, ly, lz, wallId);
                }
                else
                {
                    data.SetBlock(lx, ly, lz, cfg.airBlockId);
                }
            }

            // Ceiling variation notch
            if (!isWall && cfg.ceilingVariationChance > 0f)
            {
                float ceilNoise = WorldGenNoiseUtils.HashToFloat(lx + lz * 17, hx + hz, seed + 77);
                if (ceilNoise < cfg.ceilingVariationChance)
                    data.SetBlock(lx, ceilY - 1, lz, cfg.ceilingBlockId); // extra ceiling layer
            }
        }
    }

    // ── Room ──────────────────────────────────────────────────────────────────

    private static void CarveRoom(
        VoxelChunkData data, BackroomsStructureConfig cfg,
        int bx, int bz, int dx, int dz,
        int floorY, int ceilY,
        int hx, int hz, int seed)
    {
        // Rooms use the same algorithm as corridors — the difference is in the
        // width/length ratios defined in the config.
        CarveCorridor(data, cfg, bx, bz, dx, dz, floorY, ceilY, hx, hz, seed);
    }

    // ── Pillars ───────────────────────────────────────────────────────────────

    private static void CarvePillars(
        VoxelChunkData data, BackroomsStructureConfig cfg,
        int bx, int bz, int dx, int dz,
        int floorY, int ceilY)
    {
        int spX = cfg.pillarSpacing.x;
        int spZ = cfg.pillarSpacing.y;
        int r   = cfg.pillarRadius;

        for (int px = bx + spX; px < bx + dx - spX; px += spX)
        for (int pz = bz + spZ; pz < bz + dz - spZ; pz += spZ)
        {
            // Write pillar blocks for each voxel within radius
            for (int ox = -r + 1; ox < r; ox++)
            for (int oz = -r + 1; oz < r; oz++)
            {
                int lx = px + ox;
                int lz = pz + oz;
                if (!data.IsInBounds(lx, floorY, lz)) continue;

                for (int ly = floorY; ly <= ceilY; ly++)
                    data.SetBlock(lx, ly, lz, cfg.pillarBlockId);
            }
        }
    }

    // ── Weighted random pick ──────────────────────────────────────────────────

    private static BackroomsStructureConfig PickWeightedRandom(
        List<BackroomsStructureConfig> configs,
        float totalWeight,
        int hx, int hz, int seed, int channel)
    {
        float pick = WorldGenNoiseUtils.HashToRange(hx, hz + channel * 1000, seed, 0f, totalWeight);
        float cumulative = 0f;

        foreach (var cfg in configs)
        {
            if (cfg == null) continue;
            cumulative += cfg.spawnWeight;
            if (pick <= cumulative) return cfg;
        }

        return configs[configs.Count - 1];
    }
}
