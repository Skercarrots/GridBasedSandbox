# ─────────────────────────────────────────────────────────────────────────────
# EXAMPLES.py
# Copy-paste any of these into the Inspector Test field to try them out.
# ─────────────────────────────────────────────────────────────────────────────


# ── Example 1: Basic robot (same as before) ────────────────────────────────
robot.move(3)
robot.turn_left()
robot.move(1)


# ── Example 2: Robot reports its state ────────────────────────────────────
robot.report("state", "working")
robot.move(2)
robot.report("ore_count", "5")
robot.report("state", "done")
print("done")


# ── Example 3: Robot sends a message to a camera ──────────────────────────
robot.move(1)
robot.send("Camera_1", "look_here", "sector_b")
print("sent alert to camera")


# ── Example 4: Robot waits for a command before acting ────────────────────
print("waiting for command...")
msg = robot.wait_for_message(10.0)   # times out after 10s

if msg:
    print(msg.topic, msg.data)
    if msg.topic == "go":
        robot.move(int(msg.data))
else:
    print("no command received")


# ── Example 5: Robot polls in a loop until told to stop ───────────────────
import random
running = True
while running:
    msg = robot.receive()
    if msg and msg.topic == "stop":
        running = False
    else:
        robot.move(random.randint(1, 3))
        robot.turn_left()


# ── Example 6: Robot discovers all cameras and broadcasts to them ──────────
cameras = robot.find_devices("camera")
print(f"found {len(cameras)} cameras: {cameras}")

for cam in cameras:
    robot.send(cam, "alert", "intruder_spotted")

robot.report("state", "alert_sent")


# ── Example 7: Camera script — reads motion and sends alert ───────────────
# (assign this to a ScriptRunner whose Device is a CameraController)
import math

print(f"camera: {camera.name}")

while True:
    if camera.motion_detected():
        zone = camera.scan()
        camera.broadcast("motion", zone)
        camera.report("last_alert", zone)
        print(f"motion in {zone}")


# ── Example 8: Controller script — orchestrates robot + camera ────────────
# (assign this to a dedicated controller ScriptRunner with no device,
#  or use a ComputerAPI once you build that device type)
# Shows the communication pattern between independently running scripts.
# Robot_A script:
#   robot.wait_for_topic("start")
#   robot.move(3)
#   robot.report("state", "done")

# Camera_1 script:
#   while True:
#       robot.broadcast("start", "")    # camera triggers all robots
#       import time; time.sleep(5)


# ── Example 9: Speed control ───────────────────────────────────────────────
robot.set_speed(0.5)       # slow — nice for watching it think
robot.move(1)
robot.set_speed(6.0)       # fast — nice once the logic is trusted
robot.move(1)
print(f"moving at {robot.get_speed()} units/sec")


# ── Example 10: Simple obstacle-aware wandering (a taste of pathfinding) ───
# Put this on a ScriptRunner with scriptTimeout set to 0 (unlimited) in the
# Inspector — this loop is meant to run forever, not for the 10s IDE default.
import random

while True:
    s = robot.surroundings()   # one call, all six directions

    if s.below and not s.ahead:
        # solid ground ahead and no wall in the way — go
        robot.move(1)
    else:
        # wall ahead, or a drop below — turn and try another direction
        if random.random() < 0.5:
            robot.turn_left()
        else:
            robot.turn_right()


# ── Example 11: Grounding check and safe spawn startup ────────────────────
# Essential for autonomous scripts on spawn: waits for physics to settle the
# robot on the ground so move() doesn't fail due to being airborne.
print(f"Checking ground status: {robot.is_grounded()}")
if not robot.wait_until_grounded(5.0):
    print("Warning: Robot did not settle on ground in time!")
else:
    print("Robot grounded and ready to move.")
    robot.move(2)


# ── Example 12: In-place jump with headroom check ─────────────────────────
# Verifies overhead clearance before jumping in place.
if robot.can_jump():
    print("Headroom clear, hopping in place...")
    success = robot.jump()
    print(f"Jump landed successfully: {success}")
else:
    print("Cannot jump: ceiling or obstacle overhead!")


# ── Example 13: Step-climbing onto ledges (jump_forward & jump_back) ──────
# Checks clearance and climbs up a 1-block high elevation step.
if robot.can_jump_ahead():
    print("Ledge ahead is clear, jumping up and forward!")
    robot.jump_forward()
else:
    print("Cannot jump forward: blocked or no valid landing cell.")

# Similarly, hopping backward onto an elevated cell behind:
if robot.can_jump_behind():
    print("Clearance behind is valid, jumping back!")
    robot.jump_back()


# ── Example 14: Step-counting & collision handling ────────────────────────
# move() and move_back() return the actual number of steps completed.
# If an obstacle or unloaded chunk blocks the path, it stops safely.
requested_steps = 5
steps_taken = robot.move(requested_steps)
if steps_taken < requested_steps:
    print(f"Path blocked after {steps_taken}/{requested_steps} steps!")
    robot.turn_left()
else:
    print(f"Successfully traveled all {steps_taken} steps.")


# ── Example 15: Directional proximity sensors ─────────────────────────────
# Precise single-direction voxel checks (main-thread synchronized).
print(f"Blocked Ahead:  {robot.is_blocked_ahead()}")
print(f"Blocked Behind: {robot.is_blocked_behind()}")
print(f"Blocked Left:   {robot.is_blocked_left()}")
print(f"Blocked Right:  {robot.is_blocked_right()}")
print(f"Blocked Above:  {robot.is_blocked_above()}")
print(f"Blocked Below:  {robot.is_blocked_below()}")


# ── Example 16: Preserve momentum (Cannonball Mode) ───────────────────────
# When enabled, horizontal velocity is NOT killed when walking off ledges,
# allowing the robot to fly forward through the air before snapping to grid.
robot.set_preserve_momentum(True)
print(f"Airborne momentum enabled: {robot.get_preserve_momentum()}")
robot.set_speed(8.0)
robot.move(4)   # Run off a cliff or ledge at high speed!
# Restore default safe ground behavior:
robot.set_preserve_momentum(False)


# ── Example 17: Waiting for a specific topic ──────────────────────────────
# Blocks execution until a message with the exact topic arrives (or timeout).
print("Waiting for 'deploy' signal...")
msg = robot.wait_for_topic("deploy", 15.0)
if msg:
    print(f"Received deploy command from {msg.sender} with data: {msg.data}")
    robot.move(2)
else:
    print("Timed out waiting for deploy topic.")


# ── Example 18: Reading status board across devices ───────────────────────
# Devices can query key-value statuses reported by other devices.
robot.report("battery", "98%")
robot.report("mode", "autonomous")

# Read camera or peer robot status:
cam_alert = robot.get_status("Camera_1", "last_alert")
print(f"Camera_1 last alert status: {cam_alert}")


# ── Example 19: Camera tracking nearest device ────────────────────────────
# (Assign to a ScriptRunner whose Device is a CameraController)
if camera.motion_detected():
    target = camera.nearest_device()
    zone = camera.scan()
    print(f"Motion in {zone}! Nearest device spotted: {target}")
    if target:
        camera.send(target, "halt", f"Spotted in {zone}")


# ── Example 20: All-terrain rover (Jumping + Sensing + Patrol) ────────────
# Set scriptTimeout to 0 (unlimited) in the Inspector.
# Wanders the world, climbs 1-block hills/ledges, avoids drops, and listens
# for stop commands.
import random

robot.wait_until_grounded()
robot.set_speed(3.0)
robot.report("state", "exploring")
print("Rover started...")

while True:
    # Check for incoming commands
    msg = robot.receive()
    if msg and msg.topic == "stop":
        robot.report("state", "idle")
        print("Rover stopped by remote command.")
        break

    s = robot.surroundings()

    if s.ahead:
        # Blocked in front — try jumping up onto the block if it's a 1-block step
        if robot.can_jump_ahead():
            print("Obstacle ahead: climbing 1-block step!")
            robot.jump_forward()
        else:
            # Wall is too high or blocked — turn around
            if random.random() < 0.5:
                robot.turn_left()
            else:
                robot.turn_right()
    elif not s.below:
        # Drop / cliff directly ahead — avoid falling off unless intended
        robot.turn_left()
    else:
        # Path ahead is clear and ground is solid
        robot.move(1)
