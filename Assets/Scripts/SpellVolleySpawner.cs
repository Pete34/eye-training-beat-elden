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
                wrongActionHintDurationSeconds);

            projectile.BuildAttackVisuals();
            CreateGlyphMark(spellObject.transform, glyph);
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

    private static void CreateGlyphMark(Transform parent, SpellGlyphType glyph)
    {
        var spellRenderer = parent.GetComponent<Renderer>();
        if (spellRenderer != null)
            spellRenderer.material.color = glyph == SpellGlyphType.ShieldRune ? new Color(1f, 0.72f, 0.25f) : new Color(0.35f, 0.9f, 1f);

        if (glyph == SpellGlyphType.SlashArc)
        {
            CreateMarkPiece(parent, PrimitiveType.Cube, new Vector3(0f, 0f, -0.48f), new Vector3(0.42f, 0.09f, 0.05f), Quaternion.Euler(0f, 0f, -35f));
            return;
        }

        if (glyph == SpellGlyphType.SlashCross)
        {
            CreateMarkPiece(parent, PrimitiveType.Cube, new Vector3(0f, 0f, -0.48f), new Vector3(0.38f, 0.08f, 0.05f), Quaternion.Euler(0f, 0f, 45f));
            CreateMarkPiece(parent, PrimitiveType.Cube, new Vector3(0f, 0f, -0.48f), new Vector3(0.38f, 0.08f, 0.05f), Quaternion.Euler(0f, 0f, -45f));
            return;
        }

        CreateMarkPiece(parent, PrimitiveType.Cube, new Vector3(0f, 0f, -0.48f), new Vector3(0.32f, 0.08f, 0.06f), Quaternion.identity);
        CreateMarkPiece(parent, PrimitiveType.Cube, new Vector3(0f, -0.16f, -0.48f), new Vector3(0.22f, 0.07f, 0.06f), Quaternion.identity);
    }

    private static void CreateMarkPiece(Transform parent, PrimitiveType shape, Vector3 localPosition, Vector3 localScale, Quaternion localRotation)
    {
        var piece = GameObject.CreatePrimitive(shape);
        piece.name = "GlyphMark";
        piece.transform.SetParent(parent, false);
        piece.transform.localPosition = localPosition;
        piece.transform.localScale = localScale;
        piece.transform.localRotation = localRotation;
        var collider = piece.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        var renderer = piece.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = new Color(0.92f, 0.98f, 1f);
            if (renderer.material.HasProperty("_EmissionColor"))
            {
                renderer.material.EnableKeyword("_EMISSION");
                renderer.material.SetColor("_EmissionColor", new Color(0.6f, 0.9f, 1f));
            }
        }
    }
}
