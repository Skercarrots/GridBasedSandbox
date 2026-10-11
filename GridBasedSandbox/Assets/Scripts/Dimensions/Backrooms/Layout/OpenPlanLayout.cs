using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  OpenPlanLayout — Open-plan office floor plan (reference dossier DOC-N0-LIMINAL-84).
//
//  Each macro sector is split into a jittered grid of big HALLS. Boundaries between
//  halls are, per segment, one of:
//    • Full wall   — full height, but ALWAYS with an opening (so every pair of
//                    adjacent halls is connected: no sealed areas by construction)
//    • Half wall   — low counter-height wall, also with an opening
//    • Open        — no wall at all, only a soffit beam
//  Openings and open boundaries get a drywall-drop beam (ceilingDrop).
//
//  Inside a hall, a world-aligned lattice places 2x2 megapillars (kept clear of
//  walls so they never block a route) and free-standing half-wall counters.
//
//  CONNECTIVITY GUARANTEE
//  Boundary lines at a sector edge (local 0) are owned by the sector on the
//  positive side, so both sectors agree on them. Pillars keep `pillarClearance`
//  from every line; counters keep >= 4 blocks from every line. Nothing in here
//  can seal an opening.
//
//  All tuning comes from BackroomsZoneProfile — this asset itself has no fields.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "Layout_OpenPlan", menuName = "VoxelWorld/Backrooms/Layouts/Open Plan")]
public class OpenPlanLayout : BackroomsLayoutStrategy
{
    private const int AxisX = 0;
    private const int AxisZ = 1;

    private enum SegmentType { Full, Half, Open }

    /// <summary>Boundary-line positions of one sector along both axes. Line 0 = sector edge, line n = next sector edge.</summary>
    private readonly struct LineGrid
    {
        public readonly int n, cellW, sectorSize, jitterMax, seed, sectorKey;

        public LineGrid(int n, int cellW, int sectorSize, int jitterMax, int seed, int sectorKey)
        {
            this.n = n;
            this.cellW = cellW;
            this.sectorSize = sectorSize;
            this.jitterMax = jitterMax;
            this.seed = seed;
            this.sectorKey = sectorKey;
        }

        /// <summary>Local position of boundary line i on the given axis.</summary>
        public int Line(int i, int axis)
        {
            if (i <= 0) return 0;
            if (i >= n) return sectorSize;
            if (jitterMax <= 0) return i * cellW;

            int jitter = (DeterministicRng.Hash(seed, sectorKey, axis * 64 + i, 31) & 0x7FFFFFFF) % (2 * jitterMax + 1) - jitterMax;
            return i * cellW + jitter;
        }

        /// <summary>Index of the hall cell containing local coordinate coord.</summary>
        public int FindCell(int coord, int axis)
        {
            int cell = Mathf.Min(n - 1, coord / cellW);
            while (cell > 0 && coord < Line(cell, axis)) cell--;
            while (cell < n - 1 && coord >= Line(cell + 1, axis)) cell++;
            return cell;
        }

        /// <summary>True if any boundary line lies within [lo, hi] (local coordinates).</summary>
        public bool AnyLineWithin(int axis, int lo, int hi)
        {
            for (int i = 0; i <= n; i++)
            {
                int v = Line(i, axis);
                if (v >= lo && v <= hi) return true;
            }
            return false;
        }
    }

    public override BackroomsColumnSpec Evaluate(in BackroomsLayoutContext c)
    {
        BackroomsZoneProfile z = c.zone;
        if (z == null) return BackroomsColumnSpec.Open;

        int S = c.sectorSize;
        int hall = Mathf.Max(6, z.hallSize);
        int n = Mathf.Clamp(Mathf.RoundToInt(S / (float)hall), 1, 8);
        int cellW = Mathf.Max(1, S / n);
        int jitterMax = Mathf.FloorToInt(cellW * z.hallSizeJitter);
        int sectorKey = DeterministicRng.Hash(c.sectorX, c.sectorZ);

        var grid = new LineGrid(n, cellW, S, jitterMax, c.seed, sectorKey);

        int thick = Mathf.Max(1, z.beamThickness);
        int margin = Mathf.Max(2, thick + 1);

        int lx = c.localX;
        int lz = c.localZ;

        int ci = grid.FindCell(lx, AxisX);
        int cj = grid.FindCell(lz, AxisZ);

        int x0 = grid.Line(ci, AxisX), x1 = grid.Line(ci + 1, AxisX);
        int z0 = grid.Line(cj, AxisZ), z1 = grid.Line(cj + 1, AxisZ);
        int dx = lx - x0;
        int dz = lz - z0;

        BackroomsColumnSpec spec = default;

        // Boundary on this hall's low-X edge (a wall running along Z) …
        if (dx < thick)
            ApplyBoundary(ref spec, c, z, AxisX, ci, cj, lz - z0, z1 - z0, dx, margin);

        // … and on its low-Z edge (a wall running along X).
        if (dz < thick)
            ApplyBoundary(ref spec, c, z, AxisZ, cj, ci, lx - x0, x1 - x0, dz, margin);

        // Hall interior features only where there is neither wall nor beam
        if (spec.wallHeight == 0 && spec.ceilingDrop == 0)
        {
            int clear = Mathf.Max(z.pillarClearance, thick + 1);

            if (IsPillar(c, z, grid, lx, lz, clear))
            {
                spec.wallHeight = BackroomsColumnSpec.FullHeight;
                spec.wallIsHalf = false;
            }
            else if (IsIslandColumn(c, z, sectorKey, ci, cj, x0, x1, z0, z1, lx, lz, clear))
            {
                spec.wallHeight = Mathf.Max(1, z.halfWallHeight);
                spec.wallIsHalf = true;
            }
        }

        return spec;
    }

    // ── Boundaries ─────────────────────────────────────────────────────────

    private static void ApplyBoundary(ref BackroomsColumnSpec spec, in BackroomsLayoutContext c, BackroomsZoneProfile z,
                                      int axis, int lineIdx, int segIdx, int offsetAlong, int segLen, int depth, int margin)
    {
        SegmentType type = GetSegmentType(c, z, axis, lineIdx, segIdx);

        if (type == SegmentType.Open)
        {
            spec.ceilingDrop = Mathf.Max(spec.ceilingDrop, z.beamDrop);
            return;
        }

        GetOpening(c, axis, lineIdx, segIdx, segLen, margin, z, out int openOffset, out int openWidth);
        bool inOpening = offsetAlong >= openOffset && offsetAlong < openOffset + openWidth;

        if (inOpening)
        {
            // Wide passage framed by a soffit beam, no door leaf
            spec.ceilingDrop = Mathf.Max(spec.ceilingDrop, z.beamDrop);
            return;
        }

        // Only the first column of a boundary strip is solid; the rest of the strip stays open
        if (depth != 0) return;

        if (type == SegmentType.Full)
        {
            spec.wallHeight = BackroomsColumnSpec.FullHeight;
            spec.wallIsHalf = false;
        }
        else if (spec.wallHeight != BackroomsColumnSpec.FullHeight)
        {
            spec.wallHeight = Mathf.Max(spec.wallHeight, Mathf.Max(1, z.halfWallHeight));
            spec.wallIsHalf = true;
        }
    }

    private static SegmentType GetSegmentType(in BackroomsLayoutContext c, BackroomsZoneProfile z, int axis, int lineIdx, int segIdx)
    {
        float roll = (DeterministicRng.Hash(c.seed, c.sectorX, c.sectorZ, axis * 1024 + lineIdx, segIdx * 8 + 1) & 0xFFFF) / 65536f;

        if (roll < z.fullWallProbability) return SegmentType.Full;
        if (roll < z.fullWallProbability + z.halfWallProbability) return SegmentType.Half;
        return SegmentType.Open;
    }

    private static void GetOpening(in BackroomsLayoutContext c, int axis, int lineIdx, int segIdx, int segLen, int margin,
                                   BackroomsZoneProfile z, out int offset, out int width)
    {
        int maxWidth = Mathf.Max(1, segLen - 2 * margin);
        int wMin = Mathf.Clamp(z.openingWidthMin, 1, maxWidth);
        int wMax = Mathf.Clamp(z.openingWidthMax, wMin, maxWidth);

        int hashW = DeterministicRng.Hash(c.seed, c.sectorX, c.sectorZ, axis * 1024 + lineIdx, segIdx * 8 + 2) & 0x7FFFFFFF;
        int hashO = DeterministicRng.Hash(c.seed, c.sectorX, c.sectorZ, axis * 1024 + lineIdx, segIdx * 8 + 3) & 0x7FFFFFFF;

        width = wMin + hashW % (wMax - wMin + 1);

        int range = Mathf.Max(1, segLen - 2 * margin - width + 1);
        offset = margin + hashO % range;
    }

    // ── Interior features ──────────────────────────────────────────────────

    private static bool IsPillar(in BackroomsLayoutContext c, BackroomsZoneProfile z, in LineGrid grid, int lx, int lz, int clear)
    {
        int size = Mathf.Max(1, z.pillarSize);
        int spacing = Mathf.Max(size + 1, z.pillarSpacing);

        int px = Mod(c.wx, spacing);
        int pz = Mod(c.wz, spacing);
        if (px >= size || pz >= size) return false;

        int gx = FloorDiv(c.wx, spacing);
        int gz = FloorDiv(c.wz, spacing);
        float roll = (DeterministicRng.Hash(c.seed, gx, gz, 4242) & 0xFFFF) / 65536f;
        if (roll >= z.pillarProbability) return false;

        // The whole pillar box (not just this column) must keep its distance from every wall line,
        // otherwise a pillar could be cut in half or squeeze a passage.
        int originX = lx - px;
        int originZ = lz - pz;

        if (grid.AnyLineWithin(AxisX, originX - clear, originX + size - 1 + clear)) return false;
        if (grid.AnyLineWithin(AxisZ, originZ - clear, originZ + size - 1 + clear)) return false;

        return true;
    }

    private static bool IsIslandColumn(in BackroomsLayoutContext c, BackroomsZoneProfile z, int sectorKey, int ci, int cj,
                                       int x0, int x1, int z0, int z1, int lx, int lz, int clear)
    {
        if (z.islandProbability <= 0f) return false;

        int cellKey = ci * 64 + cj;
        float roll = (DeterministicRng.Hash(c.seed, sectorKey, cellKey, 9001) & 0xFFFF) / 65536f;
        if (roll >= z.islandProbability) return false;

        // Free interior of the hall, kept well away from the boundaries
        int inset = Mathf.Max(4, clear + 1);
        int ix0 = x0 + inset, ix1 = x1 - 1 - inset;
        int iz0 = z0 + inset, iz1 = z1 - 1 - inset;
        int width = ix1 - ix0 + 1;
        int depth = iz1 - iz0 + 1;
        if (width < 3 || depth < 3) return false;

        int h1 = DeterministicRng.Hash(c.seed, sectorKey, cellKey, 9002) & 0x7FFFFFFF;
        int h2 = DeterministicRng.Hash(c.seed, sectorKey, cellKey, 9003) & 0x7FFFFFFF;
        int h3 = DeterministicRng.Hash(c.seed, sectorKey, cellKey, 9004) & 0x7FFFFFFF;
        int h4 = DeterministicRng.Hash(c.seed, sectorKey, cellKey, 9005) & 0x7FFFFFFF;

        bool alongX = (h1 & 1) == 0;
        bool lShaped = (h1 & 2) != 0;
        bool legAtFarEnd = (h1 & 4) != 0;

        int lenMin = Mathf.Max(3, z.islandLengthMin);
        int lenMax = Mathf.Max(lenMin, z.islandLengthMax);
        int length = lenMin + h2 % (lenMax - lenMin + 1);
        length = Mathf.Min(length, alongX ? width : depth);

        int startX = ix0 + h3 % Mathf.Max(1, width - (alongX ? length : 1) + 1);
        int startZ = iz0 + h4 % Mathf.Max(1, depth - (alongX ? 1 : length) + 1);

        // Main bar
        if (alongX)
        {
            if (lz == startZ && lx >= startX && lx < startX + length) return true;
        }
        else
        {
            if (lx == startX && lz >= startZ && lz < startZ + length) return true;
        }

        // Optional perpendicular leg → L-shaped reception counter
        if (lShaped)
        {
            if (alongX)
            {
                int legX = legAtFarEnd ? startX + length - 1 : startX;
                int legLen = Mathf.Min(Mathf.Max(2, length / 2), iz1 - startZ + 1);
                if (lx == legX && lz >= startZ && lz < startZ + legLen) return true;
            }
            else
            {
                int legZ = legAtFarEnd ? startZ + length - 1 : startZ;
                int legLen = Mathf.Min(Mathf.Max(2, length / 2), ix1 - startX + 1);
                if (lz == legZ && lx >= startX && lx < startX + legLen) return true;
            }
        }

        return false;
    }
}

