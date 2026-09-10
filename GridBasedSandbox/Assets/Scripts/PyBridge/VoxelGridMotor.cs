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
// block a step. If a step or turn doesn't complete within a generous
// multiple of its expected duration, the motor gives up and releases IsBusy
// rather than hanging forever — this matters once ScriptRunner has no
// timeout for autonomous scripts.
//
// SETUP
// Same pivot convention as the player: this object's transform.position
// should be at its FEET (collider center offset upward from there), and
// it should already be grid-aligned (X/Z at a block center, integer Y) —
// which is exactly what VoxelWorldManager.GetSpawnPosition() returns.

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
    [Tooltip("Small downward offset for the ground check sphere, since the pivot sits at the feet.")]
    [SerializeField] private float groundCheckOffset = 0.06f;

    [Header("Arrival Tolerance")]
    [SerializeField] private float positionEpsilon = 0.02f;
    [SerializeField] private float rotationEpsilonDeg = 0.5f;

    private Rigidbody   _rb;
    private Vector3?    _targetPos;
    private Quaternion? _targetRot;
    private float       _stepDeadline;
    private float       _turnDeadline;
    private bool        _isGrounded;

    public bool  IsBusy     => _targetPos.HasValue || _targetRot.HasValue;
    public bool  IsGrounded => _isGrounded;
    public float MoveSpeed  => moveSpeed;
    public float TurnSpeed  => turnSpeed;

    public void SetMoveSpeed(float unitsPerSecond)   => moveSpeed = Mathf.Max(0.05f, unitsPerSecond);
    public void SetTurnSpeed(float degreesPerSecond) => turnSpeed = Mathf.Max(1f, degreesPerSecond);

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.freezeRotation         = true;
        _rb.useGravity             = false; // we apply our own, same as PlayerController
        _rb.interpolation          = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
    }

    // ── Public commands ──────────────────────────────────────────────────

    /// <summary>Starts moving exactly 1 unit in the given world-space direction
    /// (snapped to the nearest cardinal axis). Returns false if already busy —
    /// callers should check IsBusy (via IScriptableDevice.IsAnimating) or just
    /// rely on the return value.</summary>
    public bool StepInDirection(Vector3 direction)
    {
        if (IsBusy) return false;

        Vector3 flat = new Vector3(direction.x, 0f, direction.z);
        if (flat.sqrMagnitude < 0.0001f) return false;
        flat = RoundToCardinal(flat.normalized);

        _targetPos    = _rb.position + flat;
        _stepDeadline = Time.time + ExpectedTimeout(1f, moveSpeed);
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
        Vector3 checkPos = _rb.position + Vector3.down * groundCheckOffset;
        _isGrounded = Physics.CheckSphere(checkPos, groundCheckRadius, groundLayers, QueryTriggerInteraction.Ignore);
    }

    private void DriveTranslation()
    {
        if (!_targetPos.HasValue)
        {
            // Not mid-step: don't fight gravity, but don't drift horizontally either.
            Vector3 v = _rb.linearVelocity;
            _rb.linearVelocity = new Vector3(0f, v.y, 0f);
            return;
        }

        Vector3 target = _targetPos.Value;
        Vector3 toTarget = new Vector3(target.x - _rb.position.x, 0f, target.z - _rb.position.z);
        float dist = toTarget.magnitude;

        if (dist <= positionEpsilon || Time.time > _stepDeadline)
        {
            // Arrived (or gave up — see class header on stuck protection).
            // Snap exactly onto the grid so float drift never accumulates
            // across hundreds of steps.
            _rb.position = new Vector3(target.x, _rb.position.y, target.z);
            _rb.linearVelocity = new Vector3(0f, _rb.linearVelocity.y, 0f);
            _targetPos = null;
            return;
        }

        Vector3 dir = toTarget / dist;
        float speed = Mathf.Min(moveSpeed, dist / Time.fixedDeltaTime); // never overshoot in one tick
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
            // Pin a small downward velocity while grounded — same fix as
            // PlayerController, prevents floating between adjacent pillars.
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
        Vector3 checkPos = transform.position + Vector3.down * groundCheckOffset;
        Gizmos.color = _isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(checkPos, groundCheckRadius);
    }
#endif
}
