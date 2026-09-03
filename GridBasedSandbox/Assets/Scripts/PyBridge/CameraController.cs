// CameraController.cs
// Example of a second device type — a security camera.
// Shows exactly what you need to implement IScriptableDevice.
// Follow this same pattern for: servers, computers, doors, sensors, etc.

using UnityEngine;

public class CameraController : MonoBehaviour, IScriptableDevice
{
    // ── IScriptableDevice ──────────────────────────────────────────────────

    public string DeviceName   => gameObject.name; // e.g. "Camera_1"
    public string DeviceType   => "camera";
    public string VariableName => "camera";        // player writes: camera.scan()
    public bool   IsAnimating  => false;           // cameras don't have step animations

    public BaseDeviceAPI CreateAPI(ScriptRunner runner)
        => new CameraAPI(this);                    // note: runner not needed here

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void OnEnable()  => DeviceRegistry.Register(this);
    private void OnDisable() => DeviceRegistry.Unregister(DeviceName);

    // ── Camera logic (your game code goes here) ────────────────────────────

    // Returns what zone/tile the camera is currently watching
    public string ScanArea()
    {
        // Replace with your real game logic
        return "zone_A";
    }

    // Returns true if motion was detected this frame
    public bool IsMotionDetected()
    {
        // Replace with your real game logic
        return false;
    }

    // Returns the name of the nearest device the camera can see
    public string GetNearestDevice()
    {
        // Replace with your real game logic (e.g. Physics.OverlapSphere)
        return "";
    }
}
