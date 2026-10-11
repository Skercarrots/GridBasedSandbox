using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  DimensionArrivalPoint — Where the player appears when entering a dimension
//  and (optionally) where the return rift should be anchored on a wall.
//
//  Generators that carve a reserved clearing (see BackroomsGenerator) return one
//  of these so spawn/portal placement is GUARANTEED by construction instead of
//  being "read back" from the voxel data and hoped for.
// ─────────────────────────────────────────────────────────────────────────────

public struct DimensionArrivalPoint
{
    /// <summary>World position where the player's feet are placed.</summary>
    public Vector3 position;

    /// <summary>True if returnAnchorPosition / returnAnchorYaw are valid.</summary>
    public bool hasReturnAnchor;

    /// <summary>World position (floor level, flush against a wall face) for the return rift.</summary>
    public Vector3 returnAnchorPosition;

    /// <summary>Y rotation (degrees) so the rift faces into the room.</summary>
    public float returnAnchorYaw;
}

