using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  VoxelMeshBuilder  — Pure static class that converts a VoxelChunkData into
//  Unity mesh data using greedy face-culling.
//
//  HOW FACE CULLING WORKS
//  For every solid block, we check each of its 6 neighbours:
//    • If the neighbour is Air (or transparent), we emit that face.
//    • If the neighbour is solid and opaque, we skip the face.
//  This is exactly what Minecraft does — the result is that interior block
//  faces are never drawn, which massively reduces vertex and triangle counts.
//
//  CHANGED FROM THE ORIGINAL
//  The block loop now walks section-by-section (see VoxelChunkData) and skips
//  any section flagged as fully air. On a tall world this is most of the sky
//  above the terrain and most of unlit deep stone with no nearby caves — this
//  is a straight iteration-count win with no change to the resulting mesh.
//
//  CROSS-CHUNK NEIGHBOUR LOOKUP
//  The builder accepts a IChunkNeighbourSampler so it can ask "what block is
//  at local x=-1?" and get the answer from the adjacent loaded chunk.
//  If the neighbour chunk isn't loaded yet, the sampler returns 0 (Air),
//  which means border faces are emitted — conservative but always correct.
//
//  ATLAS UV MAPPING
//  Each face maps to a tile in the atlas texture using the block's GetTileForFace().
//  UV coordinates are computed from tile (col, row) + pixel-perfect inset to
//  avoid bleeding between tiles.
//
//  OUTPUT
//  Returns a MeshData struct (plain arrays) — no Unity API calls needed.
//  Call ApplyToMesh() on the main thread to push it onto a Mesh object.
// ─────────────────────────────────────────────────────────────────────────────

public static class VoxelMeshBuilder
{
    // ── Face directions (matches VoxelBlockType face index 0..5) ─────────────
    // 0=Top  1=Bottom  2=Front(+Z)  3=Back(−Z)  4=Left(−X)  5=Right(+X)
    private static readonly Vector3Int[] FaceNormals =
    {
        Vector3Int.up,    Vector3Int.down,
        Vector3Int.forward, new Vector3Int(0, 0, -1),
        new Vector3Int(-1, 0, 0), Vector3Int.right
    };

    // Four vertices per face in local block space, counter-clockwise when
    // viewed from outside (Unity's left-hand coordinate system).
    // Each row is one face; each pair is a vertex offset from block origin.
    private static readonly Vector3[,] FaceVertices =
    {
        // Top (+Y)
        { new(0,1,0), new(0,1,1), new(1,1,1), new(1,1,0) },
        // Bottom (−Y)
        { new(0,0,0), new(1,0,0), new(1,0,1), new(0,0,1) },
        // Front (+Z)
        { new(0,0,1), new(1,0,1), new(1,1,1), new(0,1,1) },
        // Back (−Z)
        { new(1,0,0), new(0,0,0), new(0,1,0), new(1,1,0) },
        // Left (−X)
        { new(0,0,0), new(0,0,1), new(0,1,1), new(0,1,0) },
        // Right (+X)
        { new(1,0,1), new(1,0,0), new(1,1,0), new(1,1,1) },
    };

    // UV corner assignment per face, matching each face's actual vertex winding
    // in FaceVertices (0,0)=bottom-left … (1,1)=top-right of the tile.
    private static readonly Vector2[,] FaceUVCorners =
    {
        // Top
        { new(0,0), new(0,1), new(1,1), new(1,0) },
        // Bottom
        { new(0,0), new(1,0), new(1,1), new(0,1) },
        // Front (+Z)
        { new(0,0), new(1,0), new(1,1), new(0,1) },
        // Back (−Z)
        { new(1,0), new(0,0), new(0,1), new(1,1) },
        // Left (−X)
        { new(0,0), new(1,0), new(1,1), new(0,1) },
        // Right (+X)
        { new(1,0), new(0,0), new(0,1), new(1,1) },
    };

    // Triangle indices for two triangles forming one quad (relative to quad base)
    private static readonly int[] QuadTriangles = { 0, 1, 2, 0, 2, 3 };

    // ── Inset to avoid atlas bleeding (in normalised UV units per tile) ───────
    private const float UV_INSET = 0.001f;

    // ─────────────────────────────────────────────────────────────────────────
    //  Public entry point
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds mesh data for <paramref name="chunk"/>.
    /// Safe to call on a background thread — no Unity API calls are made.
    /// </summary>
    public static MeshData Build(
        VoxelChunkData         chunk,
        VoxelWorldSettings     settings,
        IChunkNeighbourSampler sampler)
    {
        var registry = settings.blockRegistry;
        float tileSize = settings.TileUVSize;

        var vertices  = new List<Vector3>();
        var triangles = new List<int>();
        var uvs       = new List<Vector2>();

        for (int lx = 0; lx < chunk.Width; lx++)
        for (int lz = 0; lz < chunk.Width; lz++)
        {
            for (int section = 0; section < chunk.SectionCount; section++)
            {
                if (!chunk.SectionHasBlocks(section)) continue;

                int lyStart = section * VoxelChunkData.SECTION_HEIGHT;
                int lyEnd   = Mathf.Min(lyStart + VoxelChunkData.SECTION_HEIGHT, chunk.Height);

                for (int ly = lyStart; ly < lyEnd; ly++)
                {
                    byte id = chunk.GetBlock(lx, ly, lz);
                    if (id == 0) continue;   // air — skip

                    VoxelBlockType blockDef = registry.GetBlock(id);
                    if (blockDef == null || !blockDef.isSolid) continue;

                    var blockOrigin = new Vector3(lx, ly, lz);

                    for (int face = 0; face < 6; face++)
                    {
                        Vector3Int dir = FaceNormals[face];
                        int nx = lx + dir.x;
                        int ny = ly + dir.y;
                        int nz = lz + dir.z;

                        byte neighbourId = SampleNeighbour(chunk, sampler, nx, ny, nz, settings);
                        if (registry.IsSolid(neighbourId)) continue;

                        int baseVertex = vertices.Count;

                        for (int v = 0; v < 4; v++)
                            vertices.Add(blockOrigin + FaceVertices[face, v]);

                        foreach (int t in QuadTriangles)
                            triangles.Add(baseVertex + t);

                        Vector2Int tile = blockDef.GetTileForFace(face);
                        float uMin = tile.x       * tileSize + UV_INSET;
                        float uMax = (tile.x + 1) * tileSize - UV_INSET;
                        float vMax = 1f - tile.y       * tileSize + UV_INSET;
                        float vMin = 1f - (tile.y + 1) * tileSize - UV_INSET;

                        for (int v = 0; v < 4; v++)
                        {
                            Vector2 corner = FaceUVCorners[face, v];
                            float u = Mathf.Lerp(uMin, uMax, corner.x);
                            float vCoord = Mathf.Lerp(vMin, vMax, corner.y);
                            uvs.Add(new Vector2(u, vCoord));
                        }
                    }
                }
            }
        }

        return new MeshData(vertices, triangles, uvs);
    }

    // ── Neighbour sampling helper ─────────────────────────────────────────────

    private static byte SampleNeighbour(
        VoxelChunkData         chunk,
        IChunkNeighbourSampler sampler,
        int lx, int ly, int lz,
        VoxelWorldSettings     settings)
    {
        if (ly < 0 || ly >= chunk.Height) return 0;

        if (lx >= 0 && lx < chunk.Width && lz >= 0 && lz < chunk.Width)
            return chunk.GetBlock(lx, ly, lz);

        if (sampler == null) return 0;

        int wx = chunk.WorldOriginX + lx;
        int wy = settings.LocalYToWorld(ly);
        int wz = chunk.WorldOriginZ + lz;
        return sampler.GetBlockAt(wx, wy, wz);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  MeshData  — Plain data struct, no Unity API.
//  Build it on a thread; call ApplyToMesh() on the main thread.
// ─────────────────────────────────────────────────────────────────────────────

public readonly struct MeshData
{
    public readonly List<Vector3> Vertices;
    public readonly List<int>     Triangles;
    public readonly List<Vector2> UVs;

    public MeshData(List<Vector3> v, List<int> t, List<Vector2> u)
    {
        Vertices  = v;
        Triangles = t;
        UVs       = u;
    }

    public bool IsEmpty => Vertices == null || Vertices.Count == 0;

    /// <summary>Pushes the data into a Unity Mesh. Call on the main thread only.</summary>
    public void ApplyToMesh(Mesh mesh)
    {
        mesh.Clear();
        if (IsEmpty) return;

        mesh.SetVertices(Vertices);
        mesh.SetTriangles(Triangles, 0);
        mesh.SetUVs(0, UVs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  IChunkNeighbourSampler  — Abstraction that lets the mesh builder query
//  block data from neighbouring chunks without knowing about the chunk manager.
// ─────────────────────────────────────────────────────────────────────────────

public interface IChunkNeighbourSampler
{
    /// <summary>Returns the block id at world-space block coordinate (wx, wy, wz).</summary>
    byte GetBlockAt(int wx, int wy, int wz);
}