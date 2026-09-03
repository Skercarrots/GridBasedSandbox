// DeviceBus.cs
// Universal message bus — one bus for all device types.
// Robots, cameras, servers, computers all share this same bus.
// Thread-safe: Python threads can safely call Send/Poll from background threads.

using System.Collections.Concurrent;

public static class DeviceBus
{
    // A message sent from one device to another
    public class Message
    {
        public string From;  // sender's DeviceName
        public string Topic; // what kind of message this is
        public string Data;  // payload — use plain strings or simple key=value format
    }

    // Per-device inboxes — each device has its own message queue
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<Message>> _inboxes = new();

    // Shared status board — any device can read/write key-value pairs here
    private static readonly ConcurrentDictionary<string, string> _statusBoard = new();

    // ── Sending ────────────────────────────────────────────────────────────

    // Send a message directly to one device
    public static void Send(string to, string topic, string data = "", string from = "")
    {
        var msg = new Message { From = from, Topic = topic, Data = data };
        _inboxes.GetOrAdd(to, _ => new ConcurrentQueue<Message>()).Enqueue(msg);
    }

    // Send the same message to every registered device except the sender
    public static void Broadcast(string topic, string data = "", string from = "")
    {
        foreach (var name in DeviceRegistry.GetNames())
            if (name != from)
                Send(name, topic, data, from);
    }

    // ── Receiving ──────────────────────────────────────────────────────────

    // Check for a waiting message — returns null if inbox is empty
    public static Message Poll(string deviceName)
    {
        if (_inboxes.TryGetValue(deviceName, out var q) && q.TryDequeue(out var msg))
            return msg;
        return null;
    }

    // ── Status board ───────────────────────────────────────────────────────

    // Report a status value (e.g. "state" = "done", "ore_count" = "3")
    public static void SetStatus(string deviceName, string key, string value)
        => _statusBoard[$"{deviceName}.{key}"] = value;

    // Read a status value — returns "" if not set
    public static string GetStatus(string deviceName, string key)
    {
        _statusBoard.TryGetValue($"{deviceName}.{key}", out var val);
        return val ?? "";
    }

    // ── Cleanup ────────────────────────────────────────────────────────────

    // Clear all messages and status — call this when resetting the level
    public static void Clear()
    {
        _inboxes.Clear();
        _statusBoard.Clear();
    }
}
