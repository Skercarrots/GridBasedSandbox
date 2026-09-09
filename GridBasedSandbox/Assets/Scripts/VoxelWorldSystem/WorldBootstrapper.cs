using System.Collections;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  WorldBootstrapper — Orchestrates first-time world startup end-to-end:
//    1. Resolves a safe spawn point (VoxelWorldManager.GetSpawnPosition — the
//       one deliberately-blocking generation call in the whole pipeline).
//    2. Kicks off background generation for a small ring of chunks around it
//       (VoxelWorldManager.BeginAreaLoad — async, no player required yet).
//    3. Drives a loading screen while that ring finishes generating + meshing
//       (VoxelWorldManager.IsAreaReady).
//    4. Instantiates the player prefab only once there's real, meshed ground
//       under the spawn point (so it doesn't fall through), then hands it to
//       VoxelWorldManager (AttachPlayer) so normal streaming takes over.
//
//  SETUP
//  • Put this on any GameObject in the scene (e.g. alongside VoxelWorldManager).
//  • Assign worldManager, playerPrefab, and loadingScreen.
//  • Leave VoxelWorldManager's own "Player Transform" field EMPTY in the
//    Inspector — this script spawns the player itself. (If you DO assign one
//    there, VoxelWorldManager will stream around it immediately at Start()
//    instead of waiting for this bootstrapper — pick one or the other.)
// ─────────────────────────────────────────────────────────────────────────────

public class WorldBootstrapper : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private VoxelWorldManager worldManager;
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private LoadingScreenController loadingScreen;

    [Tooltip("The scene camera that renders during loading. Disabled once the player " +
             "prefab is spawned and its own camera takes over.")]
    [SerializeField] private Camera loadingCamera;

    [Tooltip("Optional — assign if VoxelBlockPlacer lives in the scene rather than on " +
             "the player prefab. The bootstrapper will inject the player's camera into " +
             "it automatically so you don't have to wire the Inspector field by hand.")]
    [SerializeField] private VoxelBlockPlacer blockPlacer;

    [Header("Spawn")]
    [Tooltip("World block X/Z the player spawns near. (0,0) = world center.")]
    [SerializeField] private Vector2Int spawnColumn = Vector2Int.zero;

    [Tooltip("How many chunks around the spawn point must be fully generated " +
             "and meshed before the player is dropped in. Keep this small (1-2) " +
             "— it only needs to guarantee solid ground immediately around the " +
             "player; everything further out streams in normally right after, " +
             "same as the player walking towards it would trigger anyway.")]
    [SerializeField] private int initialLoadRadiusInChunks = 2;

    private void Start()
    {
        StartCoroutine(BootstrapWorld());
    }

    private IEnumerator BootstrapWorld()
    {
        loadingScreen.Show();

        // The one deliberately synchronous step: we need this answer before we
        // can even decide where to put the player. See GetSpawnPosition's doc
        // comment for why this one call is allowed to block.
        Vector3 spawnPosition = worldManager.GetSpawnPosition(spawnColumn.x, spawnColumn.y);

        // Queues background generation for a small ring around spawn. Returns
        // immediately — nothing here blocks the frame.
        worldManager.BeginAreaLoad(spawnColumn.x, spawnColumn.y, initialLoadRadiusInChunks);

        // Poll until that ring is generated AND meshed. In practice this is a
        // handful of frames: chunk data comes back from background threads
        // roughly in parallel, and meshing is spread across frames by
        // VoxelWorldSettings.maxChunkBuildsPerFrame — not a multi-second stall.
        while (!worldManager.IsAreaReady(spawnColumn.x, spawnColumn.y, initialLoadRadiusInChunks, out float progress))
        {
            loadingScreen.SetProgress(progress);
            yield return null;
        }
        loadingScreen.SetProgress(1f);

        GameObject player = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
        worldManager.AttachPlayer(player.transform);

        // Grab the camera that lives on (or inside) the player prefab.
        Camera playerCam = player.GetComponentInChildren<Camera>();

        // Wire it into VoxelBlockPlacer — must happen before the loading camera
        // goes dark so raycasts are never left pointing at a dead camera reference.
        if (blockPlacer != null && playerCam != null)
            blockPlacer.SetPlayerCamera(playerCam);

        // The player camera is now live — safe to disable the loading camera.
        // Doing this before Hide() means there's never a frame with no active camera.
        if (loadingCamera != null)
            loadingCamera.gameObject.SetActive(false);

        loadingScreen.Hide();
    }
}