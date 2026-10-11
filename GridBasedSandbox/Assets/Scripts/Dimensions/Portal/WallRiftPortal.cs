using System.Collections;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  WallRiftPortal — Vertical glowing rift/seam on the drywall, inspired by the
//  Threshold seam in Backrooms films (Kane Pixels). Teleports player back to
//  their base / previous dimension.
// ─────────────────────────────────────────────────────────────────────────────

[RequireComponent(typeof(BoxCollider))]
public class WallRiftPortal : MonoBehaviour
{
    [Header("Destination")]
    [Tooltip("Target dimension ID to transport to (default: 'overworld').")]
    [SerializeField] private string targetDimensionId = "overworld";

    [Tooltip("If true, teleports the player back to their last known base position in the target dimension.")]
    [SerializeField] private bool useSavedBasePosition = true;

    [Tooltip("Fallback coordinates if no saved base position exists.")]
    [SerializeField] private Vector3 fallbackTargetPosition = new Vector3(0.5f, 20f, 0.5f);

    [Header("Visuals & Lighting")]
    [Tooltip("Color of the glowing tear.")]
    [SerializeField] private Color riftColor = new Color(0.75f, 0.92f, 1.0f, 1f);

    [Tooltip("Point light attached to the seam.")]
    [SerializeField] private Light seamLight;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip proximityHum;
    [SerializeField] private AudioClip teleportSound;

    // Re-trigger protection is owned by DimensionManager (IsTransitioning / IsInArrivalGrace)
    // so it can never go stale across play sessions the way a static field would.

    private void Awake()
    {
        var col = GetComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(0.6f, 2.6f, 0.6f);
        col.center = new Vector3(0f, 1.3f, 0f);

        SetupVisuals();
    }

    private void SetupVisuals()
    {
        // Add a PointLight if not already present
        if (seamLight == null)
        {
            var lightObj = new GameObject("RiftLight");
            lightObj.transform.SetParent(transform, false);
            lightObj.transform.localPosition = new Vector3(0f, 1.3f, 0f);

            seamLight = lightObj.AddComponent<Light>();
            seamLight.type = LightType.Point;
            seamLight.color = riftColor;
            seamLight.intensity = 2.5f;
            seamLight.range = 5.0f;
        }

        // Add AudioSource if missing
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1.0f;
            audioSource.maxDistance = 6.0f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.loop = true;
            audioSource.volume = 0.4f;
            audioSource.playOnAwake = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        var manager = DimensionManager.Instance;
        if (manager == null || manager.IsTransitioning || manager.IsInArrivalGrace) return;

        var player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;

        ExecuteTeleport();
    }

    private void ExecuteTeleport()
    {
        if (DimensionManager.Instance == null)
        {
            Debug.LogWarning("[WallRiftPortal] DimensionManager instance not found!");
            return;
        }

        if (audioSource != null && teleportSound != null)
        {
            audioSource.PlayOneShot(teleportSound, 0.8f);
        }

        // SwitchDimension restores the target's saved (safe) position, or its arrival point.
        // An explicit fallback position is only used when saved positions are disabled.
        if (useSavedBasePosition)
            DimensionManager.Instance.SwitchDimension(targetDimensionId);
        else
            DimensionManager.Instance.SwitchDimension(targetDimensionId, fallbackTargetPosition);
    }

    /// <summary>Sets the destination dimension (used by DimensionManager from DimensionProfile.returnDimensionId).</summary>
    public void SetTarget(string dimensionId)
    {
        targetDimensionId = dimensionId;
    }

    public void SetTarget(string dimensionId, Vector3 fallbackPos)
    {
        targetDimensionId = dimensionId;
        fallbackTargetPosition = fallbackPos;
    }
}

