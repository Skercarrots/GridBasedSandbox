// BaseDeviceAPI.cs
// The Python-facing base class that every device API extends.
// Provides all the messaging and discovery methods automatically.
// The player can call these from any device script:
//   robot.send("Camera_1", "alert", "intruder")
//   msg = robot.wait_for_message()
//   devices = robot.find_devices("camera")

using System.Threading;

public abstract class BaseDeviceAPI
{
    protected readonly string _name;
    protected readonly string _type;

    protected BaseDeviceAPI(string name, string type)
    {
        _name = name;
        _type = type;
    }

    // ── Identity ───────────────────────────────────────────────────────────

    public string name => _name;
    public string type => _type;

    // ── Messaging ──────────────────────────────────────────────────────────

    // Send a message to a specific device by name
    //   robot.send("Camera_1", "look_here", "sector_b")
    public void send(string to, string topic, string data = "")
        => DeviceBus.Send(to, topic, data, _name);

    // Send the same message to all devices
    //   robot.broadcast("alert", "fire_detected")
    public void broadcast(string topic, string data = "")
        => DeviceBus.Broadcast(topic, data, _name);

    // ── Receiving ──────────────────────────────────────────────────────────

    // Check for a waiting message — returns None if nothing is waiting
    //   msg = robot.receive()
    //   if msg: print(msg.topic, msg.data)
    public object receive()
    {
        var msg = DeviceBus.Poll(_name);
        return msg == null ? null : new PythonMessage(msg);
    }

    // Block until a message arrives or timeout (default 30s)
    //   msg = robot.wait_for_message()
    //   msg = robot.wait_for_message(5.0)  # timeout after 5 seconds
    public object wait_for_message(float timeoutSecs = 30f)
    {
        var deadline = System.DateTime.Now.AddSeconds(timeoutSecs);
        while (System.DateTime.Now < deadline)
        {
            var msg = DeviceBus.Poll(_name);
            if (msg != null) return new PythonMessage(msg);
            Thread.Sleep(50);
        }
        return null;
    }

    // Block until a specific topic arrives
    //   robot.wait_for_topic("start")
    public object wait_for_topic(string topic, float timeoutSecs = 30f)
    {
        var deadline = System.DateTime.Now.AddSeconds(timeoutSecs);
        while (System.DateTime.Now < deadline)
        {
            var msg = DeviceBus.Poll(_name);
            if (msg != null && msg.Topic == topic) return new PythonMessage(msg);
            Thread.Sleep(50);
        }
        return null;
    }

    // ── Status board ───────────────────────────────────────────────────────

    // Report this device's status — other scripts can read it
    //   robot.report("state", "done")
    //   robot.report("ore_count", str(count))
    public void report(string key, string value)
        => DeviceBus.SetStatus(_name, key, value);

    // Read any device's reported status
    //   status = robot.get_status("Robot_B", "state")
    public string get_status(string deviceName, string key)
        => DeviceBus.GetStatus(deviceName, key);

    // ── Discovery ──────────────────────────────────────────────────────────

    // List all registered device names
    //   all_devices = robot.find_devices()
    public string[] find_devices()
        => DeviceRegistry.GetNames();

    // List registered device names filtered by type
    //   cameras = robot.find_devices("camera")
    //   robots  = robot.find_devices("robot")
    public string[] find_devices(string deviceType)
        => DeviceRegistry.GetNamesOfType(deviceType);

    // ── Python message wrapper ─────────────────────────────────────────────

    // Makes DeviceBus.Message accessible as msg.sender, msg.topic, msg.data in Python
    public class PythonMessage
    {
        private readonly DeviceBus.Message _msg;
        public PythonMessage(DeviceBus.Message msg) => _msg = msg;

        public string sender => _msg.From;
        public string topic  => _msg.Topic;
        public string data   => _msg.Data;

        public override string ToString()
            => $"[{_msg.Topic}] from {_msg.From}: {_msg.Data}";
    }
}
