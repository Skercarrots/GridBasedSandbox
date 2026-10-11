using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  BackroomsLayoutStrategy — Pluggable floor-plan algorithm for a Backrooms zone.
//
//  A zone (BackroomsZoneProfile) references one strategy asset; the generator asks
//  the strategy, column by column, "what stands at this (x, z)?". Swapping the
//  strategy swaps the whole architectural character of a zone without touching
//  the generator. Write a new one by subclassing and adding [CreateAssetMenu].
//
//  THREADING
//  Evaluate() runs on background worker threads. It must be deterministic for a
//  given (seed, wx, wz) and must only read plain serialized fields — no Unity API.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>What the layout wants at one (x, z) column. The generator turns this into blocks.</summary>
public struct BackroomsColumnSpec
{
    /// <summary>Marks "wall fills the whole free height" in <see cref="wallHeight"/>.</summary>
    public const int FullHeight = -1;

    /// <summary>0 = open floor, FullHeight = solid to the ceiling, N = N blocks tall (low wall).</summary>
    public int wallHeight;

    /// <summary>True for low walls: the top block uses the half-wall cap block.</summary>
    public bool wallIsHalf;

    /// <summary>Blocks the ceiling is lowered by at this column (soffit / drop beam). 0 = none.</summary>
    public int ceilingDrop;

    /// <summary>Vertical air clearance below the wall (e.g. 2 for a doorway opening with a lintel above).</summary>
    public int wallBottomOffset;

    /// <summary>Custom block override for this column (0 = use zone wallpaper).</summary>
    public byte customWallBlock;

    /// <summary>True if this column is part of an arterial or branch corridor rather than a room interior.</summary>
    public bool isCorridor;

    public static BackroomsColumnSpec Open => default;

    public static BackroomsColumnSpec Corridor => new BackroomsColumnSpec { isCorridor = true };

    public static BackroomsColumnSpec FullWall => new BackroomsColumnSpec { wallHeight = FullHeight };

    public static BackroomsColumnSpec Doorway(int doorHeight = 2) => new BackroomsColumnSpec
    {
        wallHeight = FullHeight,
        wallBottomOffset = doorHeight,
        isCorridor = true
    };
}

/// <summary>Everything a strategy needs to evaluate one column.</summary>
public struct BackroomsLayoutContext
{
    public int wx, wz;            // world block coordinates
    public int seed;              // dimension seed
    public int sectorSize;        // macro sector edge length in blocks
    public int sectorX, sectorZ;  // macro sector indices
    public int localX, localZ;    // coordinates inside the sector, [0, sectorSize)
    public BackroomsZoneProfile zone;
}

public abstract class BackroomsLayoutStrategy : ScriptableObject
{
    /// <summary>Optional one-time main-thread hook (bake anything Evaluate() needs off-thread).</summary>
    public virtual void Prepare() { }

    /// <summary>Decides what stands at one column. Thread-safe, deterministic, allocation-free.</summary>
    public abstract BackroomsColumnSpec Evaluate(in BackroomsLayoutContext ctx);

    // ── Shared helpers for implementations ────────────────────────────────

    protected static int Mod(int val, int m)
    {
        int r = val % m;
        return r < 0 ? r + m : r;
    }

    protected static int FloorDiv(int val, int m)
    {
        return val >= 0 ? val / m : (val - m + 1) / m;
    }

    /// <summary>Deterministic non-negative hash.</summary>
    protected static int PosHash(int a, int b, int c, int d)
    {
        return DeterministicRng.Hash(a, b, c, d) & 0x7FFFFFFF;
    }

    /// <summary>Deterministic float in [0, 1).</summary>
    protected static float Hash01(int a, int b, int c, int d)
    {
        return (DeterministicRng.Hash(a, b, c, d) & 0xFFFF) / 65536f;
    }
}

