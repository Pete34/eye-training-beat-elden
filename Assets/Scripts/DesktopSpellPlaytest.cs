using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;


public sealed class DesktopSpellPlaytest : MonoBehaviour
{
    [SerializeField] private Camera playtestCamera;
    [SerializeField] private SpellCounterJudge counterJudge;
    [SerializeField] private SpellVolleySpawner volleySpawner;
    [SerializeField, Range(0f, 60f)] private float topDownAngleDegrees = 25f;
    [SerializeField] private bool startInTopDownView = true;
    [SerializeField, Min(0.1f)] private float desktopRefillDelaySeconds = 1f;
    private float nextDesktopVolleyTime;


    private Rect originalCameraRect;    private int lastScreenWidth;
    private int lastScreenHeight;

    private Quaternion originalCameraRotation;
    private bool desktopModeActive;
    private bool topDownViewActive;
    private Material desktopTrailMaterial;


private void Awake()
    {
        if (playtestCamera == null)
            playtestCamera = Camera.main;
        if (counterJudge == null)
            counterJudge = FindAnyObjectByType<SpellCounterJudge>();
        if (volleySpawner == null)
            volleySpawner = FindAnyObjectByType<SpellVolleySpawner>();

        desktopModeActive = !XRSettings.isDeviceActive;
        if (!desktopModeActive)
            return;

        nextDesktopVolleyTime = Time.time + desktopRefillDelaySeconds;
        if (playtestCamera == null)
            return;

        originalCameraRotation = playtestCamera.transform.rotation;
        originalCameraRect = playtestCamera.rect;
        ApplyDesktopViewport();
        topDownViewActive = startInTopDownView;
        ApplyCameraView();
    }

private void Update()
    {
        if (!desktopModeActive)
            return;

        if (playtestCamera != null && (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight))
            ApplyDesktopViewport();



        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.tKey.wasPressedThisFrame)
            ToggleTopDownView();

        var mouse = Mouse.current;
        if (mouse != null)
        {
            if (mouse.middleButton.wasPressedThisFrame)
                ToggleTopDownView();

            if (playtestCamera != null && counterJudge != null)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                    TryMouseCounter(SpellCounterAction.PalmBlock);
                else if (mouse.rightButton.wasPressedThisFrame)
                    TryMouseCounter(SpellCounterAction.Slash);
            }
        }

        if (volleySpawner == null)
            return;

        if (volleySpawner.ActiveProjectiles.Count > 0)
        {
            AddDesktopMotionTrails();
            nextDesktopVolleyTime = Time.time + desktopRefillDelaySeconds;
            return;
        }

        if (Time.time >= nextDesktopVolleyTime && volleySpawner.SpawnVolley())
            nextDesktopVolleyTime = Time.time + desktopRefillDelaySeconds;
    }

    private void ApplyDesktopViewport()
    {
        const float targetAspect = 16f / 9f;
        var screenAspect = (float)Screen.width / Mathf.Max(1f, Screen.height);
        if (screenAspect <= targetAspect)
            return;

        var viewportWidth = targetAspect / screenAspect;
        var expectedRect = new Rect((1f - viewportWidth) * 0.5f, 0f, viewportWidth, 1f);
        if (playtestCamera.rect != expectedRect)
            playtestCamera.rect = expectedRect;
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
    }

private void ToggleTopDownView()
    {
        topDownViewActive = !topDownViewActive;
        ApplyCameraView();
    }

    private void ApplyCameraView()
    {
        if (topDownViewActive)
        {
            var horizontalForward = Vector3.ProjectOnPlane(originalCameraRotation * Vector3.forward, Vector3.up).normalized;
            var downwardForward = (horizontalForward - Vector3.up * Mathf.Tan(topDownAngleDegrees * Mathf.Deg2Rad)).normalized;
            playtestCamera.transform.rotation = Quaternion.LookRotation(downwardForward, Vector3.up);
        }
        else
        {
            playtestCamera.transform.rotation = originalCameraRotation;
        }
    }

    private void TryMouseCounter(SpellCounterAction action)
    {
        var playerTools = FindAnyObjectByType<DesktopPlayerControls>();
        if (playerTools == null)
            return;

        if (action == SpellCounterAction.Slash)
            playerTools.TriggerSlashAnimation();
        else
            playerTools.TriggerBlockAnimation();
    }

private void AddDesktopMotionTrails()
    {
        if (volleySpawner == null)
            return;

        if (desktopTrailMaterial == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
                desktopTrailMaterial = new Material(shader);
        }

        var projectiles = volleySpawner.ActiveProjectiles;
        for (var index = 0; index < projectiles.Count; index++)
        {
            var projectile = projectiles[index];
            if (projectile == null || projectile.GetComponent<TrailRenderer>() != null)
                continue;

            var trail = projectile.gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = desktopTrailMaterial;
            trail.time = 0.75f;
            trail.startWidth = 0.04f;
            trail.endWidth = 0.006f;
            trail.minVertexDistance = 0.005f;
            trail.startColor = projectile.GlyphType == SpellGlyphType.ShieldRune
                ? new Color(1f, 0.72f, 0.25f, 0.95f)
                : new Color(0.35f, 0.9f, 1f, 0.95f);
            trail.endColor = new Color(0.35f, 0.9f, 1f, 0f);
        }
    }

private void OnDestroy()
    {
        if (desktopTrailMaterial != null)
            Destroy(desktopTrailMaterial);
        if (playtestCamera != null)
            playtestCamera.rect = originalCameraRect;
    }


}
