// IScriptableDevice.cs
// Every device in the game (robot, camera, server, computer...)
// must implement this interface to work with ScriptRunner and DeviceRegistry.

public interface IScriptableDevice
{
    string DeviceName   { get; } // unique name — use the GameObject name
    string DeviceType   { get; } // category: "robot", "camera", "server", etc.
    string VariableName { get; } // what the player types in Python: "robot", "camera", etc.
    bool   IsAnimating  { get; } // true while playing a step animation; false if not applicable

    // Factory — creates the Python-facing API object for this device
    BaseDeviceAPI CreateAPI(ScriptRunner runner);
}
