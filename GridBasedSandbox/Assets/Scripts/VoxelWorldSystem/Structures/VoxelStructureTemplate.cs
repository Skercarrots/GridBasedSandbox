using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  VoxelStructureTemplate — ScriptableObject holding a static voxel structure.
//  Create via: Assets > Create > GridBasedSandbox/Voxel World/Structures/Structure Template
//
//  Can be hand-built, generated via StructureEditorWindow, or baked from a TreeRecipe.
//  Used by WorldFeature to place trees, ruins, boulders, and other structures.
//
//  THREAD SAFETY:
//  StructureData is a lightweight plain C# data container holding the raw
//  structure blocks, bounds, and anchor. Since ScriptableObject.CreateInstance()
//  is main-thread only, runtime procedural recipes return StructureData off-thread.
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public struct StructureBlock
{
    [Tooltip("Offset relative to the structure anchor in world block units.")]
    public Vector3Int offset;

    [Tooltip("Block ID to place.")]
    public byte blockId;

    public StructureBlock(Vector3Int offset, byte blockId)
    {
        this.offset = offset;
        this.blockId = blockId;
    }

    public StructureBlock(int x, int y, int z, byte blockId)
    {
        this.offset = new Vector3Int(x, y, z);
        this.blockId = blockId;
    }
}

public enum ReplaceMode
{
    [Tooltip("Overwrites any block except bedrock.")]
    ReplaceAll,

    [Tooltip("Only places blocks where air currently exists (preserves terrain).")]
    ReplaceAirOnly,

    [Tooltip("Only replaces solid blocks (e.g. roots penetrating into ground).")]
    ReplaceSolidOnly
}

// ─────────────────────────────────────────────────────────────────────────────
//  StructureData — Pure C# class holding structure voxels. Safe on background threads.
// ─────────────────────────────────────────────────────────────────────────────

public class StructureData
{
    public StructureBlock[] blocks;
    public Vector3Int bounds;
    public Vector3Int anchor;
    public ReplaceMode replaceMode;

    public StructureData()
    {
        blocks = Array.Empty<StructureBlock>();
        bounds = Vector3Int.zero;
        anchor = Vector3Int.zero;
        replaceMode = ReplaceMode.ReplaceAirOnly;
    }

    public StructureData(StructureBlock[] blocks, Vector3Int bounds, Vector3Int anchor, ReplaceMode replaceMode)
    {
        this.blocks = blocks ?? Array.Empty<StructureBlock>();
        this.bounds = bounds;
        this.anchor = anchor;
        this.replaceMode = replaceMode;
    }

    /// <summary>Returns a copy rotated 90° clockwise around Y axis.</summary>
    public StructureData Rotate90()
    {
        if (blocks == null || blocks.Length == 0) return this;

        var rotated = new StructureBlock[blocks.Length];
        for (int i = 0; i < blocks.Length; i++)
        {
            var b = blocks[i];
            // Clockwise rotation around Y: (x, y, z) -> (z, y, -x)
            rotated[i] = new StructureBlock(new Vector3Int(b.offset.z, b.offset.y, -b.offset.x), b.blockId);
        }

        // Bounds swapped on X and Z
        var newBounds = new Vector3Int(bounds.z, bounds.y, bounds.x);
        var newAnchor = new Vector3Int(anchor.z, anchor.y, -anchor.x);
        return new StructureData(rotated, newBounds, newAnchor, replaceMode);
    }

    /// <summary>Returns a mirrored copy across the X axis.</summary>
    public StructureData MirrorX()
    {
        if (blocks == null || blocks.Length == 0) return this;

        var mirrored = new StructureBlock[blocks.Length];
        for (int i = 0; i < blocks.Length; i++)
        {
            var b = blocks[i];
            mirrored[i] = new StructureBlock(new Vector3Int(-b.offset.x, b.offset.y, b.offset.z), b.blockId);
        }

        var newAnchor = new Vector3Int(-anchor.x, anchor.y, anchor.z);
        return new StructureData(mirrored, bounds, newAnchor, replaceMode);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  VoxelStructureTemplate — ScriptableObject asset representation.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "StructureTemplate_New", menuName = "GridBasedSandbox/Voxel World/Structures/Structure Template")]
public class VoxelStructureTemplate : ScriptableObject
{
    [Tooltip("All blocks in this structure, offset relative to anchor.")]
    public StructureBlock[] blocks = Array.Empty<StructureBlock>();

    [Tooltip("Total bounding box size of the structure.")]
    public Vector3Int bounds = Vector3Int.one;

    [Tooltip("Where the structure's placement anchor is (e.g. 0,0,0 for trunk base).")]
    public Vector3Int anchor = Vector3Int.zero;

    [Tooltip("Rules for replacing existing terrain blocks when placed.")]
    public ReplaceMode replaceMode = ReplaceMode.ReplaceAirOnly;

    /// <summary>Converts this ScriptableObject into thread-safe StructureData.</summary>
    public StructureData ToData()
    {
        return new StructureData(blocks, bounds, anchor, replaceMode);
    }

    /// <summary>Populates this ScriptableObject from StructureData.</summary>
    public void FromData(StructureData data)
    {
        blocks = data.blocks;
        bounds = data.bounds;
        anchor = data.anchor;
        replaceMode = data.replaceMode;
    }

    /// <summary>Returns a runtime copy rotated 90° clockwise around Y.</summary>
    public VoxelStructureTemplate Rotate90()
    {
        var copy = CreateInstance<VoxelStructureTemplate>();
        copy.FromData(ToData().Rotate90());
        return copy;
    }

    /// <summary>Returns a runtime mirrored copy across the X axis.</summary>
    public VoxelStructureTemplate MirrorX()
    {
        var copy = CreateInstance<VoxelStructureTemplate>();
        copy.FromData(ToData().MirrorX());
        return copy;
    }
}
