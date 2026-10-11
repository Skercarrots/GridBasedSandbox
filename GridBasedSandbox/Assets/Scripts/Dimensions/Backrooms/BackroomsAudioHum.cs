using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  BackroomsAudioHum — Procedural 60 Hz fluorescent ballast hum synthesizer.
//  Reproduces the exact acoustic signature specified in DOC-N0-LIMINAL-84:
//  continuous 60 Hz resonance with 120/180/240 Hz harmonics and subtle flutter.
// ─────────────────────────────────────────────────────────────────────────────

public class BackroomsAudioHum : MonoBehaviour
{
    private AudioSource _audioSource;
    private AudioClip _humClip;
    private bool _isActive = false;

    private const int SampleRate = 44100;
    private const float Duration = 2.0f; // 2 second seamless loop

    private void Awake()
    {
        EnsureAudioSource();
        EnsureClipGenerated();
    }

    private void EnsureAudioSource()
    {
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.loop = true;
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f; // 2D ambient sound
            _audioSource.priority = 64;
        }
    }

    private void EnsureClipGenerated()
    {
        if (_humClip != null) return;

        int totalSamples = Mathf.RoundToInt(SampleRate * Duration);
        float[] samples = new float[totalSamples];

        // 60 Hz fundamental + harmonics (120 Hz, 180 Hz, 240 Hz, 360 Hz)
        const float f0 = 60f;
        const float twoPi = Mathf.PI * 2f;

        for (int i = 0; i < totalSamples; i++)
        {
            float t = (float)i / SampleRate;

            // Fundamental (strongest)
            float s = Mathf.Sin(twoPi * f0 * t) * 0.55f;

            // 2nd harmonic (120 Hz - full wave rectification hum)
            s += Mathf.Sin(twoPi * 120f * t) * 0.28f;

            // 3rd harmonic (180 Hz - magnetic saturation)
            s += Mathf.Sin(twoPi * 180f * t) * 0.12f;

            // 4th & 6th harmonics (240 Hz & 360 Hz - high frequency buzz)
            s += Mathf.Sin(twoPi * 240f * t) * 0.05f;
            s += Mathf.Sin(twoPi * 360f * t) * 0.03f;

            // Subtle sub-harmonic rumble (30 Hz)
            s += Mathf.Sin(twoPi * 30f * t) * 0.04f;

            // Very soft white noise floor (air/plasma hiss)
            float hiss = (Random.value * 2f - 1f) * 0.015f;
            s += hiss;

            samples[i] = Mathf.Clamp(s, -1f, 1f);
        }

        _humClip = AudioClip.Create("Backrooms_60Hz_BallastHum", totalSamples, 1, SampleRate, false);
        _humClip.SetData(samples, 0);

        if (_audioSource != null)
        {
            _audioSource.clip = _humClip;
        }
    }

    public void SetHumActive(bool active, float volume = 0.5f)
    {
        EnsureAudioSource();
        EnsureClipGenerated();

        _isActive = active;
        if (_isActive)
        {
            _audioSource.volume = Mathf.Clamp01(volume);
            if (!_audioSource.isPlaying)
            {
                _audioSource.Play();
            }
        }
        else
        {
            if (_audioSource.isPlaying)
            {
                _audioSource.Stop();
            }
        }
    }

    public void SetVolume(float volume)
    {
        if (_audioSource != null)
        {
            _audioSource.volume = Mathf.Clamp01(volume);
        }
    }

    public bool IsActive => _isActive;
}

