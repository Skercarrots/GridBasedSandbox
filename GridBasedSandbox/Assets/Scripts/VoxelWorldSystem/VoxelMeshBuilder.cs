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

        var vertices       = new List<Vector3>();
        var solidTriangles = new List<int>();
        var waterTriangles = new List<int>();
        var uvs            = new List<Vector2>();
        var colors         = new List<Color>();

        var colVertices  = new List<Vector3>();
        var colTriangles = new List<int>();

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

                    // Debug: ore-only mode — skip non-ore blocks for visualization
                    if (settings.debugOresOnly)
                    {
                        bool isOre = false;
                        if (settings.debugOreBlockIds != null && settings.debugOreBlockIds.Length > 0)
                        {
                            for (int oi = 0; oi < settings.debugOreBlockIds.Length; oi++)
                                if (id == settings.debugOreBlockIds[oi]) { isOre = true; break; }
                        }
                        else { isOre = id >= 11; }
                        if (!isOre) continue;
                    }

                    VoxelBlockType blockDef = registry.GetBlock(id);
                    if (blockDef == null) continue;

                    bool isWater = blockDef.isTransparent;

                    var blockOrigin = new Vector3(lx, ly, lz);
                    Color vertColor = blockDef.IsPlaceholder
                        ? blockDef.ResolvedPlaceholderColor
                        : Color.white;

                    // Water surface height (Minecraft-style, default 0.875 = 14/16 blocks tall)
                    float waterHeight = settings != null ? settings.waterHeight : 0.875f;
                    bool isWaterSurface = false;
                    if (isWater)
                    {
                        byte aboveId = SampleNeighbour(chunk, sampler, lx, ly + 1, lz, settings);
                        isWaterSurface = (aboveId != id);
                    }

                    for (int face = 0; face < 6; face++)
                    {
                        Vector3Int dir = FaceNormals[face];
                        int nx = lx + dir.x;
                        int ny = ly + dir.y;
                        int nz = lz + dir.z;

                        byte neighbourId = SampleNeighbour(chunk, sampler, nx, ny, nz, settings);

                        // Solid opaque neighbour culls the face
                        if (registry.IsSolid(neighbourId)) continue;

                        // Transparent / fluid block culls faces against the same block (e.g. water against water)
                        if (isWater && neighbourId == id) continue;

                        int baseVertex = vertices.Count;

                        for (int v = 0; v < 4; v++)
                        {
                            Vector3 vertOffset = FaceVertices[face, v];
                            // Lower the water surface if this is the topmost block of the water body
                            if (isWater && isWaterSurface && vertOffset.y > 0.5f)
                            {
                                vertOffset.y = waterHeight;
                            }

                            vertices.Add(blockOrigin + vertOffset);
                            colors.Add(vertColor);
                        }

                        if (isWater)
                        {
                            foreach (int t in QuadTriangles)
                                waterTriangles.Add(baseVertex + t);
                        }
                        else
                        {
                            foreach (int t in QuadTriangles)
                                solidTriangles.Add(baseVertex + t);
                        }

                        Vector2Int tile = blockDef.GetTileForFace(face);
                        float uMin = tile.x       * tileSize + UV_INSET;
                        float uMax = (tile.x + 1) * tileSize - UV_INSET;
                        float vMax = 1f - tile.y       * tileSize + UV_INSET;
                        float vMin = 1f - (tile.y + 1) * tileSize - UV_INSET;

                        for (int v = 0; v < 4; v++)
                        {
                            Vector2 corner = FaceUVCorners[face, v];
                            float u = Mathf.Lerp(uMin, uMax, corner.x);

                            // For side water faces, crop the top UV proportionally to avoid stretching
                            float cornerY = corner.y;
                            if (isWater && isWaterSurface && face >= 2 && cornerY > 0.5f)
                            {
                                cornerY = waterHeight;
                            }

                            float vCoord = Mathf.Lerp(vMin, vMax, cornerY);
                            uvs.Add(new Vector2(u, vCoord));
                        }

                        // Only physically solid blocks receive mesh collision geometry
                        if (blockDef.isSolid)
                        {
                            int baseColVertex = colVertices.Count;
                            for (int v = 0; v < 4; v++)
                                colVertices.Add(blockOrigin + FaceVertices[face, v]);

                            foreach (int t in QuadTriangles)
                                colTriangles.Add(baseColVertex + t);
                        }
                    }
                }
            }
        }

        bool hasNonSolid = colVertices.Count != vertices.Count;
        return new MeshData(vertices, solidTriangles, waterTriangles, uvs, colors,
                            hasNonSolid ? colVertices : null,
                            hasNonSolid ? colTriangles : null);
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
    public readonly List<int>     SolidTriangles;
    public readonly List<int>     WaterTriangles;
    public readonly List<Vector2> UVs;
    public readonly List<Color>   Colors;

    public readonly List<Vector3> ColliderVertices;
    public readonly List<int>     ColliderTriangles;

    public MeshData(List<Vector3> v, List<int> solidTri, List<int> waterTri, List<Vector2> u, List<Color> c = null,
                    List<Vector3> colV = null, List<int> colT = null)
    {
        Vertices          = v;
        SolidTriangles    = solidTri;
        WaterTriangles    = waterTri;
        UVs               = u;
        Colors            = c;
        ColliderVertices  = colV;
        ColliderTriangles = colT;
    }

    public bool IsEmpty => Vertices == null || Vertices.Count == 0;
    public bool HasWater => WaterTriangles != null && WaterTriangles.Count > 0;
    public bool HasSolid => SolidTriangles != null && SolidTriangles.Count > 0;
    public bool HasSeparateCollider => ColliderVertices != null;

    /// <summary>Pushes the data into a Unity Mesh with submeshes for solid and water.</summary>
    public void ApplyToMesh(Mesh mesh)
    {
        mesh.Clear();
        if (IsEmpty) return;

        mesh.SetVertices(Vertices);
        mesh.SetUVs(0, UVs);
        if (Colors != null && Colors.Count > 0)
            mesh.SetColors(Colors);

        if (HasWater && HasSolid)
        {
            mesh.subMeshCount = 2;
            mesh.SetTriangles(SolidTriangles, 0);
            mesh.SetTriangles(WaterTriangles, 1);
        }
        else if (HasWater)
        {
            mesh.subMeshCount = 2;
            mesh.SetTriangles(System.Array.Empty<int>(), 0);
            mesh.SetTriangles(WaterTriangles, 1);
        }
        else
        {
            mesh.subMeshCount = 1;
            mesh.SetTriangles(SolidTriangles, 0);
        }

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    /// <summary>Pushes solid-only collider data into a Unity Mesh. Call on the main thread only.</summary>
    public void ApplyToColliderMesh(Mesh colMesh)
    {
        colMesh.Clear();
        if (ColliderVertices == null || ColliderVertices.Count == 0) return;

        colMesh.SetVertices(ColliderVertices);
        colMesh.SetTriangles(ColliderTriangles, 0);
        colMesh.RecalculateBounds();
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