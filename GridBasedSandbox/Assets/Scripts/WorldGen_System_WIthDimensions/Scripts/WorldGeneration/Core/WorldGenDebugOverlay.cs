#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
//  WorldGenDebugOverlay  —  Runtime GUI overlay for quick debugging.
//
//  Shows:
//    · Active dimension ID
//    · Active generator type
//    · Number of active passes
//    · Seed
//    · Player chunk coordinate
//
//  ENABLE / DISABLE
//  Attach to any GameObject.  Toggle showOverlay in the Inspector.
//  The overlay only draws in Play mode (runtime).
// ═══════════════════════════════════════════════════════════════════════════════

public class WorldGenDebugOverlay : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private VoxelWorldManagerExtended worldManager;
    [SerializeField] private DimensionRegistry         dimensionRegistry;
    [SerializeField] private Transform                 playerTransform;

    [Header("Display")]
    public bool showOverlay = true;

    [Tooltip("Screen-space position of the overlay box (pixels from top-left).")]
    public Vector2 overlayPosition = new Vector2(10, 10);

    private GUIStyle _style;

    private void OnGUI()
    {
        if (!showOverlay || worldManager == null) return;

        _style ??= new GUIStyle(GUI.skin.box)
        {
            fontSize  = 13,
            alignment = TextAnchor.UpperLeft,
            padding   = new RectOffset(8, 8, 6, 6),
        };
        _style.normal.textColor = Color.white;

        DimensionProfile profile = dimensionRegistry?.ActiveProfile;
        IWorldGenerator  gen     = dimensionRegistry?.ActiveGenerator;
        int              passes  = dimensionRegistry?.ActivePasses?.Count ?? 0;

        Vector2Int playerChunk = Vector2Int.zero;
        if (playerTransform != null && profile?.worldSettings != null)
        {
            int cw = profile.worldSettings.chunkWidth;
            playerChunk = new Vector2Int(
                Mathf.FloorToInt(playerTransform.position.x / cw),
                Mathf.FloorToInt(playerTransform.position.z / cw));
        }

        string text =
            $"── WorldGen Debug ──\n" +
            $"Dimension : {profile?.dimensionId ?? "none"}\n" +
            $"Display   : {profile?.displayName ?? "-"}\n" +
            $"Generator : {gen?.GetType().Name ?? "null"}\n" +
            $"Passes    : {passes}\n" +
            $"Chunk     : {playerChunk}\n";

        Vector2 size = _style.CalcSize(new GUIContent(text));
        size.x += 20;
        size.y += 10;

        GUI.Box(new Rect(overlayPosition.x, overlayPosition.y, size.x, size.y), text, _style);

        // ── Dimension switch buttons (editor-only convenience) ────────────────
#if UNITY_EDITOR
        if (dimensionRegistry == null) return;
        float btnY = overlayPosition.y + size.y + 4;
        float btnX = overlayPosition.x;

        foreach (string id in dimensionRegistry.GetAllIds())
        {
            bool isActive = id == worldManager.ActiveDimensionId;
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = isActive ? Color.green : Color.gray;

            if (GUI.Button(new Rect(btnX, btnY, 140, 22), id))
                worldManager.SwitchDimension(id);

            GUI.backgroundColor = prev;
            btnX += 148;
        }
#endif
    }
}
