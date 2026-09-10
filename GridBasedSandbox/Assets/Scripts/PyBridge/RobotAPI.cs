// RobotAPI.cs
// The Python-facing API for a robot device.
// Extends BaseDeviceAPI — the player automatically gets send/receive/report/find_devices.
//
// move()/move_back() now return the number of steps actually completed —
// existing scripts that ignore the return value (e.g. `robot.move(3)`) keep
// working exactly as before, but a step stops early if a voxel is in the
// way instead of grinding against it. That, plus set_speed()/set_turn_speed()
// and the is_blocked_*()/surroundings() sensing methods below, are what a
// future pathfinding script needs to explore freely without a human watching.

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
    // Each call blocks until the physics move finishes before returning.

    // robot.move(3)  — moves forward up to N steps; stops early (and returns
    // the count actually taken) if something blocks the way.
    public int move(int steps = 1)
    {
        int completed = 0;
        for (int i = 0; i < steps; i++)
        {
            bool moved = _runner.EnqueueAndWait<bool>(() => _robot.MoveForward());
            if (!moved) break;
            completed++;
        }
        return completed;
    }

    // robot.move_back(1)
    public int move_back(int steps = 1)
    {
        int completed = 0;
        for (int i = 0; i < steps; i++)
        {
            bool moved = _runner.EnqueueAndWait<bool>(() => _robot.MoveBack());
            if (!moved) break;
            completed++;
        }
        return completed;
    }

    // robot.turn_left()
    public void turn_left()
        => _runner.EnqueueAndWait<bool>(() => _robot.TurnLeft());

    // robot.turn_right()
    public void turn_right()
        => _runner.EnqueueAndWait<bool>(() => _robot.TurnRight());

    // ── Speed control ────────────────────────────────────────────────────────
    // Tune how fast this robot moves/turns — handy to slow down for a demo,
    // or speed up once a pathfinding script is trusted to run unsupervised.

    // robot.set_speed(4.0)  — units (voxels) per second per move() step
    public void set_speed(float unitsPerSecond)
        => _robot.Motor.SetMoveSpeed(unitsPerSecond);

    // robot.set_turn_speed(360)  — degrees per second for turn_left/turn_right
    public void set_turn_speed(float degreesPerSecond)
        => _robot.Motor.SetTurnSpeed(degreesPerSecond);

    public float get_speed()      => _robot.Motor.MoveSpeed;
    public float get_turn_speed() => _robot.Motor.TurnSpeed;

    // ── World sensing (for pathfinding) ─────────────────────────────────────
    // Cheap, exact voxel lookups — no raycasts. Check before you move()
    // instead of finding out by bumping into things.
    //
    // These MUST go through EnqueueAndWait like the movement methods do:
    // VoxelBodySensor reads transform.position/forward/right, which are
    // Unity engine API calls and can only happen on the main thread. Player
    // scripts run on a background thread, so calling the sensor directly
    // from here throws "get_transform can only be called from the main
    // thread" the moment a script polls it in a loop.

    public bool is_blocked_ahead()
        => _runner.EnqueueAndWait<bool>(() => _robot.Sensor.IsBlockedAhead());

    public bool is_blocked_behind()
        => _runner.EnqueueAndWait<bool>(() => _robot.Sensor.IsBlockedBehind());

    public bool is_blocked_left()
        => _runner.EnqueueAndWait<bool>(() => _robot.Sensor.IsBlockedLeft());

    public bool is_blocked_right()
        => _runner.EnqueueAndWait<bool>(() => _robot.Sensor.IsBlockedRight());

    public bool is_blocked_above()
        => _runner.EnqueueAndWait<bool>(() => _robot.Sensor.IsBlockedAbove());

    public bool is_blocked_below()
        => _runner.EnqueueAndWait<bool>(() => _robot.Sensor.IsBlockedBelow());

    // robot.surroundings() — all six checks in one call, cheaper than six
    // separate round-trips inside a tight exploration loop.
    //   s = robot.surroundings()
    //   if not s.ahead: robot.move(1)
    public object surroundings()
        => new PythonSurroundings(_runner.EnqueueAndWait(() => _robot.Sensor.Scan()));

    // Motor.IsGrounded is just a plain bool field flipped in FixedUpdate, not
    // an engine API call, so it won't throw — but reading it from another
    // thread without synchronization is still a data race. Routing it through
    // the same queue costs nothing and keeps every device read consistent.
    public bool is_grounded()
        => _runner.EnqueueAndWait<bool>(() => _robot.Motor.IsGrounded);

    // ── Add more robot actions below as needed ─────────────────────────────
    // public void mine()   => _runner.EnqueueAndWait(() => _robot.Mine());
    // public void plant()  => _runner.EnqueueAndWait(() => _robot.Plant());
    // public string scan() => _robot.ScanTileAhead();

    // ── Python surroundings wrapper ──────────────────────────────────────────
    // Makes VoxelBodySensor.Surroundings accessible as s.ahead, s.left, etc. in Python.
    public class PythonSurroundings
    {
        private readonly VoxelBodySensor.Surroundings _s;
        public PythonSurroundings(VoxelBodySensor.Surroundings s) => _s = s;

        public bool ahead  => _s.Ahead;
        public bool behind => _s.Behind;
        public bool left   => _s.Left;
        public bool right  => _s.Right;
        public bool above  => _s.Above;
        public bool below  => _s.Below;

        public override string ToString()
            => $"ahead={_s.Ahead} behind={_s.Behind} left={_s.Left} right={_s.Right} above={_s.Above} below={_s.Below}";
    }
}