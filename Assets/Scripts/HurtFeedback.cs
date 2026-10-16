using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Hit feedback when the player takes damage: chromatic aberration + desaturated red grade (volume),
/// red screen-edge overlay, camera shake and a punch on the health bar. Strength scales with the
/// damage (a full hit = full effect, block chip = a light version). Runs on unscaled time so the
/// parry slow motion doesn't slow it.
/// </summary>
public class HurtFeedback : MonoBehaviour
{
    [Header("References")]
    public Volume volume;                   // HurtVolume: chromatic aberration, colour grade, red vignette
    public Image overlay;                   // HUD3D/HurtOverlay, red at the screen edges
    public GuardJuice juice;                // owns the camera shake
    public RectTransform healthBar;         // punched on hit

    [Header("Strength")]
    public float fullHitDamage = 20f;       // this much damage = full effect
    [Range(0f, 1f)] public float overlayAlpha = 0.55f;

    [Header("Timing (real seconds)")]
    public float attack = 0.04f;
    public float hold = 0.06f;
    public float release = 0.45f;

    [Header("Shake / health bar")]
    public float shake = 1.8f;              // degrees at full strength
    public float shakeTime = 0.25f;
    public float barPunch = 0.25f;
    public float barPunchTime = 0.2f;

    float hitTime = float.NegativeInfinity;
    float strength;

    void Start()
    {
        // Scene/domain reload is off in this project — start clean
        hitTime = float.NegativeInfinity;
        strength = 0f;
        Apply(0f, 0f);
    }

    void OnDisable()
    {
        hitTime = float.NegativeInfinity;
        Apply(0f, 0f);
    }

    public void Hit(float damage)
    {
        float s = Mathf.Clamp01(damage / Mathf.Max(fullHitDamage, 0.0001f));
        // a bigger hit restarts the pulse; a smaller one during a strong pulse doesn't weaken it
        if (s >= strength * Envelope(Time.unscaledTime - hitTime))
        {
            strength = s;
            hitTime = Time.unscaledTime;
        }
        if (juice != null) juice.Shake(shake * s, shakeTime);
    }

    void Update()
    {
        float t = Time.unscaledTime - hitTime;
        float punch = t < barPunchTime ? 1f - t / barPunchTime : 0f;
        Apply(strength * Envelope(t), strength * punch * punch);
    }

    /// <summary>0..1: fast in, short hold, smooth fade out.</summary>
    float Envelope(float t)
    {
        if (t < 0f) return 0f;
        if (t < attack) return t / attack;
        t -= attack;
        if (t < hold) return 1f;
        t -= hold;
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / release));
    }

    void Apply(float amount, float punch)
    {
        if (volume != null) volume.weight = amount;
        if (overlay != null)
        {
            Color c = overlay.color;
            c.a = overlayAlpha * amount;
            overlay.color = c;
        }
        if (healthBar != null) healthBar.localScale = Vector3.one * (1f + barPunch * punch);
    }
}
