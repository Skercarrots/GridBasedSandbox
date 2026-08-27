using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  DimensionProfile — Bundles a generator with the settings it generates into.
//  Create via: Assets > Create > VoxelWorld > Dimension Profile
//
//  One profile per dimension (Overworld, Nether, Void, ...). Assign the
//  active profile to VoxelWorldManager. To add a dimension later: write a
//  new WorldGenerator subclass, make a new VoxelWorldSettings asset (or reuse
//  one), make a new DimensionProfile asset pointing at both. VoxelWorldManager,
//  VoxelChunk, and VoxelMeshBuilder don't need to change at all.
//
//  NOTE: VoxelWorldSettings already owns the block registry, so this profile
//  doesn't duplicate it — settings.blockRegistry stays the single source of truth.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "DimensionProfile_New", menuName = "VoxelWorld/Dimension Profile")]
public class DimensionProfile : ScriptableObject
{
    public string dimensionName = "Overworld";
    public WorldGenerator generator;
    public VoxelWorldSettings settings;
}