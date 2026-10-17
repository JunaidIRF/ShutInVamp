using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Subtle speed feel: fades in a post-process volume (slight lens stretch, a touch of edge fringing and a
/// soft vignette — Assets/Settings/SpeedFeedback.asset) when moving fast: strongest while sliding,
/// building up when falling fast, faint at full sprint. Lower priority than the hurt / parry / burn
/// volumes, so those win while they play.
/// </summary>
public class SpeedOverlay : MonoBehaviour
{
    public PlayerMovement movement;
    public Volume volume;                   // SpeedVolume (global, weight driven here)

    [Header("How strong")]
    public float slideStrength = 1f;        // at slide start speed (scales with the slide's speed)
    public float slideFullSpeed = 9f;
    public float fallStartSpeed = 8f;
    public float fallFullSpeed = 20f;
    public float fallStrength = 0.8f;
    public float sprintStrength = 0.3f;     // at full sprint speed

    [Header("Easing")]
    public float fadeIn = 6f;               // per second
    public float fadeOut = 3f;

    float strength;

    void Start()
    {
        if (movement == null) movement = GetComponent<PlayerMovement>();
        strength = 0f;
        Apply();
    }

    void OnDisable()
    {
        strength = 0f;
        Apply();
    }

    void Update()
    {
        if (movement == null || volume == null) return;
        Vector3 v = movement.velocity;
        float flat = new Vector3(v.x, 0f, v.z).magnitude;

        float want = 0f;
        if (movement.IsSliding) want = Mathf.Max(want, slideStrength * Mathf.Clamp01(flat / Mathf.Max(slideFullSpeed, 0.01f)));
        if (!movement.isGrounded) want = Mathf.Max(want, fallStrength * Mathf.InverseLerp(fallStartSpeed, fallFullSpeed, -v.y));
        if (movement.isGrounded && movement.isSprinting)
            want = Mathf.Max(want, sprintStrength * Mathf.InverseLerp(movement.walkSpeed, movement.sprintSpeed, flat));

        float rate = want > strength ? fadeIn : fadeOut;
        strength = Mathf.MoveTowards(strength, want, rate * Time.unscaledDeltaTime);
        Apply();
    }

    void Apply()
    {
        if (volume != null) volume.weight = strength;
    }
}
