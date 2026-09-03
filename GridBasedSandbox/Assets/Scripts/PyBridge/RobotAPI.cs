// RobotAPI.cs
// The Python-facing API for a robot device.
// Extends BaseDeviceAPI — the player automatically gets send/receive/report/find_devices.
// Add new robot-specific methods here as your game grows.

public class RobotAPI : BaseDeviceAPI
{
    private readonly RobotController _robot;
    private readonly ScriptRunner    _runner;

    public RobotAPI(RobotController robot, ScriptRunner runner)
        : base(robot.DeviceName, robot.DeviceType)
    {
        _robot  = robot;
        _runner = runner;
    }

    // ── Movement ───────────────────────────────────────────────────────────
    // Each call blocks until the animation finishes before returning.

    // robot.move(3)  — moves forward N steps
    public void move(int steps)
    {
        for (int i = 0; i < steps; i++)
            _runner.EnqueueAndWait(() => _robot.MoveForward());
    }

    // robot.move_back(1)
    public void move_back(int steps)
    {
        for (int i = 0; i < steps; i++)
            _runner.EnqueueAndWait(() => _robot.MoveBack());
    }

    // robot.turn_left()
    public void turn_left()
        => _runner.EnqueueAndWait(() => _robot.TurnLeft());

    // robot.turn_right()
    public void turn_right()
        => _runner.EnqueueAndWait(() => _robot.TurnRight());

    // ── Add more robot actions below as needed ─────────────────────────────
    // public void mine()   => _runner.EnqueueAndWait(() => _robot.Mine());
    // public void plant()  => _runner.EnqueueAndWait(() => _robot.Plant());
    // public string scan() => _robot.ScanTileAhead();
}
