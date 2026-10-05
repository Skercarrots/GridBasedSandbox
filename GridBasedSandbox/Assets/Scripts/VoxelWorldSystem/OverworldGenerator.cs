using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  OverworldGenerator — Multi-biome terrain with climate-driven shaping,
//  chaos (3D density) terrain, biome blending, surface rules, multi-layer
//  cave carving, and deterministic ore vein placement.
//  Create via: Assets > Create > VoxelWorld > Generators > Overworld
//
//  PIPELINE (per chunk)
//  1. Per-column:
//       a. Sample all 5 climate axes (ClimateConfig) at (wx, wz).
//       b. Select biome via 5-parameter BiomeRegistry.GetBiome().
//       c. Blend height with 4 neighbouring climate samples.
//       d. Compute surface height (continentalness + erosion + ridged noise).
//       e. Evaluate chaos mask → 3D density OR heightmap fill.
//       f. Fill column blocks (surface rules, subsurface, stone, water, bedrock).
//  2. Cave carving pass (cheese + spaghetti + noodle).
//  3. Ore placement pass (deterministic per-chunk veins).
//
//  THREADING
//  Generate() runs on a background thread via ChunkGenerationScheduler.
//  All Unity API calls (AnimationCurve, ScriptableObject fields) are
//  read-only after Prepare() — safe for concurrent reads.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "OverworldGenerator", menuName = "VoxelWorld/Generators/Overworld")]
public class OverworldGenerator : WorldGenerator
{
    [Header("Biomes")]
    [SerializeField] private BiomeRegistry biomeRegistry;

    // ── Climate System (Phase 2) ──────────────────────────────────────────────
    [Header("Climate (Phase 2)")]
    [Tooltip("All 5 climate noise axes plus global world scale.")]
    [SerializeField] private ClimateConfig climate = new ClimateConfig();

    [Header("Biome Blending")]
    [Tooltip("Radius (in blocks) at which additional climate samples are taken around " +
             "each column to smooth biome boundary height transitions. " +
             "0 = disabled (sharp cliff-walls at biome borders).")]
    [Range(0, 64)] [SerializeField] private int blendRadius = 16;

    // ── Chaos Terrain (Phase 2) ───────────────────────────────────────────────
    [Header("Chaos Terrain (Phase 2)")]
    [SerializeField] private ChaosConfig chaos = new ChaosConfig();

    [Tooltip("When true, chaos terrain is active. When false, all columns use " +
             "the heightmap path (faster, smoother, no overhangs).")]
    [SerializeField] private bool enableChaos = true;

    // ── Ridged Mountain Noise ─────────────────────────────────────────────────
    [Header("Ridged Mountains")]
    [Tooltip("Scale for the ridged noise that creates sharp mountain ridge lines.")]
    [SerializeField] private float ridgeScale = 120f;

    [Tooltip("Octaves for the ridged noise.")]
    [Range(1, 5)] [SerializeField] private int ridgeOctaves = 3;

    [Tooltip("How much weirdness must exceed this threshold before ridges appear.")]
    [Range(0f, 1f)] [SerializeField] private float ridgeWeirdnessThreshold = 0.4f;

    // ── Caves (Phase 3) ───────────────────────────────────────────────────────
    [Header("Caves (Phase 3)")]
    [Tooltip("Cave configuration asset. If null, caves are disabled.")]
    [SerializeField] private CaveConfig caveConfig;

    // ── Ores (Phase 3) ────────────────────────────────────────────────────────
    [Header("Ores (Phase 3)")]
    [Tooltip("Ore definitions to generate in the world. Each ore gets its own " +
             "deterministic placement pass after cave carving.")]
    [SerializeField] private OreDefinition[] ores;

    // ── Structures & Features (Phase 4) ───────────────────────────────────────
    [Header("Global Features (Phase 4)")]
    [Tooltip("Features placed across the world regardless of biome (e.g. boulders, structures).")]
    [SerializeField] private List<WorldFeature> globalFeatures = new();

    private List<WorldFeature> _preparedFeatures;

    // ── Bedrock (Phase 1) ─────────────────────────────────────────────────────
    [Header("Bedrock")]
    [Tooltip("The block type used for the unbreakable bedrock layer at the bottom of the world.")]
    [SerializeField] private VoxelBlockType bedrockBlock;

    [Tooltip("Minimum number of bedrock layers at the bottom of the world.")]
    [Range(1, 5)] [SerializeField] private int bedrockLayerMin = 1;

    [Tooltip("Maximum number of bedrock layers (actual thickness varies per-column via noise).")]
    [Range(1, 8)] [SerializeField] private int bedrockLayerMax = 5;

    // ── Legacy fields (preserved for OverworldGenerator.asset deserialization) ─
#pragma warning disable 0414
    [HideInInspector] [SerializeField] private float caveScale = 24f;
    [HideInInspector] [SerializeField] private float caveThreshold = 0.62f;
    [HideInInspector] [SerializeField] private int caveMinDepthBelowSurface = 4;
    [HideInInspector] [SerializeField] private NoiseLayerConfig continentalness = new NoiseLayerConfig
        { scale = 300f, octaves = 4, persistence = 0.5f, lacunarity = 2f, seedOffsetMultiplier = 1f };
    [HideInInspector] [SerializeField] private NoiseLayerConfig erosion = new NoiseLayerConfig
        { scale = 180f, octaves = 3, persistence = 0.5f, lacunarity = 2f, seedOffsetMultiplier = 3.3f };
    [HideInInspector] [SerializeField] private NoiseLayerConfig temperature = new NoiseLayerConfig
        { scale = 500f, octaves = 2, persistence = 0.5f, lacunarity = 2f, seedOffsetMultiplier = 5.1f };
    [HideInInspector] [SerializeField] private NoiseLayerConfig humidity = new NoiseLayerConfig
        { scale = 500f, octaves = 2, persistence = 0.5f, lacunarity = 2f, seedOffsetMultiplier = 7.9f };
#pragma warning restore 0414

    // ── Prepare (main thread, bakes curves) ───────────────────────────────────

    public override void Prepare()
    {
        if (biomeRegistry != null)
            biomeRegistry.Initialize();

        // Bake ore height distribution curves into thread-safe LUTs
        if (ores != null)
        {
            foreach (var ore in ores)
                ore?.BakeHeightDistribution();
        }

        // Collect and cache all features from registered biomes + globalFeatures
        var featureSet = new HashSet<WorldFeature>();
        if (biomeRegistry != null && biomeRegistry.Biomes != null)
        {
            foreach (var b in biomeRegistry.Biomes)
            {
                if (b == null || b.features == null) continue;
                foreach (var fe in b.features)
                {
                    if (fe != null && fe.feature != null)
                        featureSet.Add(fe.feature);
                }
            }
        }
        if (globalFeatures != null)
        {
            foreach (var gf in globalFeatures)
            {
                if (gf != null) featureSet.Add(gf);
            }
        }
        var list = new List<WorldFeature>(featureSet);
        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        _preparedFeatures = list;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Generate (background thread)
    // ═══════════════════════════════════════════════════════════════════════════

    public override void Generate(VoxelChunkData data, VoxelWorldSettings settings, int seed)
    {
        int worldOriginX = data.WorldOriginX;
        int worldOriginZ = data.WorldOriginZ;
        int minY         = settings.minHeight;
        int maxY         = settings.maxHeight;

        byte bedrockId = bedrockBlock != null ? bedrockBlock.blockId : (byte)0;

        // Per-column buffers
        int cw = data.Width;
        int totalColumns = cw * cw;
        int[] surfaceHeights = new int[totalColumns];
        int[] bedrockTops = new int[totalColumns];
        BlendResult[] blends = new BlendResult[totalColumns];

        bool hasAnyChaos = false;
        bool[] isChaosColumn = null;
        int minChaosWy = int.MaxValue;
        int maxChaosWy = int.MinValue;

        // ── Pass 1a: Column analysis ──────────────────────────────────────────
        for (int lx = 0; lx < cw; lx++)
        for (int lz = 0; lz < cw; lz++)
        {
            int colIdx = lx * cw + lz;
            int wx = worldOriginX + lx;
            int wz = worldOriginZ + lz;

            ClimatePoint cp = climate.Sample(wx, wz, seed);
            BlendResult blend = ComputeBlendedHeight(wx, wz, cp, seed, settings);
            blends[colIdx] = blend;

            int surfaceY = blend.SurfaceY;
            surfaceHeights[colIdx] = surfaceY;

            // Bedrock thickness
            int bedrockTop = minY;
            if (bedrockId != 0)
            {
                float bedrockNoise = VoxelNoise.Sample3D(wx * 4.17f, 0f, wz * 4.17f, 8f);
                int thickness = bedrockLayerMin
                    + Mathf.FloorToInt(bedrockNoise * (bedrockLayerMax - bedrockLayerMin + 1));
                thickness  = Mathf.Clamp(thickness, bedrockLayerMin, bedrockLayerMax);
                bedrockTop = minY + thickness - 1;
            }
            bedrockTops[colIdx] = bedrockTop;

            // Chaos mask (only in biomes designed for 3D overhangs/chaos)
            bool biomeAllowsChaos = IsChaosEligibleBiome(blend.Biome, cp.Weirdness);
            if (enableChaos && chaos != null && chaos.maskThreshold < 1f && biomeAllowsChaos)
            {
                float maskOx = 10000f + (seed * 127.1f + chaos.maskSeedOffset) % 9999f;
                float maskOz = 10000f + (seed * 311.7f + chaos.maskSeedOffset) % 9999f;
                float mask   = Mathf.PerlinNoise((wx + maskOx) / chaos.maskScale,
                                                  (wz + maskOz) / chaos.maskScale);
                if (mask > chaos.maskThreshold)
                {
                    if (isChaosColumn == null)
                        isChaosColumn = new bool[totalColumns];
                    isChaosColumn[colIdx] = true;
                    hasAnyChaos = true;

                    int deepCutoff = surfaceY - 40;
                    int ceilingY = surfaceY + 30;
                    if (deepCutoff < minChaosWy) minChaosWy = deepCutoff;
                    if (ceilingY > maxChaosWy) maxChaosWy = ceilingY;
                }
            }
        }

        // ── Pass 1b: Chaos 3D coarse grid (if active) ─────────────────────────
        float[] chaosNoiseGrid = null;
        int chaosStep = chaos != null ? chaos.coarseGridStep : 4;
        int minChaosLy = 0;
        int maxChaosLy = 0;
        int cnx = 0, cny = 0, cnz = 0;

        if (hasAnyChaos && chaosStep > 1)
        {
            minChaosLy = Mathf.Clamp(settings.WorldYToLocal(minChaosWy), 0, data.Height - 1);
            maxChaosLy = Mathf.Clamp(settings.WorldYToLocal(maxChaosWy), 0, data.Height - 1);

            int numCellsX = (cw - 1) / chaosStep + 1;
            cnx = numCellsX + 1;
            int numCellsZ = (cw - 1) / chaosStep + 1;
            cnz = numCellsZ + 1;
            int numCellsY = (maxChaosLy - minChaosLy) / chaosStep + 1;
            cny = numCellsY + 1;

            chaosNoiseGrid = new float[cnx * cny * cnz];

            for (int gx = 0; gx < cnx; gx++)
            {
                int wx = worldOriginX + gx * chaosStep;
                for (int gy = 0; gy < cny; gy++)
                {
                    int wy = settings.LocalYToWorld(minChaosLy + gy * chaosStep);
                    for (int gz = 0; gz < cnz; gz++)
                    {
                        int wz = worldOriginZ + gz * chaosStep;
                        int idx = (gx * cny + gy) * cnz + gz;
                        chaosNoiseGrid[idx] = VoxelNoise.Sample3DFbm(
                            wx, wy, wz, chaos.densityScale,
                            chaos.densityOctaves, chaos.densityPersistence,
                            2f, seed + 777777);
                    }
                }
            }
        }

        float invChaosStep = chaosStep > 0 ? 1f / chaosStep : 1f;

        // ── Pass 1c: Column voxel fill ────────────────────────────────────────
        for (int lx = 0; lx < cw; lx++)
        {
            int cgx = chaosStep > 0 ? lx / chaosStep : 0;
            float cfx = chaosStep > 0 ? (lx % chaosStep) * invChaosStep : 0f;

            for (int lz = 0; lz < cw; lz++)
            {
                int colIdx = lx * cw + lz;
                int cgz = chaosStep > 0 ? lz / chaosStep : 0;
                float cfz = chaosStep > 0 ? (lz % chaosStep) * invChaosStep : 0f;

                int wx = worldOriginX + lx;
                int wz = worldOriginZ + lz;
                BlendResult blend = blends[colIdx];
                int surfaceY = blend.SurfaceY;
                BiomeDefinition biome = blend.Biome;
                int bedrockTop = bedrockTops[colIdx];
                bool columnIsChaos = isChaosColumn != null && isChaosColumn[colIdx];

                if (!columnIsChaos)
                {
                    for (int ly = 0; ly < data.Height; ly++)
                    {
                        int wy = settings.LocalYToWorld(ly);
                        byte block = AssignBlock(wx, wy, wz, surfaceY, bedrockTop, bedrockId, biome, settings, blend);
                        data.SetBlock(lx, ly, lz, block);
                    }
                }
                else
                {
                    surfaceHeights[colIdx] = FillChaosColumn(
                        data, lx, lz, wx, wz, surfaceY, bedrockTop, bedrockId, biome, settings,
                        chaosNoiseGrid, cnx, cny, cnz, cgx, cgz, cfx, cfz,
                        minChaosLy, maxChaosLy, chaosStep, invChaosStep, seed);
                }
            }
        }

        // ── Pass 2: Cave carving ──────────────────────────────────────────────
        if (caveConfig != null)
        {
            CarveCaves(data, settings, seed, bedrockId, surfaceHeights);
        }

        // ── Pass 3: Ore placement ─────────────────────────────────────────────
        if (ores != null && ores.Length > 0)
        {
            PlaceOres(data, settings, seed, bedrockId, surfaceHeights);
        }

        // ── Pass 4: Structure placement (Phase 4) ─────────────────────────────
        if (_preparedFeatures != null && _preparedFeatures.Count > 0)
        {
            var sampler = new SurfaceSampler(this, settings, seed);
            StructurePlacer.PlaceFeatures(data, settings, seed, _preparedFeatures, sampler, bedrockId);
        }

        data.RecomputeSectionOccupancy();
        data.IsDirty = true;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Pass 2: Cave carving — cheese + spaghetti + noodle (Coarse-grid sampled)
    // ═══════════════════════════════════════════════════════════════════════════

    private void CarveCaves(VoxelChunkData data, VoxelWorldSettings settings,
                             int seed, byte bedrockId, int[] surfaceHeights)
    {
        int step = caveConfig.coarseGridStep;
        if (step <= 1)
        {
            CarveCavesDirect(data, settings, seed, bedrockId, surfaceHeights);
            return;
        }

        int originX  = data.WorldOriginX;
        int originZ  = data.WorldOriginZ;
        int minY     = settings.minHeight;
        int seaLevel = settings.seaLevel;
        int cw       = data.Width;

        int bedrockFadeAbove = minY + bedrockLayerMax + caveConfig.bedrockFadeHeight;

        // Find the maximum cave ceiling across the entire chunk
        int maxCaveCeiling = int.MinValue;
        for (int lx = 0; lx < cw; lx++)
        for (int lz = 0; lz < cw; lz++)
        {
            int sY = surfaceHeights[lx * cw + lz];
            int ceiling = sY - caveConfig.minDepthBelowSurface;
            if (sY <= seaLevel)
                ceiling = Mathf.Min(ceiling, sY - caveConfig.waterProtectionDepth);

            if (ceiling > maxCaveCeiling)
                maxCaveCeiling = ceiling;
        }

        // If the highest ceiling is at or below the bedrock fade line, no caves can exist in this chunk
        if (maxCaveCeiling <= bedrockFadeAbove)
            return;

        int minWy = bedrockFadeAbove + 1;
        int maxWy = maxCaveCeiling;

        int minLy = Mathf.Clamp(settings.WorldYToLocal(minWy), 0, data.Height - 1);
        int maxLy = Mathf.Clamp(settings.WorldYToLocal(maxWy), 0, data.Height - 1);

        if (minLy > maxLy)
            return;

        // Coarse grid dimensions
        int numCellsX = (cw - 1) / step + 1;
        int nx = numCellsX + 1;
        int numCellsZ = (cw - 1) / step + 1;
        int nz = numCellsZ + 1;
        int numCellsY = (maxLy - minLy) / step + 1;
        int ny = numCellsY + 1;

        int totalGridPoints = nx * ny * nz;

        float[] cheeseGrid     = new float[totalGridPoints];
        float[] spaghetti1Grid = new float[totalGridPoints];
        float[] spaghetti2Grid = new float[totalGridPoints];
        float[] noodle1Grid    = caveConfig.enableNoodles ? new float[totalGridPoints] : null;
        float[] noodle2Grid    = caveConfig.enableNoodles ? new float[totalGridPoints] : null;

        // Sample coarse 3D noise grid
        for (int gx = 0; gx < nx; gx++)
        {
            int wx = originX + gx * step;
            for (int gy = 0; gy < ny; gy++)
            {
                int wy = settings.LocalYToWorld(minLy + gy * step);
                for (int gz = 0; gz < nz; gz++)
                {
                    int wz = originZ + gz * step;
                    int idx = (gx * ny + gy) * nz + gz;

                    cheeseGrid[idx] = VoxelNoise.Sample3DFbm(
                        wx, wy, wz, caveConfig.cheeseScale,
                        caveConfig.cheeseOctaves, caveConfig.cheesePersistence, 2f,
                        seed + caveConfig.cheeseSeedOffset);

                    spaghetti1Grid[idx] = VoxelNoise.Sample3DFbm(
                        wx, wy, wz, caveConfig.spaghettiScale,
                        caveConfig.spaghettiOctaves, caveConfig.spaghettiPersistence, 2f,
                        seed + caveConfig.spaghettiSeedOffset1);

                    spaghetti2Grid[idx] = VoxelNoise.Sample3DFbm(
                        wx, wy, wz, caveConfig.spaghettiScale,
                        caveConfig.spaghettiOctaves, caveConfig.spaghettiPersistence, 2f,
                        seed + caveConfig.spaghettiSeedOffset2);

                    if (caveConfig.enableNoodles)
                    {
                        noodle1Grid[idx] = VoxelNoise.Sample3DFbm(
                            wx, wy, wz, caveConfig.noodleScale,
                            2, 0.5f, 2f,
                            seed + caveConfig.noodleSeedOffset1);

                        noodle2Grid[idx] = VoxelNoise.Sample3DFbm(
                            wx, wy, wz, caveConfig.noodleScale,
                            2, 0.5f, 2f,
                            seed + caveConfig.noodleSeedOffset2);
                    }
                }
            }
        }

        float invStep = 1f / step;

        // Trilinear interpolation & carving
        for (int lx = 0; lx < cw; lx++)
        {
            int gx = lx / step;
            float fx = (lx % step) * invStep;

            for (int lz = 0; lz < cw; lz++)
            {
                int gz = lz / step;
                float fz = (lz % step) * invStep;

                int surfaceY = surfaceHeights[lx * cw + lz];
                bool isUnderWater = surfaceY <= seaLevel;

                int caveCeiling = surfaceY - caveConfig.minDepthBelowSurface;
                if (isUnderWater)
                    caveCeiling = Mathf.Min(caveCeiling, surfaceY - caveConfig.waterProtectionDepth);

                int colMaxLy = Mathf.Min(maxLy, settings.WorldYToLocal(caveCeiling));
                if (colMaxLy < minLy) continue;

                for (int ly = minLy; ly <= colMaxLy; ly++)
                {
                    int wy = settings.LocalYToWorld(ly);
                    if (wy <= bedrockFadeAbove) continue;

                    byte currentBlock = data.GetBlock(lx, ly, lz);
                    if (currentBlock == 0 || currentBlock == bedrockId) continue;

                    int deltaY = ly - minLy;
                    int gy = deltaY / step;
                    float fy = (deltaY % step) * invStep;

                    float depthBelow = surfaceY - wy;
                    float depthFactor = DepthCaveProbability(depthBelow, caveConfig.peakCaveDepth);

                    bool carve = false;

                    // 1. Cheese caverns
                    float cheese = TrilinearInterpolate(cheeseGrid, nx, ny, nz, gx, gy, gz, fx, fy, fz);
                    float adjustedThreshold = caveConfig.cheeseThreshold + (1f - depthFactor) * 0.15f;
                    if (cheese > adjustedThreshold)
                    {
                        carve = true;
                    }

                    // 2. Spaghetti tunnels
                    if (!carve)
                    {
                        float sp1 = TrilinearInterpolate(spaghetti1Grid, nx, ny, nz, gx, gy, gz, fx, fy, fz);
                        float sp2 = TrilinearInterpolate(spaghetti2Grid, nx, ny, nz, gx, gy, gz, fx, fy, fz);

                        float d1 = Mathf.Abs(sp1 - 0.5f);
                        float d2 = Mathf.Abs(sp2 - 0.5f);
                        float spThreshold = caveConfig.spaghettiThreshold * depthFactor;
                        if (d1 < spThreshold && d2 < spThreshold)
                        {
                            carve = true;
                        }
                    }

                    // 3. Noodle tunnels
                    if (!carve && caveConfig.enableNoodles)
                    {
                        float no1 = TrilinearInterpolate(noodle1Grid, nx, ny, nz, gx, gy, gz, fx, fy, fz);
                        float no2 = TrilinearInterpolate(noodle2Grid, nx, ny, nz, gx, gy, gz, fx, fy, fz);

                        float d1 = Mathf.Abs(no1 - 0.5f);
                        float d2 = Mathf.Abs(no2 - 0.5f);
                        float noThreshold = caveConfig.noodleThreshold * depthFactor;
                        if (d1 < noThreshold && d2 < noThreshold)
                        {
                            carve = true;
                        }
                    }

                    if (carve)
                    {
                        data.SetBlock(lx, ly, lz, 0); // carve to air
                    }
                }
            }
        }
    }

    /// <summary>Fallback direct per-voxel cave carving when coarseGridStep &lt;= 1.</summary>
    private void CarveCavesDirect(VoxelChunkData data, VoxelWorldSettings settings,
                                  int seed, byte bedrockId, int[] surfaceHeights)
    {
        int originX = data.WorldOriginX;
        int originZ = data.WorldOriginZ;
        int minY    = settings.minHeight;
        int seaLevel = settings.seaLevel;

        int bedrockFadeAbove = minY + bedrockLayerMax + caveConfig.bedrockFadeHeight;

        for (int lx = 0; lx < data.Width; lx++)
        for (int lz = 0; lz < data.Width; lz++)
        {
            int wx = originX + lx;
            int wz = originZ + lz;
            int surfaceY = surfaceHeights[lx * data.Width + lz];
            bool isUnderWater = surfaceY <= seaLevel;

            // Cave ceiling: never carve within minDepthBelowSurface of surface
            int caveCeiling = surfaceY - caveConfig.minDepthBelowSurface;

            // Additional protection under water bodies
            if (isUnderWater)
                caveCeiling = Mathf.Min(caveCeiling, surfaceY - caveConfig.waterProtectionDepth);

            for (int ly = 0; ly < data.Height; ly++)
            {
                int wy = settings.LocalYToWorld(ly);

                // Skip: above cave ceiling, below bedrock fade, or already air
                if (wy > caveCeiling) continue;
                if (wy <= bedrockFadeAbove) continue;

                byte currentBlock = data.GetBlock(lx, ly, lz);
                if (currentBlock == 0 || currentBlock == bedrockId) continue;

                // Depth factor: caves are most common at peakCaveDepth below surface,
                // fade near surface and near bedrock
                float depthBelow = surfaceY - wy;
                float depthFactor = DepthCaveProbability(depthBelow, caveConfig.peakCaveDepth);

                // Check all three cave types
                bool carve = false;

                // 1. Cheese caverns
                if (!carve)
                {
                    float cheese = VoxelNoise.Sample3DFbm(wx, wy, wz, caveConfig.cheeseScale,
                        caveConfig.cheeseOctaves, caveConfig.cheesePersistence, 2f,
                        seed + caveConfig.cheeseSeedOffset);

                    // Apply depth modulation — caves more likely at peak depth
                    float adjustedThreshold = caveConfig.cheeseThreshold + (1f - depthFactor) * 0.15f;
                    if (cheese > adjustedThreshold) carve = true;
                }

                // 2. Spaghetti tunnels
                if (!carve)
                {
                    float spaghetti1 = VoxelNoise.Sample3DFbm(wx, wy, wz, caveConfig.spaghettiScale,
                        caveConfig.spaghettiOctaves, caveConfig.spaghettiPersistence, 2f,
                        seed + caveConfig.spaghettiSeedOffset1);
                    float spaghetti2 = VoxelNoise.Sample3DFbm(wx, wy, wz, caveConfig.spaghettiScale,
                        caveConfig.spaghettiOctaves, caveConfig.spaghettiPersistence, 2f,
                        seed + caveConfig.spaghettiSeedOffset2);

                    // Tunnel exists where both noises are near 0.5
                    float d1 = Mathf.Abs(spaghetti1 - 0.5f);
                    float d2 = Mathf.Abs(spaghetti2 - 0.5f);
                    float adjustedThreshold = caveConfig.spaghettiThreshold * depthFactor;
                    if (d1 < adjustedThreshold && d2 < adjustedThreshold) carve = true;
                }

                // 3. Noodle tunnels
                if (!carve && caveConfig.enableNoodles)
                {
                    float noodle1 = VoxelNoise.Sample3DFbm(wx, wy, wz, caveConfig.noodleScale,
                        2, 0.5f, 2f, seed + caveConfig.noodleSeedOffset1);
                    float noodle2 = VoxelNoise.Sample3DFbm(wx, wy, wz, caveConfig.noodleScale,
                        2, 0.5f, 2f, seed + caveConfig.noodleSeedOffset2);

                    float d1 = Mathf.Abs(noodle1 - 0.5f);
                    float d2 = Mathf.Abs(noodle2 - 0.5f);
                    float adjustedThreshold = caveConfig.noodleThreshold * depthFactor;
                    if (d1 < adjustedThreshold && d2 < adjustedThreshold) carve = true;
                }

                if (carve)
                {
                    data.SetBlock(lx, ly, lz, 0); // carve to air
                }
            }
        }
    }

    /// <summary>Trilinear interpolation helper for 3D coarse grid sampling. Thread-safe.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static float TrilinearInterpolate(float[] grid, int nx, int ny, int nz,
                                              int gx, int gy, int gz,
                                              float fx, float fy, float fz)
    {
        int base00 = (gx * ny + gy) * nz + gz;
        int base01 = (gx * ny + (gy + 1)) * nz + gz;
        int base10 = ((gx + 1) * ny + gy) * nz + gz;
        int base11 = ((gx + 1) * ny + (gy + 1)) * nz + gz;

        float c000 = grid[base00];
        float c001 = grid[base00 + 1];
        float c010 = grid[base01];
        float c011 = grid[base01 + 1];
        float c100 = grid[base10];
        float c101 = grid[base10 + 1];
        float c110 = grid[base11];
        float c111 = grid[base11 + 1];

        float c00 = c000 + fz * (c001 - c000);
        float c01 = c010 + fz * (c011 - c010);
        float c10 = c100 + fz * (c101 - c100);
        float c11 = c110 + fz * (c111 - c110);

        float c0 = c00 + fy * (c01 - c00);
        float c1 = c10 + fy * (c11 - c10);

        return c0 + fx * (c1 - c0);
    }

    /// <summary>Returns a [0..1] probability multiplier based on how many blocks below
    /// the surface a position is. Peaks at peakDepth, fades toward 0 near surface
    /// and near bedrock. Thread-safe.</summary>
    private static float DepthCaveProbability(float depthBelowSurface, int peakDepth)
    {
        if (depthBelowSurface <= 0f) return 0f;

        // Triangle-ish distribution peaking at peakDepth
        if (depthBelowSurface < peakDepth)
            return Mathf.Clamp01(depthBelowSurface / peakDepth);

        // Slow falloff below the peak (deep caves are still common)
        float beyond = depthBelowSurface - peakDepth;
        return Mathf.Clamp01(1f - beyond / (peakDepth * 3f));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Pass 3: Ore placement — deterministic veins
    // ═══════════════════════════════════════════════════════════════════════════

    private void PlaceOres(VoxelChunkData data, VoxelWorldSettings settings,
                            int seed, byte bedrockId, int[] surfaceHeights)
    {
        int chunkX = data.ChunkCoord.x;
        int chunkZ = data.ChunkCoord.y;
        int cw     = data.Width;    // 16
        int minY   = settings.minHeight;
        int height = data.Height;

        for (int oreIdx = 0; oreIdx < ores.Length; oreIdx++)
        {
            OreDefinition ore = ores[oreIdx];
            if (ore == null || ore.oreBlock == null) continue;

            byte oreId = ore.oreBlock.blockId;

            // Evaluate veins originating from this chunk AND 26 neighbours
            for (int ncx = chunkX - 1; ncx <= chunkX + 1; ncx++)
            for (int ncz = chunkZ - 1; ncz <= chunkZ + 1; ncz++)
            {
                // Deterministic RNG per (chunk, ore) — identical regardless of generation order
                var rng = new DeterministicRng(
                    DeterministicRng.Hash(seed, ncx, ncz, oreIdx + 77777));

                int neighborOriginX = ncx * cw;
                int neighborOriginZ = ncz * cw;

                for (int attempt = 0; attempt < ore.attemptsPerChunk; attempt++)
                {
                    // Random vein origin in the neighboring chunk's world space
                    int veinWX = neighborOriginX + rng.NextInt(0, cw);
                    int veinWZ = neighborOriginZ + rng.NextInt(0, cw);
                    int veinWY = rng.NextInt(ore.minY, ore.maxY + 1);

                    // Biome filter check at vein origin
                    if (ore.biomeFilter != null && ore.biomeFilter.Length > 0)
                    {
                        ClimatePoint veinCp = climate.Sample(veinWX, veinWZ, seed);
                        BiomeDefinition veinBiome = biomeRegistry.GetBiome(in veinCp);
                        if (!ore.IsBiomeAllowed(veinBiome)) continue;
                    }

                    // Height distribution probability check
                    float normalizedY = (ore.maxY > ore.minY)
                        ? (float)(veinWY - ore.minY) / (ore.maxY - ore.minY)
                        : 0.5f;
                    float heightProb = ore.EvaluateHeightProbability(normalizedY);
                    if (!rng.NextBool(heightProb)) continue;

                    // Generate vein
                    int veinSize = rng.NextInt(ore.minVeinSize, ore.maxVeinSize + 1);
                    float elongAngle = rng.NextFloat(0f, Mathf.PI * 2f);
                    float elongTilt  = rng.NextFloat(-0.5f, 0.5f);

                    PlaceVein(data, settings, ore, oreId,
                              veinWX, veinWY, veinWZ, veinSize,
                              elongAngle, elongTilt,
                              chunkX, chunkZ, bedrockId, ref rng);
                }
            }
        }
    }

    /// <summary>Places an ellipsoidal vein of ore blocks centered at (cx,cy,cz) in world space.
    /// Only writes blocks that fall within the given chunk's local bounds.</summary>
    private static void PlaceVein(VoxelChunkData data, VoxelWorldSettings settings,
                                   OreDefinition ore, byte oreId,
                                   int cx, int cy, int cz, int veinSize,
                                   float elongAngle, float elongTilt,
                                   int chunkX, int chunkZ, byte bedrockId,
                                   ref DeterministicRng rng)
    {
        int cw     = data.Width;
        int minY   = settings.minHeight;
        int originX = chunkX * cw;
        int originZ = chunkZ * cw;

        // Compute vein radius from desired block count (sphere volume ≈ 4/3 π r³)
        float baseRadius = Mathf.Pow(veinSize * 0.75f / Mathf.PI, 1f / 3f);
        float elongFactor = 1f + ore.elongation * 2f;

        // Elongation direction (unit vector in XZ plane, tilted slightly in Y)
        float dirX = Mathf.Cos(elongAngle);
        float dirZ = Mathf.Sin(elongAngle);
        float dirY = elongTilt;

        // Bounding box for the vein in world space
        int radiusCeil = Mathf.CeilToInt(baseRadius * elongFactor) + 1;
        int wxMin = cx - radiusCeil;
        int wxMax = cx + radiusCeil;
        int wyMin = cy - radiusCeil;
        int wyMax = cy + radiusCeil;
        int wzMin = cz - radiusCeil;
        int wzMax = cz + radiusCeil;

        // Clamp to this chunk's world-space XZ bounds
        int chunkWxMin = originX;
        int chunkWxMax = originX + cw - 1;
        int chunkWzMin = originZ;
        int chunkWzMax = originZ + cw - 1;

        wxMin = Mathf.Max(wxMin, chunkWxMin);
        wxMax = Mathf.Min(wxMax, chunkWxMax);
        wzMin = Mathf.Max(wzMin, chunkWzMin);
        wzMax = Mathf.Min(wzMax, chunkWzMax);

        // Clamp Y to chunk height
        int lyMin = Mathf.Max(wyMin - minY, 0);
        int lyMax = Mathf.Min(wyMax - minY, data.Height - 1);

        float r2 = baseRadius * baseRadius;

        for (int wx = wxMin; wx <= wxMax; wx++)
        for (int ly = lyMin; ly <= lyMax; ly++)
        for (int wz = wzMin; wz <= wzMax; wz++)
        {
            int wy = ly + minY;
            float dx = wx - cx;
            float dy = wy - cy;
            float dz = wz - cz;

            // Project onto elongation direction and compress
            float dot = dx * dirX + dy * dirY + dz * dirZ;
            float px = dx - dot * dirX * (1f - 1f / elongFactor);
            float py = dy - dot * dirY * (1f - 1f / elongFactor);
            float pz = dz - dot * dirZ * (1f - 1f / elongFactor);

            float dist2 = px * px + py * py + pz * pz;
            if (dist2 > r2) continue;

            int lx = wx - originX;
            int lz = wz - originZ;

            byte existing = data.GetBlock(lx, ly, lz);
            if (existing == 0 || existing == bedrockId) continue; // don't place in air or bedrock

            if (ore.CanReplace(existing))
            {
                data.SetBlock(lx, ly, lz, oreId);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Biome blending (unchanged from Phase 2)
    // ═══════════════════════════════════════════════════════════════════════════

    private struct BlendResult
    {
        public int             SurfaceY;
        public BiomeDefinition Biome;
        public float           SlopeGradient;
    }

    private BlendResult ComputeBlendedHeight(int wx, int wz, in ClimatePoint cp,
                                              int seed, VoxelWorldSettings settings)
    {
        BiomeDefinition centerBiome  = biomeRegistry.GetBiome(in cp);
        float           centerHeight = ComputeShapedHeight(wx, wz, in cp, centerBiome, seed, settings);
        float           localSlope   = ComputeLocalSlope(wx, wz, in cp, centerBiome, seed, settings);

        if (blendRadius <= 0)
        {
            return new BlendResult
            {
                SurfaceY      = Mathf.Clamp(Mathf.RoundToInt(centerHeight), settings.minHeight, settings.maxHeight),
                Biome         = centerBiome,
                SlopeGradient = localSlope
            };
        }

        int br = blendRadius;
        ClimatePoint cpPX = climate.Sample(wx + br, wz,      seed);
        ClimatePoint cpMX = climate.Sample(wx - br, wz,      seed);
        ClimatePoint cpPZ = climate.Sample(wx,      wz + br, seed);
        ClimatePoint cpMZ = climate.Sample(wx,      wz - br, seed);

        BiomeDefinition bPX = biomeRegistry.GetBiome(in cpPX);
        BiomeDefinition bMX = biomeRegistry.GetBiome(in cpMX);
        BiomeDefinition bPZ = biomeRegistry.GetBiome(in cpPZ);
        BiomeDefinition bMZ = biomeRegistry.GetBiome(in cpMZ);

        if (bPX == centerBiome && bMX == centerBiome && bPZ == centerBiome && bMZ == centerBiome)
        {
            return new BlendResult
            {
                SurfaceY      = Mathf.Clamp(Mathf.RoundToInt(centerHeight), settings.minHeight, settings.maxHeight),
                Biome         = centerBiome,
                SlopeGradient = localSlope
            };
        }

        float hPX = ComputeShapedHeight(wx, wz, in cp, bPX, seed, settings);
        float hMX = ComputeShapedHeight(wx, wz, in cp, bMX, seed, settings);
        float hPZ = ComputeShapedHeight(wx, wz, in cp, bPZ, seed, settings);
        float hMZ = ComputeShapedHeight(wx, wz, in cp, bMZ, seed, settings);

        float blended = (centerHeight * 2f + hPX + hMX + hPZ + hMZ) / 6f;

        return new BlendResult
        {
            SurfaceY      = Mathf.Clamp(Mathf.RoundToInt(blended), settings.minHeight, settings.maxHeight),
            Biome         = centerBiome,
            SlopeGradient = localSlope
        };
    }

    /// <summary>Calculates the local terrain slope gradient (|∇h|) around (wx, wz) over a 2-block baseline. Thread-safe.</summary>
    private float ComputeLocalSlope(int wx, int wz, in ClimatePoint cp, BiomeDefinition biome, int seed, VoxelWorldSettings settings)
    {
        const int d = 2;
        float hPX = ComputeShapedHeight(wx + d, wz, in cp, biome, seed, settings);
        float hMX = ComputeShapedHeight(wx - d, wz, in cp, biome, seed, settings);
        float hPZ = ComputeShapedHeight(wx, wz + d, in cp, biome, seed, settings);
        float hMZ = ComputeShapedHeight(wx, wz - d, in cp, biome, seed, settings);

        float gradX = (hPX - hMX) / (2f * d);
        float gradZ = (hPZ - hMZ) / (2f * d);
        return Mathf.Sqrt(gradX * gradX + gradZ * gradZ);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Height computation (unchanged from Phase 2)
    // ═══════════════════════════════════════════════════════════════════════════

    private float ComputeShapedHeight(int wx, int wz, in ClimatePoint cp,
                                       BiomeDefinition biome, int seed, VoxelWorldSettings settings)
    {
        int minY = settings.minHeight;
        int maxY = settings.maxHeight;

        float shaped = biome.EvaluateHeightCurve(cp.Continentalness) * biome.heightMultiplier;

        float seaLevelNorm = (float)(settings.seaLevel - minY) / (maxY - minY);

        // Erosion flattening target:
        // On land, erosion erodes peaks down towards lowlands (seaLevel + ~10 blocks).
        // Lowlands and coastal terrain below this threshold are untouched.
        // Underwater, flatTarget stays underwater (never pulls seabed above sea level).
        // At sea level, flatTarget is strictly C0 continuous (no step discontinuities).
        float flatTarget;
        if (shaped >= seaLevelNorm)
        {
            flatTarget = Mathf.Min(shaped, seaLevelNorm + 0.04f);
        }
        else
        {
            flatTarget = shaped;
        }

        float flattened = Mathf.Lerp(shaped, flatTarget, cp.Erosion * 0.6f);

        float ridgeContrib = 0f;
        if (cp.Weirdness > ridgeWeirdnessThreshold && biome.ridgeStrength > 0.001f)
        {
            float weirdFactor = Mathf.InverseLerp(ridgeWeirdnessThreshold, 1f, cp.Weirdness);
            float ridgeNoise  = VoxelNoise.RidgedNoise2D(wx, wz, ridgeScale, ridgeOctaves, 2f, seed + 99999);
            ridgeContrib = ridgeNoise * weirdFactor * biome.ridgeStrength * 0.25f;
        }

        float heightNorm = Mathf.Clamp01(flattened + ridgeContrib);
        return Mathf.Lerp(minY, maxY, heightNorm);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Block assignment — heightmap mode
    // ═══════════════════════════════════════════════════════════════════════════

    private static byte AssignBlock(int wx, int wy, int wz,
                                     int surfaceY, int bedrockTop, byte bedrockId,
                                     BiomeDefinition biome, VoxelWorldSettings settings,
                                     in BlendResult blend)
    {
        if (wy > surfaceY)
        {
            bool underwater = wy <= settings.seaLevel && settings.waterBlock != null;
            return underwater ? settings.waterBlock.blockId : (byte)0;
        }

        if (bedrockId != 0 && wy <= bedrockTop)
            return bedrockId;

        bool isBeach = IsBeachColumn(wx, wz, surfaceY, biome, settings)
                       && (biome.steepSlopeBlock == null || blend.SlopeGradient <= biome.slopeSteepnessThreshold);

        if (wy == surfaceY)
            return ResolveSurfaceBlock(wx, wy, wz, surfaceY, biome, settings, blend.SlopeGradient);

        if (wy >= surfaceY - biome.subsurfaceDepth)
        {
            if (isBeach && biome.beachBlock != null)
                return biome.beachBlock.blockId;

            return biome.subsurfaceBlock != null ? biome.subsurfaceBlock.blockId : (byte)1;
        }

        return settings.stoneBlock != null ? settings.stoneBlock.blockId : (byte)1;
    }

    private static bool IsBeachColumn(int wx, int wz, int surfaceY, BiomeDefinition biome, VoxelWorldSettings settings)
    {
        if (biome.beachBlock == null) return false;

        // Sand extends from shallow coastal water (seaLevel - beachDepthBelowSeaLevel)
        // up through seaLevel + beachHeightAboveSeaLevel
        int minBeachY = settings.seaLevel - biome.beachDepthBelowSeaLevel;
        int pureBeachY = settings.seaLevel + Mathf.Max(1, biome.beachHeightAboveSeaLevel - 1);
        int maxBeachY = settings.seaLevel + biome.beachHeightAboveSeaLevel;

        if (surfaceY < minBeachY) return false;

        // Guaranteed sand beach from shallow water up to pureBeachY (e.g. seaLevel + 2)
        if (surfaceY <= pureBeachY) return true;

        // In the upper beach transition zone (e.g. Y = 3), use smooth noise to transition organically into grass
        if (surfaceY <= maxBeachY)
        {
            float noise = Mathf.PerlinNoise((wx + 18513.7f) / 16f, (wz + 47291.3f) / 16f);
            return noise > 0.35f;
        }

        return false;
    }

    private static byte ResolveSurfaceBlock(int wx, int wy, int wz, int surfaceY, BiomeDefinition biome,
                                             VoxelWorldSettings settings, float slopeGradient)
    {
        if (biome.snowlineBlock != null && wy >= biome.snowlineY)
            return biome.snowlineBlock.blockId;

        if (biome.steepSlopeBlock != null && slopeGradient > biome.slopeSteepnessThreshold)
            return biome.steepSlopeBlock.blockId;

        if (IsBeachColumn(wx, wz, surfaceY, biome, settings))
            return biome.beachBlock.blockId;

        // Underwater surface (CRITICAL: NEVER PLACE GRASS UNDERWATER!)
        // Any column at or below sea level that is not a sand beach must be dirt, never grass.
        if (surfaceY <= settings.seaLevel)
            return biome.subsurfaceBlock != null ? biome.subsurfaceBlock.blockId : (byte)1;

        return biome.surfaceBlock != null ? biome.surfaceBlock.blockId : (byte)0;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Block assignment — chaos (3D density) mode
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Checks whether a biome allows 3D chaos overhangs/islands. Thread-safe.</summary>
    private static bool IsChaosEligibleBiome(BiomeDefinition biome, float weirdness)
    {
        if (biome == null) return false;
        return biome.biomeName == "Fantasy Highlands" || (biome.biomeName == "Mountains" && weirdness > 0.65f);
    }

    /// <summary>
    /// Fills a 3D chaos column top-down, dynamically resolving the true surface block
    /// on the first solid voxel beneath air/water, followed by subsurface depth and stone.
    /// Returns the topmost solid world Y found in the column. Thread-safe.
    /// </summary>
    private int FillChaosColumn(
        VoxelChunkData data, int lx, int lz, int wx, int wz,
        int surfaceY, int bedrockTop, byte bedrockId, BiomeDefinition biome,
        VoxelWorldSettings settings, float[] chaosNoiseGrid,
        int cnx, int cny, int cnz, int cgx, int cgz, float cfx, float cfz,
        int minChaosLy, int maxChaosLy, int chaosStep, float invChaosStep, int seed)
    {
        int topSolidWy = int.MinValue;
        int depthFromSurface = 0;
        bool wasAirAbove = true;
        int deepCutoff = surfaceY - 40;
        int ceilingY = surfaceY + 30;

        for (int ly = data.Height - 1; ly >= 0; ly--)
        {
            int wy = settings.LocalYToWorld(ly);
            byte block;

            if (bedrockId != 0 && wy <= bedrockTop)
            {
                block = bedrockId;
                data.SetBlock(lx, ly, lz, block);
                continue;
            }

            bool isSolid;
            if (wy < deepCutoff)
            {
                isSolid = true;
            }
            else if (wy > ceilingY)
            {
                isSolid = false;
            }
            else
            {
                float noise3D;
                if (chaosNoiseGrid != null && ly >= minChaosLy && ly <= maxChaosLy)
                {
                    int deltaY = ly - minChaosLy;
                    int cgy = deltaY / chaosStep;
                    float cfy = (deltaY % chaosStep) * invChaosStep;
                    noise3D = TrilinearInterpolate(chaosNoiseGrid, cnx, cny, cnz, cgx, cgy, cgz, cfx, cfy, cfz);
                }
                else
                {
                    noise3D = VoxelNoise.Sample3DFbm(wx, wy, wz, chaos.densityScale,
                                                     chaos.densityOctaves, chaos.densityPersistence,
                                                     2f, seed + 777777);
                }

                float heightAboveSurface = wy - surfaceY;
                float gradient = -heightAboveSurface * chaos.densityGradient;
                float density = gradient + (noise3D - 0.5f) * chaos.densityAmplitude;
                isSolid = density > 0f;
            }

            if (!isSolid)
            {
                wasAirAbove = true;
                depthFromSurface = 0;
                bool underwater = wy <= settings.seaLevel && settings.waterBlock != null;
                block = underwater ? settings.waterBlock.blockId : (byte)0;
            }
            else
            {
                if (topSolidWy == int.MinValue)
                    topSolidWy = wy;

                if (wasAirAbove)
                {
                    block = ResolveSurfaceBlock(wx, wy, wz, wy, biome, settings, 0f);
                    wasAirAbove = false;
                    depthFromSurface = 1;
                }
                else if (depthFromSurface <= biome.subsurfaceDepth)
                {
                    bool isBeach = IsBeachColumn(wx, wz, wy, biome, settings);
                    if (isBeach && biome.beachBlock != null)
                        block = biome.beachBlock.blockId;
                    else
                        block = biome.subsurfaceBlock != null ? biome.subsurfaceBlock.blockId : (byte)1;
                    depthFromSurface++;
                }
                else
                {
                    block = settings.stoneBlock != null ? settings.stoneBlock.blockId : (byte)1;
                }
            }

            data.SetBlock(lx, ly, lz, block);
        }

        return topSolidWy != int.MinValue ? topSolidWy : surfaceY;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Surface sampler implementation (for StructurePlacer)
    // ═══════════════════════════════════════════════════════════════════════════

    public ClimateConfig Climate => climate;
    public BiomeRegistry BiomeRegistry => biomeRegistry;

    /// <summary>Samples all 5 climate axes at world coordinates (wx, wz).</summary>
    public ClimatePoint SampleClimate(int wx, int wz, int seed)
    {
        return climate != null ? climate.Sample(wx, wz, seed) : default;
    }

    /// <summary>Returns the BiomeDefinition matching climate at world coordinates (wx, wz).</summary>
    public BiomeDefinition GetBiome(int wx, int wz, int seed)
    {
        if (climate == null || biomeRegistry == null) return null;
        ClimatePoint cp = climate.Sample(wx, wz, seed);
        return biomeRegistry.GetBiome(in cp);
    }

    /// <summary>Returns the computed surface Y at world (wx, wz) for structure placement.</summary>
    public int GetSurfaceY(int wx, int wz, int seed, VoxelWorldSettings settings)
    {
        ClimatePoint    cp    = climate.Sample(wx, wz, seed);
        BiomeDefinition biome = biomeRegistry.GetBiome(in cp);
        float           h     = ComputeShapedHeight(wx, wz, in cp, biome, seed, settings);
        return Mathf.Clamp(Mathf.RoundToInt(h), settings.minHeight, settings.maxHeight);
    }

    /// <summary>Internal surface query method used by SurfaceSampler. Thread-safe.</summary>
    public bool TryGetSurfaceInternal(VoxelWorldSettings settings, int seed, int wx, int wz,
                                      out int surfaceY, out byte surfaceBlockId, out float slope)
    {
        ClimatePoint cp = climate.Sample(wx, wz, seed);
        BlendResult blend = ComputeBlendedHeight(wx, wz, cp, seed, settings);
        surfaceY = blend.SurfaceY;
        slope = blend.SlopeGradient;
        BiomeDefinition biome = blend.Biome;

        // In chaos terrain, scan downward from top ceiling to find the first solid block
        bool biomeAllowsChaos = IsChaosEligibleBiome(biome, cp.Weirdness);
        if (enableChaos && chaos != null && chaos.maskThreshold < 1f && biomeAllowsChaos)
        {
            float maskOx = 10000f + (seed * 127.1f + chaos.maskSeedOffset) % 9999f;
            float maskOz = 10000f + (seed * 311.7f + chaos.maskSeedOffset) % 9999f;
            float mask = Mathf.PerlinNoise((wx + maskOx) / chaos.maskScale, (wz + maskOz) / chaos.maskScale);
            if (mask > chaos.maskThreshold)
            {
                int ceilingY = surfaceY + 30;
                int deepCutoff = surfaceY - 40;
                for (int wy = ceilingY; wy >= deepCutoff; wy--)
                {
                    float heightAboveSurface = wy - surfaceY;
                    float gradient = -heightAboveSurface * chaos.densityGradient;
                    float noise3D = VoxelNoise.Sample3DFbm(wx, wy, wz, chaos.densityScale,
                                                          chaos.densityOctaves, chaos.densityPersistence,
                                                          2f, seed + 777777);
                    float density = gradient + (noise3D - 0.5f) * chaos.densityAmplitude;
                    if (density > 0f)
                    {
                        surfaceY = wy;
                        surfaceBlockId = ResolveSurfaceBlock(wx, wy, wz, surfaceY, biome, settings, 0f);
                        slope = 0f;
                        return true;
                    }
                }
            }
        }

        surfaceBlockId = ResolveSurfaceBlock(wx, surfaceY, wz, surfaceY, biome, settings, slope);
        return true;
    }

    /// <summary>Checks whether a vertical column has clear air. Thread-safe.</summary>
    public bool IsAirColumnInternal(VoxelWorldSettings settings, int seed, int wx, int startY, int wz, int height)
    {
        int endY = startY + height - 1;
        ClimatePoint cp = climate.Sample(wx, wz, seed);
        BiomeDefinition biome = biomeRegistry.GetBiome(in cp);
        int baseSurface = GetSurfaceY(wx, wz, seed, settings);
        if (startY <= baseSurface) return false;
        if (startY <= settings.seaLevel) return false;

        bool biomeAllowsChaos = IsChaosEligibleBiome(biome, cp.Weirdness);
        if (enableChaos && chaos != null && chaos.maskThreshold < 1f && biomeAllowsChaos)
        {
            float maskOx = 10000f + (seed * 127.1f + chaos.maskSeedOffset) % 9999f;
            float maskOz = 10000f + (seed * 311.7f + chaos.maskSeedOffset) % 9999f;
            float mask = Mathf.PerlinNoise((wx + maskOx) / chaos.maskScale, (wz + maskOz) / chaos.maskScale);
            if (mask > chaos.maskThreshold)
            {
                for (int wy = startY; wy <= endY; wy++)
                {
                    float heightAbove = wy - baseSurface;
                    float gradient = -heightAbove * chaos.densityGradient;
                    float noise3D = VoxelNoise.Sample3DFbm(wx, wy, wz, chaos.densityScale,
                                                          chaos.densityOctaves, chaos.densityPersistence,
                                                          2f, seed + 777777);
                    float density = gradient + (noise3D - 0.5f) * chaos.densityAmplitude;
                    if (density > 0f) return false;
                }
            }
        }

        return true;
    }

    /// <summary>Nested ISurfaceSampler implementation passed to StructurePlacer.</summary>
    private class SurfaceSampler : ISurfaceSampler
    {
        private readonly OverworldGenerator _gen;
        private readonly VoxelWorldSettings _settings;
        private readonly int _seed;

        public SurfaceSampler(OverworldGenerator gen, VoxelWorldSettings settings, int seed)
        {
            _gen = gen;
            _settings = settings;
            _seed = seed;
        }

        public bool TryGetSurface(int wx, int wz, out int surfaceY, out byte surfaceBlockId)
        {
            return _gen.TryGetSurfaceInternal(_settings, _seed, wx, wz, out surfaceY, out surfaceBlockId, out _);
        }

        public bool TryGetSurface(int wx, int wz, out int surfaceY, out byte surfaceBlockId, out float slope)
        {
            return _gen.TryGetSurfaceInternal(_settings, _seed, wx, wz, out surfaceY, out surfaceBlockId, out slope);
        }

        public bool IsAirColumn(int wx, int startY, int wz, int height)
        {
            return _gen.IsAirColumnInternal(_settings, _seed, wx, startY, wz, height);
        }

        public BiomeDefinition GetBiome(int wx, int wz)
        {
            ClimatePoint cp = _gen.climate.Sample(wx, wz, _seed);
            return _gen.biomeRegistry.GetBiome(in cp);
        }
    }
}