// DeviceRegistry.cs
// Static registry that tracks every active device in the scene.
// Devices self-register in OnEnable and unregister in OnDisable.
// No setup needed — just implement IScriptableDevice on a MonoBehaviour.

using System.Collections.Generic;
using System.Linq;

public static class DeviceRegistry
{
    private static readonly Dictionary<string, IScriptableDevice> _devices = new();

    // Called automatically by each device's OnEnable
    public static void Register(IScriptableDevice device)
        => _devices[device.DeviceName] = device;

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
