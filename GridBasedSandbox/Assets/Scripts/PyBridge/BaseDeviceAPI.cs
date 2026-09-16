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

    /// <summary>
    /// Sends a targeted message to a specific device by its registered device name.
    /// Python example: <c>robot.send("Camera_1", "look_here", "sector_b")</c>
    /// </summary>
    /// <param name="to">The destination device name.</param>
    /// <param name="topic">The message topic / subject.</param>
    /// <param name="data">Optional payload string.</param>
    public void send(string to, string topic, string data = "")
        => DeviceBus.Send(to, topic, data, _name);

    /// <summary>
    /// Broadcasts a message to all registered devices on the bus.
    /// Python example: <c>robot.broadcast("alert", "fire_detected")</c>
    /// </summary>
    /// <param name="topic">The message topic / subject.</param>
    /// <param name="data">Optional payload string.</param>
    public void broadcast(string topic, string data = "")
        => DeviceBus.Broadcast(topic, data, _name);

    // ── Receiving ──────────────────────────────────────────────────────────

    /// <summary>
    /// Checks for a waiting message without blocking.
    /// Returns <c>null</c> (None in Python) if no message is in the queue.
    /// </summary>
    /// <returns>A message object containing sender, topic, and data, or <c>null</c>.</returns>
    public object receive()
    {
        var msg = DeviceBus.Poll(_name);
        return msg == null ? null : new PythonMessage(msg);
    }

    /// <summary>
    /// Blocks execution until any message arrives for this device or until the timeout expires.
    /// </summary>
    /// <param name="timeoutSecs">Maximum duration to wait in seconds (default: 30s).</param>
    /// <returns>The received message object, or <c>null</c> if timed out.</returns>
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

    /// <summary>
    /// Blocks execution until a message matching a specific topic arrives for this device or until the timeout expires.
    /// </summary>
    /// <param name="topic">The topic filter to match.</param>
    /// <param name="timeoutSecs">Maximum duration to wait in seconds (default: 30s).</param>
    /// <returns>The received message object, or <c>null</c> if timed out.</returns>
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

    /// <summary>
    /// Publishes a status key-value pair for this device to the shared status board.
    /// </summary>
    /// <param name="key">Status property name.</param>
    /// <param name="value">Status value string.</param>
    public void report(string key, string value)
        => DeviceBus.SetStatus(_name, key, value);

    /// <summary>
    /// Reads a reported status value from another device.
    /// </summary>
    /// <param name="deviceName">The device name to inspect.</param>
    /// <param name="key">The status property key.</param>
    /// <returns>The reported value string, or an empty string if not found.</returns>
    public string get_status(string deviceName, string key)
        => DeviceBus.GetStatus(deviceName, key);

    // ── Discovery ──────────────────────────────────────────────────────────

    /// <summary>
    /// Lists all registered device names currently active on the device bus.
    /// </summary>
    public string[] find_devices()
        => DeviceRegistry.GetNames();

    /// <summary>
    /// Lists registered device names filtered by device type (e.g. "robot", "camera").
    /// </summary>
    /// <param name="deviceType">The device type to filter by.</param>
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
