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
    // the count actually taken) if a wall, unloaded chunk, airborne state,
    // or timeout rollback blocks the way.
    // When descending a staircase/slope, automatically waits for the robot to
    // touch down before initiating the next step so multi-step moves down slopes work smoothly.
    public int move(int steps = 1)
    {
        int completed = 0;
        for (int i = 0; i < steps; i++)
        {
            if (!_robot.Motor.PreserveAirMomentum)
            {
                if (!wait_until_grounded(3f)) break;
            }

            bool moved = _runner.EnqueueAndWait<bool>(() => _robot.MoveForward());
            if (!moved) break;
            bool success = _runner.EnqueueAndWait<bool>(() => _robot.Motor.LastStepSucceeded);
            if (!success) break;
            completed++;

            if (!_robot.Motor.PreserveAirMomentum)
            {
                bool stillGrounded = _runner.EnqueueAndWait<bool>(() => _robot.Motor.IsGrounded);
                if (!stillGrounded)
                {
                    if (!wait_until_grounded(3f)) break;
                }
            }
        }
        return completed;
    }

    // robot.move_back(1)
    public int move_back(int steps = 1)
    {
        int completed = 0;
        for (int i = 0; i < steps; i++)
        {
            if (!_robot.Motor.PreserveAirMomentum)
            {
                if (!wait_until_grounded(3f)) break;
            }

            bool moved = _runner.EnqueueAndWait<bool>(() => _robot.MoveBack());
            if (!moved) break;
            bool success = _runner.EnqueueAndWait<bool>(() => _robot.Motor.LastStepSucceeded);
            if (!success) break;
            completed++;

            if (!_robot.Motor.PreserveAirMomentum)
            {
                bool stillGrounded = _runner.EnqueueAndWait<bool>(() => _robot.Motor.IsGrounded);
                if (!stillGrounded)
                {
                    if (!wait_until_grounded(3f)) break;
                }
            }
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

    // ── Airborne Momentum (Cannonball Mode) ───────────────────────────────────

    // robot.set_preserve_momentum(True)  — if True, horizontal velocity is NOT
    // killed when the robot steps off a ledge, allowing it to fly through the
    // air like a cannonball projectile before snapping onto the grid on touchdown.
    public void set_preserve_momentum(bool enable)
        => _robot.Motor.PreserveAirMomentum = enable;

    public bool get_preserve_momentum()
        => _robot.Motor.PreserveAirMomentum;

    // ── Grounding ────────────────────────────────────────────────────────────

    // FIX 3 — exposes the grounded state and a blocking wait to Python scripts.
    //
    // WHY THIS IS NEEDED
    // RobotController.MoveForward/MoveBack now return false when the robot is
    // airborne (the fix for Bug 3). A script that calls robot.move() immediately
    // after the robot is placed may get 0 steps completed if physics hasn't
    // settled the robot onto the ground yet — confusing if the script doesn't
    // expect it. wait_until_grounded() gives scripts a clean way to wait for
    // landing before doing anything else:
    //
    //   robot.wait_until_grounded()
    //   robot.move(3)
    //
    // This is especially important for scripts that run on robot spawn (e.g.
    // autonomous patrol loops), where the robot might still be falling into its
    // spawn position when the script begins. Without this call the first move()
    // silently returns 0 and the loop logic gets confused.

    // robot.is_grounded()  — True if the robot is standing on solid ground.
    // Motor.IsGrounded is a plain bool flipped in FixedUpdate, not an engine
    // API call, so it's technically readable from any thread — but routing it
    // through EnqueueAndWait keeps every device read on the main thread and
    // consistent with all the other sensing methods.
    public bool is_grounded()
        => _runner.EnqueueAndWait<bool>(() => _robot.Motor.IsGrounded);

    // robot.wait_until_grounded()  — blocks the Python thread until the robot
    // is standing on solid ground, or until timeoutSecs elapses.
    // Returns True if it landed within the timeout, False if it timed out.
    //
    //   # Safe startup pattern for any autonomous script:
    //   if not robot.wait_until_grounded():
    //       print("robot didn't land in time — aborting")
    //   else:
    //       robot.move(3)
    public bool wait_until_grounded(float timeoutSecs = 5f)
    {
        var deadline = System.DateTime.Now.AddSeconds(timeoutSecs);
        while (System.DateTime.Now < deadline)
        {
            bool grounded = _runner.EnqueueAndWait<bool>(() => _robot.Motor.IsGrounded);
            if (grounded) return true;
            System.Threading.Thread.Sleep(50);
        }
        return false;
    }

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

    // ── Jumping ───────────────────────────────────────────────────────────────
    // Each jump method enqueues the action on the main thread and then blocks
    // the Python thread until the robot touches down (wait_until_grounded).
    // This makes jump() synchronous from Python's perspective — the script
    // only resumes after the robot has landed, just like move() only resumes
    // after the step is complete.

    // robot.jump()  — vertical hop in place. Returns True if the jump completed
    // successfully and the robot landed, False if blocked by ceiling or airborne state.
    public bool jump()
    {
        bool jumped = _runner.EnqueueAndWait<bool>(() => _robot.Jump());
        if (!jumped) return false;

        // Block until the robot lands so the next script line runs on solid ground.
        wait_until_grounded(5f);
        return _runner.EnqueueAndWait<bool>(() => _robot.Motor.LastStepSucceeded);
    }

    // robot.jump_forward()  — jump up and forward onto a 1-block-higher surface.
    // Pre-checks headroom above robot, landing cell ahead+up, and headroom at
    // landing. Returns True if the jump arc completed, False if blocked.
    public bool jump_forward()
    {
        bool jumped = _runner.EnqueueAndWait<bool>(() => _robot.JumpForward());
        if (!jumped) return false;

        // Wait for landing — the robot is mid-arc after JumpForward().
        wait_until_grounded(5f);
        return _runner.EnqueueAndWait<bool>(() => _robot.Motor.LastStepSucceeded);
    }

    // robot.jump_back()  — jump up and backward onto a 1-block-higher surface.
    // Same clearance checks as jump_forward() but in the opposite direction.
    public bool jump_back()
    {
        bool jumped = _runner.EnqueueAndWait<bool>(() => _robot.JumpBack());
        if (!jumped) return false;

        wait_until_grounded(5f);
        return _runner.EnqueueAndWait<bool>(() => _robot.Motor.LastStepSucceeded);
    }

    // robot.can_jump()  — returns True if the robot has enough headroom (Y+1 and Y+2)
    // to perform an in-place jump without bonking into a ceiling.
    public bool can_jump()
        => _runner.EnqueueAndWait<bool>(() => _robot.Sensor.CanJump());

    // robot.can_jump_ahead()  — returns True if the 4-cell clearance check
    // for a forward jump passes (takeoff headroom + landing + landing headroom).
    // Use before jump_forward() to decide whether to attempt it.
    public bool can_jump_ahead()
        => _runner.EnqueueAndWait<bool>(() => _robot.Sensor.CanJumpAhead());

    // robot.can_jump_behind()  — same check for a backward jump.
    public bool can_jump_behind()
        => _runner.EnqueueAndWait<bool>(() => _robot.Sensor.CanJumpBehind());

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