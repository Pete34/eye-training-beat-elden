using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
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
    [SerializeField, Min(0.01f)] private float referenceDistanceMeters = 1f;
    [SerializeField, Min(0f)] private float playerAimOffsetMeters = 0.65f;
    [SerializeField, Min(0f)] private float slotCatchUpMetersPerSecond = 0.35f;
    [SerializeField, Min(0f)] private float hangDurationSeconds = 0.6f;

    [SerializeField, Min(0f)] private float missBoundaryMeters = 0.45f;
    [SerializeField, Min(0.5f)] private float wrongActionHintDurationSeconds = 6f;
    [SerializeField, Min(0f)] private float shardOrbitDegreesPerSecond = 150f;
    [SerializeField, Min(0f)] private float corePulseCyclesPerSecond = 2.5f;

    public event Action<SpellProjectile> Missed;

    public SpellGlyphType GlyphType { get; private set; }
    public float DepthMeters { get; private set; }
    public bool IsResolved { get; private set; }
    private Transform spellCore;
    private Transform[] orbitingShards;
    private Renderer[] shardRenderers;
    private readonly List<Material> attackMaterials = new List<Material>();
    private TrailRenderer attackTrail;
    private Color shardColor;
    private Color trailColor;
    private Transform bodyRoot;
    private float approachDepthMeters;
    private float homeRightMeters;
    private float hangTimeRemaining;
    private bool hasLaneSlot;
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
        SpellGlyphType glyphType,
        float wrongActionHintSeconds,
        float slotCatchUpSpeedMetersPerSecond,
        float hangSeconds)
    {
        viewingEye = eye;
        flightDirection = direction.normalized;
        speedMetersPerSecond = Mathf.Max(0.01f, speed);
        worldSizeAtReferenceDistance = Mathf.Max(0.01f, sharedWorldSizeAtReferenceDistance);
        referenceDistanceMeters = Mathf.Max(0.01f, referenceDistance);
        missBoundaryMeters = Mathf.Max(0f, missBoundary);
        wrongActionHintDurationSeconds = Mathf.Max(0.5f, wrongActionHintSeconds);
        slotCatchUpMetersPerSecond = Mathf.Max(0f, slotCatchUpSpeedMetersPerSecond);
        hangDurationSeconds = Mathf.Max(0f, hangSeconds);
        hangTimeRemaining = hangDurationSeconds;
        GlyphType = glyphType;
        desktopLateralDirection = UnityEngine.Random.value < 0.5f ? -1f : 1f;

        IsResolved = false;
        CaptureLaneSlot();
        RefreshDepthAndApparentSize();
    }

    public void BuildAttackVisuals()
    {
        var shieldSpell = GlyphType == SpellGlyphType.ShieldRune;
        shardColor = shieldSpell ? new Color(1f, 0.43f, 0.08f) : new Color(0.08f, 0.55f, 1f);
        trailColor = shieldSpell ? new Color(1f, 0.68f, 0.2f) : new Color(0.25f, 0.85f, 1f);

        var bodyRenderer = GetComponent<Renderer>();
        if (bodyRenderer != null)
            bodyRenderer.enabled = false;

        var coreColor = shieldSpell ? new Color(1f, 0.83f, 0.4f) : new Color(0.55f, 0.95f, 1f);
        spellCore = CreateVisualPart("Spell Core", PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.58f, coreColor);
        orbitingShards = new Transform[3];
        shardRenderers = new Renderer[3];
        for (var index = 0; index < orbitingShards.Length; index++)
        {
            var shard = CreateVisualPart("Orbiting Shard", PrimitiveType.Cube, Vector3.zero, new Vector3(0.13f, 0.42f, 0.12f), shardColor);
            shard.localRotation = Quaternion.Euler(0f, 0f, index * 120f);
            orbitingShards[index] = shard;
            shardRenderers[index] = shard.GetComponent<Renderer>();
        }

        attackTrail = gameObject.AddComponent<TrailRenderer>();
        attackTrail.time = 0.36f;
        attackTrail.minVertexDistance = 0.01f;
        attackTrail.alignment = LineAlignment.View;
        attackTrail.numCapVertices = 2;
        attackTrail.sharedMaterial = CreateAttackMaterial(trailColor);
        attackTrail.colorGradient = CreateTrailGradient(trailColor);
    }

private void Update()
    {
        if (viewingEye == null || IsResolved)
            return;

        // The hang holds the spell in the room so the player can step into a sword or shield line.
        // When it releases, the lane is taken from where the player is standing then.
        // Sideways catch-up stays slow, so that lineup is not erased during the approach.
        if (hangTimeRemaining > 0f && !IsRetaliating)
        {
            hangTimeRemaining -= Time.deltaTime;
            if (hangTimeRemaining <= 0f)
                CaptureLaneSlot();
        }
        else if (IsRetaliating || !hasLaneSlot)
            ChaseTorso();
        else
            AdvanceLaneSlot(Time.deltaTime);

        AnimateAttackVisuals();
        RefreshDepthAndApparentSize();

        if (DepthMeters <= missBoundaryMeters)
        {
            IsResolved = true;
            Missed?.Invoke(this);
        }
    }

    private void CaptureLaneSlot()
    {
        if (!TryGetApproachFrame(out var chest, out var forward, out var right))
            return;

        var offset = transform.position - chest;
        approachDepthMeters = Vector3.Dot(offset, forward);
        homeRightMeters = Vector3.Dot(offset, right);
        hasLaneSlot = true;
    }

    private void AdvanceLaneSlot(float deltaTime)
    {
        if (!TryGetApproachFrame(out var chest, out var forward, out var right))
            return;

        var previousPosition = transform.position;
        approachDepthMeters -= SpeedMetersPerSecond * deltaTime;

        var offset = previousPosition - chest;
        var nextRight = Mathf.MoveTowards(Vector3.Dot(offset, right), homeRightMeters, slotCatchUpMetersPerSecond * deltaTime);
        var nextUp = Mathf.MoveTowards(offset.y, 0f, SpeedMetersPerSecond * deltaTime);
        var nextPosition = chest + forward * approachDepthMeters + right * nextRight + Vector3.up * nextUp;
        transform.position = nextPosition;

        var travel = nextPosition - previousPosition;
        if (travel.sqrMagnitude > 0.0000001f)
            flightDirection = travel.normalized;

        transform.rotation = Quaternion.LookRotation(-flightDirection, viewingEye.up);
    }

    private void ChaseTorso()
    {
        var targetPoint = viewingEye.position - Vector3.up * playerAimOffsetMeters;
        var toTarget = targetPoint - transform.position;
        if (toTarget.sqrMagnitude < 0.000001f)
            return;

        flightDirection = toTarget.normalized;
        transform.position += flightDirection * SpeedMetersPerSecond * Time.deltaTime;
        transform.rotation = Quaternion.LookRotation(-flightDirection, viewingEye.up);
    }

    private bool TryGetApproachFrame(out Vector3 chest, out Vector3 forward, out Vector3 right)
    {
        chest = default;
        forward = Vector3.forward;
        right = Vector3.right;
        if (viewingEye == null)
            return false;

        if (bodyRoot == null)
        {
            var origin = FindAnyObjectByType<XROrigin>();
            if (origin != null)
                bodyRoot = origin.transform;
        }

        var facing = bodyRoot != null ? bodyRoot.forward : viewingEye.forward;
        var planar = Vector3.ProjectOnPlane(facing, Vector3.up);
        if (planar.sqrMagnitude < 0.0001f)
            planar = Vector3.ProjectOnPlane(viewingEye.forward, Vector3.up);
        if (planar.sqrMagnitude < 0.0001f)
            return false;

        forward = planar.normalized;
        right = Vector3.Cross(Vector3.up, forward);
        chest = viewingEye.position - Vector3.up * playerAimOffsetMeters;
        return true;
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

        var retaliationColor = new Color(1f, 0.08f, 0.015f);
        for (var index = 0; index < shardRenderers.Length; index++)
        {
            if (shardRenderers[index] != null)
                shardRenderers[index].material.color = retaliationColor;
        }

        if (attackTrail != null)
            attackTrail.colorGradient = CreateTrailGradient(retaliationColor);
    }

    public void ShowWrongActionFeedback(SpellCounterAction expectedAction, SpellCounterAction attemptedAction)
    {
        var block = expectedAction == SpellCounterAction.PalmBlock;
        var attemptedLabel = attemptedAction == SpellCounterAction.PalmBlock ? "shield" : "sword";
        var expectedLabel = block ? "shield block" : "sword slash";
        var hint = block
            ? "Hint: hold steady and block (Desktop: Left Mouse / J)."
            : "Hint: slash quickly (Desktop: Right Mouse / F).";

        ScreenFeedback.Flash(
            string.Format("Wrong input: used {0}. Use {1}.\n{2}", attemptedLabel, expectedLabel, hint),
            block ? new Color(1f, 0.82f, 0.3f) : new Color(0.55f, 0.95f, 1f),
            wrongActionHintDurationSeconds);
    }

    private Transform CreateVisualPart(string partName, PrimitiveType shape, Vector3 localPosition, Vector3 localScale, Color color)
    {
        var part = GameObject.CreatePrimitive(shape);
        part.name = partName;
        part.transform.SetParent(transform, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;

        var collider = part.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        var renderer = part.GetComponent<Renderer>();
        renderer.sharedMaterial = CreateAttackMaterial(color);
        return part.transform;
    }

    private Material CreateAttackMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Standard");

        var material = new Material(shader) { color = color };
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.8f);
        }

        attackMaterials.Add(material);
        return material;
    }

    private void AnimateAttackVisuals()
    {
        if (orbitingShards == null)
            return;

        var pulse = 1f + Mathf.Sin(Time.time * corePulseCyclesPerSecond * Mathf.PI * 2f) * 0.1f;
        var charge = hangDurationSeconds <= 0f || hangTimeRemaining <= 0f
            ? 1f
            : Mathf.Lerp(0.72f, 1f, 1f - Mathf.Clamp01(hangTimeRemaining / hangDurationSeconds));
        spellCore.localScale = Vector3.one * 0.58f * pulse * charge;

        for (var index = 0; index < orbitingShards.Length; index++)
        {
            var angle = Time.time * shardOrbitDegreesPerSecond + index * 120f;
            var rotation = Quaternion.Euler(0f, 0f, angle);
            orbitingShards[index].localPosition = rotation * new Vector3(0.58f, 0f, 0.08f);
            orbitingShards[index].localRotation = rotation * Quaternion.Euler(0f, 0f, 35f);
        }

        if (attackTrail != null)
        {
            var apparentSize = worldSizeAtReferenceDistance * Mathf.Max(0f, DepthMeters) / referenceDistanceMeters;
            attackTrail.startWidth = apparentSize * 0.18f;
            attackTrail.endWidth = apparentSize * 0.01f;
        }
    }

    private static Gradient CreateTrailGradient(Color color)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        return gradient;
    }

    private void OnDestroy()
    {
        for (var index = 0; index < attackMaterials.Count; index++)
            Destroy(attackMaterials[index]);
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
