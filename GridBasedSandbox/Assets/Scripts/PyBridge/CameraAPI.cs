// CameraAPI.cs
// Python-facing API for the camera device.
// Extends BaseDeviceAPI — player automatically gets send/receive/report/find_devices.
// Only add camera-specific methods here.

public class CameraAPI : BaseDeviceAPI
{
    private readonly CameraController _camera;

    public CameraAPI(CameraController camera)
        : base(camera.DeviceName, camera.DeviceType)
    {
        _camera = camera;
    }

    // ── Camera-specific Python methods ─────────────────────────────────────

    /// <summary>
    /// Scans and returns the zone or tile information currently observed by the camera.
    /// </summary>
    public string scan()
        => _camera.ScanArea();

    /// <summary>
    /// Returns true if motion was detected within the camera's field of view.
    /// </summary>
    public bool motion_detected()
        => _camera.IsMotionDetected();

    /// <summary>
    /// Returns the device name of the nearest visible scriptable device.
    /// </summary>
    public string nearest_device()
        => _camera.GetNearestDevice();
}
