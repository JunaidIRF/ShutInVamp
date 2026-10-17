using UnityEngine;

/// <summary>
/// Speed feel: faint air streaks in the world that fly past the camera when moving fast. A world-space
/// particle system is kept a little ahead of the camera along the travel direction; its particles stay
/// put, so moving makes them stream past with real parallax, stretched by the camera's own speed.
/// Strongest while sliding, building up when falling fast, faint at full sprint.
/// </summary>
public class SpeedOverlay : MonoBehaviour
{
    public PlayerMovement movement;
    public ParticleSystem streaks;          // AirStreaks (world simulation space)
    public Transform view;                  // Player Camera

    [Header("How strong")]
    public float slideStrength = 1f;        // at slide start speed (scales with the slide's speed)
    public float slideFullSpeed = 9f;
    public float fallStartSpeed = 8f;
    public float fallFullSpeed = 20f;
    public float fallStrength = 0.8f;
    public float sprintStrength = 0.3f;     // at full sprint speed

    [Header("Streaks")]
    public float maxRate = 120f;            // particles per second at full strength
    public float spawnAhead = 0.35f;        // seconds of travel ahead of the camera they appear
    public float minAhead = 2.5f;           // ... but at least this far (m)

    [Header("Easing")]
    public float fadeIn = 6f;               // per second
    public float fadeOut = 3f;

    float strength;

    void Start()
    {
        if (movement == null) movement = GetComponent<PlayerMovement>();
        strength = 0f;
        Apply(Vector3.zero);
    }

    void OnDisable()
    {
        strength = 0f;
        Apply(Vector3.zero);
    }

    void Update()
    {
        if (movement == null || streaks == null || view == null) return;
        Vector3 v = movement.velocity;
        float flat = new Vector3(v.x, 0f, v.z).magnitude;

        float want = 0f;
        if (movement.IsSliding) want = Mathf.Max(want, slideStrength * Mathf.Clamp01(flat / Mathf.Max(slideFullSpeed, 0.01f)));
        if (!movement.isGrounded) want = Mathf.Max(want, fallStrength * Mathf.InverseLerp(fallStartSpeed, fallFullSpeed, -v.y));
        if (movement.isGrounded && movement.isSprinting)
            want = Mathf.Max(want, sprintStrength * Mathf.InverseLerp(movement.walkSpeed, movement.sprintSpeed, flat));

        float rate = want > strength ? fadeIn : fadeOut;
        strength = Mathf.MoveTowards(strength, want, rate * Time.unscaledDeltaTime);
        Apply(v);
    }

    void Apply(Vector3 velocity)
    {
        if (streaks == null) return;
        var emission = streaks.emission;
        emission.rateOverTime = strength * maxRate;
        if (strength <= 0f || view == null) return;

        // the spawn volume sits ahead along where we're going, facing that way
        float speed = velocity.magnitude;
        Vector3 dir = speed > 0.5f ? velocity / speed : view.forward;
        streaks.transform.SetPositionAndRotation(view.position + dir * Mathf.Max(minAhead, speed * spawnAhead), Quaternion.LookRotation(dir));
        if (!streaks.isPlaying) streaks.Play();
    }
}
