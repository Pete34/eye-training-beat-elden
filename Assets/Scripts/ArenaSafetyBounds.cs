using Unity.XR.CoreUtils;
using UnityEngine;

// Builds a larger gameplay floor and collider walls so the player cannot fall out of bounds.
[DefaultExecutionOrder(-500)]
public sealed class ArenaSafetyBounds : MonoBehaviour
{
    [Header("Arena Size")]
    [SerializeField, Min(5f)] private float arenaWidthMeters = 60f;
    [SerializeField, Min(5f)] private float arenaDepthMeters = 60f;
    [SerializeField, Min(0.1f)] private float floorThicknessMeters = 1f;

    [Header("Boundary Walls")]
    [SerializeField, Min(0.5f)] private float wallHeightMeters = 6f;
    [SerializeField, Min(0.1f)] private float wallThicknessMeters = 1f;
    [SerializeField] private bool showBoundaryWalls = false;

    [Header("Fall Rescue")]
    [SerializeField] private bool rescueIfFallen = true;
    [SerializeField, Min(1f)] private float fallThresholdBelowFloor = 3f;
    [SerializeField, Min(0f)] private float rescueHeightAboveFloor = 1.8f;

    private const string RootName = "Arena Safety Bounds";
    private Transform floorTransform;
    private Transform[] wallTransforms;
    private XROrigin xrOrigin;
    private Camera mainCamera;
    private Vector3 originPosition;
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

        BuildArena();
    }

    private void LateUpdate()
    {
        if (!rescueIfFallen)
            return;

        var reference = GetPlayerReference();
        if (reference == null || floorTransform == null)
            return;

        var floorTopY = floorTransform.position.y + floorThicknessMeters * 0.5f;
        if (reference.position.y >= floorTopY - fallThresholdBelowFloor)
        {
            rescuedRecently = false;
            return;
        }

        RescuePlayer(floorTopY);
    }

    private void BuildArena()
    {
        floorTransform = CreateBox(
            "Safety Floor",
            new Vector3(originPosition.x, originPosition.y - floorThicknessMeters * 0.5f, originPosition.z),
            new Vector3(arenaWidthMeters, floorThicknessMeters, arenaDepthMeters),
            new Color(0.18f, 0.22f, 0.28f));

        var halfWidth = arenaWidthMeters * 0.5f;
        var halfDepth = arenaDepthMeters * 0.5f;
        var wallY = originPosition.y + wallHeightMeters * 0.5f;

        wallTransforms = new Transform[4];
        wallTransforms[0] = CreateWall(
            "North Wall",
            new Vector3(originPosition.x, wallY, originPosition.z + halfDepth + wallThicknessMeters * 0.5f),
            new Vector3(arenaWidthMeters + wallThicknessMeters * 2f, wallHeightMeters, wallThicknessMeters));
        wallTransforms[1] = CreateWall(
            "South Wall",
            new Vector3(originPosition.x, wallY, originPosition.z - halfDepth - wallThicknessMeters * 0.5f),
            new Vector3(arenaWidthMeters + wallThicknessMeters * 2f, wallHeightMeters, wallThicknessMeters));
        wallTransforms[2] = CreateWall(
            "East Wall",
            new Vector3(originPosition.x + halfWidth + wallThicknessMeters * 0.5f, wallY, originPosition.z),
            new Vector3(wallThicknessMeters, wallHeightMeters, arenaDepthMeters));
        wallTransforms[3] = CreateWall(
            "West Wall",
            new Vector3(originPosition.x - halfWidth - wallThicknessMeters * 0.5f, wallY, originPosition.z),
            new Vector3(wallThicknessMeters, wallHeightMeters, arenaDepthMeters));
    }

    private Transform CreateWall(string name, Vector3 position, Vector3 scale)
    {
        var wall = CreateBox(name, position, scale, new Color(0.7f, 0.15f, 0.15f));
        var renderer = wall.GetComponent<Renderer>();
        if (renderer != null)
            renderer.enabled = showBoundaryWalls;

        return wall;
    }

    private Transform CreateBox(string objectName, Vector3 position, Vector3 scale, Color color)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = objectName;
        box.transform.SetParent(transform, true);
        box.transform.position = position;
        box.transform.localScale = scale;

        var renderer = box.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material.color = color;

        return box.transform;
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

    private void RescuePlayer(float floorTopY)
    {
        if (rescuedRecently)
            return;

        rescuedRecently = true;

        if (xrOrigin == null)
            xrOrigin = FindAnyObjectByType<XROrigin>();

        if (xrOrigin != null)
        {
            xrOrigin.transform.position = new Vector3(originPosition.x, floorTopY + rescueHeightAboveFloor, originPosition.z);
        }

        ScreenFeedback.Flash("Out of bounds rescued", new Color(1f, 0.9f, 0.35f), 2f);
    }
}
