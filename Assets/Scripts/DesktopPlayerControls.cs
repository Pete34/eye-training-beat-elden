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

    [SerializeField] private bool showDesktopTools = true;

    private bool desktopModeActive;
    private GameObject desktopSword;
    private GameObject desktopShield;    private Vector3 swordRestPosition;
    private Quaternion swordRestRotation;
    private Vector3 shieldRestPosition;
    private Quaternion shieldRestRotation;
    private float slashStartTime = -10f;
    private float blockStartTime = -10f;    private bool slashContactResolved;
    private bool blockContactResolved;    private Vector3 previousSwordBase;
    private Vector3 previousSwordTip;
    private bool hasPreviousSwordPose;




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
        if (!desktopModeActive || xrOrigin == null)
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
        xrOrigin.transform.position = nextPosition;
    }

public void TriggerSlashAnimation()
    {
        if (!desktopModeActive)
            return;

        slashStartTime = Time.time - 0.07f;
        slashContactResolved = false;
        hasPreviousSwordPose = false;
        UpdateCounterToolAnimations();
    }

public void TriggerBlockAnimation()
    {
        if (!desktopModeActive)
            return;

        blockStartTime = Time.time - 0.08f;
        blockContactResolved = false;
        UpdateCounterToolAnimations();
    }

    private void UpdateCounterToolAnimations()
    {        var idleSway = Mathf.Sin(Time.time * 1.7f) * 0.02f;

        if (desktopSword != null)
        {
            var slashProgress = Mathf.Clamp01((Time.time - slashStartTime) / 0.28f);
            var slashArc = Mathf.Sin(slashProgress * Mathf.PI);
            desktopSword.transform.localPosition = swordRestPosition + new Vector3(-0.3f * slashArc + idleSway, 0.16f * slashArc + idleSway, 0.05f * slashArc);
            desktopSword.transform.localRotation = swordRestRotation * Quaternion.Euler(idleSway * 18f, 0f, -105f * slashArc + idleSway * 24f);
        }

        if (desktopShield != null)
        {
            var blockProgress = Mathf.Clamp01((Time.time - blockStartTime) / 0.32f);
            var blockThrust = Mathf.Sin(blockProgress * Mathf.PI);
            desktopShield.transform.localPosition = shieldRestPosition + new Vector3(-idleSway, idleSway, 0.5f * blockThrust);
            desktopShield.transform.localRotation = shieldRestRotation * Quaternion.Euler(-14f * blockThrust + idleSway * 20f, 0f, idleSway * 16f);
        }
    }

private void TryResolveDesktopToolContacts()
    {
        if (volleySpawner == null || counterJudge == null)
            return;

        if (!slashContactResolved && desktopSword != null)
        {
            var progress = (Time.time - slashStartTime) / 0.28f;
            if (progress >= 0.08f && progress <= 0.95f)
            {
                var swordBase = desktopSword.transform.TransformPoint(new Vector3(0f, 0f, 0.18f));
                var swordTip = desktopSword.transform.TransformPoint(new Vector3(0f, 0f, 1.04f));
                var contact = FindClosestSpellInSweptSwordVolume(swordBase, swordTip);
                if (contact != null)
                {
                    slashContactResolved = true;
                    counterJudge.EvaluateContact(contact, SpellCounterAction.Slash);
                }

                previousSwordBase = swordBase;
                previousSwordTip = swordTip;
                hasPreviousSwordPose = true;
            }
            else
            {
                hasPreviousSwordPose = false;
            }
        }

        if (!blockContactResolved && desktopShield != null)
        {
            var progress = (Time.time - blockStartTime) / 0.32f;
            if (progress >= 0.08f && progress <= 0.9f)
            {
                var contact = FindClosestSpellInShieldVolume();
                if (contact != null)
                {
                    blockContactResolved = true;
                    counterJudge.EvaluateContact(contact, SpellCounterAction.PalmBlock);
                }
            }
        }
    }

private SpellProjectile FindClosestSpellInSweptSwordVolume(Vector3 swordBase, Vector3 swordTip)
    {
        var best = (SpellProjectile)null;
        var bestDepth = float.PositiveInfinity;
        var projectiles = volleySpawner.ActiveProjectiles;
        for (var index = 0; index < projectiles.Count; index++)
        {
            var projectile = projectiles[index];
            if (projectile == null || projectile.IsResolved)
                continue;

            var spellPosition = projectile.transform.position;
            var spellRadius = projectile.transform.lossyScale.x * 0.5f;
            var minimumDistance = DistanceToSegment(spellPosition, swordBase, swordTip);
            if (hasPreviousSwordPose)
            {
                minimumDistance = Mathf.Min(minimumDistance, DistanceToSegment(spellPosition, previousSwordBase, swordBase));
                minimumDistance = Mathf.Min(minimumDistance, DistanceToSegment(spellPosition, previousSwordTip, swordTip));
                minimumDistance = Mathf.Min(minimumDistance, DistanceToSegment(spellPosition, previousSwordBase, previousSwordTip));
            }

            if (minimumDistance <= 0.09f + spellRadius && projectile.DepthMeters < bestDepth)
            {
                best = projectile;
                bestDepth = projectile.DepthMeters;
            }
        }

        return best;
    }

    private SpellProjectile FindClosestSpellInShieldVolume()
    {
        var best = (SpellProjectile)null;
        var bestDepth = float.PositiveInfinity;
        var padding = 0.08f;
        var projectiles = volleySpawner.ActiveProjectiles;
        for (var index = 0; index < projectiles.Count; index++)
        {
            var projectile = projectiles[index];
            if (projectile == null || projectile.IsResolved)
                continue;

            var localPosition = desktopShield.transform.InverseTransformPoint(projectile.transform.position);
            var spellRadius = projectile.transform.lossyScale.x * 0.5f;
            var insideShield = localPosition.z >= -padding - spellRadius
                && localPosition.z <= 0.3f + padding + spellRadius
                && Mathf.Abs(localPosition.x) <= 0.17f + padding + spellRadius
                && Mathf.Abs(localPosition.y) <= 0.23f + padding + spellRadius;

            if (insideShield && projectile.DepthMeters < bestDepth)
            {
                best = projectile;
                bestDepth = projectile.DepthMeters;
            }
        }

        return best;
    }

    private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        var segment = end - start;
        var lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= Mathf.Epsilon)
            return Vector3.Distance(point, start);

        var amount = Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);
        return Vector3.Distance(point, start + segment * amount);
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

        desktopShield = new GameObject("Desktop Shield Placeholder");
        desktopShield.transform.SetParent(playerCamera.transform, false);
        desktopShield.transform.localPosition = new Vector3(-0.43f, -0.25f, 0.82f);
        desktopShield.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);        shieldRestPosition = desktopShield.transform.localPosition;
        shieldRestRotation = desktopShield.transform.localRotation;

        CreateToolPart(desktopShield.transform, PrimitiveType.Cube, "Shield Face", Vector3.zero, new Vector3(0.34f, 0.46f, 0.08f), new Color(0.12f, 0.42f, 0.62f));
        CreateToolPart(desktopShield.transform, PrimitiveType.Cube, "Shield Boss", new Vector3(0f, 0f, 0.055f), new Vector3(0.1f, 0.1f, 0.04f), new Color(0.95f, 0.68f, 0.22f));
    }

    private static void CreateToolPart(Transform parent, PrimitiveType primitive, string partName, Vector3 localPosition, Vector3 localScale, Color color)
    {
        var part = GameObject.CreatePrimitive(primitive);
        part.name = partName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        var collider = part.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        var renderer = part.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material.color = color;
    }
}
