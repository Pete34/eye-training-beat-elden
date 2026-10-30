using UnityEngine;

public sealed class BossPlaceholderMotion : MonoBehaviour
{
    [SerializeField, Min(0f)] private float hoverHeightMeters = 0.06f;
    [SerializeField, Min(0f)] private float hoverCyclesPerSecond = 0.45f;
    [SerializeField, Min(0f)] private float turnDegrees = 5f;
    [SerializeField, Min(0f)] private float turnsPerSecond = 0.25f;

    private Vector3 restPosition;
    private Quaternion restRotation;
    private float phaseOffset;

    private void Awake()
    {
        restPosition = transform.position;
        restRotation = transform.rotation;
        phaseOffset = Random.Range(0f, Mathf.PI * 2f);
    }

    private void Update()
    {
        var phase = Time.time + phaseOffset;
        var hover = Mathf.Sin(phase * hoverCyclesPerSecond * Mathf.PI * 2f) * hoverHeightMeters;
        var yaw = Mathf.Sin(phase * turnsPerSecond * Mathf.PI * 2f) * turnDegrees;
        transform.position = restPosition + Vector3.up * hover;
        transform.rotation = restRotation * Quaternion.Euler(0f, yaw, 0f);
    }
}
