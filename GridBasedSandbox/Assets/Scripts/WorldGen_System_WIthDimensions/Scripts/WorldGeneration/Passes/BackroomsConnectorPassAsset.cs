using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
//  BackroomsConnectorPassAsset  —  Generation pass that punches guaranteed
//  doorway openings on every chunk edge, ensuring navigability between chunks.
//
//  WHY THIS EXISTS
//  BackroomsLayout carves structures within a single chunk.  Without any
//  cross-chunk coordination, neighbouring chunks might generate back-to-back
//  solid walls, creating impassable barriers.
//
//  HOW IT WORKS
//  After the main generator fills the chunk, this pass:
//    1. Picks a deterministic X position on the North and South chunk edges.
//    2. Picks a deterministic Z position on the East and West chunk edges.
//    3. Carves a doorway (configurable width × height) centred on that position.
//
//  CONSISTENCY GUARANTEE
//  The doorway position on the North edge of chunk (cx, cz) uses the same
//  hash as the South edge of chunk (cx, cz-1), so openings always align across
//  chunk boundaries.
//
//  Create via: Assets > Create > WorldGen > Passes > Backrooms Connector
// ═══════════════════════════════════════════════════════════════════════════════

[CreateAssetMenu(
    fileName = "BackroomsConnectorPassAsset",
    menuName = "WorldGen/Passes/Backrooms Connector")]
public class BackroomsConnectorPassAsset : BaseGenerationPassAsset
{
    [Header("Doorway Dimensions")]
    [Tooltip("Width of the carved doorway in blocks.")]
    [Range(1, 6)] public int doorwayWidth  = 2;

    [Tooltip("Height of the carved doorway in blocks (not counting floor/ceiling slabs).")]
    [Range(1, 5)] public int doorwayHeight = 3;

    [Header("Block IDs")]
    [Tooltip("Air block ID used when carving the doorway.")]
    public byte airBlockId = 0;

    [Tooltip("Floor block ID placed at the doorway threshold.")]
    public byte floorBlockId = 10;

    [Tooltip("Ceiling block ID placed above the doorway.")]
    public byte ceilingBlockId = 11;

    public override IGenerationPass CreatePass()
        => new BackroomsConnectorPass(doorwayWidth, doorwayHeight, airBlockId, floorBlockId, ceilingBlockId);
}

// ─────────────────────────────────────────────────────────────────────────────

public sealed class BackroomsConnectorPass : IGenerationPass
{
    public string PassName => "Backrooms Connector";

    private readonly int  _doorW;
    private readonly int  _doorH;
    private readonly byte _airId;
    private readonly byte _floorId;
    private readonly byte _ceilId;

    public BackroomsConnectorPass(int doorW, int doorH, byte air, byte floor, byte ceil)
    {
        _doorW   = doorW;
        _doorH   = doorH;
        _airId   = air;
        _floorId = floor;
        _ceilId  = ceil;
    }

    public void Apply(VoxelChunkData data, VoxelWorldSettings settings, int seed)
    {
        int cx = data.ChunkCoord.x;
        int cz = data.ChunkCoord.y;
        int w  = data.Width;
        int h  = data.Height;

        // ── Doorway Y range ────────────────────────────────────────────────
        // Place doorway at the floor (local Y = 0)
        int floorLocalY = 0;
        int ceilLocalY  = Mathf.Min(floorLocalY + _doorH + 1, h - 1);

        // ── North edge (lz = 0) ────────────────────────────────────────────
        // Hash uses (cx, cz-1) for the south edge of the chunk to the north,
        // so both chunks carve the same X position.
        int northX = GetDoorwayCenter(cx, cz - 1, seed, channel: 0, maxCenter: w - 1);
        CarveDoorwayAlongX(data, northX, 0, floorLocalY, ceilLocalY, w);

        // ── South edge (lz = w-1) ─────────────────────────────────────────
        int southX = GetDoorwayCenter(cx, cz, seed, channel: 0, maxCenter: w - 1);
        CarveDoorwayAlongX(data, southX, w - 1, floorLocalY, ceilLocalY, w);

        // ── West edge (lx = 0) ────────────────────────────────────────────
        int westZ = GetDoorwayCenter(cx - 1, cz, seed, channel: 1, maxCenter: w - 1);
        CarveDoorwayAlongZ(data, 0, westZ, floorLocalY, ceilLocalY, w);

        // ── East edge (lx = w-1) ──────────────────────────────────────────
        int eastZ = GetDoorwayCenter(cx, cz, seed, channel: 1, maxCenter: w - 1);
        CarveDoorwayAlongZ(data, w - 1, eastZ, floorLocalY, ceilLocalY, w);
    }

    // ── Deterministic doorway centre ──────────────────────────────────────────

    /// <summary>
    /// Returns a centre position that is consistent between adjacent chunks.
    /// Two adjacent chunks query the same (edgeChunkX, edgeChunkZ) so the
    /// openings always align.
    /// </summary>
    private int GetDoorwayCenter(int edgeCx, int edgeCz, int seed, int channel, int maxCenter)
    {
        int margin = _doorW / 2 + 2; // keep doorway away from corners
        return WorldGenNoiseUtils.HashToInt(
            edgeCx * 7 + channel * 1000,
            edgeCz * 13,
            seed,
            margin,
            maxCenter - margin);
    }

    // ── Carving helpers ───────────────────────────────────────────────────────

    /// <summary>Carves a doorway on a Z-constant edge (North/South).</summary>
    private void CarveDoorwayAlongX(
        VoxelChunkData data,
        int centreX, int fixedZ,
        int floorY, int ceilY, int w)
    {
        int half = _doorW / 2;
        for (int lx = centreX - half; lx <= centreX + half; lx++)
        {
            if (lx < 0 || lx >= w) continue;

            data.SetBlock(lx, floorY, fixedZ, _floorId);
            data.SetBlock(lx, ceilY,  fixedZ, _ceilId);

            for (int ly = floorY + 1; ly < ceilY; ly++)
                data.SetBlock(lx, ly, fixedZ, _airId);
        }
    }

    /// <summary>Carves a doorway on an X-constant edge (East/West).</summary>
    private void CarveDoorwayAlongZ(
        VoxelChunkData data,
        int fixedX, int centreZ,
        int floorY, int ceilY, int w)
    {
        int half = _doorW / 2;
        for (int lz = centreZ - half; lz <= centreZ + half; lz++)
        {
            if (lz < 0 || lz >= w) continue;

            data.SetBlock(fixedX, floorY, lz, _floorId);
            data.SetBlock(fixedX, ceilY,  lz, _ceilId);

            for (int ly = floorY + 1; ly < ceilY; ly++)
                data.SetBlock(fixedX, ly, lz, _airId);
        }
    }
}
