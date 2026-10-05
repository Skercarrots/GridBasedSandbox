using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  VoidKillPlane — Teleports the player to a safe position if they fall
//  below the world's minimum height. Attach to the player GameObject.
//
//  WHY THIS EXISTS
//  With bedrock at world min Y, players should never fall below the world —
//  but edge cases (glitches, spectator mode, creative digging) can still do
//  it. This is a safety net, not a gameplay mechanic.
// ─────────────────────────────────────────────────────────────────────────────

public class VoidKillPlane : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The VoxelWorldManager instance. If left empty, uses VoxelWorldManager.Instance.")]
    [SerializeField] private VoxelWorldManager worldManager;

    [Tooltip("The VoxelWorldSettings asset. Used to read minHeight.")]
    [SerializeField] private VoxelWorldSettings settings;

    [Header("Settings")]
    [Tooltip("How many blocks below minHeight the player must fall before teleporting.")]
    [Range(1, 20)]
    [SerializeField] private int depthBelowMin = 5;

    [Tooltip("World block X/Z to teleport to. Uses spawn column by default.")]
    [SerializeField] private Vector2Int safeColumn = Vector2Int.zero;

    private void Update()
    {
        if (settings == null) return;

        float killY = settings.minHeight - depthBelowMin;
        if (transform.position.y >= killY) return;

        // Resolve world manager
        var mgr = worldManager != null ? worldManager : VoxelWorldManager.Instance;
        if (mgr == null)
        {
            // Fallback: just teleport above min height
            Vector3 fallbackPos = new Vector3(
                transform.position.x,
                settings.minHeight + 10f,
                transform.position.z);
            TeleportPlayer(fallbackPos);
            Debug.LogWarning("[VoidKillPlane] No VoxelWorldManager — teleported to fallback height.");
            return;
        }

        Vector3 safePos = mgr.GetSpawnPosition(safeColumn.x, safeColumn.y);
        TeleportPlayer(safePos);
        Debug.Log($"[VoidKillPlane] Player fell below void — teleported to {safePos}.");
    }

    /// <summary>Safely relocates the player and zeroes out velocity on PlayerController, CharacterController, or Rigidbody.</summary>
    private void TeleportPlayer(Vector3 targetPos)
    {
        // 1. PlayerController (custom voxel controller)
        var playerCtrl = GetComponent<PlayerController>();
        if (playerCtrl != null)
        {
            playerCtrl.Teleport(targetPos);
            return;
        }

        // 2. CharacterController
        var cc = GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.enabled = false;
            transform.position = targetPos;
            cc.enabled = true;
            return;
        }

        // 3. Rigidbody
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = targetPos;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // 4. Default Transform fallback
        transform.position = targetPos;
    }
}
