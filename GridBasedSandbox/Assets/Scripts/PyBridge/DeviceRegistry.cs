// DeviceRegistry.cs
// Static registry that tracks every active device in the scene.
// Devices self-register in OnEnable and unregister in OnDisable.
// No setup needed — just implement IScriptableDevice on a MonoBehaviour.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class DeviceRegistry
{
    private static readonly Dictionary<string, IScriptableDevice> _devices = new();

    // Called automatically by each device's OnEnable
    //
    // Handles a real problem that only shows up once devices start being
    // spawned at runtime (e.g. a robot placed from a "spawn egg" item):
    // every clone of the same prefab shares Unity's default "(Clone)" name,
    // and DeviceName is just gameObject.name — so a second spawned robot
    // would silently overwrite the first one's entry here, and
    // robot.send("Robot_A(Clone)", ...) would only ever reach whichever one
    // registered last. If the name's taken, rename the GameObject itself
    // rather than silently colliding — DeviceName reads gameObject.name
    // live, so this keeps working the moment it's set.
    public static void Register(IScriptableDevice device)
    {
        string name = device.DeviceName;

        if (_devices.ContainsKey(name) && device is Component c)
        {
            int suffix = 2;
            string baseName = name;
            while (_devices.ContainsKey($"{baseName}_{suffix}")) suffix++;

            c.gameObject.name = $"{baseName}_{suffix}";
            name = c.gameObject.name;
        }

        _devices[name] = device;
    }

    // Called automatically by each device's OnDisable
    public static void Unregister(string name)
        => _devices.Remove(name);

    // Get a device by its exact name
    public static IScriptableDevice Get(string name)
        => _devices.TryGetValue(name, out var d) ? d : null;

    // Get all registered device names
    public static string[] GetNames()
        => _devices.Keys.ToArray();

    // Get names of all devices of a specific type
    public static string[] GetNamesOfType(string type)
        => _devices.Values
            .Where(d => d.DeviceType == type)
            .Select(d => d.DeviceName)
            .ToArray();
}