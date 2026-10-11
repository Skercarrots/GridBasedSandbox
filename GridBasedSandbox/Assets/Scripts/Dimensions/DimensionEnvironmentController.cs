using System.Collections;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  DimensionEnvironmentController — Manages scene lighting, fog, skybox,
//  and atmospheric audio for the active dimension.
// ─────────────────────────────────────────────────────────────────────────────

public class DimensionEnvironmentController : MonoBehaviour
{
    public static DimensionEnvironmentController Instance { get; private set; }

    [Header("Scene References")]
    [Tooltip("Scene directional light (sun). If unassigned, auto-resolved from RenderSettings.sun or scene.")]
    [SerializeField] private Light sceneDirectionalLight;

    [Tooltip("Audio source for ambient loops. Auto-created if unassigned.")]
    [SerializeField] private AudioSource ambientAudioSource;

    private BackroomsAudioHum _proceduralHum;
    private BackroomsLightingFlicker _flicker;
    private Coroutine _blendCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (sceneDirectionalLight == null)
        {
            if (RenderSettings.sun != null)
                sceneDirectionalLight = RenderSettings.sun;
            else
            {
                var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i].type == LightType.Directional)
                    {
                        sceneDirectionalLight = lights[i];
                        break;
                    }
                }
            }
        }

        if (ambientAudioSource == null)
        {
            ambientAudioSource = gameObject.AddComponent<AudioSource>();
            ambientAudioSource.loop = true;
            ambientAudioSource.playOnAwake = false;
            ambientAudioSource.spatialBlend = 0f; // 2D ambient sound
        }

        _proceduralHum = GetComponent<BackroomsAudioHum>();
        if (_proceduralHum == null)
        {
            _proceduralHum = gameObject.AddComponent<BackroomsAudioHum>();
        }
    }

    /// <summary>
    /// Applies the visual and acoustic settings of a dimension profile immediately or smoothly.
    /// </summary>
    public void ApplyEnvironment(DimensionEnvironmentProfile profile, bool immediate = true)
    {
        if (profile == null) return;

        if (_blendCoroutine != null)
            StopCoroutine(_blendCoroutine);

        // Must run BEFORE applying the new ambient: disabling the previous dimension's flicker
        // restores its baseline colour, which the new ambient then correctly overwrites.
        ConfigureFlicker(profile);

        if (immediate)
        {
            ApplyInstant(profile);
        }
        else
        {
            _blendCoroutine = StartCoroutine(BlendEnvironmentCoroutine(profile, 1.2f));
        }

        UpdateAudio(profile);
    }

    private void ConfigureFlicker(DimensionEnvironmentProfile profile)
    {
        if (_flicker == null)
        {
            if (!profile.enableLightFlicker) return;
            _flicker = GetComponent<BackroomsLightingFlicker>();
            if (_flicker == null) _flicker = gameObject.AddComponent<BackroomsLightingFlicker>();
        }

        if (profile.enableLightFlicker)
        {
            _flicker.Configure(profile.ambientLightColor, profile.flickerIntervalRange, profile.flickerBrightness);
            _flicker.enabled = true;
        }
        else
        {
            _flicker.enabled = false;
        }
    }

    private void ApplyInstant(DimensionEnvironmentProfile profile)
    {
        RenderSettings.fog = profile.enableFog;
        RenderSettings.fogColor = profile.fogColor;
        RenderSettings.fogMode = profile.fogMode;
        RenderSettings.fogDensity = profile.fogDensity;
        RenderSettings.fogStartDistance = profile.linearFogStart;
        RenderSettings.fogEndDistance = profile.linearFogEnd;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = profile.ambientLightColor;
        RenderSettings.skybox = profile.skyboxMaterial;

        if (sceneDirectionalLight != null)
        {
            sceneDirectionalLight.enabled = profile.enableDirectionalLight;
            sceneDirectionalLight.color = profile.directionalLightColor;
            sceneDirectionalLight.intensity = profile.directionalLightIntensity;
        }
    }

    private IEnumerator BlendEnvironmentCoroutine(DimensionEnvironmentProfile target, float duration)
    {
        Color startFogColor = RenderSettings.fogColor;
        float startFogDensity = RenderSettings.fogDensity;
        Color startAmbient = RenderSettings.ambientLight;
        float startSunIntensity = sceneDirectionalLight != null ? sceneDirectionalLight.intensity : 1f;

        RenderSettings.fog = target.enableFog;
        RenderSettings.fogMode = target.fogMode;
        RenderSettings.skybox = target.skyboxMaterial;

        if (sceneDirectionalLight != null && target.enableDirectionalLight)
        {
            sceneDirectionalLight.enabled = true;
            sceneDirectionalLight.color = target.directionalLightColor;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            RenderSettings.fogColor = Color.Lerp(startFogColor, target.fogColor, t);
            RenderSettings.fogDensity = Mathf.Lerp(startFogDensity, target.fogDensity, t);
            RenderSettings.ambientLight = Color.Lerp(startAmbient, target.ambientLightColor, t);

            if (sceneDirectionalLight != null)
            {
                sceneDirectionalLight.intensity = Mathf.Lerp(startSunIntensity, target.directionalLightIntensity, t);
            }

            yield return null;
        }

        ApplyInstant(target);
    }

    private void UpdateAudio(DimensionEnvironmentProfile profile)
    {
        if (profile.useProcedural60HzHum)
        {
            if (ambientAudioSource != null && ambientAudioSource.isPlaying)
                ambientAudioSource.Stop();

            if (_proceduralHum != null)
                _proceduralHum.SetHumActive(true, profile.ambientAudioVolume);
        }
        else
        {
            if (_proceduralHum != null)
                _proceduralHum.SetHumActive(false);

            if (profile.ambientAudioClip != null && ambientAudioSource != null)
            {
                ambientAudioSource.clip = profile.ambientAudioClip;
                ambientAudioSource.volume = profile.ambientAudioVolume;
                if (!ambientAudioSource.isPlaying)
                    ambientAudioSource.Play();
            }
            else if (ambientAudioSource != null)
            {
                ambientAudioSource.Stop();
            }
        }
    }
}

