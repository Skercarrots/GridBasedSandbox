using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  VoxelNoise — Shared noise helpers that don't belong to any single generator.
//
//  Sample3D is a cheap pseudo-3D noise built from three 2D Perlin samples
//  (one per axis plane, averaged). It's good enough for cave carving. If you
//  need higher-quality 3D noise later (fewer directional artifacts along the
//  planes), swap this implementation for a real simplex/OpenSimplex library —
//  callers only see Sample3D(), so nothing else needs to change.
// ─────────────────────────────────────────────────────────────────────────────

public static class VoxelNoise
{
    public static float Sample3D(float x, float y, float z, float scale)
    {
        x /= scale; y /= scale; z /= scale;

        float xy = Mathf.PerlinNoise(x, y);
        float yz = Mathf.PerlinNoise(y, z);
        float xz = Mathf.PerlinNoise(x, z);

        return (xy + yz + xz) / 3f;
    }
}