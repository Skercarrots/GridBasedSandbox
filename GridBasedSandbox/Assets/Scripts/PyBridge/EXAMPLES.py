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