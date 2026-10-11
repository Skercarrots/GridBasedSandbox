using System.Collections;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  BackroomsLightingFlicker — Simulates perceptual flicker and intermittent
//  ballast overloading of commercial fluorescent troffers (DOC-N0-LIMINAL-84).
//
//  Driven by DimensionEnvironmentController from the active dimension's
//  DimensionEnvironmentProfile (enableLightFlicker & co.). Configure() sets the
//  baseline colour so the flicker never fights the environment controller or
//  leaks a stale colour into another dimension.
// ─────────────────────────────────────────────────────────────────────────────

public class BackroomsLightingFlicker : MonoBehaviour
{
    [Header("Flicker Tuning")]
    [Tooltip("Base ambient color to modulate.")]
    [SerializeField] private Color baseColor = new Color(0.72f, 0.65f, 0.41f, 1f);

    [Tooltip("Minimum time between flicker events.")]
    [SerializeField] private float minInterval = 3f;

    [Tooltip("Maximum time between flicker events.")]
    [SerializeField] private float maxInterval = 12f;

    [Tooltip("Drop multiplier during flicker (0.3 = 70% brightness drop).")]
    [Range(0.1f, 0.9f)]
    [SerializeField] private float flickerIntensityDrop = 0.45f;

    private Light _attachedLight;
    private float _baseLightIntensity = 1f;
    private Coroutine _flickerRoutine;

    private void Awake()
    {
        _attachedLight = GetComponent<Light>();
        if (_attachedLight != null)
        {
            _baseLightIntensity = _attachedLight.intensity;
        }
    }

    private void OnEnable()
    {
        _flickerRoutine = StartCoroutine(FlickerLoop());
    }

    private void OnDisable()
    {
        if (_flickerRoutine != null)
            StopCoroutine(_flickerRoutine);

        RestoreBaseLighting();
    }

    /// <summary>
    /// Sets the baseline ambient colour and timing. Safe to call while enabled — the loop restarts
    /// so the new values take effect immediately.
    /// </summary>
    public void Configure(Color ambientBaseline, Vector2 intervalRange, float brightnessOnDip)
    {
        baseColor = ambientBaseline;
        minInterval = Mathf.Max(0.1f, intervalRange.x);
        maxInterval = Mathf.Max(minInterval, intervalRange.y);
        flickerIntensityDrop = Mathf.Clamp(brightnessOnDip, 0.1f, 0.9f);

        if (isActiveAndEnabled)
        {
            if (_flickerRoutine != null) StopCoroutine(_flickerRoutine);
            RestoreBaseLighting();
            _flickerRoutine = StartCoroutine(FlickerLoop());
        }
    }

    private IEnumerator FlickerLoop()
    {
        while (true)
        {
            float waitTime = Random.Range(minInterval, maxInterval);
            yield return new WaitForSeconds(waitTime);

            int pulseCount = Random.Range(1, 4);
            for (int i = 0; i < pulseCount; i++)
            {
                // Quick flicker dip
                ApplyDim(flickerIntensityDrop);
                yield return new WaitForSeconds(Random.Range(0.04f, 0.09f));

                RestoreBaseLighting();
                yield return new WaitForSeconds(Random.Range(0.03f, 0.08f));
            }
        }
    }

    private void ApplyDim(float drop)
    {
        if (_attachedLight != null)
        {
            _attachedLight.intensity = _baseLightIntensity * drop;
        }

        // Modulate scene ambient light slightly
        RenderSettings.ambientLight = baseColor * drop;
    }

    private void RestoreBaseLighting()
    {
        if (_attachedLight != null)
        {
            _attachedLight.intensity = _baseLightIntensity;
        }

        RenderSettings.ambientLight = baseColor;
    }
}
