using System;
using UnityEngine;

public enum SpellGlyphType
{
    SlashArc,
    SlashCross,
    ShieldRune
}

public sealed class SpellProjectile : MonoBehaviour
{
    [SerializeField] private Transform viewingEye;
    [SerializeField] private Vector3 flightDirection = Vector3.back;
    [SerializeField, Min(0.01f)] private float speedMetersPerSecond = 1.5f;    [SerializeField, Min(1f)] private float desktopSpeedMultiplier = 2f;    [SerializeField, Min(0f)] private float desktopLateralSpeedMetersPerSecond = 0.02f;
    private float desktopLateralDirection;    [SerializeField, Min(0.1f)] private float retaliationSpeedMetersPerSecond = 7f;
    private float speedOverrideMetersPerSecond;
    public bool IsRetaliating { get; private set; }



    [SerializeField, Min(0.01f)] private float worldSizeAtReferenceDistance = 0.12f;
    [SerializeField, Min(0.01f)] private float referenceDistanceMeters = 1f;    [SerializeField, Min(0f)] private float playerAimOffsetMeters = 0.65f;

    [SerializeField, Min(0f)] private float missBoundaryMeters = 0.45f;

    public event Action<SpellProjectile> Missed;

    public SpellGlyphType GlyphType { get; private set; }
    public float DepthMeters { get; private set; }
    public bool IsResolved { get; private set; }
    public float SpeedMetersPerSecond => speedOverrideMetersPerSecond > 0f
        ? speedOverrideMetersPerSecond
        : speedMetersPerSecond * (UnityEngine.XR.XRSettings.isDeviceActive ? 1f : desktopSpeedMultiplier);

    public float ArrivalTime
    {
        get
        {
            if (viewingEye == null)
                return float.PositiveInfinity;

            var closingSpeed = Vector3.Dot(flightDirection, -viewingEye.forward) * SpeedMetersPerSecond;
            if (closingSpeed <= 0.001f)
                return float.PositiveInfinity;

            return Time.time + Mathf.Max(0f, DepthMeters - missBoundaryMeters) / closingSpeed;
        }
    }

    public void Initialize(
        Transform eye,
        Vector3 direction,
        float speed,
        float sharedWorldSizeAtReferenceDistance,
        float referenceDistance,
        float missBoundary,
        SpellGlyphType glyphType)
    {
        viewingEye = eye;
        flightDirection = direction.normalized;
        speedMetersPerSecond = Mathf.Max(0.01f, speed);
        worldSizeAtReferenceDistance = Mathf.Max(0.01f, sharedWorldSizeAtReferenceDistance);
        referenceDistanceMeters = Mathf.Max(0.01f, referenceDistance);
        missBoundaryMeters = Mathf.Max(0f, missBoundary);
        GlyphType = glyphType;        desktopLateralDirection = UnityEngine.Random.value < 0.5f ? -1f : 1f;

        IsResolved = false;
        RefreshDepthAndApparentSize();
    }

private void Update()
    {
        if (viewingEye == null || IsResolved)
            return;

        var targetPoint = viewingEye.position - viewingEye.up * playerAimOffsetMeters;
        flightDirection = (targetPoint - transform.position).normalized;
        transform.position += flightDirection * SpeedMetersPerSecond * Time.deltaTime;        transform.rotation = Quaternion.LookRotation(-flightDirection, viewingEye.up);

        RefreshDepthAndApparentSize();

        if (DepthMeters <= missBoundaryMeters)
        {
            IsResolved = true;
            Missed?.Invoke(this);
        }
    }

public void BeginRetaliation(float speedMetersPerSecond)
    {
        if (IsResolved || IsRetaliating)
            return;

        IsRetaliating = true;
        speedOverrideMetersPerSecond = Mathf.Max(0.1f, speedMetersPerSecond);
        var spellRenderer = GetComponent<Renderer>();
        if (spellRenderer != null)
            spellRenderer.material.color = new Color(1f, 0.12f, 0.04f, 1f);
    }

public void MarkCountered()
    {
        if (IsResolved)
            return;

        IsResolved = true;
        var renderers = GetComponentsInChildren<Renderer>();
        for (var index = 0; index < renderers.Length; index++)
            renderers[index].enabled = false;

        var colliders = GetComponentsInChildren<Collider>();
        for (var index = 0; index < colliders.Length; index++)
            colliders[index].enabled = false;

        Destroy(gameObject, 0.1f);
    }

    private void RefreshDepthAndApparentSize()
    {
        // Project onto the viewing axis so lateral offsets do not change near/far order.
        DepthMeters = Vector3.Dot(transform.position - viewingEye.position, viewingEye.forward);
        var apparentSize = worldSizeAtReferenceDistance * Mathf.Max(0f, DepthMeters) / referenceDistanceMeters;
        transform.localScale = Vector3.one * apparentSize;
    }
}
