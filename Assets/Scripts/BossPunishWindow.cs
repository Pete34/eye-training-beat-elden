using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;

// After enough consecutive clean volleys the boss is open to attack: the player can close in and slash it
// until the window ends, and is repelled back to the start if the boss survives.
public sealed class BossPunishWindow : MonoBehaviour
{
    [SerializeField] private SpellVolleySpawner volleySpawner;
    [SerializeField] private SpellCounterJudge counterJudge;
    [SerializeField] private Transform bossAnchor;
    [Header("Training Phases")]
    [SerializeField] private bool enableTwoPhaseTraining = true;
    [SerializeField, Min(0.5f)] private float phaseAnnouncementSeconds = 4.5f;
    [SerializeField] private GameObject phaseTwoBossPrefab;
    [SerializeField] private Transform phaseTwoSpawnPoint;
    [SerializeField, Min(1)] private int cleanVolleysRequired = 3;
    [SerializeField, Min(1f)] private float windowSeconds = 10f;
    [SerializeField, Min(0.1f)] private float bossHitRadiusMeters = 0.9f;
    [SerializeField, Min(1f)] private float bossMaxHealth = 100f;
    [SerializeField, Min(1f)] private float strikeDamage = 25f;
    [SerializeField, Min(0.1f)] private float strikeCooldownSeconds = 0.45f;
    [SerializeField, Min(0f)] private float desktopReachMarginMeters = 0.6f;
    [SerializeField, Min(0.1f)] private float repelSeconds = 0.5f;
    [SerializeField, Min(0.1f)] private float healthBarWidthMeters = 1.2f;

    private int cleanStreak;
    private bool windowActive;
    private bool repelling;
    private float windowEndTime;
    private float nextStrikeTime;
    private float bossHealth;
    private Renderer[] bossRenderers;
    private Color[] bossColors;
    private DesktopPlayerControls desktopControls;
    private XROrigin xrOrigin;
    private Vector3 windowStartOriginPosition;
    private float originalArenaRadius;
    private Transform healthBarRoot;
    private Transform healthBarFill;
    private float healthBarHeightOffset;
    private bool phaseTwoStarted;

    public bool WindowActive => windowActive;
    public float BossHealth => bossHealth;

    private void Awake()
    {
        if (volleySpawner == null)
            volleySpawner = FindAnyObjectByType<SpellVolleySpawner>();
        if (counterJudge == null)
            counterJudge = FindAnyObjectByType<SpellCounterJudge>();
        if (bossAnchor == null)
        {
            var boss = GameObject.Find("Ember Adept Placeholder");
            if (boss != null)
                bossAnchor = boss.transform;
        }

        bossHealth = bossMaxHealth;
        desktopControls = FindAnyObjectByType<DesktopPlayerControls>();
        xrOrigin = FindAnyObjectByType<XROrigin>();

        if (enableTwoPhaseTraining && volleySpawner != null)
        {
            volleySpawner.SpawnBlocked = true;
            volleySpawner.GlyphSpawnMode = SpellGlyphSpawnMode.SwordOnly;
        }

        BuildHealthBar();
    }

    private void Start()
    {
        if (!enableTwoPhaseTraining)
            return;

        ScreenFeedback.Flash("Training 1: block these symbols with your sword.", new Color(0.55f, 0.95f, 1f), phaseAnnouncementSeconds);
        StartCoroutine(BeginFirstTrainingPhaseAfterAnnouncement());
    }

    private void OnEnable()
    {
        if (volleySpawner != null)
            volleySpawner.VolleyCompleted += OnVolleyCompleted;
        if (counterJudge != null)
            counterJudge.SlashSwept += OnSlashSwept;
    }

    private void OnDisable()
    {
        if (volleySpawner != null)
            volleySpawner.VolleyCompleted -= OnVolleyCompleted;
        if (counterJudge != null)
            counterJudge.SlashSwept -= OnSlashSwept;
    }

    private void Update()
    {
        if (windowActive && Time.time >= windowEndTime)
            StartCoroutine(RepelPlayer());
    }

    private void LateUpdate()
    {
        var cam = Camera.main;
        if (healthBarRoot == null || cam == null || bossAnchor == null)
            return;

        healthBarRoot.position = bossAnchor.position + Vector3.up * healthBarHeightOffset;
        var away = healthBarRoot.position - cam.transform.position;
        if (away.sqrMagnitude > 0.0001f)
            healthBarRoot.rotation = Quaternion.LookRotation(away, Vector3.up);
    }

    private void OnVolleyCompleted(bool clean)
    {
        if (windowActive || repelling || ScreenFeedback.IsGameOver)
            return;

        cleanStreak = clean ? cleanStreak + 1 : 0;
        if (cleanStreak >= cleanVolleysRequired)
            BeginWindow();
    }

    private void BeginWindow()
    {
        if (bossAnchor == null)
            return;

        windowActive = true;
        windowEndTime = Time.time + windowSeconds;
        nextStrikeTime = 0f;
        volleySpawner.SpawnBlocked = true;
        SetBossGlow(true);
        ScreenFeedback.Flash("BOSS OPEN! SLASH HIM!", new Color(1f, 0.9f, 0.3f), 2.5f);

        if (xrOrigin != null)
            windowStartOriginPosition = xrOrigin.transform.position;

        // Desktop movement is normally bounded; widen it so the boss is reachable.
        if (desktopControls != null)
        {
            originalArenaRadius = desktopControls.ArenaMovementRadiusMeters;
            var eye = Camera.main != null ? Camera.main.transform.position : bossAnchor.position;
            var toBoss = Vector3.ProjectOnPlane(bossAnchor.position - eye, Vector3.up).magnitude;
            desktopControls.ArenaMovementRadiusMeters = Mathf.Max(originalArenaRadius, toBoss - bossHitRadiusMeters + desktopReachMarginMeters);
        }
    }

    private void OnSlashSwept(Vector3 swordBase, Vector3 swordTip)
    {
        if (!windowActive || bossAnchor == null || Time.time < nextStrikeTime)
            return;

        var segment = swordTip - swordBase;
        var lengthSquared = segment.sqrMagnitude;
        var amount = lengthSquared > Mathf.Epsilon
            ? Mathf.Clamp01(Vector3.Dot(bossAnchor.position - swordBase, segment) / lengthSquared)
            : 0f;
        if (Vector3.Distance(bossAnchor.position, swordBase + segment * amount) > bossHitRadiusMeters)
            return;

        nextStrikeTime = Time.time + strikeCooldownSeconds;
        bossHealth = Mathf.Max(0f, bossHealth - strikeDamage);
        UpdateHealthBar();

        if (bossHealth > 0f)
        {
            ScreenFeedback.Flash("BOSS HIT!", new Color(1f, 0.55f, 0.15f), 0.7f);
            return;
        }

        if (enableTwoPhaseTraining && !phaseTwoStarted)
        {
            StartPhaseTwoTraining();
            return;
        }

        windowActive = false;
        SetBossGlow(false);
        ScreenFeedback.EndGame("VICTORY!", new Color(0.4f, 1f, 0.5f));
    }

    private IEnumerator BeginFirstTrainingPhaseAfterAnnouncement()
    {
        yield return new WaitForSeconds(phaseAnnouncementSeconds);

        if (volleySpawner == null || ScreenFeedback.IsGameOver)
            yield break;

        volleySpawner.SpawnBlocked = false;
        volleySpawner.SpawnVolley();
    }

    private void StartPhaseTwoTraining()
    {
        phaseTwoStarted = true;
        windowActive = false;
        repelling = false;
        cleanStreak = 0;
        nextStrikeTime = 0f;
        bossHealth = bossMaxHealth;
        SetBossGlow(false);

        var previousBoss = bossAnchor;
        var spawnPosition = phaseTwoSpawnPoint != null
            ? phaseTwoSpawnPoint.position
            : (previousBoss != null ? previousBoss.position + new Vector3(1.6f, 0f, 0f) : Vector3.zero);
        var spawnRotation = phaseTwoSpawnPoint != null
            ? phaseTwoSpawnPoint.rotation
            : (previousBoss != null ? previousBoss.rotation : Quaternion.identity);
        Transform nextBoss = null;

        if (phaseTwoBossPrefab != null)
        {
            var spawnedBoss = Instantiate(phaseTwoBossPrefab, spawnPosition, spawnRotation);
            nextBoss = spawnedBoss.transform;
        }
        else if (previousBoss != null)
        {
            var spawnedBoss = Instantiate(previousBoss.gameObject, spawnPosition, spawnRotation);
            spawnedBoss.name = previousBoss.gameObject.name + " Phase 2";
            nextBoss = spawnedBoss.transform;
        }
        else if (phaseTwoSpawnPoint != null)
        {
            nextBoss = phaseTwoSpawnPoint;
        }

        if (nextBoss != null)
        {
            if (previousBoss != null && previousBoss != nextBoss)
                previousBoss.gameObject.SetActive(false);

            bossAnchor = nextBoss;
        }
        else
        {
            Debug.LogWarning("Phase 2 boss could not be created: assign phaseTwoBossPrefab or keep a valid bossAnchor.", this);
        }

        BuildHealthBar();
        UpdateHealthBar();

        if (volleySpawner != null)
        {
            volleySpawner.GlyphSpawnMode = SpellGlyphSpawnMode.ShieldOnly;
            volleySpawner.SpawnBlocked = true;
        }

        ScreenFeedback.Flash("Training 2: block all these orbs with your shield.", new Color(1f, 0.82f, 0.3f), phaseAnnouncementSeconds);
        StartCoroutine(BeginSecondTrainingPhaseAfterAnnouncement());
    }

    private IEnumerator BeginSecondTrainingPhaseAfterAnnouncement()
    {
        yield return new WaitForSeconds(phaseAnnouncementSeconds);

        if (volleySpawner == null || ScreenFeedback.IsGameOver)
            yield break;

        volleySpawner.SpawnBlocked = false;
        volleySpawner.SpawnVolley();
    }

    private IEnumerator RepelPlayer()
    {
        windowActive = false;
        repelling = true;
        cleanStreak = 0;
        SetBossGlow(false);
        ScreenFeedback.Flash("REPELLED!", new Color(1f, 0.35f, 0.25f), 1.8f);

        if (desktopControls != null)
            desktopControls.MovementLocked = true;

        if (xrOrigin != null)
        {
            var from = xrOrigin.transform.position;
            for (var elapsed = 0f; elapsed < repelSeconds; elapsed += Time.deltaTime)
            {
                xrOrigin.transform.position = Vector3.Lerp(from, windowStartOriginPosition, elapsed / repelSeconds);
                yield return null;
            }

            xrOrigin.transform.position = windowStartOriginPosition;
        }

        if (desktopControls != null)
        {
            desktopControls.MovementLocked = false;
            desktopControls.ArenaMovementRadiusMeters = originalArenaRadius;
        }

        repelling = false;
        if (volleySpawner != null && !ScreenFeedback.IsGameOver)
            volleySpawner.SpawnBlocked = false;
    }

    private void BuildHealthBar()
    {
        if (bossAnchor == null)
            return;

        if (healthBarRoot != null)
            Destroy(healthBarRoot.gameObject);

        var bounds = new Bounds(bossAnchor.position, Vector3.zero);
        foreach (var bossRenderer in bossAnchor.GetComponentsInChildren<Renderer>())
            bounds.Encapsulate(bossRenderer.bounds);
        healthBarHeightOffset = bounds.max.y - bossAnchor.position.y + 0.12f;

        healthBarRoot = new GameObject("Boss Health Bar").transform;
        CreateBarPart("Back", new Color(0.1f, 0.1f, 0.1f), 0.1f, 0f);
        healthBarFill = CreateBarPart("Fill", new Color(0.9f, 0.15f, 0.1f), 0.08f, -0.02f);
        UpdateHealthBar();
    }

    private Transform CreateBarPart(string partName, Color color, float height, float z)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = partName;
        part.transform.SetParent(healthBarRoot, false);
        part.transform.localPosition = new Vector3(0f, 0f, z);
        part.transform.localScale = new Vector3(healthBarWidthMeters, height, 0.02f);
        Destroy(part.GetComponent<Collider>());
        part.GetComponent<Renderer>().material.color = color;
        return part.transform;
    }

    private void UpdateHealthBar()
    {
        if (healthBarFill == null)
            return;

        var amount = bossHealth / bossMaxHealth;
        healthBarFill.localScale = new Vector3(healthBarWidthMeters * Mathf.Max(0.001f, amount), healthBarFill.localScale.y, healthBarFill.localScale.z);
        healthBarFill.localPosition = new Vector3(-healthBarWidthMeters * (1f - amount) * 0.5f, 0f, healthBarFill.localPosition.z);
    }

    private void SetBossGlow(bool on)
    {
        if (bossAnchor == null)
            return;

        if (on)
        {
            bossRenderers = bossAnchor.GetComponentsInChildren<Renderer>();
            bossColors = new Color[bossRenderers.Length];
            for (var index = 0; index < bossRenderers.Length; index++)
            {
                bossColors[index] = bossRenderers[index].material.color;
                bossRenderers[index].material.color = Color.white;
            }

            return;
        }

        if (bossRenderers == null)
            return;

        for (var index = 0; index < bossRenderers.Length; index++)
        {
            if (bossRenderers[index] != null)
                bossRenderers[index].material.color = bossColors[index];
        }
    }
}
