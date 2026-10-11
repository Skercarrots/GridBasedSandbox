using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  DimensionEnvironmentProfile — Defines the visual and acoustic atmosphere
//  for a dimension (skybox, fog, ambient lighting, sun, and ambient sound).
//  Create via: Assets > Create > VoxelWorld > Dimension Environment Profile
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "Environment_New", menuName = "VoxelWorld/Dimension Environment Profile")]
public class DimensionEnvironmentProfile : ScriptableObject
{
    [Header("Fog Settings")]
    public bool enableFog = true;
    public Color fogColor = new Color(0.486f, 0.694f, 0.812f, 1f);
    public FogMode fogMode = FogMode.ExponentialSquared;
    [Range(0f, 0.2f)] public float fogDensity = 0.004f;
    public float linearFogStart = 0f;
    public float linearFogEnd = 300f;

    [Header("Lighting & Atmosphere")]
    [Tooltip("Skybox material. Null/None renders a dark enclosed environment.")]
    public Material skyboxMaterial;
    public Color ambientLightColor = new Color(0.94f, 0.90f, 0.85f, 1f);
    public bool enableDirectionalLight = true;
    public Color directionalLightColor = Color.white;
    [Range(0f, 3f)] public float directionalLightIntensity = 1f;

    [Header("Audio Ambience")]
    [Tooltip("Ambient audio loop clip. Can be null if using procedural sound.")]
    public AudioClip ambientAudioClip;
    [Range(0f, 1f)] public float ambientAudioVolume = 0.5f;

    [Tooltip("If true, dynamically generates and loops the canonical Backrooms 60 Hz ballast hum.")]
    public bool useProcedural60HzHum = false;

    [Header("Light Flicker (optional)")]
    [Tooltip("Briefly dips the ambient light at random intervals, like failing fluorescent ballasts.")]
    public bool enableLightFlicker = false;

    [Tooltip("Min / max seconds between flicker bursts.")]
    public Vector2 flickerIntervalRange = new Vector2(3f, 12f);

    [Tooltip("Brightness multiplier during a flicker dip (0.45 = 55% drop).")]
    [Range(0.1f, 0.9f)] public float flickerBrightness = 0.45f;
}

