// RobotController.cs
// Handles Unity-side robot movement by delegating to two reusable, modular
// pieces instead of owning movement logic itself:
//   • VoxelGridMotor  — physics-based translation/rotation, exactly 1 voxel
//                        or 90° per command, speed-configurable.
//   • VoxelBodySensor — "is there a block ahead/above/below/left/right?"
// Implements IScriptableDevice so ScriptRunner can use it automatically.
// The GameObject name is used as the device name — make it unique in the scene.
//
// Any future device that walks the voxel grid (a delivery cart, a
// maintenance drone, whatever comes next) can reuse the same two components
// instead of reimplementing this — RobotController itself is now just the
// "robot-flavoured" glue: which direction is forward/back, and what
// IScriptableDevice reports.

using UnityEngine;

[RequireComponent(typeof(VoxelGridMotor))]
[RequireComponent(typeof(VoxelBodySensor))]
public class RobotController : MonoBehaviour, IScriptableDevice
{
    // ── IScriptableDevice ──────────────────────────────────────────────────

    public string DeviceName   => gameObject.name; // e.g. "Robot_A"
    public string DeviceType   => "robot";
    public string VariableName => "robot";         // player writes: robot.move(1)
    public bool   IsAnimating  => Motor.IsBusy;

    public BaseDeviceAPI CreateAPI(ScriptRunner runner)
        => new RobotAPI(this, runner);

    // ── Composed components ─────────────────────────────────────────────────

    public VoxelGridMotor  Motor  { get; private set; }
    public VoxelBodySensor Sensor { get; private set; }

    private void Awake()
    {
        Motor  = GetComponent<VoxelGridMotor>();
        Sensor = GetComponent<VoxelBodySensor>();
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void OnEnable()  => DeviceRegistry.Register(this);
    private void OnDisable() => DeviceRegistry.Unregister(DeviceName);

    // ── Actions (called by RobotAPI via ScriptRunner.EnqueueAndWait) ───────
    // Each returns whether the step was actually taken — false means blocked
    // (a solid voxel was in the way, the robot is airborne, or the motor is
    // already mid-move). RobotAPI uses this to stop a multi-step move() early
    // instead of grinding against a wall or walking off a ledge.

    public bool MoveForward()
    {
        // FIX 3 — guard against initiating a step while the robot is airborne.
        // Without this check, a script that runs immediately after the robot is
        // placed (before physics settles it onto the ground) or right after it
        // walks off a ledge could start a horizontal step mid-fall. The robot
        // would land offset from the grid and every subsequent sensor read would
        // be misaligned — the same class of bug as grid drift, but caused by
        // the physics state rather than floating-point accumulation.
        if (!Motor.IsGrounded) return false;

        if (Sensor.IsBlockedAhead()) return false;
        return Motor.StepInDirection(transform.forward);
    }

    public bool MoveBack()
    {
        // FIX 3 — same grounded guard as MoveForward().
        if (!Motor.IsGrounded) return false;

        if (Sensor.IsBlockedBehind()) return false;
        return Motor.StepInDirection(-transform.forward);
    }

    public bool TurnLeft()  => Motor.TurnBy(-90f);
    public bool TurnRight() => Motor.TurnBy(90f);
}