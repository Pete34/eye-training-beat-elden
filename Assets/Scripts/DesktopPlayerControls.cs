using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

public sealed class DesktopPlayerControls : MonoBehaviour
{
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private Camera playerCamera;    [SerializeField] private SpellVolleySpawner volleySpawner;
    [SerializeField] private SpellCounterJudge counterJudge;

    [SerializeField, Min(0.1f)] private float moveSpeedMetersPerSecond = 2.5f;
    [SerializeField, Min(1f)] private float sprintMultiplier = 1.8f;    [SerializeField, Min(0.5f)] private float arenaMovementRadiusMeters = 2f;
    private Vector3 movementOriginPosition;
    private ArenaSafetyBounds arenaBounds;

    public float ArenaMovementRadiusMeters
    {
        get => arenaMovementRadiusMeters;
        set => arenaMovementRadiusMeters = Mathf.Max(0.5f, value);
    }

    public void ReturnToStart()
    {
        if (xrOrigin != null)
            xrOrigin.transform.position = movementOriginPosition;
    }

    [SerializeField] private bool showDesktopTools = true;
    [SerializeField, Min(0.05f)] private float shieldHitboxForwardMeters = 0.16f;
    [SerializeField, Min(0.05f)] private float shieldHitboxHalfWidthMeters = 0.2f;
    [SerializeField, Min(0.05f)] private float shieldHitboxHalfHeightMeters = 0.2f;
    [SerializeField, Min(0f)] private float shieldHitboxPaddingMeters = 0f;
    [SerializeField, Min(0f)] private float earlyBlockDepthMeters = 0.9f;
    [SerializeField, Min(0f)] private float lateBlockDepthMeters = 0.38f;

    private bool desktopModeActive;
    private GameObject desktopSword;
    private GameObject desktopShield;
    private GameObject swordAura;
    private GameObject shieldAura;
    private Vector3 swordRestPosition;
    private Quaternion swordRestRotation;
    private Vector3 shieldRestPosition;
    private Quaternion shieldRestRotation;
    private float slashStartTime = -10f;
    private float blockStartTime = -10f;    private bool slashContactResolved;
    private bool blockContactResolved;
    private Vector3 previousSwordPosition;
    private Quaternion previousSwordRotation;
    private bool hasPreviousSwordPose;
    private bool slashWhiffReported = true;
    private const float SlashDurationSeconds = 0.72f;
    private const float SlashContactStart = 0.2f;
    private const float SlashContactEnd = 0.64f;
    // The visible blade is 7.5 cm wide and 3.5 cm thick. The glow is that same flat bar, a little larger.
    private const float BladeHalfWidthMeters = 0.0375f;
    private const float BladeHalfThicknessMeters = 0.0175f;
    private const float BladeAuraScale = 1.75f;
    internal const float SwordHitHalfWidthMeters = BladeHalfWidthMeters * BladeAuraScale;
    internal const float SwordHitHalfThicknessMeters = BladeHalfThicknessMeters * BladeAuraScale;
    private const float SwordHitStartLocalZ = 0.21f;
    private const float SwordHitEndLocalZ = 1f;
    private const float BlockDurationSeconds = 0.32f;
    private const float BlockContactStart = 0.05f;
    private const float BlockContactEnd = 1.05f;
    private bool blockWhiffReported = true;

    public bool MovementLocked { get; set; }




    private void Awake()
    {
        if (xrOrigin == null)
            xrOrigin = FindAnyObjectByType<XROrigin>();
        if (playerCamera == null)
            playerCamera = Camera.main;        if (volleySpawner == null)
            volleySpawner = FindAnyObjectByType<SpellVolleySpawner>();
        if (counterJudge == null)
            counterJudge = FindAnyObjectByType<SpellCounterJudge>();


        desktopModeActive = !XRSettings.isDeviceActive;        movementOriginPosition = xrOrigin != null ? xrOrigin.transform.position : Vector3.zero;

        if (!desktopModeActive)
            return;

        if (showDesktopTools && playerCamera != null)
            CreateDesktopCounterTools();
    }

private void Update()
    {
        if (!desktopModeActive)
            return;

        UpdateCounterToolAnimations();
        TryResolveDesktopToolContacts();
        if (xrOrigin == null || Keyboard.current == null)
            return;

        var keyboard = Keyboard.current;
        var forwardInput = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
            - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
        var sideInput = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
            - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);

        var sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        MovePlayer(new Vector2(sideInput, forwardInput), sprint, Time.deltaTime);
    }

    public void MovePlayer(Vector2 axes, bool sprint, float deltaTime)
    {
        if (!desktopModeActive || xrOrigin == null || MovementLocked)
            return;

        var cameraForward = playerCamera != null ? playerCamera.transform.forward : xrOrigin.transform.forward;
        var forward = Vector3.ProjectOnPlane(cameraForward, Vector3.up).normalized;
        var right = Vector3.Cross(Vector3.up, forward).normalized;
        var movement = forward * axes.y + right * axes.x;
        if (movement.sqrMagnitude > 1f)
            movement.Normalize();

        var speed = moveSpeedMetersPerSecond * (sprint ? sprintMultiplier : 1f);
        var nextPosition = xrOrigin.transform.position + movement * speed * deltaTime;
        var offsetFromStart = Vector3.ProjectOnPlane(nextPosition - movementOriginPosition, Vector3.up);
        if (offsetFromStart.magnitude > arenaMovementRadiusMeters)
            nextPosition = movementOriginPosition + offsetFromStart.normalized * arenaMovementRadiusMeters;

        if (arenaBounds == null)
            arenaBounds = FindAnyObjectByType<ArenaSafetyBounds>();
        if (arenaBounds != null)
            nextPosition = arenaBounds.ClampToWalkable(nextPosition, 0.25f);

        xrOrigin.transform.position = nextPosition;
    }

public void TriggerSlashAnimation()
    {
        if (!desktopModeActive)
            return;

        slashStartTime = Time.time;
        slashContactResolved = false;
        slashWhiffReported = false;
        hasPreviousSwordPose = false;
        UpdateCounterToolAnimations();
    }

public void TriggerBlockAnimation()
    {
        if (!desktopModeActive)
            return;

        blockStartTime = Time.time - 0.08f;
        blockContactResolved = false;
        blockWhiffReported = false;
        UpdateCounterToolAnimations();
    }

    private void UpdateCounterToolAnimations()
    {
        var idleSway = Mathf.Sin(Time.time * 1.7f) * 0.02f;

        if (desktopSword != null)
        {
            var slashProgress = Mathf.Clamp01((Time.time - slashStartTime) / SlashDurationSeconds);
            EvaluateSwordSwing(slashProgress, out var slashPosition, out var slashRotation);
            if (slashProgress <= 0f || slashProgress >= 1f)
                slashPosition += new Vector3(idleSway, idleSway * 0.4f, 0f);
            desktopSword.transform.localPosition = slashPosition;
            desktopSword.transform.localRotation = slashRotation;
            if (swordAura != null)
                swordAura.SetActive(slashProgress >= SlashContactStart && slashProgress <= SlashContactEnd);
        }

        if (desktopShield != null)
        {
            var blockProgress = (Time.time - blockStartTime) / BlockDurationSeconds;
            var blockThrust = Mathf.Sin(Mathf.Clamp01(blockProgress) * Mathf.PI);
            desktopShield.transform.localPosition = shieldRestPosition + new Vector3(-idleSway, idleSway, 0.5f * blockThrust);
            desktopShield.transform.localRotation = shieldRestRotation * Quaternion.Euler(-14f * blockThrust + idleSway * 20f, 0f, idleSway * 16f);
            if (shieldAura != null)
                shieldAura.SetActive(blockProgress >= BlockContactStart && blockProgress <= BlockContactEnd);
        }
    }

private void TryResolveDesktopToolContacts()
    {
        if (volleySpawner == null || counterJudge == null)
            return;

        if (!slashContactResolved && desktopSword != null)
        {
            var progress = (Time.time - slashStartTime) / SlashDurationSeconds;
            if (progress >= SlashContactStart && progress <= SlashContactEnd)
            {
                var swordBase = desktopSword.transform.TransformPoint(new Vector3(0f, 0f, SwordHitStartLocalZ));
                var swordTip = desktopSword.transform.TransformPoint(new Vector3(0f, 0f, SwordHitEndLocalZ));
                counterJudge.ReportSlash(swordBase, swordTip);
                var contact = FindClosestSpellInSweptSwordVolume();
                if (contact != null)
                {
                    slashContactResolved = true;
                    counterJudge.EvaluateContact(contact, SpellCounterAction.Slash);
                }

                previousSwordPosition = desktopSword.transform.position;
                previousSwordRotation = desktopSword.transform.rotation;
                hasPreviousSwordPose = true;
            }
            else
            {
                hasPreviousSwordPose = false;
                if (progress > 0.95f && !slashWhiffReported)
                {
                    slashWhiffReported = true;
                    ReportWhiff("TOO EARLY!");
                }
            }
        }

        if (!blockContactResolved && desktopShield != null)
        {
            var progress = (Time.time - blockStartTime) / BlockDurationSeconds;
            if (progress >= BlockContactStart && progress <= BlockContactEnd)
            {
                var contact = FindClosestSpellInShieldVolume();
                if (contact != null)
                {
                    blockContactResolved = true;
                    counterJudge.EvaluateContact(contact, SpellCounterAction.PalmBlock);
                }
            }
            else if (progress > 1.05f && !blockWhiffReported)
            {
                blockWhiffReported = true;
                ReportBlockWhiff();
            }
        }
    }

    private void ReportBlockWhiff()
    {
        var required = counterJudge != null ? counterJudge.FindNearestRequiredTarget() : null;
        if (required == null)
            return;

        if (required.DepthMeters > earlyBlockDepthMeters)
            ReportWhiff("TOO EARLY!");
        else if (required.DepthMeters < lateBlockDepthMeters)
            ReportWhiff("TOO LATE!");
        else
            ReportWhiff("BLOCK MISSED!");
    }

    // A swing with nothing to hit teaches timing; stay quiet when no spells are in flight.
    private void ReportWhiff(string message)
    {
        if (volleySpawner.ActiveProjectiles.Count > 0)
            ScreenFeedback.Flash(message, new Color(1f, 0.6f, 0.15f), 1.2f);
    }

    private SpellProjectile FindClosestSpellInSweptSwordVolume()
    {
        var best = (SpellProjectile)null;
        var bestDepth = float.PositiveInfinity;
        var sword = desktopSword.transform;
        var projectiles = volleySpawner.ActiveProjectiles;
        for (var index = 0; index < projectiles.Count; index++)
        {
            var projectile = projectiles[index];
            if (projectile == null || projectile.IsResolved)
                continue;

            var spellPosition = projectile.transform.position;
            var spellRadius = projectile.transform.lossyScale.x * 0.5f;
            var hit = SpellOverlapsBlade(spellPosition, spellRadius, sword.position, sword.rotation);
            if (!hit && hasPreviousSwordPose)
            {
                var midPosition = Vector3.Lerp(previousSwordPosition, sword.position, 0.5f);
                var midRotation = Quaternion.Slerp(previousSwordRotation, sword.rotation, 0.5f);
                hit = SpellOverlapsBlade(spellPosition, spellRadius, previousSwordPosition, previousSwordRotation)
                    || SpellOverlapsBlade(spellPosition, spellRadius, midPosition, midRotation);
            }

            if (hit && projectile.DepthMeters < bestDepth)
            {
                best = projectile;
                bestDepth = projectile.DepthMeters;
            }
        }

        return best;
    }

    private static bool SpellOverlapsBlade(Vector3 spellPosition, float spellRadius, Vector3 swordPosition, Quaternion swordRotation)
    {
        var localPosition = Quaternion.Inverse(swordRotation) * (spellPosition - swordPosition);
        return Mathf.Abs(localPosition.x) <= SwordHitHalfWidthMeters + spellRadius
            && Mathf.Abs(localPosition.y) <= SwordHitHalfThicknessMeters + spellRadius
            && localPosition.z >= SwordHitStartLocalZ - spellRadius
            && localPosition.z <= SwordHitEndLocalZ + spellRadius;
    }

    private SpellProjectile FindClosestSpellInShieldVolume()
    {
        var best = (SpellProjectile)null;
        var bestDepth = float.PositiveInfinity;
        var padding = shieldHitboxPaddingMeters;
        var projectiles = volleySpawner.ActiveProjectiles;
        for (var index = 0; index < projectiles.Count; index++)
        {
            var projectile = projectiles[index];
            if (projectile == null || projectile.IsResolved)
                continue;

            var localPosition = desktopShield.transform.InverseTransformPoint(projectile.transform.position);
            var spellRadius = projectile.transform.lossyScale.x * 0.5f;
            var insideShield = localPosition.z >= -padding - spellRadius
                && localPosition.z <= shieldHitboxForwardMeters + padding + spellRadius
                && Mathf.Abs(localPosition.x) <= shieldHitboxHalfWidthMeters + padding + spellRadius
                && Mathf.Abs(localPosition.y) <= shieldHitboxHalfHeightMeters + padding + spellRadius;

            if (insideShield && projectile.DepthMeters < bestDepth)
            {
                best = projectile;
                bestDepth = projectile.DepthMeters;
            }
        }

        return best;
    }

    private void CreateDesktopCounterTools()
    {
        desktopSword = new GameObject("Desktop Sword Placeholder");
        desktopSword.transform.SetParent(playerCamera.transform, false);
        desktopSword.transform.localPosition = new Vector3(0.42f, -0.28f, 0.62f);
        desktopSword.transform.localRotation = Quaternion.Euler(18f, -8f, -16f);        swordRestPosition = desktopSword.transform.localPosition;
        swordRestRotation = desktopSword.transform.localRotation;


        CreateToolPart(desktopSword.transform, PrimitiveType.Cube, "Sword Grip", new Vector3(0f, 0f, 0.02f), new Vector3(0.065f, 0.07f, 0.22f), new Color(0.24f, 0.15f, 0.1f));
        CreateToolPart(desktopSword.transform, PrimitiveType.Cube, "Sword Guard", new Vector3(0f, 0f, 0.17f), new Vector3(0.24f, 0.055f, 0.07f), new Color(0.88f, 0.58f, 0.18f));
        CreateToolPart(desktopSword.transform, PrimitiveType.Cube, "Sword Blade", new Vector3(0f, 0f, 0.52f), new Vector3(0.075f, 0.035f, 0.72f), new Color(0.68f, 0.83f, 0.9f));
        CreateToolPart(desktopSword.transform, PrimitiveType.Cube, "Sword Tip", new Vector3(0f, 0f, 0.91f), new Vector3(0.035f, 0.035f, 0.12f), new Color(0.82f, 0.9f, 0.95f));
        var bladeLength = SwordHitEndLocalZ - SwordHitStartLocalZ;
        swordAura = CreateAuraPart(
            desktopSword.transform,
            PrimitiveType.Cube,
            "Sword Aura",
            new Vector3(0f, 0f, (SwordHitStartLocalZ + SwordHitEndLocalZ) * 0.5f),
            new Vector3(SwordHitHalfWidthMeters * 2f, SwordHitHalfThicknessMeters * 2f, bladeLength),
            new Color(0.55f, 0.86f, 1f, 0.28f),
            Quaternion.identity);

        desktopShield = new GameObject("Desktop Shield Placeholder");
        desktopShield.transform.SetParent(playerCamera.transform, false);
        desktopShield.transform.localPosition = new Vector3(-0.4f, -0.23f, 0.78f);
        desktopShield.transform.localRotation = Quaternion.Euler(6f, 2f, -10f);        shieldRestPosition = desktopShield.transform.localPosition;
        shieldRestRotation = desktopShield.transform.localRotation;

        CreateToolPart(desktopShield.transform, PrimitiveType.Cylinder, "Buckler Face", Vector3.zero, new Vector3(0.32f, 0.04f, 0.32f), new Color(0.14f, 0.44f, 0.64f), Quaternion.Euler(90f, 0f, 0f));
        CreateToolPart(desktopShield.transform, PrimitiveType.Sphere, "Buckler Boss", new Vector3(0f, 0f, 0.07f), new Vector3(0.14f, 0.14f, 0.14f), new Color(0.95f, 0.68f, 0.22f));
        CreateToolPart(desktopShield.transform, PrimitiveType.Cube, "Buckler Strap", new Vector3(0f, -0.02f, -0.05f), new Vector3(0.12f, 0.035f, 0.045f), new Color(0.24f, 0.15f, 0.1f));
        var shieldDepth = shieldHitboxForwardMeters + shieldHitboxPaddingMeters * 2f;
        shieldAura = CreateAuraPart(
            desktopShield.transform,
            PrimitiveType.Cube,
            "Shield Aura",
            new Vector3(0f, 0f, shieldHitboxForwardMeters * 0.5f),
            new Vector3(
                (shieldHitboxHalfWidthMeters + shieldHitboxPaddingMeters) * 2f,
                (shieldHitboxHalfHeightMeters + shieldHitboxPaddingMeters) * 2f,
                shieldDepth),
            new Color(0.55f, 0.82f, 1f, 0.28f),
            Quaternion.identity);
    }

    private void EvaluateSwordSwing(float progress, out Vector3 position, out Quaternion rotation)
    {
        // A cut chambers high on the right, comes down across the body, and finishes low on the left.
        SwordFrame(new Vector3(0.5f, -0.02f, 0.28f), new Vector3(0.4f, 1f, -0.25f), new Vector3(-0.1f, 0.05f, 1f), out var chamberPosition, out var chamberRotation);
        SwordFrame(new Vector3(0.04f, -0.16f, 0.64f), new Vector3(-0.25f, -0.12f, 1f), new Vector3(-0.85f, -0.5f, 0.05f), out var strikePosition, out var strikeRotation);
        SwordFrame(new Vector3(-0.22f, -0.46f, 0.4f), new Vector3(-0.8f, -0.7f, 0.15f), new Vector3(-0.2f, 0.25f, -0.3f), out var followPosition, out var followRotation);

        if (progress <= 0.18f)
            BlendSword(progress / 0.18f, swordRestPosition, swordRestRotation, chamberPosition, chamberRotation, out position, out rotation);
        else if (progress <= 0.4f)
            BlendSword((progress - 0.18f) / 0.22f, chamberPosition, chamberRotation, strikePosition, strikeRotation, out position, out rotation);
        else if (progress <= 0.62f)
            BlendSword((progress - 0.4f) / 0.22f, strikePosition, strikeRotation, followPosition, followRotation, out position, out rotation);
        else
            BlendSword((progress - 0.62f) / 0.38f, followPosition, followRotation, swordRestPosition, swordRestRotation, out position, out rotation);
    }

    private static void SwordFrame(Vector3 grip, Vector3 tipDirection, Vector3 edgeDirection, out Vector3 position, out Quaternion rotation)
    {
        position = grip;
        var forward = tipDirection.normalized;
        var edge = Vector3.ProjectOnPlane(edgeDirection, forward);
        if (edge.sqrMagnitude < 0.0001f)
            edge = Vector3.Cross(forward, Vector3.up);
        edge.Normalize();
        rotation = Quaternion.LookRotation(forward, Vector3.Cross(forward, edge));
    }

    private static void BlendSword(float amount, Vector3 fromPosition, Quaternion fromRotation, Vector3 toPosition, Quaternion toRotation, out Vector3 position, out Quaternion rotation)
    {
        var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(amount));
        position = Vector3.Lerp(fromPosition, toPosition, t);
        rotation = Quaternion.Slerp(fromRotation, toRotation, t);
    }

    private static void CreateToolPart(Transform parent, PrimitiveType primitive, string partName, Vector3 localPosition, Vector3 localScale, Color color, Quaternion localRotation = default)
    {
        var part = GameObject.CreatePrimitive(primitive);
        part.name = partName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        part.transform.localRotation = localRotation == default ? Quaternion.identity : localRotation;
        var collider = part.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        var renderer = part.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material.color = color;
    }

    private static GameObject CreateAuraPart(Transform parent, PrimitiveType primitive, string partName, Vector3 localPosition, Vector3 localScale, Color color, Quaternion localRotation)
    {
        var part = GameObject.CreatePrimitive(primitive);
        part.name = partName;
        part.SetActive(false);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        part.transform.localRotation = localRotation;

        var collider = part.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        var renderer = part.GetComponent<Renderer>();
        if (renderer == null)
            return part;

        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.sharedMaterial = CreateAuraMaterial(color);
        return part;
    }

    private static Material CreateAuraMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        var material = new Material(shader);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        material.color = color;
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.SetInt("_Cull", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(color.r, color.g, color.b, 1f));
        }

        return material;
    }
}
