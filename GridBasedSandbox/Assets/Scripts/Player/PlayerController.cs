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
    private float           _yaw;        // free-running look yaw, driven purely by Update() — never touches the Rigidbody
    private bool            _isGrounded;
    private bool            _jumpQueued;

    // ── Setup ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        _rb  = GetComponent<Rigidbody>();
        _col = GetComponent<CapsuleCollider>();

        // These four settings are the key to a jitter-free, stable voxel controller
        _rb.freezeRotation         = true;                              // physics won't tip the player over
        _rb.useGravity             = false;                             // we apply our own gravity
        _rb.interpolation          = RigidbodyInterpolation.Interpolate;// smooths camera between physics steps — eliminates jitter
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous; // prevents tunneling through thin blocks at high speed

        // Capsule sized to fit through 1-wide × 2-tall voxel gaps
        // Diameter = 0.6 (radius 0.3) fits inside a 1-unit-wide gap with clearance
        // Height  = 1.8 fits under a 2-unit ceiling with clearance
        _col.height = 1.8f;
        _col.radius = 0.3f;
        _col.center = new Vector3(0f, _col.height * 0.5f, 0f); //capsule sits on top of pivot (feet at ground)

        LockCursor();
    }

    // ── Unity loop ─────────────────────────────────────────────────────────

    private void Update()
    {
        // Mouse look runs entirely here, at full render framerate — it never
        // touches the Rigidbody, so it's immune to physics-tick jitter.
        HandleMouseLook();

        // Ground check in Update — checks before FixedUpdate so jump input is ready
        HandleGroundCheck();

        // Buffer jump input so it isn't dropped between Update and FixedUpdate
        if (Input.GetButtonDown("Jump") && _isGrounded)
            _jumpQueued = true;

        HandleCursorToggle();
    }

    private void FixedUpdate()
    {
        // Note: the Rigidbody's rotation is never touched, anywhere, by anything.
        // freezeRotation keeps it locked, and we no longer call MoveRotation on it.
        // A capsule collider is rotationally symmetric around Y, so this costs us
        // nothing physically, and it's what lets camera look be 100% Update()-driven
        // (see HandleMouseLook) instead of bottlenecked by the fixed physics tick.
        HandleMovement();
        HandleGravity();
    }

    // ── Mouse look ─────────────────────────────────────────────────────────

    private void HandleMouseLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        // GetAxisRaw = raw pixel delta, no Unity smoothing — matches Minecraft's direct feel
        float mouseX = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;

        // Accumulate yaw as a free-running value — never applied to the Rigidbody,
        // so it's not bottlenecked by the fixed physics tick or fought by interpolation.
        _yaw += mouseX;

        _pitch = Mathf.Clamp(_pitch - mouseY, -maxPitch, maxPitch);

        // Set world rotation directly (not localRotation) so this is correct
        // regardless of the parent Player's rotation, which now stays at identity.
        cameraTransform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    // ── Ground check ───────────────────────────────────────────────────────

    private void HandleGroundCheck()
    {
        // Place a sphere at the very bottom of the capsule, pushed down slightly.
        // CheckSphere is more reliable than SphereCast on flat voxel faces
        // because it has no direction bias.
        Vector3 checkPos = transform.position + _col.center
            + Vector3.down * (_col.height * 0.5f - _col.radius + groundCheckOffset);

        _isGrounded = Physics.CheckSphere(
            checkPos,
            groundCheckRadius,
            groundLayers,
            QueryTriggerInteraction.Ignore // never detect triggers as ground
        );
    }

    // ── Movement ───────────────────────────────────────────────────────────

    private void HandleMovement()
    {
        // GetAxisRaw = instant response, no input smoothing
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        bool sprinting = Input.GetKey(KeyCode.LeftShift) && v > 0f; // sprint forward only
        float speed = sprinting ? sprintSpeed : walkSpeed;

        // Build move direction relative to where the player is looking (yaw only —
        // pitch shouldn't tilt movement). The Player transform itself no longer
        // rotates, so we derive the basis from _yaw directly rather than
        // transform.right/transform.forward.
        Quaternion yawRotation = Quaternion.Euler(0f, _yaw, 0f);
        Vector3 forward = yawRotation * Vector3.forward;
        Vector3 right   = yawRotation * Vector3.right;
        Vector3 inputDir = right * h + forward * v;
        if (inputDir.sqrMagnitude > 1f) inputDir.Normalize(); // prevent diagonal speed boost
        Vector3 targetVelocity = inputDir * speed;

        if (_isGrounded)
        {
            // Set horizontal velocity directly — instant, snappy, Minecraft-like
            // Y velocity is preserved so gravity and jumps still work
            _rb.linearVelocity = new Vector3(
                targetVelocity.x,
                _rb.linearVelocity.y,
                targetVelocity.z
            );

            // Apply jump using the physics formula: v = sqrt(2 * |g| * h)
            // This guarantees reaching exactly jumpHeight regardless of gravity value
            if (_jumpQueued)
            {
                float jumpVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * jumpHeight);
                _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, jumpVelocity, _rb.linearVelocity.z);
            }
        }
        else
        {
            // In air: minimal control — just a gentle nudge toward the target
            // Prevents full mid-air redirection (more realistic, less exploitable)
            Vector3 currentH = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            Vector3 diff = targetVelocity - currentH;
            _rb.AddForce(new Vector3(diff.x, 0f, diff.z) * 0.08f, ForceMode.VelocityChange);

            // Cap horizontal speed so the player can't exceed walk speed in air
            if (currentH.magnitude > walkSpeed)
            {
                currentH = currentH.normalized * walkSpeed;
                _rb.linearVelocity = new Vector3(currentH.x, _rb.linearVelocity.y, currentH.z);
            }
        }

        _jumpQueued = false; // always clear — prevents mid-air jump if grounded status changed
    }

    // ── Custom gravity ─────────────────────────────────────────────────────

    private void HandleGravity()
    {
        if (_isGrounded && _rb.linearVelocity.y <= 0f)
        {
            // While grounded, pin downward velocity to a small constant.
            // This is the fix for "floating between pillars" — without it,
            // the player's downward velocity can become 0 from pillar contacts,
            // causing them to levitate. -2 keeps the player pressed to the ground.
            _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, -2f, _rb.linearVelocity.z);
        }
        else
        {
            // Falling gets extra gravity on top of the base value — this asymmetry
            // (heavier down than up) is the main thing that reads as "weight."
            // Rising is untouched, so jumpHeight is still hit exactly at the apex;
            // only the descent afterward gets noticeably snappier/heavier.
            float appliedGravity = gravity;
            if (_rb.linearVelocity.y < 0f)
                appliedGravity *= fallGravityMultiplier;

            _rb.AddForce(Vector3.up * appliedGravity, ForceMode.Acceleration);

            // Clamp to terminal velocity so long drops don't keep accelerating
            // forever — Minecraft's fall speed caps out too.
            if (_rb.linearVelocity.y < -maxFallSpeed)
                _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, -maxFallSpeed, _rb.linearVelocity.z);
        }
    }

    // ── Cursor ─────────────────────────────────────────────────────────────

    private void HandleCursorToggle()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            UnlockCursor();

        if (Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.None)
            LockCursor();
    }

    private void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    private void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    // ── Debug ──────────────────────────────────────────────────────────────

    // Draws the ground check sphere in the Scene view so you can see exactly
    // where and how big the ground detection is. Only visible in the editor.
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