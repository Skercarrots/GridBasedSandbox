// PlayerController.cs
// Minecraft-style first-person controller built for voxel games.
// Fixes all CharacterController issues: no stuck-on-edges, fits through 1x2 gaps,
// no pillar-floating, no jitter. Uses Rigidbody with custom physics.
//
// Required setup:
//   - Rigidbody   (configured automatically in Awake)
//   - CapsuleCollider (configured automatically in Awake, or set manually)
//   - A zero-friction PhysicsMaterial on the CapsuleCollider (see setup guide)
//   - A child GameObject with the Camera at eye level (y ≈ 1.6)
//
// CHANGED: Reads GameState.IsIDEOpen to suppress movement/jump when the
// in-game IDE is open. Mouse-look is already suppressed by the existing
// "return if cursor not locked" guard in HandleMouseLook() — the IDE's
// ToggleIDE() unlocks the cursor on open, so that path is already covered.

using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerController : MonoBehaviour
{
    // ── Movement ───────────────────────────────────────────────────────────

    [Header("Movement")]
    [Tooltip("Normal walking speed in units/sec. Minecraft default ≈ 4.3")]
    [SerializeField] private float walkSpeed   = 4.3f;

    [Tooltip("Sprint speed. Hold Left Shift while moving forward to sprint.")]
    [SerializeField] private float sprintSpeed = 5.6f;

    [Tooltip("How high the player jumps in Unity units.")]
    [SerializeField] private float jumpHeight  = 1.2f;

    [Tooltip("Custom gravity (negative). Stronger than Unity default for snappier jumps.")]
    [SerializeField] private float gravity     = -28f;

    [Tooltip("Extra gravity multiplier applied only while falling (velocity.y < 0). " +
             "This is what kills the 'floaty' feeling — rise speed (and therefore jumpHeight) " +
             "is untouched, but the descent gets noticeably heavier. Minecraft-like ≈ 1.8–2.2.")]
    [SerializeField] private float fallGravityMultiplier = 2f;

    [Tooltip("Terminal velocity in units/sec — the fastest the player can ever fall. " +
             "Prevents long drops from feeling like an ever-accelerating slide.")]
    [SerializeField] private float maxFallSpeed = 40f;

    // ── Camera ─────────────────────────────────────────────────────────────

    [Header("Camera")]
    [Tooltip("Drag the child Camera (or its parent at eye level) here.")]
    [SerializeField] private Transform cameraTransform;

    [Tooltip("Mouse sensitivity. Increase for faster look, decrease for slower.")]
    [SerializeField] private float mouseSensitivity = 0.15f;

    [Tooltip("Max up/down angle. 89 = almost straight up/down without flipping.")]
    [SerializeField] private float maxPitch = 89f;

    // ── Ground check ───────────────────────────────────────────────────────

    [Header("Ground Check")]
    [Tooltip("Only detect ground on these layers. Set to your block/terrain layer.")]
    [SerializeField] private LayerMask groundLayers;

    [Tooltip("Sphere radius for ground detection. Should be slightly less than CapsuleCollider radius.")]
    [SerializeField] private float groundCheckRadius = 0.25f;

    [Tooltip("How far below the capsule to check. Small value prevents false positives on walls.")]
    [SerializeField] private float groundCheckOffset = 0.06f;

    // ── Private state ──────────────────────────────────────────────────────

    private Rigidbody       _rb;
    private CapsuleCollider _col;
    private float           _pitch;
    private float           _yaw;
    private bool            _isGrounded;
    private bool            _jumpQueued;
    private float           _takeoffSpeed;

    // ── Setup ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        _rb  = GetComponent<Rigidbody>();
        _col = GetComponent<CapsuleCollider>();

        _rb.freezeRotation         = true;
        _rb.useGravity             = false;
        _rb.interpolation          = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        _col.height = 1.8f;
        _col.radius = 0.3f;
        _col.center = new Vector3(0f, _col.height * 0.5f, 0f);

        _takeoffSpeed = walkSpeed;

        LockCursor();
    }

    // ── Unity loop ─────────────────────────────────────────────────────────

    private void Update()
    {
        // Mouse-look: already a no-op when the cursor is unlocked, so when the
        // IDE is open (which unlocks the cursor) this automatically suppresses.
        HandleMouseLook();

        HandleGroundCheck();

        // ── CHANGED: suppress gameplay input while IDE is open ──────────────
        if (GameState.IsIDEOpen) return;

        if (Input.GetButtonDown("Jump") && _isGrounded)
            _jumpQueued = true;

        HandleCursorToggle();
    }

    private void FixedUpdate()
    {
        HandleMovement();
        HandleGravity();
    }

    // ── Mouse look ─────────────────────────────────────────────────────────

    private void HandleMouseLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        float mouseX = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;

        _yaw += mouseX;

        _pitch = Mathf.Clamp(_pitch - mouseY, -maxPitch, maxPitch);

        cameraTransform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    // ── Ground check ───────────────────────────────────────────────────────

    private void HandleGroundCheck()
    {
        Vector3 checkPos = transform.position + _col.center
            + Vector3.down * (_col.height * 0.5f - _col.radius + groundCheckOffset);

        _isGrounded = Physics.CheckSphere(
            checkPos,
            groundCheckRadius,
            groundLayers,
            QueryTriggerInteraction.Ignore
        );
    }

    // ── Movement ───────────────────────────────────────────────────────────

    private void HandleMovement()
    {
        // ── CHANGED: drain horizontal velocity and bail when IDE is open ────
        // We drain rather than just returning so the player doesn't keep
        // sliding in the direction they were walking before opening the IDE.
        if (GameState.IsIDEOpen)
        {
            _rb.linearVelocity = new Vector3(0f, _rb.linearVelocity.y, 0f);
            _jumpQueued = false;
            return;
        }

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        bool sprinting = Input.GetKey(KeyCode.LeftShift) && v > 0f;
        float speed = sprinting ? sprintSpeed : walkSpeed;

        Quaternion yawRotation = Quaternion.Euler(0f, _yaw, 0f);
        Vector3 forward = yawRotation * Vector3.forward;
        Vector3 right   = yawRotation * Vector3.right;
        Vector3 inputDir = right * h + forward * v;
        if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();
        Vector3 targetVelocity = inputDir * speed;

        if (_isGrounded)
        {
            _rb.linearVelocity = new Vector3(
                targetVelocity.x,
                _rb.linearVelocity.y,
                targetVelocity.z
            );

            _takeoffSpeed = speed;

            if (_jumpQueued)
            {
                float jumpVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * jumpHeight);
                _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, jumpVelocity, _rb.linearVelocity.z);
            }
        }
        else
        {
            Vector3 currentH = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            Vector3 diff = targetVelocity - currentH;
            _rb.AddForce(new Vector3(diff.x, 0f, diff.z) * 0.08f, ForceMode.VelocityChange);

            if (currentH.magnitude > _takeoffSpeed)
            {
                currentH = currentH.normalized * _takeoffSpeed;
                _rb.linearVelocity = new Vector3(currentH.x, _rb.linearVelocity.y, currentH.z);
            }
        }

        _jumpQueued = false;
    }

    // ── Custom gravity ─────────────────────────────────────────────────────

    private void HandleGravity()
    {
        if (_isGrounded && _rb.linearVelocity.y <= 0f)
        {
            _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, -2f, _rb.linearVelocity.z);
        }
        else
        {
            float appliedGravity = gravity;
            if (_rb.linearVelocity.y < 0f)
                appliedGravity *= fallGravityMultiplier;

            _rb.AddForce(Vector3.up * appliedGravity, ForceMode.Acceleration);

            if (_rb.linearVelocity.y < -maxFallSpeed)
                _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, -maxFallSpeed, _rb.linearVelocity.z);
        }
    }

    // ── Cursor ─────────────────────────────────────────────────────────────

    private void HandleCursorToggle()
    {
        // NOTE: Escape-to-unlock is intentionally NOT handled here while the IDE
        // is open — InGameIDEController.Update() takes over Escape in that state
        // and uses it to close the IDE instead.
        if (Input.GetKeyDown(KeyCode.Escape))
            UnlockCursor();

        if (Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.None)
            LockCursor();
    }

    public void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    public void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    // ── Debug ──────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (_col == null) _col = GetComponent<CapsuleCollider>();
        if (_col == null) return;

        Vector3 checkPos = transform.position + _col.center
            + Vector3.down * (_col.height * 0.5f - _col.radius + groundCheckOffset);

        Gizmos.color = _isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(checkPos, groundCheckRadius);
    }
}