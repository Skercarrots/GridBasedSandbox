using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  CorridorMazeLayout — Canonical Liminal Network (Found Footage & Film Accurate).
//
//  Key Architectural Features (Matches Canonical Film & Lore Maps):
//  • Sparse, Distant Rooms: Rooms are separated by long stretches of winding
//    hallways and solid wall mass, appearing organically every 30-70 meters.
//  • Organic, Non-Square Geometries:
//      - L-shaped chambers with corner turns.
//      - T-shaped halls with deep recessed alcoves / nooks.
//      - Elongated rectangular galleries (aspect ratios 2:1 and 3:1).
//      - Hypostyle halls with support pillars (original 4chan photo).
//      - Compact utility / storage chambers.
//  • Winding Orthogonal Hallways: 2-3 block wide corridors with 90-degree doglegs,
//    T-junctions, and dead-end cul-de-sacs cutting through solid drywall mass.
//  • Guaranteed Doorways & Exits: Corridors naturally puncture room perimeters
//    to create guaranteed doorways (standard 3x2, wide archways, or cased openings).
//  • Negative Space: Unexplored areas between distant rooms are solid wall mass.
//  • 100% thread-safe, deterministic, allocation-free, and seam-free across chunks.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "Layout_CorridorMaze", menuName = "VoxelWorld/Backrooms/Layouts/Corridor Maze (3x3)")]
public class CorridorMazeLayout : BackroomsLayoutStrategy
{
    [Header("Macro Grid & Distances")]
    [Tooltip("Distance in blocks between sector node centers (recommended: 36 to 48). Higher = rooms farther apart.")]
    [Range(28, 64)]
    public int corridorGridSize = 38;

    [Tooltip("Width of corridors in blocks (user requested: 3).")]
    [Range(2, 4)]
    public int corridorWidth = 3;

    [Header("Room Distribution & Spacing")]
    [Tooltip("Probability of a cell containing a feature room (0.2 to 0.5). Lower = rooms farther apart with more corridor stretches in between.")]
    [Range(0.15f, 0.60f)]
    public float roomProbability = 0.35f;

    [Tooltip("Minimum room dimension in blocks.")]
    [Range(6, 14)]
    public int roomMinSize = 8;

    [Tooltip("Maximum room dimension in blocks.")]
    [Range(14, 28)]
    public int roomMaxSize = 20;

    [Header("Corridor Network & Winding")]
    [Tooltip("Chance of connecting to adjacent neighbors (creates branching loops and turns).")]
    [Range(0.5f, 0.95f)]
    public float corridorBranchChance = 0.75f;

    [Tooltip("Chance of an optional dead-end corridor branching off (cul-de-sac).")]
    [Range(0.0f, 0.40f)]
    public float deadEndBranchChance = 0.20f;

    [Header("Doorways & Clearances")]
    [Tooltip("Height in blocks of doorway openings (user requested: 2).")]
    [Range(2, 3)]
    public int doorHeight = 2;

    [Tooltip("Width of doorway openings in blocks (user requested: 3).")]
    [Range(2, 4)]
    public int doorWidth = 3;

    [Tooltip("Base chance that a room perimeter wall has an additional doorway opening.")]
    [Range(0.2f, 0.8f)]
    public float roomDoorProbability = 0.45f;

    [Header("Variety & Lore Features")]
    [Tooltip("Enable irregular room shapes: L-shaped, T-shaped, elongated galleries, alcoves.")]
    public bool enableIrregularShapes = true;

    [Tooltip("Enable support pillars inside large rooms (original 4chan photo style).")]
    public bool enablePillarsInLargeRooms = true;

    [Header("Deformities & Diagonal Architecture (Non-Euclidean Lore)")]
    [Tooltip("Chance that a generated room has diagonal, slanted, or trapezoidal walls (0 = only straight orthogonal rooms, 1 = all rooms deformed).")]
    [Range(0f, 1f)]
    public float diagonalRoomChance = 0.35f;

    [Tooltip("How strongly the diagonal walls slope (wedge angle intensity, 0.2 = gentle slope, 0.7 = sharp diagonal wedge).")]
    [Range(0.2f, 0.8f)]
    public float diagonalSlopeIntensity = 0.5f;

    public override BackroomsColumnSpec Evaluate(in BackroomsLayoutContext ctx)
    {
        int wx = ctx.wx;
        int wz = ctx.wz;
        int seed = ctx.seed;

        int C = Mathf.Max(28, corridorGridSize);
        int W = Mathf.Clamp(corridorWidth, 2, 4);
        int halfW = W / 2;
        int dH = Mathf.Clamp(doorHeight, 2, 3);
        int dW = Mathf.Clamp(doorWidth, 2, W);

        // ── Special Spawn / Arrival Chamber at (0, 0) ────────────────────────
        if (wx >= 0 && wx <= 6 && wz >= 0 && wz <= 6)
        {
            // West wall (holding the Kane Pixels Wall Rift Portal)
            if (wx == 0 && wz >= 1 && wz <= 5)
                return BackroomsColumnSpec.FullWall;

            // East exit into the maze
            if (wx == 6 && wz >= 2 && wz <= 4)
                return BackroomsColumnSpec.Doorway(dH);

            // North exit into the maze
            if (wz == 6 && wx >= 2 && wx <= 4)
                return BackroomsColumnSpec.Doorway(dH);

            // South exit
            if (wz == 0 && wx >= 2 && wx <= 4)
                return BackroomsColumnSpec.Doorway(dH);

            // Perimeter walls of the spawn chamber
            if (wx == 0 || wx == 6 || wz == 0 || wz == 6)
                return BackroomsColumnSpec.FullWall;

            // Interior air
            return BackroomsColumnSpec.Open;
        }

        int curCellX = FloorDiv(wx, C);
        int curCellZ = FloorDiv(wz, C);

        // ── Step 1: Check if inside any Feature Room (in 3x3 neighborhood) ───
        bool inRoomInterior = false;
        bool onRoomPerimeter = false;
        bool isRoomPillar = false;

        for (int dx = -1; dx <= 1; dx++)
        {
            int kcx = curCellX + dx;
            for (int dz = -1; dz <= 1; dz++)
            {
                int kcz = curCellZ + dz;

                EvaluateCellRoom(seed, kcx, kcz, C, wx, wz, dW, dH,
                    out bool inside, out bool perim, out bool pillar);

                if (pillar) isRoomPillar = true;
                if (inside) inRoomInterior = true;
                if (perim) onRoomPerimeter = true;
            }
        }

        // ── Step 2: Check if inside any Corridor (in 3x3 neighborhood) ───────
        bool inCorridor = false;
        bool isCorridorTransverseBeam = false;

        for (int dx = -1; dx <= 1; dx++)
        {
            int kcx = curCellX + dx;
            for (int dz = -1; dz <= 1; dz++)
            {
                int kcz = curCellZ + dz;

                if (EvaluateCellCorridors(seed, kcx, kcz, C, halfW, wx, wz, out bool beam))
                {
                    inCorridor = true;
                    if (beam) isCorridorTransverseBeam = true;
                }
            }
        }

        // ── Step 3: Resolve Block Placement ──────────────────────────────────
        // A. Support pillars inside rooms
        if (isRoomPillar)
        {
            return BackroomsColumnSpec.FullWall;
        }

        // B. Room perimeter walls
        if (onRoomPerimeter)
        {
            // Where a corridor intersects a room perimeter, it forms a GUARANTEED DOORWAY!
            if (inCorridor)
            {
                return BackroomsColumnSpec.Doorway(dH);
            }

            // Solid drywall wall of the room
            return BackroomsColumnSpec.FullWall;
        }

        // C. Inside room interior
        if (inRoomInterior)
        {
            return BackroomsColumnSpec.Open;
        }

        // D. Inside corridor
        if (inCorridor)
        {
            if (isCorridorTransverseBeam)
            {
                var spec = BackroomsColumnSpec.Corridor;
                spec.ceilingDrop = 1; // Subtle dropped ceiling beam lintel
                return spec;
            }
            return BackroomsColumnSpec.Corridor;
        }

        // E. Negative Space (Solid wall mass / drywall void between rooms)
        // This is the core aesthetic of Backrooms lore: you are walking through
        // a complex of corridors carved into an infinite solid mass!
        return BackroomsColumnSpec.FullWall;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Room Shape & Geometry Evaluation
    // ─────────────────────────────────────────────────────────────────────────

    private void EvaluateCellRoom(
        int seed, int kcx, int kcz, int C, int wx, int wz, int dW, int dH,
        out bool inside, out bool onPerimeter, out bool isPillar)
    {
        inside = false;
        onPerimeter = false;
        isPillar = false;

        // Skip spawn cell (it has its own custom spawn chamber)
        if (kcx == 0 && kcz == 0) return;

        // Check if this cell spawned a room (sparse distribution)
        int rSeed = PosHash(seed, kcx, kcz, 4001);
        float rRoll = (rSeed & 0xFFFF) / 65536f;
        if (rRoll >= roomProbability) return;

        // Node center
        GetNodePos(seed, kcx, kcz, C, out int nx, out int nz);

        // Dimensions
        int minS = Mathf.Max(6, roomMinSize);
        int maxS = Mathf.Max(minS + 2, roomMaxSize);
        int rw = minS + ((rSeed >> 4) % (maxS - minS + 1));
        int rl = minS + ((rSeed >> 8) % (maxS - minS + 1));

        // Ensure room stays comfortably within cell
        rw = Mathf.Clamp(rw, 6, C - 10);
        rl = Mathf.Clamp(rl, 6, C - 10);

        int shapeType = enableIrregularShapes ? ((rSeed >> 16) % 5) : 0;

        int x0 = nx - rw / 2;
        int x1 = x0 + rw - 1;
        int z0 = nz - rl / 2;
        int z1 = z0 + rl - 1;

        // Quick AABB reject
        if (wx < x0 - 2 || wx > x1 + 2 || wz < z0 - 2 || wz > z1 + 2) return;

        // Check if this room has a diagonal/wedge wall deformity
        bool isDiagonal = ((rSeed >> 12) & 0xFF) < (diagonalRoomChance * 255f);

        if (isDiagonal && rw >= 10 && rl >= 10)
        {
            EvaluateDiagonalRoom(wx, wz, x0, x1, z0, z1, rw, rl, rSeed,
                out inside, out onPerimeter, out isPillar);
            return;
        }

        switch (shapeType)
        {
            case 1:
                // ── L-Shaped Room ─────────────────────────────────────────────
                EvaluateLShapedRoom(wx, wz, x0, x1, z0, z1, rw, rl, rSeed,
                    out inside, out onPerimeter);
                break;

            case 2:
                // ── T-Shaped Room / Room with Alcove ──────────────────────────
                EvaluateTShapedRoom(wx, wz, x0, x1, z0, z1, rw, rl, rSeed,
                    out inside, out onPerimeter);
                break;

            case 3:
                // ── Hypostyle Chamber with Pillars ────────────────────────────
                EvaluatePillaredRoom(wx, wz, x0, x1, z0, z1, rw, rl,
                    out inside, out onPerimeter, out isPillar);
                break;

            default:
                // ── Staggered Rectangle / Gallery ─────────────────────────────
                EvaluateRectRoom(wx, wz, x0, x1, z0, z1,
                    out inside, out onPerimeter);
                break;
        }
    }

    private void EvaluateDiagonalRoom(
        int wx, int wz, int x0, int x1, int z0, int z1, int rw, int rl, int rSeed,
        out bool inside, out bool onPerimeter, out bool isPillar)
    {
        inside = false;
        onPerimeter = false;
        isPillar = false;

        int variant = (rSeed >> 20) & 3; // 0: Slanted South, 1: Slanted North, 2: Slanted West, 3: Slanted East
        float intensity = Mathf.Clamp(diagonalSlopeIntensity, 0.2f, 0.8f);

        switch (variant)
        {
            case 0:
            {
                // ── Slanted South Wall (Tapered Wedge) ─────────────────────────
                // Bottom wall slopes upward from left to right: wz >= z0 + dx * dz / rw
                int dZ = Mathf.Clamp(Mathf.RoundToInt(rl * intensity), 3, rl - 4);
                int zMin = z0 + ((wx - x0) * dZ) / Mathf.Max(1, rw);

                if (wx >= x0 && wx <= x1 && wz >= zMin && wz <= z1)
                {
                    if (wx == x0 || wx == x1 || wz == z1 || wz == zMin)
                        onPerimeter = true;
                    else
                        inside = true;
                }
                break;
            }
            case 1:
            {
                // ── Slanted North Wall ─────────────────────────────────────────
                int dZ = Mathf.Clamp(Mathf.RoundToInt(rl * intensity), 3, rl - 4);
                int zMax = z1 - ((wx - x0) * dZ) / Mathf.Max(1, rw);

                if (wx >= x0 && wx <= x1 && wz >= z0 && wz <= zMax)
                {
                    if (wx == x0 || wx == x1 || wz == z0 || wz == zMax)
                        onPerimeter = true;
                    else
                        inside = true;
                }
                break;
            }
            case 2:
            {
                // ── Slanted West Wall ──────────────────────────────────────────
                int dX = Mathf.Clamp(Mathf.RoundToInt(rw * intensity), 3, rw - 4);
                int xMin = x0 + ((wz - z0) * dX) / Mathf.Max(1, rl);

                if (wz >= z0 && wz <= z1 && wx >= xMin && wx <= x1)
                {
                    if (wz == z0 || wz == z1 || wx == x1 || wx == xMin)
                        onPerimeter = true;
                    else
                        inside = true;
                }
                break;
            }
            default:
            {
                // ── Slanted East Wall ──────────────────────────────────────────
                int dX = Mathf.Clamp(Mathf.RoundToInt(rw * intensity), 3, rw - 4);
                int xMax = x1 - ((wz - z0) * dX) / Mathf.Max(1, rl);

                if (wz >= z0 && wz <= z1 && wx >= x0 && wx <= xMax)
                {
                    if (wz == z0 || wz == z1 || wx == x0 || wx == xMax)
                        onPerimeter = true;
                    else
                        inside = true;
                }
                break;
            }
        }

        // Support pillars inside larger diagonal halls
        if (inside && enablePillarsInLargeRooms && rw >= 14 && rl >= 12)
        {
            int pX1 = x0 + rw / 3;
            int pX2 = x0 + 2 * rw / 3;
            int pZ1 = z0 + rl / 3;
            int pZ2 = z0 + 2 * rl / 3;

            if ((wx == pX1 || wx == pX2) && (wz == pZ1 || wz == pZ2))
            {
                isPillar = true;
            }
        }
    }

    private static void EvaluateRectRoom(
        int wx, int wz, int x0, int x1, int z0, int z1,
        out bool inside, out bool onPerimeter)
    {
        inside = false;
        onPerimeter = false;

        if (wx < x0 || wx > x1 || wz < z0 || wz > z1) return;

        if (wx == x0 || wx == x1 || wz == z0 || wz == z1)
        {
            onPerimeter = true;
        }
        else
        {
            inside = true;
        }
    }

    private static void EvaluateLShapedRoom(
        int wx, int wz, int x0, int x1, int z0, int z1, int rw, int rl, int rSeed,
        out bool inside, out bool onPerimeter)
    {
        inside = false;
        onPerimeter = false;

        // Corner notch cut out of the rectangle
        int notchCorner = (rSeed >> 20) & 3; // 0: NE, 1: NW, 2: SE, 3: SW
        int cutW = Mathf.Max(3, rw / 2);
        int cutL = Mathf.Max(3, rl / 2);

        bool inL = IsInLBounds(wx, wz, x0, x1, z0, z1, cutW, cutL, notchCorner);
        if (!inL) return;

        // On perimeter if any 4-way neighbor is outside the L-shape
        bool pE = !IsInLBounds(wx + 1, wz, x0, x1, z0, z1, cutW, cutL, notchCorner);
        bool pW = !IsInLBounds(wx - 1, wz, x0, x1, z0, z1, cutW, cutL, notchCorner);
        bool pN = !IsInLBounds(wx, wz + 1, x0, x1, z0, z1, cutW, cutL, notchCorner);
        bool pS = !IsInLBounds(wx, wz - 1, x0, x1, z0, z1, cutW, cutL, notchCorner);

        if (pE || pW || pN || pS)
        {
            onPerimeter = true;
        }
        else
        {
            inside = true;
        }
    }

    private static bool IsInLBounds(int wx, int wz, int x0, int x1, int z0, int z1, int cutW, int cutL, int corner)
    {
        if (wx < x0 || wx > x1 || wz < z0 || wz > z1) return false;

        switch (corner)
        {
            case 0: // NE notch removed
                if (wx > x1 - cutW && wz > z1 - cutL) return false;
                break;
            case 1: // NW notch removed
                if (wx < x0 + cutW && wz > z1 - cutL) return false;
                break;
            case 2: // SE notch removed
                if (wx > x1 - cutW && wz < z0 + cutL) return false;
                break;
            case 3: // SW notch removed
                if (wx < x0 + cutW && wz < z0 + cutL) return false;
                break;
        }

        return true;
    }

    private static void EvaluateTShapedRoom(
        int wx, int wz, int x0, int x1, int z0, int z1, int rw, int rl, int rSeed,
        out bool inside, out bool onPerimeter)
    {
        inside = false;
        onPerimeter = false;

        // Main chamber
        bool inMain = (wx >= x0 && wx <= x1 && wz >= z0 && wz <= z1);

        // Attached alcove
        int alcoveW = Mathf.Max(4, rw / 2);
        int alcoveL = Mathf.Max(4, rl / 3);
        int ax0 = x0 + (rw - alcoveW) / 2;
        int ax1 = ax0 + alcoveW - 1;
        int az0 = z1 + 1;
        int az1 = az0 + alcoveL - 1;

        bool inAlcove = (wx >= ax0 && wx <= ax1 && wz >= az0 && wz <= az1);
        bool inT = inMain || inAlcove;

        if (!inT) return;

        bool pE = !IsInTBounds(wx + 1, wz, x0, x1, z0, z1, ax0, ax1, az0, az1);
        bool pW = !IsInTBounds(wx - 1, wz, x0, x1, z0, z1, ax0, ax1, az0, az1);
        bool pN = !IsInTBounds(wx, wz + 1, x0, x1, z0, z1, ax0, ax1, az0, az1);
        bool pS = !IsInTBounds(wx, wz - 1, x0, x1, z0, z1, ax0, ax1, az0, az1);

        if (pE || pW || pN || pS)
        {
            onPerimeter = true;
        }
        else
        {
            inside = true;
        }
    }

    private static bool IsInTBounds(int wx, int wz, int x0, int x1, int z0, int z1, int ax0, int ax1, int az0, int az1)
    {
        if (wx >= x0 && wx <= x1 && wz >= z0 && wz <= z1) return true;
        if (wx >= ax0 && wx <= ax1 && wz >= az0 && wz <= az1) return true;
        return false;
    }

    private void EvaluatePillaredRoom(
        int wx, int wz, int x0, int x1, int z0, int z1, int rw, int rl,
        out bool inside, out bool onPerimeter, out bool isPillar)
    {
        inside = false;
        onPerimeter = false;
        isPillar = false;

        if (wx < x0 || wx > x1 || wz < z0 || wz > z1) return;

        if (wx == x0 || wx == x1 || wz == z0 || wz == z1)
        {
            onPerimeter = true;
            return;
        }

        inside = true;

        if (enablePillarsInLargeRooms && rw >= 14 && rl >= 14)
        {
            int pX1 = x0 + rw / 3;
            int pX2 = x0 + 2 * rw / 3;
            int pZ1 = z0 + rl / 3;
            int pZ2 = z0 + 2 * rl / 3;

            // 1x1 support pillars (iconic Backrooms original photo)
            if ((wx == pX1 || wx == pX2) && (wz == pZ1 || wz == pZ2))
            {
                isPillar = true;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Corridor Network (Winding Orthogonal Doglegs)
    // ─────────────────────────────────────────────────────────────────────────

    private bool EvaluateCellCorridors(
        int seed, int kcx, int kcz, int C, int halfW, int wx, int wz,
        out bool isBeam)
    {
        isBeam = false;

        GetNodePos(seed, kcx, kcz, C, out int nxA, out int nzA);

        // 1. East Corridor: connects (kcx, kcz) to (kcx + 1, kcz)
        int eastHash = PosHash(seed, kcx, kcz, 1001);
        bool hasEast = (kcx == 0 && kcz == 0) || ((eastHash & 0xFF) < (corridorBranchChance * 255f));

        if (hasEast)
        {
            GetNodePos(seed, kcx + 1, kcz, C, out int nxB, out int nzB);

            // Intermediate orthogonal turn X
            int turnX = nxA + (nxB - nxA) / 2 + ((eastHash >> 8) % 5 - 2);

            // Segment 1: from nxA to turnX at nzA
            if (IsInSegment(wx, wz, nxA, nzA, turnX, nzA, halfW))
            {
                if (wx == turnX && (eastHash & 0x10) != 0) isBeam = true;
                return true;
            }
            // Segment 2: from nzA to nzB at turnX
            if (IsInSegment(wx, wz, turnX, nzA, turnX, nzB, halfW))
            {
                return true;
            }
            // Segment 3: from turnX to nxB at nzB
            if (IsInSegment(wx, wz, turnX, nzB, nxB, nzB, halfW))
            {
                return true;
            }
        }

        // 2. North Corridor: connects (kcx, kcz) to (kcx, kcz + 1)
        int northHash = PosHash(seed, kcx, kcz, 2001);
        bool hasNorth = (kcx == 0 && kcz == 0) || ((northHash & 0xFF) < (corridorBranchChance * 255f));

        if (hasNorth)
        {
            GetNodePos(seed, kcx, kcz + 1, C, out int nxC, out int nzC);

            // Intermediate orthogonal turn Z
            int turnZ = nzA + (nzC - nzA) / 2 + ((northHash >> 8) % 5 - 2);

            // Segment 1: from nzA to turnZ at nxA
            if (IsInSegment(wx, wz, nxA, nzA, nxA, turnZ, halfW))
            {
                if (wz == turnZ && (northHash & 0x10) != 0) isBeam = true;
                return true;
            }
            // Segment 2: from nxA to nxC at turnZ
            if (IsInSegment(wx, wz, nxA, turnZ, nxC, turnZ, halfW))
            {
                return true;
            }
            // Segment 3: from turnZ to nzC at nxC
            if (IsInSegment(wx, wz, nxC, turnZ, nxC, nzC, halfW))
            {
                return true;
            }
        }

        // 3. Optional Dead-End Cul-de-Sac branch (Film & Lore exploration tension)
        if (deadEndBranchChance > 0f)
        {
            int deadHash = PosHash(seed, kcx, kcz, 3001);
            if (((deadHash & 0xFF) / 255f) < deadEndBranchChance)
            {
                int dir = (deadHash >> 8) & 3; // 0: East, 1: West, 2: North, 3: South
                int deadLen = 8 + (deadHash >> 12) % 10; // 8 to 17 blocks

                int dX0 = nxA, dZ0 = nzA;
                int dX1 = nxA, dZ1 = nzA;

                switch (dir)
                {
                    case 0: dX1 += deadLen; break;
                    case 1: dX1 -= deadLen; break;
                    case 2: dZ1 += deadLen; break;
                    case 3: dZ1 -= deadLen; break;
                }

                if (IsInSegment(wx, wz, dX0, dZ0, dX1, dZ1, halfW))
                    return true;
            }
        }

        return false;
    }

    private static bool IsInSegment(int wx, int wz, int x1, int z1, int x2, int z2, int halfW)
    {
        // Horizontal segment: z1 == z2
        if (z1 == z2)
        {
            if (Mathf.Abs(wz - z1) <= halfW)
            {
                int minX = Mathf.Min(x1, x2) - halfW;
                int maxX = Mathf.Max(x1, x2) + halfW;
                if (wx >= minX && wx <= maxX) return true;
            }
        }
        // Vertical segment: x1 == x2
        else if (x1 == x2)
        {
            if (Mathf.Abs(wx - x1) <= halfW)
            {
                int minZ = Mathf.Min(z1, z2) - halfW;
                int maxZ = Mathf.Max(z1, z2) + halfW;
                if (wz >= minZ && wz <= maxZ) return true;
            }
        }
        return false;
    }

    private static void GetNodePos(int seed, int cx, int cz, int C, out int nx, out int nz)
    {
        if (cx == 0 && cz == 0)
        {
            nx = 3;
            nz = 3;
            return;
        }

        int h = PosHash(seed, cx, cz, 777);
        int jMax = Mathf.Max(2, C / 6);
        int jx = ((h & 0xFF) % (2 * jMax + 1)) - jMax;
        int jz = (((h >> 8) & 0xFF) % (2 * jMax + 1)) - jMax;

        nx = cx * C + (C / 2) + jx;
        nz = cz * C + (C / 2) + jz;
    }
}
