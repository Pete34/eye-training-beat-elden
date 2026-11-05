using System;
using System.Collections.Generic;
using UnityEngine;

public enum SpellGlyphSpawnMode
{
    Mixed,
    SwordOnly,
    ShieldOnly
}

public sealed class SpellVolleySpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform viewingEye;
    [SerializeField] private Transform bossAnchor;

    [Header("Volley")]
    [SerializeField, Min(1)] private int minimumSpells = 2;
    [SerializeField, Min(1)] private int maximumSpells = 2;
    [SerializeField, Min(0.01f)] private float minimumSpeedMetersPerSecond = 0.35f;
    [SerializeField, Min(0.01f)] private float maximumSpeedMetersPerSecond = 0.6f;
    [SerializeField, Min(0f)] private float slotCatchUpMetersPerSecond = 0.35f;
    [SerializeField, Min(0f)] private float spellHangSeconds = 0.6f;
    [SerializeField, Min(0.01f)] private float sharedWorldSizeAtReferenceDistance = 0.12f;
    [SerializeField, Min(0.01f)] private float referenceDistanceMeters = 1f;
    [SerializeField, Min(0f)] private float minimumSpellDistanceMeters = 1.5f;    [SerializeField, Min(0.5f)] private float desktopMinimumSpellDistanceMeters = 3.8f;

    [SerializeField, Min(0f)] private float bossClearanceMeters = 0.5f;
    [SerializeField, Range(0f, 1f)] private float deltaDifficulty;
    [SerializeField, Min(0.01f)] private float fallbackBossDistanceMeters = 5f;
    [SerializeField, Min(0f)] private float missBoundaryMeters = 0.45f;
    [SerializeField] private bool spawnOnStart = true;
    [SerializeField] private bool autoRepeatVolleys;
    [SerializeField, Min(0f)] private float delayBetweenVolleys = 1f;
    [SerializeField] private SpellGlyphSpawnMode glyphSpawnMode = SpellGlyphSpawnMode.Mixed;
    [SerializeField] private bool forgivingTrainingNpc;
    [SerializeField, Min(0.5f)] private float wrongActionHintDurationSeconds = 6f;

    private readonly List<SpellProjectile> activeProjectiles = new List<SpellProjectile>();
    private float nextVolleyTime;    [SerializeField, Min(1f)] private float retaliationSpeedMetersPerSecond = 7f;
    private bool retaliationStartedThisVolley;


    public IReadOnlyList<SpellProjectile> ActiveProjectiles => activeProjectiles;    public event Action<SpellProjectile> RetaliationStarted;
    public event Action<SpellProjectile> RetaliationImpact;
    public event Action<SpellProjectile> ProjectileHitPlayer;
    public event Action<bool> VolleyCompleted;

    private SpellCounterJudge counterJudge;
    private bool volleyHadMiss;

    public bool SpawnBlocked { get; set; }

    public float DeltaDifficulty
    {
        get => deltaDifficulty;
        set => deltaDifficulty = Mathf.Clamp01(value);
    }

    public SpellGlyphSpawnMode GlyphSpawnMode
    {
        get => glyphSpawnMode;
        set => glyphSpawnMode = value;
    }

    private void Start()
    {
        if (viewingEye == null && Camera.main != null)
            viewingEye = Camera.main.transform;

        counterJudge = FindAnyObjectByType<SpellCounterJudge>();
        if (counterJudge != null)
        {
            counterJudge.WrongAction += OnFailedCounterAttempt;
            counterJudge.OrderError += OnOrderError;
            counterJudge.CorrectCounter += OnCorrectCounter;
        }

        if (spawnOnStart)
            SpawnVolley();
    }

    private void OnDestroy()
    {
        if (counterJudge == null)
            return;

        counterJudge.WrongAction -= OnFailedCounterAttempt;
        counterJudge.OrderError -= OnOrderError;
        counterJudge.CorrectCounter -= OnCorrectCounter;
    }

    private void OnOrderError(SpellProjectile required, SpellProjectile contacted) => OnFailedCounterAttempt(contacted);

    private void OnCorrectCounter(SpellProjectile source)
    {
        if (!forgivingTrainingNpc)
            return;

        for (var index = 0; index < activeProjectiles.Count; index++)
        {
            var remainingProjectile = activeProjectiles[index];
            if (remainingProjectile == null || remainingProjectile == source || remainingProjectile.IsResolved)
                continue;

            remainingProjectile.MarkCountered();
        }

        ScreenFeedback.Flash("Clean counter! Extra orb cleared for training.", new Color(0.58f, 0.95f, 0.55f), 1.6f);
    }

    // A botched counter attempt also provokes the remaining spells.
    private void OnFailedCounterAttempt(SpellProjectile source)
    {
        StartRetaliation(source);
    }

private void Update()
    {
        var hadProjectiles = activeProjectiles.Count > 0;
        for (var index = activeProjectiles.Count - 1; index >= 0; index--)
        {
            var projectile = activeProjectiles[index];
            if (projectile == null || projectile.IsResolved)
                activeProjectiles.RemoveAt(index);
        }

        if (hadProjectiles && activeProjectiles.Count == 0)
            VolleyCompleted?.Invoke(!volleyHadMiss);

        if (autoRepeatVolleys && activeProjectiles.Count == 0 && Time.time >= nextVolleyTime)
            SpawnVolley();
    }

    public bool SpawnVolley()
    {        if (SpawnBlocked || activeProjectiles.Count > 0 || Time.time < nextVolleyTime)
            return false;


        if (viewingEye == null)
        {
            Debug.LogError("SpellVolleySpawner needs a viewing eye or a MainCamera.", this);
            return false;
        }

        var eyeToBossDistance = bossAnchor != null
            ? Vector3.Dot(bossAnchor.position - viewingEye.position, viewingEye.forward)
            : fallbackBossDistanceMeters;
        var depthSpan = Mathf.Lerp(0.30f, 0.05f, deltaDifficulty) * eyeToBossDistance;
        var nearDepth = UnityEngine.XR.XRSettings.isDeviceActive
            ? minimumSpellDistanceMeters
            : Mathf.Max(minimumSpellDistanceMeters, desktopMinimumSpellDistanceMeters);
        var farDepth = Mathf.Min(nearDepth + depthSpan, eyeToBossDistance - bossClearanceMeters);

        if (eyeToBossDistance - bossClearanceMeters <= nearDepth)
        {
            Debug.LogError("Spell depth band is invalid: move the boss farther away or reduce the near-distance/clearance settings.", this);
            return false;
        }

        var effectiveSpan = farDepth - nearDepth;
        if (effectiveSpan < depthSpan)
            Debug.Log($"Spell depth band narrowed to {effectiveSpan:F2} m by the available arena depth.", this);

        var spellCount = UnityEngine.Random.Range(minimumSpells, Mathf.Max(minimumSpells, maximumSpells) + 1);
        for (var index = 0; index < spellCount; index++)
        {
            var depth = UnityEngine.Random.Range(nearDepth, farDepth);
            var row = 0;
            var desktopPreview = !UnityEngine.XR.XRSettings.isDeviceActive;
            var laneSpacing = desktopPreview ? 0.22f : 0.18f;
            var laneJitter = desktopPreview ? 0.02f : 0.03f;
            var horizontalOffset = (index - (spellCount - 1) * 0.5f) * laneSpacing + UnityEngine.Random.Range(-laneJitter, laneJitter);
            var baseVerticalOffset = desktopPreview ? 0.22f : (row == 0 ? 0.06f : -0.18f);
            var verticalOffset = baseVerticalOffset + UnityEngine.Random.Range(-0.02f, 0.02f);
            var rayDirection = (viewingEye.forward + viewingEye.right * horizontalOffset + viewingEye.up * verticalOffset).normalized;
            var forwardComponent = Vector3.Dot(rayDirection, viewingEye.forward);
            var spawnPosition = viewingEye.position + rayDirection * (depth / Mathf.Max(0.01f, forwardComponent));

            var spellObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            spellObject.name = string.Format("Spell_{0}", index);
            spellObject.transform.position = spawnPosition;
            spellObject.transform.rotation = Quaternion.LookRotation(rayDirection);

            var projectile = spellObject.AddComponent<SpellProjectile>();
            var glyph = PickGlyph();
            var speed = UnityEngine.Random.Range(minimumSpeedMetersPerSecond, maximumSpeedMetersPerSecond);
            projectile.Initialize(
                viewingEye,
                -rayDirection,
                speed,
                sharedWorldSizeAtReferenceDistance,
                referenceDistanceMeters,
                missBoundaryMeters,
                glyph,
                wrongActionHintDurationSeconds,
                slotCatchUpMetersPerSecond,
                spellHangSeconds);

            projectile.BuildAttackVisuals();
            var ball = spellObject.transform.Find("Spell Core");
            CreateGlyphMark(ball != null ? ball : spellObject.transform, glyph);
            projectile.Missed += OnProjectileMissed;
            activeProjectiles.Add(projectile);
        }

        retaliationStartedThisVolley = false;
        volleyHadMiss = false;
        nextVolleyTime = Time.time + delayBetweenVolleys;
        return true;
    }

    private SpellGlyphType PickGlyph()
    {
        switch (glyphSpawnMode)
        {
            case SpellGlyphSpawnMode.SwordOnly:
                return UnityEngine.Random.value < 0.5f ? SpellGlyphType.SlashArc : SpellGlyphType.SlashCross;
            case SpellGlyphSpawnMode.ShieldOnly:
                return SpellGlyphType.ShieldRune;
            default:
                return (SpellGlyphType)UnityEngine.Random.Range(0, 3);
        }
    }

    public void SetDepthDifficulty(float value)
    {
        deltaDifficulty = Mathf.Clamp01(value);
    }

private void OnProjectileMissed(SpellProjectile projectile)
    {
        volleyHadMiss = true;
        ProjectileHitPlayer?.Invoke(projectile);

        if (projectile.IsRetaliating)
        {
            RetaliationImpact?.Invoke(projectile);
            Debug.Log("Retaliation hit the player.", projectile);
            Destroy(projectile.gameObject);
            return;
        }

        Debug.Log(string.Format("Missed spell: {0}", projectile.GlyphType), projectile);
        Destroy(projectile.gameObject);
        StartRetaliation(projectile);
    }

    private void StartRetaliation(SpellProjectile source)
    {
        if (retaliationStartedThisVolley)
            return;

        retaliationStartedThisVolley = true;
        RetaliationStarted?.Invoke(source);
        for (var index = 0; index < activeProjectiles.Count; index++)
        {
            var remainingProjectile = activeProjectiles[index];
            if (remainingProjectile != null && !remainingProjectile.IsResolved)
                remainingProjectile.BeginRetaliation(retaliationSpeedMetersPerSecond);
        }
    }

    private static void CreateGlyphMark(Transform ball, SpellGlyphType glyph)
    {
        var ink = glyph == SpellGlyphType.ShieldRune
            ? new Color(1f, 0.96f, 0.82f)
            : new Color(0.94f, 0.98f, 1f);

        if (glyph == SpellGlyphType.SlashArc)
        {
            PaintStroke(ball, new Vector3(-0.2f, 0.3f, -0.36f), new Vector3(0.32f, -0.1f, -0.34f), ink, 0.09f);
            return;
        }

        if (glyph == SpellGlyphType.SlashCross)
        {
            PaintStroke(ball, new Vector3(-0.28f, 0.26f, -0.34f), new Vector3(0.28f, -0.26f, -0.34f), ink, 0.075f);
            PaintStroke(ball, new Vector3(0.28f, 0.26f, -0.34f), new Vector3(-0.28f, -0.26f, -0.34f), ink, 0.075f);
            return;
        }

        PaintRing(ball, 0.5f, 0.075f, ink);
        PaintStroke(ball, new Vector3(-0.16f, -0.24f, -0.4f), new Vector3(0.16f, -0.24f, -0.4f), ink, 0.07f);
    }

    private static void PaintStroke(Transform ball, Vector3 from, Vector3 to, Color ink, float width)
    {
        const int segments = 5;
        var fromDirection = from.normalized;
        var toDirection = to.normalized;
        for (var index = 0; index < segments; index++)
        {
            var t = index / (segments - 1f);
            var direction = Vector3.Slerp(fromDirection, toDirection, t);
            var ahead = Vector3.Slerp(fromDirection, toDirection, Mathf.Min(1f, t + 0.12f));
            var along = ahead - direction;
            if (along.sqrMagnitude < 0.0001f)
                along = toDirection - fromDirection;
            PlaceInk(ball, direction, along, width, 0.18f, ink);
        }
    }

    private static void PaintRing(Transform ball, float angularRadius, float width, Color ink)
    {
        const int segments = 10;
        var center = Vector3.back;
        var tangent = Vector3.Cross(center, Vector3.up).normalized;
        var bitangent = Vector3.Cross(center, tangent).normalized;
        for (var index = 0; index < segments; index++)
        {
            var angle = index / (float)segments * Mathf.PI * 2f;
            var nextAngle = (index + 1) / (float)segments * Mathf.PI * 2f;
            var direction = center + (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * angularRadius;
            var ahead = center + (tangent * Mathf.Cos(nextAngle) + bitangent * Mathf.Sin(nextAngle)) * angularRadius;
            PlaceInk(ball, direction, ahead - direction, width, 0.12f, ink);
        }
    }

    private static void PlaceInk(Transform ball, Vector3 outward, Vector3 along, float width, float length, Color ink)
    {
        var normal = outward.normalized;
        var stroke = Vector3.ProjectOnPlane(along, normal);
        if (stroke.sqrMagnitude < 0.0001f)
            stroke = Vector3.Cross(normal, Vector3.up);
        stroke.Normalize();

        const float thickness = 0.02f;
        var mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mark.name = "GlyphMark";
        mark.transform.SetParent(ball, false);
        mark.transform.localRotation = Quaternion.LookRotation(normal, stroke);
        mark.transform.localPosition = normal * (0.5f + thickness * 0.35f);
        mark.transform.localScale = new Vector3(width, length, thickness);

        var collider = mark.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        var renderer = mark.GetComponent<Renderer>();
        if (renderer == null)
            return;

        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            return;

        var material = new Material(shader) { color = ink };
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", ink);
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", ink * 1.4f);
        }

        renderer.sharedMaterial = material;
    }
}
