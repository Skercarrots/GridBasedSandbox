using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  ChaosConfig — Parameters for the chaos (3D density) terrain mode.
//
//  WHAT CHAOS TERRAIN IS
//  In normal mode, terrain height is determined by a 2D heightmap — blocks are
//  solid below the surface line and air above it. This creates natural-looking
//  ground but can't produce overhangs, arches, or floating chunks.
//
//  In chaos mode, each block's solidity is determined by a 3D density function
//  instead. The density function combines:
//    (a) A height-based gradient that biases toward solid near the surface
//    (b) 3D fBm noise that carves overhangs and creates floating shapes
//
//  WHERE CHAOS ACTIVATES
//  A 2D low-frequency "mask" noise value is compared against maskThreshold per
//  column. Only columns with mask > threshold enter 3D density evaluation.
//  This keeps chaos terrain confined to specific dramatic regions while the
//  rest of the world uses the cheap heightmap approach.
//
//  PERFORMANCE
//  3D noise is expensive. We evaluate it on a coarse grid (step = coarseGridStep)
//  and trilinearly interpolate between grid corners. This reduces evaluations
//  by ~(coarseGridStep)³ with minimal visual quality loss.
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class ChaosConfig
{
    [Header("Activation Mask")]
    [Tooltip("Scale of the 2D low-frequency noise that controls WHERE chaos terrain appears. " +
             "Larger = bigger chaos regions.")]
    public float maskScale = 800f;

    [Tooltip("Mask value above which chaos terrain activates. " +
             "0 = chaos everywhere (expensive!), 1 = chaos nowhere. " +
             "Recommended: 0.6–0.72 for dramatic highlands.")]
    [Range(0f, 1f)] public float maskThreshold = 0.65f;

    [Tooltip("Seed offset for the mask noise so it doesn't align with weirdness noise.")]
    public float maskSeedOffset = 31337f;

    [Header("3D Density Noise")]
    [Tooltip("Horizontal scale of the 3D density noise. " +
             "Larger = wider overhangs and arches.")]
    public float densityScale = 30f;

    [Tooltip("Number of fBm octaves for the 3D density noise. " +
             "More octaves = more fine detail in overhangs, but slower generation.")]
    [Range(1, 5)] public int densityOctaves = 3;

    [Tooltip("Amplitude falloff per octave in the density fBm.")]
    [Range(0f, 1f)] public float densityPersistence = 0.5f;

    [Tooltip("How strongly the 3D noise contributes to the density function. " +
             "Higher = more dramatic overhangs and floating chunks. " +
             "Recommended: 0.6–1.0.")]
    public float densityAmplitude = 0.8f;

    [Tooltip("How steeply density falls off per block above the base surface height. " +
             "Higher = overhangs stay close to the surface. " +
             "Lower = floating chunks can extend higher above the terrain.")]
    public float densityGradient = 0.04f;

    [Header("Performance")]
    [Tooltip("Coarse grid step for trilinear interpolation of 3D density. " +
             "Density is evaluated at grid corners and interpolated in between. " +
             "Step 4 = ~64× fewer evaluations than per-block. " +
             "Increase for performance, decrease for quality.")]
    [Range(2, 8)] public int coarseGridStep = 4;
}

