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

    // camera.scan()  — returns the zone/tile the camera sees
    public string scan()
        => _camera.ScanArea();

    // camera.motion_detected()  — returns True/False
    public bool motion_detected()
        => _camera.IsMotionDetected();

    // camera.nearest_device()  — returns the name of the closest device visible
    public string nearest_device()
        => _camera.GetNearestDevice();

    // ── Add more camera methods below as needed ────────────────────────────
    // public void rotate(float angle) => _camera.Rotate(angle);
    // public void set_zoom(float zoom) => _camera.SetZoom(zoom);
}
