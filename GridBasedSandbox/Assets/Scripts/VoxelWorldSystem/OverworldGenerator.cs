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

        // Per-column surface heights for cave protection (stored once, read twice)
        int[] surfaceHeights = new int[data.Width * data.Width];

        // ── Pass 1: Column fill ───────────────────────────────────────────────
        for (int lx = 0; lx < data.Width; lx++)
        for (int lz = 0; lz < data.Width; lz++)
        {
            int wx = worldOriginX + lx;
            int wz = worldOriginZ + lz;

            ClimatePoint cp = climate.Sample(wx, wz, seed);
            BlendResult blend = ComputeBlendedHeight(wx, wz, cp, seed, settings);

            int   surfaceY = blend.SurfaceY;
            BiomeDefinition biome = blend.Biome;

            surfaceHeights[lx * data.Width + lz] = surfaceY;

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

            // Chaos mask
            bool columnIsChaos = false;
            if (enableChaos && chaos.maskThreshold < 1f)
            {
                float maskOx = 10000f + (seed * 127.1f + chaos.maskSeedOffset) % 9999f;
                float maskOz = 10000f + (seed * 311.7f + chaos.maskSeedOffset) % 9999f;
                float mask   = Mathf.PerlinNoise((wx + maskOx) / chaos.maskScale,
                                                  (wz + maskOz) / chaos.maskScale);
                columnIsChaos = mask > chaos.maskThreshold;
            }

            // Column fill
            for (int ly = 0; ly < data.Height; ly++)
            {
                int  wy = settings.LocalYToWorld(ly);
                byte block;

                if (columnIsChaos)
                    block = AssignBlockChaos(wx, wy, wz, surfaceY, bedrockTop, bedrockId, biome, settings, seed);
                else
                    block = AssignBlock(wx, wy, wz, surfaceY, bedrockTop, bedrockId, biome, settings, blend);

                data.SetBlock(lx, ly, lz, block);
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

        data.RecomputeSectionOccupancy();
        data.IsDirty = true;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Pass 2: Cave carving — cheese + spaghetti + noodle
    // ═══════════════════════════════════════════════════════════════════════════

    private void CarveCaves(VoxelChunkData data, VoxelWorldSettings settings,
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

        if (blendRadius <= 0)
        {
            return new BlendResult { SurfaceY = Mathf.RoundToInt(centerHeight), Biome = centerBiome, SlopeGradient = 0f };
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
            return new BlendResult { SurfaceY = Mathf.RoundToInt(centerHeight), Biome = centerBiome, SlopeGradient = 0f };
        }

        float hPX = ComputeShapedHeight(wx + br, wz,      in cpPX, bPX, seed, settings);
        float hMX = ComputeShapedHeight(wx - br, wz,      in cpMX, bMX, seed, settings);
        float hPZ = ComputeShapedHeight(wx,      wz + br, in cpPZ, bPZ, seed, settings);
        float hMZ = ComputeShapedHeight(wx,      wz - br, in cpMZ, bMZ, seed, settings);

        float blended = (centerHeight * 2f + hPX + hMX + hPZ + hMZ) / 6f;
        float gradX = (hPX - hMX) / (2f * br);
        float gradZ = (hPZ - hMZ) / (2f * br);
        float slope = Mathf.Sqrt(gradX * gradX + gradZ * gradZ);

        return new BlendResult
        {
            SurfaceY      = Mathf.Clamp(Mathf.RoundToInt(blended), settings.minHeight, settings.maxHeight),
            Biome         = centerBiome,
            SlopeGradient = slope
        };
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
        float flattened = Mathf.Lerp(shaped, 0.5f, cp.Erosion * 0.6f);

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
    //  Block assignment — heightmap mode (unchanged from Phase 2)
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

        if (wy == surfaceY)
            return ResolveSurfaceBlock(wy, surfaceY, biome, settings, blend.SlopeGradient);

        if (wy >= surfaceY - biome.subsurfaceDepth)
            return biome.subsurfaceBlock != null ? biome.subsurfaceBlock.blockId : (byte)1;

        return settings.stoneBlock != null ? settings.stoneBlock.blockId : (byte)1;
    }

    private static byte ResolveSurfaceBlock(int wy, int surfaceY, BiomeDefinition biome,
                                             VoxelWorldSettings settings, float slopeGradient)
    {
        if (biome.snowlineBlock != null && wy >= biome.snowlineY)
            return biome.snowlineBlock.blockId;

        if (biome.beachBlock != null
            && surfaceY <= settings.seaLevel
            && surfaceY >= settings.seaLevel - biome.beachDepthBelowSeaLevel)
            return biome.beachBlock.blockId;

        if (biome.steepSlopeBlock != null && slopeGradient > biome.slopeSteepnessThreshold)
            return biome.steepSlopeBlock.blockId;

        return biome.surfaceBlock != null ? biome.surfaceBlock.blockId : (byte)0;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Block assignment — chaos (3D density) mode (unchanged from Phase 2)
    // ═══════════════════════════════════════════════════════════════════════════

    private byte AssignBlockChaos(int wx, int wy, int wz,
                                   int surfaceY, int bedrockTop, byte bedrockId,
                                   BiomeDefinition biome, VoxelWorldSettings settings, int seed)
    {
        if (bedrockId != 0 && wy <= bedrockTop)
            return bedrockId;

        int deepCutoff = surfaceY - 40;
        if (wy < deepCutoff)
            return settings.stoneBlock != null ? settings.stoneBlock.blockId : (byte)1;

        int ceilingY = surfaceY + 30;
        if (wy > ceilingY)
        {
            bool underwater = wy <= settings.seaLevel && settings.waterBlock != null;
            return underwater ? settings.waterBlock.blockId : (byte)0;
        }

        float heightAboveSurface = wy - surfaceY;
        float gradient = -heightAboveSurface * chaos.densityGradient;
        float noise3D  = VoxelNoise.Sample3DFbm(wx, wy, wz, chaos.densityScale,
                                                 chaos.densityOctaves, chaos.densityPersistence,
                                                 2f, seed + 777777);

        float density = gradient + (noise3D - 0.5f) * chaos.densityAmplitude;

        if (density <= 0f)
        {
            bool underwater = wy <= settings.seaLevel && settings.waterBlock != null;
            return underwater ? settings.waterBlock.blockId : (byte)0;
        }

        if (wy == surfaceY)
            return ResolveSurfaceBlock(wy, surfaceY, biome, settings, 0f);

        if (wy >= surfaceY - biome.subsurfaceDepth && wy < surfaceY)
            return biome.subsurfaceBlock != null ? biome.subsurfaceBlock.blockId : (byte)1;

        return settings.stoneBlock != null ? settings.stoneBlock.blockId : (byte)1;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Public surface sampler (for Phase 4 structure placement)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Returns the computed surface Y at world (wx, wz) for structure placement.</summary>
    public int GetSurfaceY(int wx, int wz, int seed, VoxelWorldSettings settings)
    {
        ClimatePoint    cp    = climate.Sample(wx, wz, seed);
        BiomeDefinition biome = biomeRegistry.GetBiome(in cp);
        float           h     = ComputeShapedHeight(wx, wz, in cp, biome, seed, settings);
        return Mathf.Clamp(Mathf.RoundToInt(h), settings.minHeight, settings.maxHeight);
    }
}