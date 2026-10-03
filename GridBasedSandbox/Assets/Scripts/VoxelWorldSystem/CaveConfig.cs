using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  CaveConfig — ScriptableObject holding all parameters for the multi-layer
//  cave carving system.
//  Create via: Assets > Create > GridBasedSandbox/Voxel World/Cave Config
//
//  THREE CAVE TYPES (inspired by Minecraft 1.18+ cave generation)
//
//  1. CHEESE CAVERNS — Large open underground spaces. A 3D noise field is
//     sampled; blocks where the noise exceeds cheeseThreshold are carved out.
//     These create the big dramatic caverns visible in the reference images.
//
//  2. SPAGHETTI TUNNELS — Long winding tunnels formed where TWO independent
//     3D noise fields are both near zero simultaneously. This creates the
//     classic "two-noodle intersection" tunnel shape that winds through stone
//     in organic paths. These are the connective tissue between cheese caverns.
//
//  3. NOODLE TUNNELS — Similar to spaghetti but thinner and at a different
//     scale. Optional layer for extra tunnel variety and connectivity.
//
//  SURFACE & BEDROCK PROTECTION
//  • minDepthBelowSurface — prevents caves from puncturing through hillsides
//  • bedrockFadeHeight — caves fade out near bedrock to avoid exposing the void
//  • waterProtectionDepth — suppresses caves under oceans/lakes to prevent
//    catastrophic water flooding of the cave system
//
//  PERFORMANCE
//  3D noise is expensive. The coarseGridStep parameter evaluates noise only
//  at grid corners and trilinearly interpolates between them, reducing noise
//  evaluations by ~(step)³ with minimal visual quality loss.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "CaveConfig", menuName = "GridBasedSandbox/Voxel World/Cave Config")]
public class CaveConfig : ScriptableObject
{
    // ── Cheese Caverns (large open spaces) ────────────────────────────────────

    [Header("Cheese Caverns (large open spaces)")]
    [Tooltip("Scale of the 3D noise for cheese caves. Larger = wider, more open caverns. " +
             "Recommended: 30–60.")]
    public float cheeseScale = 45f;

    [Tooltip("Noise threshold above which blocks are carved. " +
             "Lower = more caves, higher = fewer caves. " +
             "Recommended: 0.55–0.65 for dramatic caverns.")]
    [Range(0f, 1f)] public float cheeseThreshold = 0.58f;

    [Tooltip("Number of fBm octaves for cheese cave noise. " +
             "More octaves = more irregular shapes and detail. " +
             "Recommended: 2–3.")]
    [Range(1, 5)] public int cheeseOctaves = 2;

    [Tooltip("Persistence (amplitude decay) per cheese noise octave.")]
    [Range(0f, 1f)] public float cheesePersistence = 0.5f;

    [Tooltip("Seed offset for cheese noise so it doesn't align with other noise fields.")]
    public int cheeseSeedOffset = 100000;

    // ── Spaghetti Tunnels (winding tunnels) ───────────────────────────────────

    [Header("Spaghetti Tunnels (winding tunnels)")]
    [Tooltip("Scale of the two 3D noises that define spaghetti tunnels. " +
             "Larger = wider, gentler curves. Smaller = tighter twists. " +
             "Recommended: 25–40.")]
    public float spaghettiScale = 30f;

    [Tooltip("Both noise values must be within ±this threshold from 0.5 for a tunnel. " +
             "Smaller = thinner tunnels, larger = wider tunnels. " +
             "Recommended: 0.04–0.10.")]
    [Range(0f, 0.3f)] public float spaghettiThreshold = 0.06f;

    [Tooltip("Number of fBm octaves for spaghetti noise.")]
    [Range(1, 5)] public int spaghettiOctaves = 3;

    [Tooltip("Persistence per spaghetti noise octave.")]
    [Range(0f, 1f)] public float spaghettiPersistence = 0.5f;

    [Tooltip("Seed offset for spaghetti noise channel 1.")]
    public int spaghettiSeedOffset1 = 200000;

    [Tooltip("Seed offset for spaghetti noise channel 2.")]
    public int spaghettiSeedOffset2 = 300000;

    // ── Noodle Tunnels (thinner, optional) ────────────────────────────────────

    [Header("Noodle Tunnels (thinner, optional)")]
    [Tooltip("Enable the noodle tunnel layer for extra connectivity between caves.")]
    public bool enableNoodles = true;

    [Tooltip("Scale of the noodle tunnel noise. Smaller = tighter tunnels than spaghetti.")]
    public float noodleScale = 20f;

    [Tooltip("Threshold for noodle tunnels. Smaller = thinner noodles. " +
             "Recommended: 0.03–0.06.")]
    [Range(0f, 0.2f)] public float noodleThreshold = 0.045f;

    [Tooltip("Seed offset for noodle noise channel 1.")]
    public int noodleSeedOffset1 = 400000;

    [Tooltip("Seed offset for noodle noise channel 2.")]
    public int noodleSeedOffset2 = 500000;

    // ── Depth Scaling ─────────────────────────────────────────────────────────

    [Header("Depth Scaling")]
    [Tooltip("Caves are most common at this many blocks below the surface. " +
             "A gentle probability curve peaks here and fades toward both surface and bedrock.")]
    public int peakCaveDepth = 40;

    [Tooltip("How many blocks above bedrock caves stop forming. " +
             "Prevents caves from exposing the void below bedrock.")]
    public int bedrockFadeHeight = 5;

    // ── Surface Protection ────────────────────────────────────────────────────

    [Header("Surface Protection")]
    [Tooltip("Minimum number of solid blocks below the surface that caves never carve through. " +
             "Prevents caves from opening into hillsides or creating sinkholes.")]
    public int minDepthBelowSurface = 4;

    [Tooltip("Under water bodies (columns where surface ≤ sea level), caves are suppressed " +
             "within this many blocks below the ocean floor. Prevents catastrophic flooding.")]
    public int waterProtectionDepth = 8;

    // ── Performance ───────────────────────────────────────────────────────────

    [Header("Performance")]
    [Tooltip("Coarse grid step for trilinear interpolation. " +
             "Noise is evaluated at grid corners only — intermediate blocks are interpolated. " +
             "Step 4 = ~64× fewer noise evaluations. Higher = faster but blockier. " +
             "Recommended: 4 for most setups. Set to 1 to disable interpolation.")]
    [Range(1, 8)] public int coarseGridStep = 4;
}

