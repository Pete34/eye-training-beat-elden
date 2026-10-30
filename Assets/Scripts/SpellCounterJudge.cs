using System;
using UnityEngine;

public enum SpellCounterAction
{
    Slash,
    PalmBlock
}

public enum SpellCounterResult
{
    NoTarget,
    Correct,
    WrongAction,
    OrderError
}

public sealed class SpellCounterJudge : MonoBehaviour
{
    [SerializeField] private Transform viewingEye;
    [SerializeField] private SpellVolleySpawner volleySpawner;
    [SerializeField, Min(0.01f)] private float acquisitionMinimumDepthMeters = 0.45f;
    [SerializeField, Min(0.1f)] private float acquisitionMaximumDepthMeters = 4f;
    [SerializeField, Range(1f, 179f)] private float acquisitionFieldOfViewDegrees = 55f;

    public event Action<SpellProjectile> CorrectCounter;
    public event Action<SpellProjectile> WrongAction;
    public event Action<SpellProjectile, SpellProjectile> OrderError;

    private void Awake()
    {
        if (viewingEye == null && Camera.main != null)
            viewingEye = Camera.main.transform;

        if (volleySpawner == null)
            volleySpawner = FindAnyObjectByType<SpellVolleySpawner>();
    }

    public SpellProjectile FindNearestRequiredTarget()
    {
        if (viewingEye == null || volleySpawner == null)
            return null;

        var nearest = (SpellProjectile)null;
        var nearestDepth = float.PositiveInfinity;
        var halfFieldOfView = acquisitionFieldOfViewDegrees * 0.5f;
        var projectiles = volleySpawner.ActiveProjectiles;

        for (var index = 0; index < projectiles.Count; index++)
        {
            var projectile = projectiles[index];
            if (projectile == null || projectile.IsResolved)
                continue;

            var depth = projectile.DepthMeters;
            if (depth < acquisitionMinimumDepthMeters || depth > acquisitionMaximumDepthMeters)
                continue;

            var direction = projectile.transform.position - viewingEye.position;
            if (Vector3.Angle(viewingEye.forward, direction) > halfFieldOfView)
                continue;

            if (depth < nearestDepth)
            {
                nearest = projectile;
                nearestDepth = depth;
            }
        }

        return nearest;
    }

    public SpellCounterResult EvaluateContact(SpellProjectile contactedSpell, SpellCounterAction action)
    {
        var requiredSpell = FindNearestRequiredTarget();
        if (requiredSpell == null || contactedSpell == null)
            return SpellCounterResult.NoTarget;

        if (contactedSpell != requiredSpell)
        {
            OrderError?.Invoke(requiredSpell, contactedSpell);
            return SpellCounterResult.OrderError;
        }

        var expectedAction = contactedSpell.GlyphType == SpellGlyphType.ShieldRune
            ? SpellCounterAction.PalmBlock
            : SpellCounterAction.Slash;

        if (action != expectedAction)
        {
            WrongAction?.Invoke(contactedSpell);
            return SpellCounterResult.WrongAction;
        }

        contactedSpell.MarkCountered();
        CorrectCounter?.Invoke(contactedSpell);
        return SpellCounterResult.Correct;
    }
}
