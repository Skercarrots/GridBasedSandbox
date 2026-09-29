#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  VoxelBlockTypeEditor — Custom inspector for VoxelBlockType.
//  Shows a prominent "PLACEHOLDER" warning badge when the block has no real
//  atlas tile assigned, so designers know it's using vertex-color tinting
//  instead of proper art.
// ─────────────────────────────────────────────────────────────────────────────

[CustomEditor(typeof(VoxelBlockType))]
public class VoxelBlockTypeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var block = (VoxelBlockType)target;

        if (block.IsPlaceholder && block.blockId != 0)
        {
            // Draw a coloured preview swatch next to the badge
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox(
                "PLACEHOLDER — No atlas tile assigned.\n" +
                "This block renders with vertex-color tint in-game.\n" +
                "Assign a real atlas tile to remove this badge.",
                MessageType.Warning);

            var swatchRect = GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32));
            EditorGUI.DrawRect(swatchRect, block.ResolvedPlaceholderColor);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
        }

        DrawDefaultInspector();
    }
}
#endif
