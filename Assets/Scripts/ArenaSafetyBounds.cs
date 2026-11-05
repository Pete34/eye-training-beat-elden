using Unity.XR.CoreUtils;
using UnityEngine;

// Walls follow the visible platform so the player cannot walk off the edge.
[DefaultExecutionOrder(-500)]
public sealed class ArenaSafetyBounds : MonoBehaviour
{
    [Header("Boundary Walls")]
    [SerializeField, Min(0.5f)] private float wallHeightMeters = 2.2f;
    [SerializeField, Min(0.1f)] private float wallThicknessMeters = 0.4f;
    [SerializeField, Min(0f)] private float wallInsetMeters = 0.08f;

    [Header("Fall Rescue")]
    [SerializeField] private bool rescueIfFallen = true;
    [SerializeField, Min(1f)] private float fallThresholdBelowFloor = 3f;
    [SerializeField, Min(0f)] private float rescueHeightAboveFloor = 1.8f;

    private const string RootName = "Arena Safety Bounds";
    private const string PlatformName = "Prototype Floor";

    private XROrigin xrOrigin;
    private Camera mainCamera;
    private Vector3 originPosition;
    private float floorTopY;
    private bool hasWalkableRect;
    private Vector3 walkableMin;
    private Vector3 walkableMax;
    private bool rescuedRecently;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindAnyObjectByType<ArenaSafetyBounds>() != null)
            return;

        var root = new GameObject(RootName);
        root.AddComponent<ArenaSafetyBounds>();
    }

    private void Awake()
    {
        xrOrigin = FindAnyObjectByType<XROrigin>();
        mainCamera = Camera.main;
        originPosition = xrOrigin != null ? xrOrigin.transform.position : Vector3.zero;
        BuildWalls();
    }

    private void LateUpdate()
    {
        if (!rescueIfFallen || !hasWalkableRect)
            return;

        var reference = GetPlayerReference();
        if (reference == null)
            return;

        if (reference.position.y >= floorTopY - fallThresholdBelowFloor)
        {
            rescuedRecently = false;
            return;
        }

        RescuePlayer();
    }

    public Vector3 ClampToWalkable(Vector3 worldPosition, float padding)
    {
        if (!hasWalkableRect)
            return worldPosition;

        var inset = Mathf.Max(0f, padding);
        return new Vector3(
            Mathf.Clamp(worldPosition.x, walkableMin.x + inset, walkableMax.x - inset),
            worldPosition.y,
            Mathf.Clamp(worldPosition.z, walkableMin.z + inset, walkableMax.z - inset));
    }

    private void BuildWalls()
    {
        var platform = GameObject.Find(PlatformName);
        var renderer = platform != null ? platform.GetComponent<Renderer>() : null;
        if (renderer == null)
        {
            Debug.LogWarning("Prototype Floor was not found. Platform walls were not built.", this);
            return;
        }

        var bounds = renderer.bounds;
        floorTopY = bounds.max.y;
        var inset = Mathf.Min(wallInsetMeters, bounds.extents.x, bounds.extents.z);
        walkableMin = new Vector3(bounds.min.x + inset, floorTopY, bounds.min.z + inset);
        walkableMax = new Vector3(bounds.max.x - inset, floorTopY, bounds.max.z - inset);
        hasWalkableRect = walkableMax.x > walkableMin.x && walkableMax.z > walkableMin.z;
        if (!hasWalkableRect)
            return;

        var width = walkableMax.x - walkableMin.x;
        var depth = walkableMax.z - walkableMin.z;
        var wallY = floorTopY + wallHeightMeters * 0.5f;
        var color = new Color(0.42f, 0.4f, 0.38f);
        var alongX = new Vector3(width + wallThicknessMeters * 2f, wallHeightMeters, wallThicknessMeters);
        var alongZ = new Vector3(wallThicknessMeters, wallHeightMeters, depth);

        CreateWall("North Wall", new Vector3(bounds.center.x, wallY, walkableMax.z + wallThicknessMeters * 0.5f), alongX, color);
        CreateWall("South Wall", new Vector3(bounds.center.x, wallY, walkableMin.z - wallThicknessMeters * 0.5f), alongX, color);
        CreateWall("East Wall", new Vector3(walkableMax.x + wallThicknessMeters * 0.5f, wallY, bounds.center.z), alongZ, color);
        CreateWall("West Wall", new Vector3(walkableMin.x - wallThicknessMeters * 0.5f, wallY, bounds.center.z), alongZ, color);
    }

    private void CreateWall(string objectName, Vector3 position, Vector3 scale, Color color)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = objectName;
        wall.transform.SetParent(transform, true);
        wall.transform.position = position;
        wall.transform.localScale = scale;

        var renderer = wall.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material.color = color;
    }

    private Transform GetPlayerReference()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera != null)
            return mainCamera.transform;

        if (xrOrigin == null)
            xrOrigin = FindAnyObjectByType<XROrigin>();

        return xrOrigin != null ? xrOrigin.transform : null;
    }

    private void RescuePlayer()
    {
        if (rescuedRecently)
            return;

        rescuedRecently = true;

        if (xrOrigin == null)
            xrOrigin = FindAnyObjectByType<XROrigin>();

        if (xrOrigin != null)
            xrOrigin.transform.position = new Vector3(originPosition.x, floorTopY + rescueHeightAboveFloor, originPosition.z);

        ScreenFeedback.Flash("Out of bounds rescued", new Color(1f, 0.9f, 0.35f), 2f);
    }
}
