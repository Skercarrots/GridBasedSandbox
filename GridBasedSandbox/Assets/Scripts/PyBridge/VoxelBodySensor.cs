// VoxelBodySensor.cs
// Reusable "what's around me" query for any device standing in the voxel
// grid. Assumes the device's own pivot is at its feet, aligned to the same
// convention VoxelWorldManager.GetSpawnPosition() uses: integer world Y,
// X/Z at a block-center (n + 0.5). VoxelGridMotor keeps a device aligned to
// this after every completed step, so as long as something only moves via
// VoxelGridMotor, this sensor's block math is exact — no raycasts needed,
// just integer lookups into VoxelWorldManager.
//
// This is what a pathfinding script leans on:
//   robot.is_blocked_ahead()   — wall in the way?
//   robot.is_blocked_below()   — safe to step forward, or is it a drop?
//   robot.is_blocked_above()   — headroom to stand here?
//   robot.surroundings()       — all six checks in one call (cheaper than
//                                 six separate Python↔C# round-trips inside
//                                 a tight exploration loop)
//
// Not robot-specific on purpose — any future mobile device can add this
// component alongside VoxelGridMotor and get the same awareness for free.

using UnityEngine;

public class VoxelBodySensor : MonoBehaviour
{
    [Tooltip("Leave empty to use VoxelWorldManager.Instance.")]
    [SerializeField] private VoxelWorldManager world;

    // Plain snapshot struct — cheap to build once per Scan() call instead of
    // six separate calls out to Python/back for a script that wants "all of it".
    public readonly struct Surroundings
    {
        public readonly bool Ahead, Behind, Left, Right, Above, Below;
        public Surroundings(bool ahead, bool behind, bool left, bool right, bool above, bool below)
        {
            Ahead = ahead; Behind = behind; Left = left; Right = right; Above = above; Below = below;
        }
    }

    private VoxelWorldManager World => world != null ? world : VoxelWorldManager.Instance;

    // ── Individual checks ────────────────────────────────────────────────

    public bool IsBlockedAhead()  => IsSolid(OccupiedBlock() + FacingDirection());
    public bool IsBlockedBehind() => IsSolid(OccupiedBlock() - FacingDirection());
    public bool IsBlockedLeft()   => IsSolid(OccupiedBlock() - RightDirection());
    public bool IsBlockedRight()  => IsSolid(OccupiedBlock() + RightDirection());
    public bool IsBlockedAbove()  => IsSolid(OccupiedBlock() + Vector3Int.up);
    public bool IsBlockedBelow()  => IsSolid(OccupiedBlock() + Vector3Int.down);

    public Surroundings Scan() => new Surroundings(
        IsBlockedAhead(), IsBlockedBehind(), IsBlockedLeft(), IsBlockedRight(),
        IsBlockedAbove(), IsBlockedBelow());

    // ── Grid math ────────────────────────────────────────────────────────

    /// <summary>The voxel cell this device's body currently occupies (feet position).</summary>
    public Vector3Int OccupiedBlock()
    {
        Vector3 p = transform.position;
        return new Vector3Int(Mathf.FloorToInt(p.x), Mathf.RoundToInt(p.y), Mathf.FloorToInt(p.z));
    }

    public Vector3Int FacingDirection() => RoundToCardinal(transform.forward);
    public Vector3Int RightDirection()  => RoundToCardinal(transform.right);

    private bool IsSolid(Vector3Int block)
    {
        var w = World;
        if (w == null) return false; // no world reference — fail open rather than freeze a script
        return w.IsSolidBlock(block.x, block.y, block.z);
    }

    private static Vector3Int RoundToCardinal(Vector3 dir)
    {
        return Mathf.Abs(dir.x) > Mathf.Abs(dir.z)
            ? new Vector3Int((int)Mathf.Sign(dir.x), 0, 0)
            : new Vector3Int(0, 0, (int)Mathf.Sign(dir.z));
    }
}
