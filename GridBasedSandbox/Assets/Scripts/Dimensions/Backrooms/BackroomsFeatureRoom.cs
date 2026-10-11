using System;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  BackroomsFeatureRoom — A rare, hand-authored landmark ("sala única") stamped
//  into the Backrooms. Fully data-driven: a landmark is a footprint plus an
//  ordered list of BRUSHES edited in the Inspector. No code needed to add one.
//
//  A brush fills a box (or a repeated lattice of boxes) with one block — or with
//  AIR when no block is assigned, which is how you carve space. Brushes run top to
//  bottom, so later brushes paint over earlier ones:
//    1. carve the volume, 2. restore the ceiling, 3. lay a light grid, 4. add pillars…
//
//  Vertical placement is relative to the dimension's floor, so landmarks keep
//  working if the generator's ceiling height changes:
//    FullFreeHeight — every free air layer between floor and ceiling
//    FreeVolume     — a custom slice (offset.y / size.y, 0 = first air layer)
//    CeilingLayer   — the ceiling tile layer
//    FloorLayer     — the carpet layer
//
//  The reference dossier asks for furniture-free spaces, so the bundled landmarks
//  are architecture only (halls, counters, beams) — but nothing stops you from
//  painting anything you like.
// ─────────────────────────────────────────────────────────────────────────────

public enum LandmarkLayer
{
    FullFreeHeight,
    FreeVolume,
    CeilingLayer,
    FloorLayer
}

[Serializable]
public class LandmarkBrush
{
    [Tooltip("Label shown in the Inspector list.")]
    public string label = "Brush";

    public LandmarkLayer layer = LandmarkLayer.FullFreeHeight;

    [Tooltip("Offset of the box from the landmark origin. X/Z in blocks; Y (FreeVolume only) above the first air layer.")]
    public Vector3Int offset = Vector3Int.zero;

    [Tooltip("Box size. X/Z in blocks; Y is used by FreeVolume only.")]
    public Vector3Int size = new Vector3Int(1, 1, 1);

    [Tooltip("How many copies along X / Z (1,1 = a single box).")]
    public Vector2Int repeat = new Vector2Int(1, 1);

    [Tooltip("Distance in blocks from one copy's origin to the next, along X / Z.")]
    public Vector2Int spacing = new Vector2Int(4, 4);

    [Tooltip("Block to paint. Leave EMPTY to paint air (carve).")]
    public VoxelBlockType block;
}

[CreateAssetMenu(fileName = "Room_Landmark", menuName = "VoxelWorld/Backrooms/Feature Room (Landmark)")]
public class BackroomsFeatureRoom : ScriptableObject
{
    [Header("Identity")]
    public string roomName = "Landmark";

    [Header("Footprint (blocks)")]
    [Tooltip("Bounding box on X. Used for placement only — brushes may paint anywhere.")]
    [Range(6, 48)] public int width = 24;

    [Tooltip("Bounding box on Z.")]
    [Range(6, 48)] public int length = 24;

    [Header("Rarity")]
    [Tooltip("Relative weight among the generator's landmarks.")]
    [Range(0.1f, 5.0f)] public float rarityWeight = 1.0f;

    [Header("Brushes (applied in order)")]
    public LandmarkBrush[] brushes = new LandmarkBrush[0];

    // Block IDs baked on the main thread (Generate() runs on worker threads)
    [NonSerialized] private byte[] _blockIds;

    /// <summary>Bakes brush block IDs. Main thread only; called by the generator's Prepare().</summary>
    public void Bake()
    {
        int count = brushes != null ? brushes.Length : 0;
        _blockIds = new byte[count];
        for (int i = 0; i < count; i++)
            _blockIds[i] = brushes[i] != null && brushes[i].block != null ? brushes[i].block.blockId : (byte)0;
    }

    /// <summary>
    /// Paints the landmark into a chunk. Deterministic and chunk-border safe: every chunk overlapping the
    /// footprint paints only its own slice. carpetLocalY = chunk-local Y of the carpet layer.
    /// </summary>
    public void Stamp(VoxelChunkData chunk, int roomOriginX, int roomOriginZ, int carpetLocalY, int freeHeight)
    {
        if (brushes == null || brushes.Length == 0) return;
        if (_blockIds == null || _blockIds.Length != brushes.Length) Bake();

        int firstAirY = carpetLocalY + 1;
        int ceilingY = carpetLocalY + 1 + freeHeight;

        for (int b = 0; b < brushes.Length; b++)
        {
            LandmarkBrush brush = brushes[b];
            if (brush == null) continue;

            int yMin, yMax;
            switch (brush.layer)
            {
                case LandmarkLayer.FullFreeHeight:
                    yMin = firstAirY;
                    yMax = ceilingY - 1;
                    break;
                case LandmarkLayer.FreeVolume:
                    yMin = firstAirY + brush.offset.y;
                    yMax = yMin + Mathf.Max(1, brush.size.y) - 1;
                    break;
                case LandmarkLayer.CeilingLayer:
                    yMin = yMax = ceilingY;
                    break;
                default: // FloorLayer
                    yMin = yMax = carpetLocalY;
                    break;
            }

            int repeatX = Mathf.Max(1, brush.repeat.x);
            int repeatZ = Mathf.Max(1, brush.repeat.y);
            int sizeX = Mathf.Max(1, brush.size.x);
            int sizeZ = Mathf.Max(1, brush.size.z);

            for (int rx = 0; rx < repeatX; rx++)
            {
                for (int rz = 0; rz < repeatZ; rz++)
                {
                    int bx0 = roomOriginX + brush.offset.x + rx * brush.spacing.x;
                    int bz0 = roomOriginZ + brush.offset.z + rz * brush.spacing.y;

                    FillBox(chunk, bx0, bx0 + sizeX, bz0, bz0 + sizeZ, yMin, yMax, _blockIds[b]);
                }
            }
        }
    }

    /// <summary>Fills the part of [x0,x1) × [z0,z1) × [yMin,yMax] that falls inside this chunk.</summary>
    private static void FillBox(VoxelChunkData chunk, int x0, int x1, int z0, int z1, int yMin, int yMax, byte id)
    {
        int chunkMinX = chunk.WorldOriginX;
        int chunkMinZ = chunk.WorldOriginZ;

        int startX = Mathf.Max(x0, chunkMinX);
        int endX   = Mathf.Min(x1, chunkMinX + chunk.Width);
        int startZ = Mathf.Max(z0, chunkMinZ);
        int endZ   = Mathf.Min(z1, chunkMinZ + chunk.Width);
        if (startX >= endX || startZ >= endZ) return;

        int y0 = Mathf.Max(0, yMin);
        int y1 = Mathf.Min(chunk.Height - 1, yMax);

        for (int wx = startX; wx < endX; wx++)
        {
            for (int wz = startZ; wz < endZ; wz++)
            {
                for (int y = y0; y <= y1; y++)
                    chunk.SetBlock(wx - chunkMinX, y, wz - chunkMinZ, id);
            }
        }
    }
}
