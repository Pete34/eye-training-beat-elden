using UnityEngine;

public sealed class PlayerDamageMeter : MonoBehaviour
{
    [SerializeField] private SpellVolleySpawner volleySpawner;
    [SerializeField] private Camera viewCamera;
    [SerializeField, Min(1f)] private float maxHealth = 100f;
    [SerializeField, Min(0f)] private float missDamage = 10f;
    [SerializeField, Min(0f)] private float retaliationDamage = 20f;
    [SerializeField] private Vector3 barLocalPosition = new Vector3(0f, -0.3f, 1f);
    [SerializeField, Min(0.05f)] private float barWidthMeters = 0.5f;

    private float health;
    private Transform fill;
    private Renderer fillRenderer;

    public float Health => health;
    public float Normalized => health / maxHealth;

    private void Awake()
    {
        if (volleySpawner == null)
            volleySpawner = FindAnyObjectByType<SpellVolleySpawner>();
        if (viewCamera == null)
            viewCamera = Camera.main;

        health = maxHealth;
        BuildBar();
    }

    private void OnEnable()
    {
        if (volleySpawner != null)
            volleySpawner.ProjectileHitPlayer += OnProjectileHitPlayer;
    }

    private void OnDisable()
    {
        if (volleySpawner != null)
            volleySpawner.ProjectileHitPlayer -= OnProjectileHitPlayer;
    }

    private void OnProjectileHitPlayer(SpellProjectile projectile)
    {
        if (ScreenFeedback.IsGameOver)
            return;

        var previousHealth = health;
        health = Mathf.Max(0f, health - (projectile.IsRetaliating ? retaliationDamage : missDamage));
        var healthLost = previousHealth - health;
        var percentLost = maxHealth > 0f ? healthLost / maxHealth * 100f : 0f;
        UpdateBar();
        ScreenFeedback.Flash(string.Format("-{0}%", Mathf.RoundToInt(percentLost)), new Color(1f, 0.25f, 0.2f), 0.8f);

        if (health > 0f)
            return;

        volleySpawner.SpawnBlocked = true;
        var projectiles = volleySpawner.ActiveProjectiles;
        for (var index = 0; index < projectiles.Count; index++)
        {
            if (projectiles[index] != null && projectiles[index] != projectile)
                projectiles[index].MarkCountered();
        }

        ScreenFeedback.EndGame("GAME OVER", new Color(1f, 0.2f, 0.15f));
    }

    private void BuildBar()
    {
        if (viewCamera == null)
            return;

        var root = new GameObject("Player Damage Meter");
        root.transform.SetParent(viewCamera.transform, false);
        root.transform.localPosition = barLocalPosition;

        CreatePart(root.transform, "Back", new Color(0.1f, 0.1f, 0.1f), 0.01f, 0f);
        fill = CreatePart(root.transform, "Fill", Color.green, 0.01f, -0.005f);
        fillRenderer = fill.GetComponent<Renderer>();
        UpdateBar();
    }

    private Transform CreatePart(Transform parent, string partName, Color color, float height, float z)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = partName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = new Vector3(0f, 0f, z);
        part.transform.localScale = new Vector3(barWidthMeters, height * 2f, 0.002f);
        var collider = part.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
        part.GetComponent<Renderer>().material.color = color;
        return part.transform;
    }

    private void UpdateBar()
    {
        if (fill == null)
            return;

        var amount = Normalized;
        fill.localScale = new Vector3(barWidthMeters * amount, fill.localScale.y, fill.localScale.z);
        // Left-anchored fill.
        fill.localPosition = new Vector3(-barWidthMeters * (1f - amount) * 0.5f, 0f, fill.localPosition.z);
        fillRenderer.material.color = Color.Lerp(Color.red, Color.green, amount);
    }
}
