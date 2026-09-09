// RobotController.cs
// Handles all Unity-side robot movement and animation.
// Implements IScriptableDevice so ScriptRunner can use it automatically.
// The GameObject name is used as the device name — make it unique in the scene.

using System.Collections;
using UnityEngine;

public class RobotController : MonoBehaviour, IScriptableDevice
{
    // ── IScriptableDevice ──────────────────────────────────────────────────

    public string DeviceName   => gameObject.name; // e.g. "Robot_A"
    public string DeviceType   => "robot";
    public string VariableName => "robot";         // player writes: robot.move(1)
    public bool   IsAnimating  { get; private set; }

    public BaseDeviceAPI CreateAPI(ScriptRunner runner)
        => new RobotAPI(this, runner);

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void OnEnable()  => DeviceRegistry.Register(this);
    private void OnDisable() => DeviceRegistry.Unregister(DeviceName);

    // ── Actions (called by RobotAPI via ScriptRunner.EnqueueAndWait) ───────

    public void MoveForward() => StartCoroutine(MoveCoroutine(transform.forward));
    public void MoveBack()    => StartCoroutine(MoveCoroutine(-transform.forward));
    public void TurnLeft()    => StartCoroutine(RotateCoroutine(-90f));
    public void TurnRight()   => StartCoroutine(RotateCoroutine(90f));

    // ── Coroutines ─────────────────────────────────────────────────────────

    private IEnumerator MoveCoroutine(Vector3 direction)
    {
        IsAnimating = true;
        Vector3 start  = transform.position;
        Vector3 target = start + direction;
        float elapsed  = 0f;
        float duration = 0.4f; //0.4

        while (elapsed < duration)
        {
            transform.position = Vector3.Lerp(start, target, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.position = target;
        IsAnimating = false;
    }

    private IEnumerator RotateCoroutine(float degrees)
    {
        IsAnimating = true;
        Quaternion start  = transform.rotation;
        Quaternion target = start * Quaternion.Euler(0, degrees, 0);
        float elapsed  = 0f;
        float duration = 0.3f; //0.3

        while (elapsed < duration)
        {
            transform.rotation = Quaternion.Slerp(start, target, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.rotation = target;
        IsAnimating = false;
    }
}
