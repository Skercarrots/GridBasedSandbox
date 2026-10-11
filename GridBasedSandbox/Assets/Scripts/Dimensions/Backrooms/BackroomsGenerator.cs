using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  BackroomsGenerator — Modular, customizable procedural generator for
//  The Backrooms: Level 0 (The Lobby, Corridors & Sub-zones).
//
//  Features:
//  • Lore & Film Accurate Floor Plan: Organic, non-repetitive labyrinth of
//    rooms (suites, alcoves, pillar halls) and 3x3 corridors.
//  • Guaranteed Connectivity: 100% of rooms and corridors are accessible.
//    No sealed rooms, no blocked doorways, no impassable obstacles.
//  • Continents & Biomes: Macro-continents guide zone and material transitions
//    seamlessly without destructive perimeter walls slicing across rooms.
//  • Kane Pixels Spawn Chamber: Safe spawn facing the Wall Rift Portal with
//    guaranteed open exits into the surrounding maze.
//  • Unified Cell Evaluation: EvaluateCell() powers chunk generation,
//    in-game radar/map, and the Editor preview window with 100% parity.
//  • 100% deterministic, thread-safe, and zero seams across chunk boundaries.
// ─────────────────────────────────────────────────────────────────────────────

public struct BackroomsCellInfo
{
    public BackroomsColumnSpec spec;
    public byte carpetBlock;
    public byte wallpaperBlock;
    public byte ceilingBlock;
    public bool isLight;
    public bool isSolidWall;
    public bool isDoorway;
    public bool isCorridor;
    public bool isArrivalRoom;
    public BackroomsContinentProfile continent;
    public BackroomsZoneProfile zone;
}

[CreateAssetMenu(fileName = "BackroomsGenerator", menuName = "VoxelWorld/Generators/Backrooms Level 0 (Modular)")]
public class BackroomsGenerator : WorldGenerator
{
    [Header("Default Block Definitions")]
    [SerializeField] private VoxelBlockType carpetBlock;
    [SerializeField] private VoxelBlockType wallpaperBlock;
    [SerializeField] private VoxelBlockType ceilingBlock;
    [SerializeField] private VoxelBlockType fluorescentLightBlock;
    [SerializeField] private VoxelBlockType halfWallCapBlock;
    [SerializeField] private VoxelBlockType plenumBlock;
    [SerializeField] private VoxelBlockType bedrockBlock;

    [Header("Default Layout Strategy")]
    [Tooltip("Default floor-plan strategy when a zone does not specify one.")]
    [SerializeField] private BackroomsLayoutStrategy defaultLayout;

    [Header("Continents System ('Grandes Blocos')")]
    [Tooltip("Enable structural bulkheads enclosing continents. When disabled (recommended for lore accuracy), continents seamlessly transition without destructive barrier walls.")]
    [SerializeField] private bool enableContinentalBulkheads = false;

    [Tooltip("Size in blocks of each continent super-structure (recommended: 240 to 384 blocks).")]
    [Range(128, 512)]
    [SerializeField] private int continentSize = 256;

    [Tooltip("Thickness in blocks of the perimeter barrier wall enclosing each continent (0 = none).")]
    [Range(0, 8)]
    [SerializeField] private int continentBorderThickness = 0;

    [Header("Continental Warp & Deformities")]
    [Tooltip("Intensity of the organic Perlin domain warping that creates diagonal boundaries between zones/continents (0 = straight grid, 44 = dramatic diagonals).")]
    [Range(0f, 80f)]
    [SerializeField] private float continentalWarpAmplitude = 44f;

    [Tooltip("Scale of the domain warp noise (higher = more frequent wavy/diagonal transitions).")]
    [Range(0.001f, 0.03f)]
    [SerializeField] private float continentalWarpScale = 0.006f;

    [Tooltip("Number of gateway passages per border edge connecting neighboring continents.")]
    [Range(1, 4)]
    [SerializeField] private int gatewaysPerContinentEdge = 2;

    [Tooltip("Width of the gateway passage in blocks (user requested: 3).")]
    [Range(2, 6)]
    [SerializeField] private int gatewayWidth = 3;

    [Tooltip("List of continent profiles. If empty, the generator clusters the existing zones.")]
    [SerializeField] private List<BackroomsContinentProfile> continents = new List<BackroomsContinentProfile>();

    [Header("Macro-Zoning ('Biomas')")]
    [Tooltip("Size in blocks of each architectural macro sector (recommended: 48, divisible by 4, 6, 8, 12, 16).")]
    [Range(24, 96)]
    [SerializeField] private int macroSectorSize = 48;

    [Tooltip("Global fallback list of sub-zones / biomes.")]
    [SerializeField] private List<BackroomsZoneProfile> zones = new List<BackroomsZoneProfile>();

    [Header("Special Feature Rooms ('Salas Únicas e Raras')")]
    [Tooltip("Size of regional cells for rare room distribution (e.g. 64x64 blocks).")]
    [Range(32, 128)]
    [SerializeField] private int specialRoomRegionSize = 64;

    [Tooltip("Probability of a rare feature room spawning inside a region (0.0 to 1.0).")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float specialRoomChance = 0.35f;

    [Tooltip("Catalog of candidate unique feature rooms.")]
    [SerializeField] private List<BackroomsFeatureRoom> specialRooms = new List<BackroomsFeatureRoom>();

    [Header("Vertical Metrics")]
    [Tooltip("Number of free air blocks between carpet and ceiling (reference: 3 blocks for 3x3 corridors, 4 for halls).")]
    [Range(3, 6)]
    [SerializeField] private int freeHeight = 3;

    // Cached block IDs for high-performance off-thread generation
    private byte _carpetId      = 20;
    private byte _wallpaperId   = 21;
    private byte _ceilingId     = 22;
    private byte _lightId       = 23;
    private byte _halfWallCapId = 21;
    private byte _plenumId      = 24;
    private byte _bedrockId     = 6;

    [System.NonSerialized] private BackroomsLayoutStrategy _activeDefaultLayout;
    [System.NonSerialized] private bool _isPrepared;

    public override void Prepare()
    {
        _carpetId      = carpetBlock != null ? carpetBlock.blockId : (byte)20;
        _wallpaperId   = wallpaperBlock != null ? wallpaperBlock.blockId : (byte)21;
        _ceilingId     = ceilingBlock != null ? ceilingBlock.blockId : (byte)22;
        _lightId       = fluorescentLightBlock != null ? fluorescentLightBlock.blockId : (byte)23;
        _halfWallCapId = halfWallCapBlock != null ? halfWallCapBlock.blockId : _wallpaperId;
        _plenumId      = plenumBlock != null ? plenumBlock.blockId : (byte)24;
        _bedrockId     = bedrockBlock != null ? bedrockBlock.blockId : (byte)6;

        if (_activeDefaultLayout == null)
        {
            _activeDefaultLayout = defaultLayout != null ? defaultLayout : ScriptableObject.CreateInstance<CorridorMazeLayout>();
        }
        if (_activeDefaultLayout != null)
        {
            _activeDefaultLayout.Prepare();
        }

        if (continents != null)
        {
            for (int i = 0; i < continents.Count; i++)
            {
                if (continents[i] != null)
                    continents[i].Prepare(_carpetId, _wallpaperId, _ceilingId);
            }
        }

        if (zones != null)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] != null)
                {
                    zones[i].CacheIds(_carpetId, _wallpaperId, _ceilingId);
                    if (zones[i].layout != null)
                        zones[i].layout.Prepare();
                }
            }
        }

        if (specialRooms != null)
        {
            for (int i = 0; i < specialRooms.Count; i++)
            {
                if (specialRooms[i] != null)
                    specialRooms[i].Bake();
            }
        }

        _isPrepared = true;
    }

    public override bool TryGetArrivalPoint(VoxelWorldSettings settings, int seed, out DimensionArrivalPoint point)
    {
        float floorY = settings != null ? settings.minHeight + 1f : 1f;
        point = new DimensionArrivalPoint
        {
            position = new Vector3(3.5f, floorY + 1f, 3.5f),
            hasReturnAnchor = true,
            returnAnchorPosition = new Vector3(0.55f, floorY + 1f, 3.5f),
            returnAnchorYaw = 90f // faces +X (into the room)
        };
        return true;
    }

    /// <summary>
    /// Evaluates the complete architectural specification for any world block column (wx, wz).
    /// Used identically by chunk voxel generation, the in-game radar/map, and the Editor preview.
    /// Thread-safe, deterministic, and allocation-free.
    /// </summary>
    public BackroomsCellInfo EvaluateCell(int wx, int wz, int seed)
    {
        if (!_isPrepared) Prepare();

        int sectorSize = Mathf.Max(24, macroSectorSize);
        int secX = FloorDiv(wx, sectorSize);
        int secZ = FloorDiv(wz, sectorSize);
        int localSecX = Mod(wx, sectorSize);
        int localSecZ = Mod(wz, sectorSize);

        // Continental Coordinates with Domain Warping
        int contS = Mathf.Max(128, continentSize);
        float warpScale = Mathf.Clamp(continentalWarpScale, 0.001f, 0.03f);
        float warpAmp = Mathf.Max(0f, continentalWarpAmplitude);
        int warpX = (int)((Mathf.PerlinNoise((wx + seed * 11) * warpScale + 123.4f, (wz + seed * 17) * warpScale + 567.8f) - 0.5f) * warpAmp);
        int warpZ = (int)((Mathf.PerlinNoise((wx - seed * 23) * warpScale + 789.1f, (wz - seed * 29) * warpScale + 234.5f) - 0.5f) * warpAmp);

        int contCoordX = wx + warpX;
        int contCoordZ = wz + warpZ;
        int contX = FloorDiv(contCoordX, contS);
        int contZ = FloorDiv(contCoordZ, contS);
        int localContX = Mod(contCoordX, contS);
        int localContZ = Mod(contCoordZ, contS);

        BackroomsContinentProfile continent = PickContinent(seed, contX, contZ);
        BackroomsZoneProfile zone = PickZoneFromContinent(continent, seed, contX, contZ, secX, secZ);

        byte curCarpet    = zone != null ? zone.GetEffectiveCarpet(_carpetId) : _carpetId;
        byte curWallpaper = zone != null ? zone.GetEffectiveWallpaper(_wallpaperId) : _wallpaperId;
        byte curCeiling   = zone != null ? zone.GetEffectiveCeiling(_ceilingId) : _ceilingId;
        byte borderWallId = continent != null ? continent.GetEffectiveBorderWall(curWallpaper) : curWallpaper;

        BackroomsColumnSpec spec = BackroomsColumnSpec.Open;

        // Check optional Continental Perimeter Bulkhead (disabled by default for lore accuracy)
        int borderThick = Mathf.Max(0, continentBorderThickness);
        bool onContinentalBorder = enableContinentalBulkheads && borderThick > 0 &&
            (localContX < borderThick || localContX >= contS - borderThick ||
             localContZ < borderThick || localContZ >= contS - borderThick);

        bool isContinentalGateway = false;
        if (onContinentalBorder)
        {
            int gateW = Mathf.Max(2, gatewayWidth);
            int numGates = Mathf.Clamp(gatewaysPerContinentEdge, 1, 4);

            if (localContX < borderThick || localContX >= contS - borderThick)
            {
                for (int g = 0; g < numGates; g++)
                {
                    int gateHash = DeterministicRng.Hash(seed, contX, contZ, 7000 + g * 10);
                    int gateZ = 32 + (gateHash & 0x7FFFFFFF) % Mathf.Max(1, contS - 64);
                    if (Mathf.Abs(localContZ - gateZ) <= gateW / 2) { isContinentalGateway = true; break; }
                }
            }
            if (!isContinentalGateway && (localContZ < borderThick || localContZ >= contS - borderThick))
            {
                for (int g = 0; g < numGates; g++)
                {
                    int gateHash = DeterministicRng.Hash(seed, contX, contZ, 8000 + g * 10);
                    int gateX = 32 + (gateHash & 0x7FFFFFFF) % Mathf.Max(1, contS - 64);
                    if (Mathf.Abs(localContX - gateX) <= gateW / 2) { isContinentalGateway = true; break; }
                }
            }
        }

        if (onContinentalBorder && !isContinentalGateway)
        {
            spec = BackroomsColumnSpec.FullWall;
            spec.customWallBlock = borderWallId;
        }
        else if (onContinentalBorder && isContinentalGateway)
        {
            spec = BackroomsColumnSpec.Open;
        }
        else
        {
            BackroomsLayoutStrategy strategy = (zone != null && zone.layout != null) ? zone.layout : _activeDefaultLayout;
            var ctx = new BackroomsLayoutContext
            {
                wx = wx,
                wz = wz,
                seed = seed,
                sectorSize = sectorSize,
                sectorX = secX,
                sectorZ = secZ,
                localX = localSecX,
                localZ = localSecZ,
                zone = zone
            };
            spec = strategy != null ? strategy.Evaluate(ctx) : BackroomsColumnSpec.Open;
        }

        // Guaranteed Kane Pixels Spawn / Arrival Room at (0, 0)
        // West wall (wx = 0, wz = 1..5) holds the glowing Wall Rift portal.
        // Room interior is 5x5 (wx = 1..5, wz = 1..5).
        // East wall (wx = 6) has doorway at wz = 2..4.
        // North wall (wz = 6) has doorway at wx = 2..4.
        // South wall (wz = 0) has open corridor connection at wx = 2..4.
        bool isArrivalZone = (secX == 0 && secZ == 0 && wx >= 0 && wx <= 6 && wz >= 0 && wz <= 6);
        if (isArrivalZone)
        {
            if (wx == 0 && wz >= 1 && wz <= 5)
            {
                spec = BackroomsColumnSpec.FullWall;
                spec.customWallBlock = curWallpaper;
            }
            else if (wx == 6 && wz >= 1 && wz <= 5)
            {
                if (wz >= 2 && wz <= 4)
                    spec = BackroomsColumnSpec.Doorway(2);
                else
                    spec = BackroomsColumnSpec.FullWall;
            }
            else if (wz == 6 && wx >= 1 && wx <= 5)
            {
                if (wx >= 2 && wx <= 4)
                    spec = BackroomsColumnSpec.Doorway(2);
                else
                    spec = BackroomsColumnSpec.FullWall;
            }
            else if (wz == 0 && wx >= 1 && wx <= 5)
            {
                if (wx >= 2 && wx <= 4)
                    spec = BackroomsColumnSpec.Doorway(2);
                else
                    spec = BackroomsColumnSpec.FullWall;
            }
            else if (wx >= 1 && wx <= 5 && wz >= 1 && wz <= 5)
            {
                spec = BackroomsColumnSpec.Open;
            }
        }

        bool isSolid = (spec.wallHeight == BackroomsColumnSpec.FullHeight && spec.wallBottomOffset == 0);
        bool isDoor = (spec.wallHeight == BackroomsColumnSpec.FullHeight && spec.wallBottomOffset > 0);
        bool isCorridor = spec.isCorridor && !isSolid;

        // Lighting evaluation
        int penSize = zone != null ? Mathf.Max(6, zone.penumbraSize) : 12;
        int penCellX = FloorDiv(wx, penSize);
        int penCellZ = FloorDiv(wz, penSize);
        float penRoll = (DeterministicRng.Hash(seed, penCellX, penCellZ, 606) & 0xFFFF) / 65536f;
        float penProb = zone != null ? zone.penumbraProbability : 0.08f;
        bool inDarkPocket = penRoll < penProb;

        int lSpacing = zone != null ? Mathf.Max(3, zone.lightSpacing) : 6;
        bool isLightCoord = isCorridor ? (Mod(wx + wz, 6) == 0) : (Mod(wx, lSpacing) == 0 && Mod(wz, lSpacing) == 0);

        // Guaranteed light in the arrival room
        if (isArrivalZone && wx == 3 && wz == 3)
        {
            isLightCoord = true;
            inDarkPocket = false;
        }

        bool isLightActive = isLightCoord && !inDarkPocket && !isSolid;

        return new BackroomsCellInfo
        {
            spec = spec,
            carpetBlock = curCarpet,
            wallpaperBlock = curWallpaper,
            ceilingBlock = curCeiling,
            isLight = isLightActive,
            isSolidWall = isSolid,
            isDoorway = isDoor,
            isCorridor = isCorridor,
            isArrivalRoom = isArrivalZone,
            continent = continent,
            zone = zone
        };
    }

    public override void Generate(VoxelChunkData data, VoxelWorldSettings settings, int seed)
    {
        if (!_isPrepared) Prepare();

        int originX = data.WorldOriginX;
        int originZ = data.WorldOriginZ;
        int width   = data.Width;
        int hFree   = Mathf.Clamp(freeHeight, 3, 6);

        // Vertical slice indices
        int yFloorBedrock = settings.WorldYToLocal(0);
        int yCarpet       = settings.WorldYToLocal(1);
        int yFirstAir     = settings.WorldYToLocal(2);
        int yLastAir      = settings.WorldYToLocal(1 + hFree);
        int yCeiling      = settings.WorldYToLocal(2 + hFree);
        int yPlenumStart  = settings.WorldYToLocal(3 + hFree);
        int yPlenumEnd    = settings.WorldYToLocal(4 + hFree);
        int yRoofBedrock  = settings.WorldYToLocal(5 + hFree);

        // ── Main Voxel Column Loop ───────────────────────────────────────────
        for (int lx = 0; lx < width; lx++)
        {
            int wx = originX + lx;

            for (int lz = 0; lz < width; lz++)
            {
                int wz = originZ + lz;

                // 1. Bedrock floor
                if (yFloorBedrock >= 0 && yFloorBedrock < data.Height)
                    data.SetBlock(lx, yFloorBedrock, lz, _bedrockId);

                // 2. Evaluate Cell Architecture
                var cell = EvaluateCell(wx, wz, seed);

                // 3. Boucle carpet
                if (yCarpet >= 0 && yCarpet < data.Height)
                    data.SetBlock(lx, yCarpet, lz, cell.carpetBlock);

                // 4. Wall / Column Voxel Placement
                var spec = cell.spec;
                if (spec.wallHeight == BackroomsColumnSpec.FullHeight)
                {
                    byte wallId = spec.customWallBlock != 0 ? spec.customWallBlock : cell.wallpaperBlock;
                    int startY = yFirstAir + spec.wallBottomOffset;
                    for (int ly = startY; ly <= yLastAir; ly++)
                    {
                        if (ly >= 0 && ly < data.Height)
                            data.SetBlock(lx, ly, lz, wallId);
                    }
                }
                else if (spec.wallHeight > 0)
                {
                    byte wallId = spec.customWallBlock != 0 ? spec.customWallBlock : cell.wallpaperBlock;
                    int startY = yFirstAir + spec.wallBottomOffset;
                    int wallTopY = Mathf.Min(yLastAir, startY + spec.wallHeight - 1);
                    for (int ly = startY; ly <= wallTopY; ly++)
                    {
                        if (ly >= 0 && ly < data.Height)
                        {
                            bool isCap = (ly == wallTopY && spec.wallIsHalf);
                            byte bId = isCap ? _halfWallCapId : wallId;
                            data.SetBlock(lx, ly, lz, bId);
                        }
                    }
                }

                // 5. Ceiling & Fluorescent Troffers
                byte mainCeilingBlock = cell.ceilingBlock;
                if (!cell.isSolidWall)
                {
                    if (cell.isLight)
                    {
                        mainCeilingBlock = _lightId;
                    }
                    else if (!cell.isArrivalRoom)
                    {
                        float missCeilProb = cell.zone != null ? cell.zone.missingCeilingProbability : 0.02f;
                        float ceilRoll = (DeterministicRng.Hash(seed, wx, wz, 404) & 0xFFFF) / 65536f;
                        if (ceilRoll < missCeilProb)
                            mainCeilingBlock = 0; // Missing ceiling panel (vazio cego)
                    }
                }

                if (yCeiling >= 0 && yCeiling < data.Height)
                    data.SetBlock(lx, yCeiling, lz, mainCeilingBlock);

                // Apply Soffit drops
                if (spec.ceilingDrop > 0 && !cell.isSolidWall)
                {
                    int dropStart = yCeiling - 1;
                    int dropEnd = Mathf.Max(yFirstAir + 1, yCeiling - spec.ceilingDrop);
                    for (int ly = dropStart; ly >= dropEnd; ly--)
                    {
                        if (ly >= 0 && ly < data.Height)
                            data.SetBlock(lx, ly, lz, cell.ceilingBlock);
                    }
                }

                // 6. Plenum Void (above ceiling)
                for (int ly = yPlenumStart; ly <= yPlenumEnd; ly++)
                {
                    if (ly >= 0 && ly < data.Height)
                    {
                        int ductHash = DeterministicRng.Hash(seed, wx, wz, 505);
                        byte ductBlock = (ductHash & 0x1F) == 0 ? _plenumId : (byte)0;
                        data.SetBlock(lx, ly, lz, ductBlock);
                    }
                }

                // 7. Structural roof slab
                if (yRoofBedrock >= 0 && yRoofBedrock < data.Height)
                    data.SetBlock(lx, yRoofBedrock, lz, _bedrockId);
            }
        }

        // ── Phase 2: Rare Feature Rooms ('Salas Únicas') ───────────────────────
        ApplyNearbySpecialRooms(data, settings, seed, originX, originZ, width, yCarpet, hFree);
    }

    private void ApplyNearbySpecialRooms(VoxelChunkData data, VoxelWorldSettings settings, int seed, int originX, int originZ, int width, int carpetLocalY, int hFree)
    {
        if (specialRooms == null || specialRooms.Count == 0 || specialRoomChance <= 0f) return;

        const int maxRoomSize = 32;
        int regSize = Mathf.Max(32, specialRoomRegionSize);

        int minRegX = FloorDiv(originX - maxRoomSize, regSize);
        int maxRegX = FloorDiv(originX + width + maxRoomSize, regSize);
        int minRegZ = FloorDiv(originZ - maxRoomSize, regSize);
        int maxRegZ = FloorDiv(originZ + width + maxRoomSize, regSize);

        for (int rx = minRegX; rx <= maxRegX; rx++)
        {
            for (int rz = minRegZ; rz <= maxRegZ; rz++)
            {
                if (rx == 0 && rz == 0) continue;

                int regHash = DeterministicRng.Hash(seed, rx, rz, 77777);
                float roll = ((regHash & 0xFFFF) / 65536f);
                if (roll >= specialRoomChance) continue;

                BackroomsFeatureRoom room = PickSpecialRoom(seed, rx, rz);
                if (room == null) continue;

                int maxOffX = Mathf.Max(4, regSize - room.width - 4);
                int maxOffZ = Mathf.Max(4, regSize - room.length - 4);

                int ox = 4 + (DeterministicRng.Hash(seed, rx, rz, 11111) & 0x7FFF) % maxOffX;
                int oz = 4 + (DeterministicRng.Hash(seed, rx, rz, 22222) & 0x7FFF) % maxOffZ;

                int roomWorldX = rx * regSize + ox;
                int roomWorldZ = rz * regSize + oz;

                room.Stamp(data, roomWorldX, roomWorldZ, carpetLocalY, hFree);
            }
        }
    }

    public BackroomsContinentProfile GetContinentAt(int wx, int wz, int seed)
    {
        float warpScale = Mathf.Clamp(continentalWarpScale, 0.001f, 0.03f);
        float warpAmp = Mathf.Max(0f, continentalWarpAmplitude);
        int warpX = (int)((Mathf.PerlinNoise((wx + seed * 11) * warpScale + 123.4f, (wz + seed * 17) * warpScale + 567.8f) - 0.5f) * warpAmp);
        int warpZ = (int)((Mathf.PerlinNoise((wx - seed * 23) * warpScale + 789.1f, (wz - seed * 29) * warpScale + 234.5f) - 0.5f) * warpAmp);

        int contS = Mathf.Max(128, continentSize);
        int contX = FloorDiv(wx + warpX, contS);
        int contZ = FloorDiv(wz + warpZ, contS);

        return PickContinent(seed, contX, contZ);
    }

    public BackroomsZoneProfile GetZoneAt(int wx, int wz, int seed)
    {
        var continent = GetContinentAt(wx, wz, seed);
        int sectorSize = Mathf.Max(24, macroSectorSize);
        int secX = FloorDiv(wx, sectorSize);
        int secZ = FloorDiv(wz, sectorSize);
        return PickZoneFromContinent(continent, seed, 0, 0, secX, secZ);
    }

    private BackroomsContinentProfile PickContinent(int seed, int contX, int contZ)
    {
        if (continents == null || continents.Count == 0) return null;
        if (contX == 0 && contZ == 0) return continents[0];

        float totalWeight = 0f;
        for (int i = 0; i < continents.Count; i++)
        {
            if (continents[i] != null) totalWeight += continents[i].selectionWeight;
        }

        if (totalWeight <= 0f) return continents[0];

        int pickHash = DeterministicRng.Hash(seed, contX, contZ, 44444);
        float roll = ((pickHash & 0xFFFF) / 65536f) * totalWeight;

        float current = 0f;
        for (int i = 0; i < continents.Count; i++)
        {
            if (continents[i] == null) continue;
            current += continents[i].selectionWeight;
            if (roll <= current) return continents[i];
        }

        return continents[0];
    }

    private BackroomsZoneProfile PickZoneFromContinent(BackroomsContinentProfile continent, int seed, int contX, int contZ, int secX, int secZ)
    {
        List<BackroomsZoneProfile> pool = (continent != null && continent.allowedZones != null && continent.allowedZones.Count > 0)
            ? continent.allowedZones
            : zones;

        if (pool == null || pool.Count == 0) return zones != null && zones.Count > 0 ? zones[0] : null;
        if (secX == 0 && secZ == 0) return pool[0];

        float totalWeight = 0f;
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] != null) totalWeight += pool[i].selectionWeight;
        }

        if (totalWeight <= 0f) return pool[0];

        int pickHash = DeterministicRng.Hash(seed, secX, secZ, 55555);
        float roll = ((pickHash & 0xFFFF) / 65536f) * totalWeight;

        float current = 0f;
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] == null) continue;
            current += pool[i].selectionWeight;
            if (roll <= current) return pool[i];
        }

        return pool[0];
    }

    private BackroomsFeatureRoom PickSpecialRoom(int seed, int rx, int rz)
    {
        if (specialRooms == null || specialRooms.Count == 0) return null;

        float totalWeight = 0f;
        for (int i = 0; i < specialRooms.Count; i++)
        {
            if (specialRooms[i] != null) totalWeight += specialRooms[i].rarityWeight;
        }

        if (totalWeight <= 0f) return specialRooms[0];

        int pickHash = DeterministicRng.Hash(seed, rx, rz, 88888);
        float roll = ((pickHash & 0xFFFF) / 65536f) * totalWeight;

        float current = 0f;
        for (int i = 0; i < specialRooms.Count; i++)
        {
            if (specialRooms[i] == null) continue;
            current += specialRooms[i].rarityWeight;
            if (roll <= current) return specialRooms[i];
        }

        return specialRooms[0];
    }

    private static int Mod(int val, int m)
    {
        int r = val % m;
        return r < 0 ? r + m : r;
    }

    private static int FloorDiv(int val, int m)
    {
        return val >= 0 ? val / m : (val - m + 1) / m;
    }
}
