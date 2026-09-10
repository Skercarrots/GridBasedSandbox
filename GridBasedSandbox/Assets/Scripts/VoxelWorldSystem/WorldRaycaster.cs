using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  WorldRaycaster — the single source of truth for "what is the player
//  looking at right now". Fires exactly ONE Physics.Raycast per frame, from
//  the player camera through the mouse position, and caches the result for
//  anyone who needs it that frame:
//    • CellVisualizer      — where to draw the highlight box
//    • SimpleObjectPlacer  — where to spawn a decorative object / entity,
//                            and what PlacedItem (if any) is under the cursor
//    • GameInputManager    — interact / place / remove decisions
//
//  WHY THIS EXISTS
//  Before this, CellVisualizer, SimpleObjectPlacer, RayVisualizer, and
//  GridInteractorExample each ran their own Physics.Raycast(ray, ...) every
//  Update() — four raycasts a frame doing nearly the same query. Worse: each
//  of them resolved Camera.main themselves in Start(). Since the player is
//  now spawned asynchronously by WorldBootstrapper (after the loading-screen
//  wait), Camera.main is null at the time those Start() methods run, and
//  they silently never get a valid camera. VoxelBlockPlacer already avoids
//  this via SetPlayerCamera() called from WorldBootstrapper once the real
//  camera exists — this script follows the exact same pattern so every
//  interaction script gets wired up the same way, at the same moment.
//
//  RayVisualizer.cs and GridInteractorExample.cs are now redundant — their
//  jobs are fully covered by this + CellVisualizer. Recommend removing those
//  components from the scene so you're not paying for extra raycasts.
//
//  SETUP
//  • Put this on the same object as GameInputManager (or anywhere in the
//    scene — it's a singleton, like VoxelWorldManager).
//  • Set hittableLayers to whatever your terrain/chunk layer AND your
//    placed-object layer are, so one raycast covers both.
//  • Don't assign Player Camera in the Inspector — WorldBootstrapper calls
//    SetPlayerCamera() once the player prefab exists, same as it already
//    does for VoxelBlockPlacer.
// ─────────────────────────────────────────────────────────────────────────────

public class WorldRaycaster : MonoBehaviour
{
    public static WorldRaycaster Instance { get; private set; }

    [Header("Camera")]
    [Tooltip("Leave empty — WorldBootstrapper assigns this at runtime via SetPlayerCamera() once the player exists.")]
    [SerializeField] private Camera playerCamera;

    [Header("Raycast")]
    [SerializeField] private float maxDistance = 5f;

    [Tooltip("Layers this raycast can hit. Include BOTH your terrain/chunk layer and your placed-object layer, so this one raycast covers terrain aiming, decorative objects, and entities (robots, etc.) alike.")]
    [SerializeField] private LayerMask hittableLayers = ~0; // everything by default

    // Small nudge off the hit surface — reused by anyone converting a hit
    // point into a grid/voxel cell index, so a point sitting exactly on a
    // cell boundary doesn't get floored to the wrong side.
    public const float HitBias = 0.001f;

    public bool     HasHit    { get; private set; }
    public Vector3  Point     { get; private set; }
    public Vector3  Normal    { get; private set; }
    public Collider Collider  { get; private set; }
    public Ray      LastRay   { get; private set; }

    /// <summary>Hit point nudged slightly along the surface normal — use this
    /// (not Point) before converting to a grid/voxel cell index.</summary>
    public Vector3 AdjustedPoint => Point + Normal * HitBias;

    /// <summary>The PlacedItem under the cursor right now, if any — a
    /// decorative object, a robot, a button, whatever was placed/marked.</summary>
    public PlacedItem HoveredEntity =>
        HasHit ? Collider.GetComponentInParent<PlacedItem>() : null;

    public void SetPlayerCamera(Camera cam) => playerCamera = cam;

    private void Awake() => Instance = this;

    private void Update()
    {
        if (playerCamera == null)
        {
            HasHit = false;
            return;
        }

        LastRay = playerCamera.ScreenPointToRay(Input.mousePosition);
        HasHit = Physics.Raycast(LastRay, out RaycastHit hit, maxDistance, hittableLayers, QueryTriggerInteraction.Ignore);

        if (HasHit)
        {
            Point    = hit.point;
            Normal   = hit.normal;
            Collider = hit.collider;
        }
        else
        {
            Collider = null;
        }
    }
}
