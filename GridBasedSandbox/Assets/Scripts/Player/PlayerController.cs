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

    // ── Flight (Dev / Exploration) ──────────────────────────────────────────

    [Header("Flight (Dev / Creative)")]
    [Tooltip("Is flight mode enabled?")]
    [SerializeField] private bool isFlying = false;

    [Tooltip("Base flying movement velocity in units/second.")]
    [SerializeField] private float flySpeed = 16f;

    [Tooltip("Multiplier applied to flySpeed when holding Left Shift.")]
    [SerializeField] private float flySprintMultiplier = 2f;

    [Tooltip("If true, flying forward/backward moves along camera look direction instead of horizontal plane.")]
    [SerializeField] private bool cameraRelativeFly = false;

    [Tooltip("If true, passes through solid blocks while flying (No-Clip).")]
    [SerializeField] private bool noClip = false;

    // Double-tap Space detection for creative flight toggle
    private float _lastSpacePressTime = -1f;
    private const float DoubleTapTimeThreshold = 0.28f;

    // Events for UI sync
    public event System.Action<bool> OnFlightStateChanged;
    public event System.Action<float> OnFlySpeedChanged;
    public event System.Action<bool> OnNoClipChanged;

    public static PlayerController Instance { get; private set; }

    public bool IsFlying => isFlying;
    public float FlySpeed
    {
        get => flySpeed;
        set
        {
            flySpeed = Mathf.Max(0.5f, value);
            OnFlySpeedChanged?.Invoke(flySpeed);
        }
    }
    public float FlySprintMultiplier
    {
        get => flySprintMultiplier;
        set => flySprintMultiplier = Mathf.Max(1f, value);
    }
    public bool CameraRelativeFly
    {
        get => cameraRelativeFly;
        set => cameraRelativeFly = value;
    }
    public bool NoClip
    {
        get => noClip;
        set => SetNoClip(value);
    }

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
        Instance = this;

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

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void SetFlying(bool enable)
    {
        isFlying = enable;
        if (isFlying)
        {
            _jumpQueued = false;
            _isGrounded = false;
            if (_rb != null) _rb.linearVelocity = Vector3.zero;
            if (noClip && _col != null) _col.enabled = false;
        }
        else
        {
            if (_col != null) _col.enabled = true;
            if (_rb != null) _rb.linearVelocity = Vector3.zero;
        }
        OnFlightStateChanged?.Invoke(isFlying);
    }

    public void SetFlySpeed(float speed)
    {
        FlySpeed = speed;
    }

    public void SetNoClip(bool enable)
    {
        noClip = enable;
        if (_col != null)
        {
            _col.enabled = !(isFlying && noClip);
        }
        OnNoClipChanged?.Invoke(noClip);
    }

    public void SetCameraRelativeFly(bool enable)
    {
        cameraRelativeFly = enable;
    }

    // ── Unity loop ─────────────────────────────────────────────────────────

    private void Update()
    {
        // Mouse-look: already a no-op when the cursor is unlocked, so when the
        // IDE or Dev Menu is open (which unlocks the cursor) this automatically suppresses.
        HandleMouseLook();

        if (isFlying)
        {
            _isGrounded = false;
        }
        else
        {
            HandleGroundCheck();
        }

        // Check for Dev Menu hotkey (F1 or F4)
        if (Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.F4))
        {
            if (DevMenuController.Instance != null)
            {
                DevMenuController.Instance.ToggleMenu();
            }
            else
            {
                var existing = FindFirstObjectByType<DevMenuController>();
                if (existing != null)
                {
                    existing.ToggleMenu();
                }
                else
                {
                    var go = new GameObject("DevMenuController");
                    var dev = go.AddComponent<DevMenuController>();
                    dev.SetMenuOpen(true);
                }
            }
        }

        // Suppress gameplay input while IDE or Dev Menu is open
        if (GameState.IsAnyUIOpen) return;

        // Double-tap Space detection for quick flight toggle
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (Time.time - _lastSpacePressTime < DoubleTapTimeThreshold)
            {
                SetFlying(!isFlying);
                _lastSpacePressTime = -1f;
            }
            else
            {
                _lastSpacePressTime = Time.time;
            }
        }

        if (!isFlying)
        {
            if (Input.GetButtonDown("Jump") && _isGrounded)
                _jumpQueued = true;
        }

        HandleCursorToggle();
    }

    private void FixedUpdate()
    {
        if (isFlying)
        {
            HandleFlyingMovement();
        }
        else
        {
            HandleMovement();
            HandleGravity();
        }
    }

    private void HandleFlyingMovement()
    {
        if (GameState.IsAnyUIOpen)
        {
            _rb.linearVelocity = Vector3.zero;
            return;
        }

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        float upDown = 0f;
        if (Input.GetKey(KeyCode.Space)) upDown += 1f;
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C)) upDown -= 1f;

        bool sprinting = Input.GetKey(KeyCode.LeftShift);
        float currentSpeed = flySpeed * (sprinting ? flySprintMultiplier : 1f);

        Vector3 moveDir;
        if (cameraRelativeFly && cameraTransform != null)
        {
            moveDir = cameraTransform.forward * v + cameraTransform.right * h + Vector3.up * upDown;
        }
        else
        {
            Quaternion yawRotation = Quaternion.Euler(0f, _yaw, 0f);
            moveDir = yawRotation * Vector3.forward * v + yawRotation * Vector3.right * h + Vector3.up * upDown;
        }

        if (moveDir.sqrMagnitude > 1f)
            moveDir.Normalize();

        _rb.linearVelocity = moveDir * currentSpeed;
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
        if (GameState.IsAnyUIOpen)
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

    // ── Velocity & Teleport ────────────────────────────────────────────────
    
    /// <summary>Resets horizontal and vertical velocities, queued jumps, and momentum. Useful for respawns.</summary>
    public void ResetVelocity()
    {
        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
        _jumpQueued = false;
        _takeoffSpeed = walkSpeed;
    }

    /// <summary>Teleports the player to a target position and zeroes out velocity cleanly.</summary>
    public void Teleport(Vector3 position)
    {
        transform.position = position;
        if (_rb != null)
        {
            _rb.position = position;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
        _jumpQueued = false;
        _takeoffSpeed = walkSpeed;
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