using Unity.XR.CoreUtils;
using UnityEngine;using UnityEngine.InputSystem;

using UnityEngine.XR;using XRCommonUsages = UnityEngine.XR.CommonUsages;


public sealed class SpellCounterInput : MonoBehaviour
{
    [SerializeField] private SpellVolleySpawner volleySpawner;
    [SerializeField] private SpellCounterJudge counterJudge;
    [SerializeField, Min(0.1f)] private float slashSpeedThreshold = 1.2f;
    [SerializeField, Min(0.1f)] private float swordLengthMeters = 0.8f;
    [SerializeField, Min(0.01f)] private float shieldProjectionMeters = 0.16f;
    [SerializeField, Min(0.01f)] private float shieldWidthMeters = 0.4f;
    [SerializeField, Min(0.01f)] private float shieldHeightMeters = 0.4f;
    [SerializeField, Min(0f)] private float shieldPaddingMeters = 0f;
    [SerializeField] private bool enableEditorKeyboardTesting = true;

    private XROrigin xrOrigin;
    private Vector3 previousLeftPosition;
    private Vector3 previousRightPosition;
    private Quaternion previousLeftRotation = Quaternion.identity;
    private Quaternion previousRightRotation = Quaternion.identity;
    private bool hasLeftPose;
    private bool hasRightPose;    private bool leftTriggerWasPressed;
    private bool rightTriggerWasPressed;


    private void Awake()
    {
        xrOrigin = FindAnyObjectByType<XROrigin>();
        if (volleySpawner == null)
            volleySpawner = FindAnyObjectByType<SpellVolleySpawner>();
        if (counterJudge == null)
            counterJudge = FindAnyObjectByType<SpellCounterJudge>();
    }

private void Update()
    {
        ProcessHand(XRNode.LeftHand, ref previousLeftPosition, ref previousLeftRotation, ref hasLeftPose, ref leftTriggerWasPressed);
        ProcessHand(XRNode.RightHand, ref previousRightPosition, ref previousRightRotation, ref hasRightPose, ref rightTriggerWasPressed);

        var keyboard = Keyboard.current;
        if (!enableEditorKeyboardTesting || counterJudge == null || keyboard == null)
            return;

        if (keyboard.fKey.wasPressedThisFrame)
            DebugCounter(SpellCounterAction.Slash);
        if (keyboard.jKey.wasPressedThisFrame)
            DebugCounter(SpellCounterAction.PalmBlock);
        if (keyboard.spaceKey.wasPressedThisFrame && volleySpawner != null)
            volleySpawner.SpawnVolley();
    }

private void ProcessHand(XRNode handNode, ref Vector3 previousPosition, ref Quaternion previousRotation, ref bool hasPose, ref bool triggerWasPressed)
    {
        var device = InputDevices.GetDeviceAtXRNode(handNode);
        if (!device.isValid || !device.TryGetFeatureValue(XRCommonUsages.devicePosition, out var localPosition))
            return;

        device.TryGetFeatureValue(XRCommonUsages.deviceRotation, out var localRotation);
        device.TryGetFeatureValue(XRCommonUsages.deviceVelocity, out var localVelocity);
        device.TryGetFeatureValue(XRCommonUsages.triggerButton, out var triggerPressed);

        var handPosition = xrOrigin != null ? xrOrigin.transform.TransformPoint(localPosition) : localPosition;
        var handRotation = xrOrigin != null ? xrOrigin.transform.rotation * localRotation : localRotation;
        var handVelocity = xrOrigin != null ? xrOrigin.transform.TransformDirection(localVelocity) : localVelocity;
        var triggerStarted = triggerPressed && !triggerWasPressed;

        if (triggerStarted)
        {
            var action = handVelocity.magnitude >= slashSpeedThreshold
                ? SpellCounterAction.Slash
                : SpellCounterAction.PalmBlock;
            var contact = action == SpellCounterAction.Slash
                ? FindSlashContact(handPosition, handRotation, previousPosition, previousRotation, hasPose)
                : FindShieldContact(handPosition, handRotation);

            if (action == SpellCounterAction.Slash && counterJudge != null)
                counterJudge.ReportSlash(handPosition, handPosition + handRotation * Vector3.forward * swordLengthMeters * 1.1f);

            if (contact != null && counterJudge != null)
                counterJudge.EvaluateContact(contact, action);
            else if (contact == null && volleySpawner != null && volleySpawner.ActiveProjectiles.Count > 0)
                ReportWhiffFromDepth();
        }

        previousPosition = handPosition;
        previousRotation = handRotation;
        hasPose = true;
        triggerWasPressed = triggerPressed;
    }

    private void ReportWhiffFromDepth()
    {
        var required = counterJudge != null ? counterJudge.FindNearestRequiredTarget() : null;
        var message = "TOO EARLY!";

        if (required != null)
        {
            if (required.DepthMeters < 0.38f)
                message = "TOO LATE!";
            else if (required.DepthMeters <= 0.9f)
                message = "MISSED!";
        }

        ScreenFeedback.Flash(message, new Color(1f, 0.6f, 0.15f), 1.2f);
    }

    private SpellProjectile FindSlashContact(Vector3 handPosition, Quaternion handRotation, Vector3 previousPosition, Quaternion previousRotation, bool hasPose)
    {
        var best = (SpellProjectile)null;
        var nearestDepth = float.PositiveInfinity;
        var effectiveSwordLength = swordLengthMeters * 1.1f;

        for (var index = 0; index < volleySpawner.ActiveProjectiles.Count; index++)
        {
            var projectile = volleySpawner.ActiveProjectiles[index];
            if (projectile == null || projectile.IsResolved)
                continue;

            var spellPosition = projectile.transform.position;
            var spellRadius = projectile.transform.lossyScale.x * 0.5f;
            var hit = SpellOverlapsBlade(spellPosition, spellRadius, handPosition, handRotation, effectiveSwordLength);
            if (!hit && hasPose)
            {
                var midPosition = Vector3.Lerp(previousPosition, handPosition, 0.5f);
                var midRotation = Quaternion.Slerp(previousRotation, handRotation, 0.5f);
                hit = SpellOverlapsBlade(spellPosition, spellRadius, previousPosition, previousRotation, effectiveSwordLength)
                    || SpellOverlapsBlade(spellPosition, spellRadius, midPosition, midRotation, effectiveSwordLength);
            }

            if (hit && projectile.DepthMeters < nearestDepth)
            {
                best = projectile;
                nearestDepth = projectile.DepthMeters;
            }
        }

        return best;
    }

    private static bool SpellOverlapsBlade(Vector3 spellPosition, float spellRadius, Vector3 handPosition, Quaternion handRotation, float length)
    {
        var localPosition = Quaternion.Inverse(handRotation) * (spellPosition - handPosition);
        return Mathf.Abs(localPosition.x) <= DesktopPlayerControls.SwordHitHalfWidthMeters + spellRadius
            && Mathf.Abs(localPosition.y) <= DesktopPlayerControls.SwordHitHalfThicknessMeters + spellRadius
            && localPosition.z >= -spellRadius
            && localPosition.z <= length + spellRadius;
    }

    private SpellProjectile FindShieldContact(Vector3 handPosition, Quaternion handRotation)
    {
        var best = (SpellProjectile)null;
        var nearestDepth = float.PositiveInfinity;
        var padding = shieldPaddingMeters;

        for (var index = 0; index < volleySpawner.ActiveProjectiles.Count; index++)
        {
            var projectile = volleySpawner.ActiveProjectiles[index];
            if (projectile == null || projectile.IsResolved)
                continue;

            var localPosition = Quaternion.Inverse(handRotation) * (projectile.transform.position - handPosition);
            var radius = projectile.transform.lossyScale.x * 0.5f;
            var intersectsShield = localPosition.z >= -padding - radius
                && localPosition.z <= shieldProjectionMeters + padding + radius
                && Mathf.Abs(localPosition.x) <= shieldWidthMeters * 0.5f + padding + radius
                && Mathf.Abs(localPosition.y) <= shieldHeightMeters * 0.5f + padding + radius;

            if (intersectsShield && projectile.DepthMeters < nearestDepth)
            {
                best = projectile;
                nearestDepth = projectile.DepthMeters;
            }
        }

        return best;
    }

private void DebugCounter(SpellCounterAction action)
    {
        var desktopTools = FindAnyObjectByType<DesktopPlayerControls>();
        if (desktopTools == null)
            return;

        if (action == SpellCounterAction.Slash)
            desktopTools.TriggerSlashAnimation();
        else
            desktopTools.TriggerBlockAnimation();
    }
}
