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

    // ── Jump-specific spatial checks ──────────────────────────────────────
    // These verify whether the robot has enough clearance to execute a jump.
    //
    // HEADROOM CALCULATION & CEILING CLEARANCE:
    // With jump apex h = 1.25 units and robot collider height 0.95:
    //   Takeoff feet: Y = 0
    //   Takeoff head: Y = 0.95
    //   Jump apex feet: Y = 1.25
    //   Jump apex head: Y = 1.25 + 0.95 = 2.20
    //
    // This means:
    // 1. A block at Y=+1 (above robot head) MUST be air, or the robot cannot even begin jumping.
    // 2. A block at Y=+2 (two blocks above feet): bottom face is at Y=2.0. If solid, the robot's
    //    apex (2.20) would collide into it! Therefore, Y=+2 must also be air.
    // 3. A block at Y=+3 or higher: bottom face is at Y=3.0 or higher. Since 2.20 < 3.0, the robot
    //    comfortably clears without touching it! So blocks at Y=+3 do not block jumping.
    //
    // For directional jumps (CanJumpAhead / CanJumpBehind):
    //   - Takeoff headroom: Y=+1 and Y=+2 above robot must be air.
    //   - Landing cell: ahead + Y=+1 must be air (this is where the robot will land).
    //   - Landing headroom: ahead + Y=+2 must be air (standing clearance at destination).
    // If ANY of these cells are solid, the jump is refused.

    /// <summary>Can the robot perform a vertical jump in place? Checks 2 blocks of headroom (Y+1 and Y+2).</summary>
    public bool CanJump()
    {
        Vector3Int pos = OccupiedBlock();
        if (IsSolid(pos + Vector3Int.up))     return false; // Y+1 immediate headroom
        if (IsSolid(pos + Vector3Int.up * 2)) return false; // Y+2 apex clearance (apex reaches 2.20)
        return true;
    }

    /// <summary>Can the robot jump forward and land on top of a 1-block step ahead?</summary>
    public bool CanJumpAhead()
    {
        Vector3Int pos = OccupiedBlock();
        Vector3Int facing = FacingDirection();

        // 1. Takeoff headroom (must be air so robot can leave the ground)
        if (IsSolid(pos + Vector3Int.up))          return false;
        if (IsSolid(pos + Vector3Int.up * 2))      return false;

        // 2. Landing cell (ahead + Y=+1, where the robot's feet will touch down)
        if (IsSolid(pos + facing + Vector3Int.up)) return false;

        // 3. Standing headroom at destination (ahead + Y=+2, clearance for the robot's body)
        if (IsSolid(pos + facing + Vector3Int.up * 2)) return false;

        return true;
    }

    /// <summary>Can the robot jump backward and land on top of a 1-block step behind?</summary>
    public bool CanJumpBehind()
    {
        Vector3Int pos = OccupiedBlock();
        Vector3Int facing = FacingDirection();

        // Same 4-cell clearance check as CanJumpAhead, but in the opposite direction.
        if (IsSolid(pos + Vector3Int.up))           return false;
        if (IsSolid(pos + Vector3Int.up * 2))       return false;
        if (IsSolid(pos - facing + Vector3Int.up))  return false;
        if (IsSolid(pos - facing + Vector3Int.up * 2)) return false;

        return true;
    }

    public Surroundings Scan() => new Surroundings(
        IsBlockedAhead(), IsBlockedBehind(), IsBlockedLeft(), IsBlockedRight(),
        IsBlockedAbove(), IsBlockedBelow());

    // ── Grid math ────────────────────────────────────────────────────────

    /// <summary>
    /// The voxel cell this device's body currently occupies (feet position).
    ///
    /// COORDINATE CONVENTIONS — read before changing the rounding:
    ///
    ///   X / Z  use FloorToInt.
    ///     Block centres are at n+0.5 (e.g. 0.5, 1.5, 2.5).
    ///     FloorToInt(n+0.5) = n — always the correct block index.
    ///     Works for negative coords too: FloorToInt(-0.5) = -1 = block -1. ✓
    ///
    ///   Y  uses RoundToInt — intentionally different from X/Z.
    ///     The pivot sits at the robot's feet, which should be at integer Y
    ///     (standing on the top surface of a block). In practice, physics
    ///     drift can leave the robot at Y=3.999 or Y=4.001 rather than
    ///     exactly Y=4. FloorToInt(3.999) = 3, which wrongly identifies the
    ///     robot as being inside the ground block — IsBlockedBelow() would
    ///     then check one block too low and miss the actual ground.
    ///     RoundToInt(3.999) = 4, RoundToInt(4.001) = 4 — both correctly
    ///     identify the robot as standing in the cell above the ground. ✓
    ///
    ///   This asymmetry is load-bearing. Do not "normalise" both axes to the
    ///   same rounding without understanding the above. The sensor is designed
    ///   to be called while the robot is stationary (VoxelGridMotor.IsBusy is
    ///   false); behaviour during mid-fall is undefined by design.
    /// </summary>
    public Vector3Int OccupiedBlock()
    {
        Vector3 p = transform.position;
        return new Vector3Int(
            Mathf.FloorToInt(p.x), // block index = floor of block-centre X (n+0.5)
            Mathf.RoundToInt(p.y), // round to nearest integer to absorb Y drift (see above)
            Mathf.FloorToInt(p.z)  // block index = floor of block-centre Z (n+0.5)
        );
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