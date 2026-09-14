// VoxelGridMotor.cs
// Reusable physics-driven mover for anything that needs to travel the voxel
// world exactly one voxel (1 unit) at a time, or turn in exact 90° steps —
// robots today, but any future mobile device (drones, carts, critters...)
// can add this component instead of reimplementing movement.
//
// WHY PHYSICS INSTEAD OF A LERP TWEEN
// The old RobotController drove position/rotation with Vector3.Lerp inside a
// coroutine — visually smooth, but it never touched the physics engine, so
// it could clip through colliders, wouldn't push/be-pushed by other
// rigidbodies, and had no notion of gravity. This mover instead sets the
// Rigidbody's velocity every FixedUpdate, exactly like PlayerController does
// for the player — real collisions, real gravity, same "feel" — but every
// StepInDirection() call is still guaranteed to land exactly 1 unit away,
// because we track an explicit target and snap onto it on arrival.
//
// SPEED
// moveSpeed/turnSpeed are just fields — tune per-device in the Inspector, or
// call SetMoveSpeed()/SetTurnSpeed() at runtime (this is what
// RobotAPI.set_speed() calls from Python). A pathfinding script can slow the
// robot down to watch it think, or speed it up to explore fast.
//
// STUCK PROTECTION
// A caller is expected to check the world first (see VoxelBodySensor) so it
// never walks into a known wall. But physical obstacles the voxel grid
// doesn't know about (the player, another robot standing there) can still
// block a step. If a step doesn't complete within a generous multiple of its
// expected duration, the motor rolls back to the step's origin rather than
// teleporting forward — see DriveTranslation() for the full explanation.
//
// PIVOT CONVENTION
// This object's Transform.position MUST be at the robot's feet (the bottom
// of the body). The collider center is then offset upward from there. If the
// pivot is at the body center instead (Unity primitive default), GroundCheck
// still works (it uses collider bounds), but VoxelBodySensor.OccupiedBlock()
// will read the wrong block Y — see its header for details. Fix the prefab:
// empty root at feet, cube/mesh child offset Y+0.5.

using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class VoxelGridMotor : MonoBehaviour
{
    [Header("Speed")]
    [Tooltip("Units (voxels) per second for a single move step. Change at runtime with SetMoveSpeed().")]
    [SerializeField] private float moveSpeed = 2f;

    [Tooltip("Degrees per second for turn_left/turn_right. Change at runtime with SetTurnSpeed().")]
    [SerializeField] private float turnSpeed = 220f;

    [Header("Gravity")]
    [Tooltip("Same shape as PlayerController's gravity — negative, applied via AddForce.")]
    [SerializeField] private float gravity = -28f;
    [SerializeField] private float maxFallSpeed = 40f;

    [Header("Ground Check")]
    [Tooltip("Layers considered ground/terrain — set to your voxel chunk layer.")]
    [SerializeField] private LayerMask groundLayers;
    [SerializeField] private float groundCheckRadius = 0.3f;
    [Tooltip("Small upward nudge applied to bounds.min.y when placing the ground-check sphere. " +
             "Prevents the sphere sitting exactly on the surface and flickering in/out.")]
    [SerializeField] private float groundCheckOffset = 0.06f;

    [Header("Arrival Tolerance")]
    [SerializeField] private float positionEpsilon = 0.02f;
    [SerializeField] private float rotationEpsilonDeg = 0.5f;

    private Rigidbody   _rb;
    private Collider    _col;       // cached on Awake — used in GroundCheck

    private Vector3?    _targetPos;
    private Quaternion? _targetRot;
    private float       _stepDeadline;
    private float       _turnDeadline;
    private bool        _isGrounded;

    // FIX 1 — stores the grid-snapped position where the current step began.
    // Used by DriveTranslation() to roll back to a safe location on timeout
    // instead of teleporting the robot into whatever was blocking it.
    private Vector3? _stepOrigin;

    public bool  IsBusy              => _targetPos.HasValue || _targetRot.HasValue;
    public bool  IsGrounded          => _isGrounded;
    public bool  LastStepSucceeded   { get; private set; } = true;
    public float MoveSpeed           => moveSpeed;
    public float TurnSpeed           => turnSpeed;

    public void SetMoveSpeed(float unitsPerSecond)   => moveSpeed = Mathf.Max(0.05f, unitsPerSecond);
    public void SetTurnSpeed(float degreesPerSecond) => turnSpeed = Mathf.Max(1f, degreesPerSecond);

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.freezeRotation         = true;
        _rb.useGravity             = false;
        _rb.interpolation          = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        _col = GetComponentInChildren<Collider>();
        if (_col == null)
            Debug.LogWarning($"[VoxelGridMotor] No Collider found on '{gameObject.name}' or its children. " +
                             "The robot will not be able to detect the ground.");

        // AUTO-FIX 1: Ensure BoxCollider sits on top of the feet pivot (center Y = 0.5)
        // and is slightly smaller than 1.0 (0.85) so it never scrapes adjacent voxel walls or snags mesh seams.
        if (_col is BoxCollider box)
        {
            if (box.center == Vector3.zero)
            {
                box.center = new Vector3(0f, 0.5f, 0f);
                box.size   = new Vector3(0.85f, 0.95f, 0.85f);
            }

            // AUTO-FIX 2: Ensure Zero Friction material so the robot glides effortlessly over chunk meshes.
            if (box.sharedMaterial == null)
            {
                box.sharedMaterial = new PhysicsMaterial("Robot_ZeroFriction")
                {
                    dynamicFriction = 0f,
                    staticFriction  = 0f,
                    bounciness      = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounceCombine   = PhysicsMaterialCombine.Minimum
                };
            }
        }

        // AUTO-FIX 3: If MeshFilter is on the root object, the Unity cube mesh is centered at 0,0,0
        // (meaning its lower half is sunken into the ground). Shift the visual mesh up to local Y=0.5
        // so it visually sits on top of the ground/feet.
        var mf = GetComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();
        if (mf != null && mr != null)
        {
            var visual = new GameObject("Visual");
            visual.transform.SetParent(transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale    = Vector3.one;

            var childMf = visual.AddComponent<MeshFilter>();
            childMf.sharedMesh = mf.sharedMesh;
            var childMr = visual.AddComponent<MeshRenderer>();
            childMr.sharedMaterials = mr.sharedMaterials;

            Destroy(mr);
            Destroy(mf);
        }
    }

    // ── Public commands ──────────────────────────────────────────────────

    /// <summary>Starts moving exactly 1 unit in the given world-space direction
    /// (snapped to the nearest cardinal axis). Returns false if already busy.</summary>
    public bool StepInDirection(Vector3 direction)
    {
        if (IsBusy) return false;

        Vector3 flat = new Vector3(direction.x, 0f, direction.z);
        if (flat.sqrMagnitude < 0.0001f) return false;
        flat = RoundToCardinal(flat.normalized);

        // FIX 1 — snap the origin to the nearest block centre before computing
        // the target. Without this, any physics drift in _rb.position (from
        // gravity, collision responses, or floating-point error) carries into
        // the target and accumulates across every subsequent step. After enough
        // steps, FloorToInt in VoxelBodySensor.OccupiedBlock() returns the
        // wrong block index, producing phantom walls or false-clear paths.
        Vector3 snappedOrigin = new Vector3(
            SnapToBlockCenter(_rb.position.x),
            _rb.position.y,
            SnapToBlockCenter(_rb.position.z));

        _stepOrigin         = snappedOrigin;
        _targetPos          = snappedOrigin + flat;
        _stepDeadline       = Time.time + ExpectedTimeout(1f, moveSpeed);
        LastStepSucceeded   = false;
        return true;
    }

    /// <summary>Starts turning by the given number of degrees (typically ±90).
    /// Returns false if already busy.</summary>
    public bool TurnBy(float degrees)
    {
        if (IsBusy) return false;

        _targetRot    = _rb.rotation * Quaternion.Euler(0f, degrees, 0f);
        _turnDeadline = Time.time + ExpectedTimeout(Mathf.Abs(degrees), turnSpeed);
        return true;
    }

    // ── Physics loop ─────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        GroundCheck();
        DriveTranslation();
        DriveRotation();
        ApplyGravity();
    }

    private void GroundCheck()
    {
        Vector3 feetPos = transform.position;

        // 1. Sphere check slightly above feet, reaching down through the ground plane
        Vector3 checkPos = feetPos + Vector3.up * groundCheckOffset;
        bool grounded = Physics.CheckSphere(checkPos, groundCheckRadius, groundLayers, QueryTriggerInteraction.Ignore);

        // 2. Downward Raycast from above feet — guarantees frontface contact on non-convex chunk meshes
        if (!grounded)
        {
            grounded = Physics.Raycast(feetPos + Vector3.up * 0.15f, Vector3.down, 0.3f, groundLayers, QueryTriggerInteraction.Ignore);
        }

        // 3. Voxel grid fallback — check if the block directly beneath feet is solid in VoxelWorldManager
        if (!grounded && VoxelWorldManager.Instance != null)
        {
            int bx = Mathf.FloorToInt(feetPos.x);
            int by = Mathf.RoundToInt(feetPos.y) - 1;
            int bz = Mathf.FloorToInt(feetPos.z);
            if (VoxelWorldManager.Instance.IsSolidBlock(bx, by, bz))
            {
                float surfaceY = by + 1f;
                // If feet are within 0.25 units of the block surface and falling speed is low
                if (Mathf.Abs(feetPos.y - surfaceY) < 0.25f && _rb.linearVelocity.y <= 0.5f)
                {
                    grounded = true;
                }
            }
        }

        _isGrounded = grounded;
    }

    private void DriveTranslation()
    {
        if (!_targetPos.HasValue)
        {
            Vector3 v = _rb.linearVelocity;
            _rb.linearVelocity = new Vector3(0f, v.y, 0f);
            return;
        }

        Vector3 target   = _targetPos.Value;
        Vector3 toTarget = new Vector3(target.x - _rb.position.x, 0f, target.z - _rb.position.z);
        float   dist     = toTarget.magnitude;

        bool timedOut = Time.time > _stepDeadline;

        if (dist <= positionEpsilon || timedOut)
        {
            float snapX, snapZ;

            if (timedOut)
            {
                LastStepSucceeded = false;
                // FIX 2 — the original code snapped FORWARD to the target on
                // timeout, teleporting the robot through whatever physical obstacle
                // was blocking it. The robot would end up inside a collider, and
                // every subsequent sensor read would operate on the wrong block
                // indices from that point on.
                //
                // On timeout we now snap BACK to _stepOrigin — the grid-aligned
                // position where this step began, which the sensor already confirmed
                // was clear. The step returns "blocked" to the Python thread.
                snapX = _stepOrigin.HasValue
                    ? _stepOrigin.Value.x
                    : SnapToBlockCenter(_rb.position.x);
                snapZ = _stepOrigin.HasValue
                    ? _stepOrigin.Value.z
                    : SnapToBlockCenter(_rb.position.z);

                Debug.LogWarning(
                    $"[VoxelGridMotor] Step on '{gameObject.name}' timed out — " +
                    $"rolling back to ({snapX:F2}, {snapZ:F2}). " +
                    $"A physical obstacle blocked the path after the sensor check passed.");
            }
            else
            {
                LastStepSucceeded = true;
                snapX = SnapToBlockCenter(target.x);
                snapZ = SnapToBlockCenter(target.z);
            }

            _rb.position       = new Vector3(snapX, _rb.position.y, snapZ);
            _rb.linearVelocity = new Vector3(0f, _rb.linearVelocity.y, 0f);
            _targetPos         = null;
            _stepOrigin        = null;
            return;
        }

        Vector3 dir   = toTarget / dist;
        float   speed = Mathf.Min(moveSpeed, dist / Time.fixedDeltaTime);
        _rb.linearVelocity = new Vector3(dir.x * speed, _rb.linearVelocity.y, dir.z * speed);
    }

    private void DriveRotation()
    {
        if (!_targetRot.HasValue) return;

        Quaternion target = _targetRot.Value;
        float angle = Quaternion.Angle(_rb.rotation, target);

        if (angle <= rotationEpsilonDeg || Time.time > _turnDeadline)
        {
            _rb.MoveRotation(target);
            _targetRot = null;
            return;
        }

        float maxDelta = turnSpeed * Time.fixedDeltaTime;
        _rb.MoveRotation(Quaternion.RotateTowards(_rb.rotation, target, maxDelta));
    }

    private void ApplyGravity()
    {
        if (_isGrounded && _rb.linearVelocity.y <= 0f)
        {
            _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, -2f, _rb.linearVelocity.z);
        }
        else
        {
            _rb.AddForce(Vector3.up * gravity, ForceMode.Acceleration);
            if (_rb.linearVelocity.y < -maxFallSpeed)
                _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, -maxFallSpeed, _rb.linearVelocity.z);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>Snaps any float to the nearest voxel block centre (n + 0.5).
    /// Floor(v) gives the block index; adding 0.5 gives its world-space centre.
    /// Works for negative coords: Floor(−0.503) = −1, result = −0.5. </summary>
    private static float SnapToBlockCenter(float v) => Mathf.Floor(v) + 0.5f;

    private static Vector3 RoundToCardinal(Vector3 flatDir)
    {
        return Mathf.Abs(flatDir.x) > Mathf.Abs(flatDir.z)
            ? new Vector3(Mathf.Sign(flatDir.x), 0f, 0f)
            : new Vector3(0f, 0f, Mathf.Sign(flatDir.z));
    }

    private static float ExpectedTimeout(float amount, float speed)
        => Mathf.Max((amount / Mathf.Max(0.01f, speed)) * 3f, 1f);

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Show the bounds-based ground check sphere so you can see in the Scene
        // view exactly where it sits relative to your collider and the terrain.
        Collider col = GetComponentInChildren<Collider>();
        float bottomY = col != null ? col.bounds.min.y : transform.position.y;
        Vector3 checkPos = new Vector3(transform.position.x, bottomY + groundCheckOffset, transform.position.z);
        Gizmos.color = _isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(checkPos, groundCheckRadius);
    }
#endif
}