using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  TreeRecipe — Procedural generator for trees and giant plants.
//  Create via: Assets > Create > GridBasedSandbox/Voxel World/Structures/Tree Recipe
//
//  Produces diverse tree silhouettes:
//  • Oak: round ellipsoid canopy, low-to-medium trunk
//  • Birch: tall thin trunk, elongated canopy
//  • Spruce / Conifer: tall trunk with tiered conical leaf layers
//  • Savanna / Acacia: leaning trunk with flat umbrella canopy
//  • Tall Fantasy: thick multi-block trunk with clustered leaf spheres
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "TreeRecipe_New", menuName = "GridBasedSandbox/Voxel World/Structures/Tree Recipe")]
public class TreeRecipe : StructureRecipe
{
    public enum CanopyShape
    {
        [Tooltip("Classic rounded tree canopy.")]
        Ellipsoid,

        [Tooltip("Tiered conical layers (pine, spruce, fir).")]
        LayeredCone,

        [Tooltip("Multiple organic leaf blobs scattered around branches and top.")]
        ClusteredBlobs,

        [Tooltip("Flat-topped umbrella shape (savanna, acacia).")]
        Umbrella
    }

    [Header("Trunk")]
    [Tooltip("Minimum trunk height in blocks.")]
    [Range(3, 30)] public int trunkMinHeight = 4;

    [Tooltip("Maximum trunk height in blocks.")]
    [Range(3, 30)] public int trunkMaxHeight = 7;

    [Tooltip("Trunk thickness: 1 = 1×1 column, 2 = 2×2 thick, 3 = 3×3 massive.")]
    [Range(1, 3)] public int trunkThickness = 1;

    [Tooltip("Trunk taper: reduces thickness towards the top.")]
    [Range(0f, 0.5f)] public float trunkTaper = 0f;

    [Tooltip("Trunk lean amount (horizontal shift per vertical block).")]
    [Range(0f, 0.4f)] public float trunkLean = 0f;

    [Header("Branches")]
    [Tooltip("Number of primary branches extending from upper trunk.")]
    [Range(0, 8)] public int branchCount = 3;

    [Tooltip("Upward angle of branches in degrees (0 = horizontal, 90 = vertical).")]
    [Range(15f, 75f)] public float branchAngle = 45f;

    [Tooltip("Length of primary branches in blocks.")]
    [Range(1, 8)] public int branchLength = 3;

    [Tooltip("Recursive branching levels (0 = no sub-branches, 1 = sub-twigs).")]
    [Range(0, 2)] public int branchDepth = 1;

    [Header("Root Flare")]
    [Tooltip("Extra log blocks placed around the trunk base to anchor it into the terrain.")]
    [Range(0, 3)] public int rootFlareRadius = 0;

    [Header("Canopy")]
    [Tooltip("Overall silhouette of the foliage.")]
    public CanopyShape canopyShape = CanopyShape.Ellipsoid;

    [Tooltip("Horizontal leaf radius along X.")]
    [Range(1, 8)] public int canopyRadiusX = 3;

    [Tooltip("Vertical leaf radius along Y.")]
    [Range(1, 8)] public int canopyRadiusY = 3;

    [Tooltip("Horizontal leaf radius along Z.")]
    [Range(1, 8)] public int canopyRadiusZ = 3;

    [Tooltip("Fraction of exterior leaf blocks randomly omitted for an organic look.")]
    [Range(0f, 0.5f)] public float leafDensityErosion = 0.15f;

    [Header("Blocks")]
    [Tooltip("Block type used for wood / trunk / branches.")]
    public VoxelBlockType logBlock;

    [Tooltip("Block type used for leaves / foliage.")]
    public VoxelBlockType leafBlock;

    // ── Max extent calculation ────────────────────────────────────────────────

    public override int MaxExtent
    {
        get
        {
            int maxR = Mathf.Max(canopyRadiusX, canopyRadiusZ) + branchLength + rootFlareRadius + 2;
            int maxH = trunkMaxHeight + canopyRadiusY + 3;
            return Mathf.Max(maxR, maxH);
        }
    }

    // ── Procedural Generation (Deterministic, Thread-Safe) ────────────────────

    public override StructureData GenerateData(int seed)
    {
        var rng = new DeterministicRng(seed);

        byte logId = logBlock != null ? logBlock.blockId : (byte)7;
        byte leafId = leafBlock != null ? leafBlock.blockId : (byte)8;

        // Use dictionaries for coordinate deduplication; logs take priority over leaves
        var logs = new HashSet<Vector3Int>();
        var leaves = new HashSet<Vector3Int>();

        int height = rng.NextInt(trunkMinHeight, trunkMaxHeight + 1);

        // Trunk lean direction
        float leanAngle = rng.NextFloat(0f, Mathf.PI * 2f);
        float leanCos = Mathf.Cos(leanAngle);
        float leanSin = Mathf.Sin(leanAngle);

        var branchTips = new List<Vector3Int>();

        // 1. Root flare
        if (rootFlareRadius > 0)
        {
            for (int dx = -rootFlareRadius; dx <= rootFlareRadius; dx++)
            for (int dz = -rootFlareRadius; dz <= rootFlareRadius; dz++)
            {
                int distSq = dx * dx + dz * dz;
                if (distSq <= rootFlareRadius * rootFlareRadius && distSq > 0)
                {
                    logs.Add(new Vector3Int(dx, 0, dz));
                    if (distSq == 1) logs.Add(new Vector3Int(dx, 1, dz));
                }
            }
        }

        // 2. Trunk blocks
        Vector3Int topCenter = Vector3Int.zero;
        for (int y = 0; y < height; y++)
        {
            int shiftX = Mathf.RoundToInt(y * trunkLean * leanCos);
            int shiftZ = Mathf.RoundToInt(y * trunkLean * leanSin);

            int currentThickness = trunkThickness;
            if (trunkTaper > 0f && y > height / 2 && currentThickness > 1)
            {
                if (rng.NextFloat() < trunkTaper) currentThickness--;
            }

            PlaceTrunkSlice(logs, shiftX, y, shiftZ, currentThickness);
            if (y == height - 1)
                topCenter = new Vector3Int(shiftX, y, shiftZ);
        }

        // 3. Branches
        if (branchCount > 0 && branchLength > 0)
        {
            float angleStep = (Mathf.PI * 2f) / branchCount;
            float branchRad = branchAngle * Mathf.Deg2Rad;
            float branchUp = Mathf.Sin(branchRad);
            float branchHoriz = Mathf.Cos(branchRad);

            int branchStartY = Mathf.Max(2, height * 5 / 10);

            for (int b = 0; b < branchCount; b++)
            {
                float baseA = b * angleStep + rng.NextFloat(-0.25f, 0.25f);
                int attachY = rng.NextInt(branchStartY, height);

                int startX = Mathf.RoundToInt(attachY * trunkLean * leanCos);
                int startZ = Mathf.RoundToInt(attachY * trunkLean * leanSin);

                Vector3 currentPos = new Vector3(startX, attachY, startZ);
                Vector3 branchDir = new Vector3(
                    Mathf.Cos(baseA) * branchHoriz,
                    branchUp,
                    Mathf.Sin(baseA) * branchHoriz
                ).normalized;

                int len = Mathf.Max(1, branchLength + rng.NextInt(-1, 2));
                for (int s = 1; s <= len; s++)
                {
                    currentPos += branchDir;
                    var posInt = new Vector3Int(
                        Mathf.RoundToInt(currentPos.x),
                        Mathf.RoundToInt(currentPos.y),
                        Mathf.RoundToInt(currentPos.z)
                    );
                    logs.Add(posInt);
                }

                branchTips.Add(new Vector3Int(
                    Mathf.RoundToInt(currentPos.x),
                    Mathf.RoundToInt(currentPos.y),
                    Mathf.RoundToInt(currentPos.z)
                ));

                // Optional sub-branch
                if (branchDepth > 0 && rng.NextBool(0.6f))
                {
                    float subAngle = baseA + (rng.NextBool() ? 0.6f : -0.6f);
                    Vector3 subDir = new Vector3(Mathf.Cos(subAngle) * branchHoriz, branchUp * 0.7f, Mathf.Sin(subAngle) * branchHoriz).normalized;
                    Vector3 subPos = currentPos;
                    for (int s = 1; s <= 2; s++)
                    {
                        subPos += subDir;
                        logs.Add(new Vector3Int(
                            Mathf.RoundToInt(subPos.x),
                            Mathf.RoundToInt(subPos.y),
                            Mathf.RoundToInt(subPos.z)
                        ));
                    }
                    branchTips.Add(new Vector3Int(
                        Mathf.RoundToInt(subPos.x),
                        Mathf.RoundToInt(subPos.y),
                        Mathf.RoundToInt(subPos.z)
                    ));
                }
            }
        }
        else
        {
            branchTips.Add(topCenter);
        }

        // 4. Canopy generation
        switch (canopyShape)
        {
            case CanopyShape.Ellipsoid:
                GenerateEllipsoidCanopy(leaves, logs, topCenter, canopyRadiusX, canopyRadiusY, canopyRadiusZ, leafDensityErosion, ref rng);
                break;

            case CanopyShape.LayeredCone:
                GenerateLayeredConeCanopy(leaves, logs, topCenter, height, canopyRadiusX, canopyRadiusY, canopyRadiusZ, leafDensityErosion, ref rng);
                break;

            case CanopyShape.ClusteredBlobs:
                GenerateClusteredBlobsCanopy(leaves, logs, topCenter, branchTips, canopyRadiusX, canopyRadiusY, canopyRadiusZ, leafDensityErosion, ref rng);
                break;

            case CanopyShape.Umbrella:
                GenerateUmbrellaCanopy(leaves, logs, topCenter, branchTips, canopyRadiusX, canopyRadiusY, canopyRadiusZ, leafDensityErosion, ref rng);
                break;
        }

        // 5. Build final block array (logs overwrite leaves)
        leaves.ExceptWith(logs);

        var resultList = new List<StructureBlock>(logs.Count + leaves.Count);

        Vector3Int min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
        Vector3Int max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);

        foreach (var l in logs)
        {
            resultList.Add(new StructureBlock(l, logId));
            min = Vector3Int.Min(min, l);
            max = Vector3Int.Max(max, l);
        }

        foreach (var lf in leaves)
        {
            resultList.Add(new StructureBlock(lf, leafId));
            min = Vector3Int.Min(min, lf);
            max = Vector3Int.Max(max, lf);
        }

        if (resultList.Count == 0)
        {
            resultList.Add(new StructureBlock(Vector3Int.zero, logId));
            min = Vector3Int.zero;
            max = Vector3Int.zero;
        }

        Vector3Int bounds = max - min + Vector3Int.one;
        Vector3Int anchor = Vector3Int.zero; // anchor is at (0,0,0), base of trunk

        return new StructureData(resultList.ToArray(), bounds, anchor, ReplaceMode.ReplaceAirOnly);
    }

    private static void PlaceTrunkSlice(HashSet<Vector3Int> logs, int x, int y, int z, int thickness)
    {
        switch (thickness)
        {
            case 1:
                logs.Add(new Vector3Int(x, y, z));
                break;
            case 2:
                logs.Add(new Vector3Int(x, y, z));
                logs.Add(new Vector3Int(x + 1, y, z));
                logs.Add(new Vector3Int(x, y, z + 1));
                logs.Add(new Vector3Int(x + 1, y, z + 1));
                break;
            case 3:
                for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    logs.Add(new Vector3Int(x + dx, y, z + dz));
                break;
        }
    }

    // ── Canopy shape implementations ──────────────────────────────────────────

    private static void GenerateEllipsoidCanopy(HashSet<Vector3Int> leaves, HashSet<Vector3Int> logs,
                                                 Vector3Int center, int rx, int ry, int rz,
                                                 float erosion, ref DeterministicRng rng)
    {
        for (int dx = -rx; dx <= rx; dx++)
        for (int dy = -ry; dy <= ry; dy++)
        for (int dz = -rz; dz <= rz; dz++)
        {
            float normX = (float)dx / rx;
            float normY = (float)dy / ry;
            float normZ = (float)dz / rz;
            float distSq = normX * normX + normY * normY + normZ * normZ;

            if (distSq <= 1.05f)
            {
                // Surface erosion for organic look
                if (distSq > 0.75f && rng.NextFloat() < erosion) continue;

                leaves.Add(new Vector3Int(center.x + dx, center.y + dy, center.z + dz));
            }
        }
    }

    private static void GenerateLayeredConeCanopy(HashSet<Vector3Int> leaves, HashSet<Vector3Int> logs,
                                                   Vector3Int topCenter, int trunkHeight,
                                                   int maxRx, int maxRy, int maxRz,
                                                   float erosion, ref DeterministicRng rng)
    {
        int canopyHeight = Mathf.Max(4, maxRy * 2);
        int startY = topCenter.y + 1;
        int endY = Mathf.Max(2, topCenter.y - canopyHeight);

        for (int y = startY; y >= endY; y--)
        {
            float t = (float)(startY - y) / (startY - endY); // 0 at top, 1 at bottom
            int radius = Mathf.RoundToInt(Mathf.Lerp(1, maxRx, t));

            // Conical tiers: slight step-in every 2-3 blocks for a pine look
            int tierMod = (startY - y) % 3;
            if (tierMod == 0 && radius > 1) radius--;

            for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                int distSq = dx * dx + dz * dz;
                if (distSq <= radius * radius + 1)
                {
                    if (distSq >= radius * radius && rng.NextFloat() < erosion) continue;
                    leaves.Add(new Vector3Int(topCenter.x + dx, y, topCenter.z + dz));
                }
            }
        }
    }

    private static void GenerateClusteredBlobsCanopy(HashSet<Vector3Int> leaves, HashSet<Vector3Int> logs,
                                                     Vector3Int topCenter, List<Vector3Int> branchTips,
                                                     int rx, int ry, int rz,
                                                     float erosion, ref DeterministicRng rng)
    {
        // Place a blob at the trunk top
        int blobR = Mathf.Max(2, rx - 1);
        GenerateEllipsoidCanopy(leaves, logs, topCenter + new Vector3Int(0, 1, 0), blobR, blobR, blobR, erosion, ref rng);

        // Place a blob at each branch tip
        foreach (var tip in branchTips)
        {
            int subR = Mathf.Max(2, rx - rng.NextInt(0, 2));
            GenerateEllipsoidCanopy(leaves, logs, tip, subR, Mathf.Max(1, ry - 1), subR, erosion, ref rng);
        }
    }

    private static void GenerateUmbrellaCanopy(HashSet<Vector3Int> leaves, HashSet<Vector3Int> logs,
                                               Vector3Int topCenter, List<Vector3Int> branchTips,
                                               int rx, int ry, int rz,
                                               float erosion, ref DeterministicRng rng)
    {
        int umbrellaY = topCenter.y + 1;
        int thickness = Mathf.Max(1, ry / 2);

        // Broad flat umbrella centered slightly above highest points
        foreach (var tip in branchTips)
        {
            for (int dy = 0; dy < thickness; dy++)
            {
                int r = (dy == thickness - 1) ? rx - 1 : rx;
                for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist <= r)
                    {
                        if (dist > r - 1 && rng.NextFloat() < erosion) continue;
                        leaves.Add(new Vector3Int(tip.x + dx, tip.y + dy, tip.z + dz));
                    }
                }
            }
        }
    }
}
