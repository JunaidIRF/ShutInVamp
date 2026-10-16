using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Vampire sunlight. If the sun can reach the player's head or chest (a ray towards the sun isn't blocked),
/// the player warms up and, after a short moment, catches fire: steady burn damage while lit, lingering
/// briefly after reaching shade. Safe exactly where the scene draws shadow.
/// Drives a warm grade + flickering edge glow (a warning while warming up) and flames on the hands.
/// </summary>
public class SunBurn : MonoBehaviour
{
    [Header("References")]
    public Player player;
    public Light sun;                       // falls back to RenderSettings.sun / the first directional light
    public Volume volume;                   // BurnVolume: warm grade + orange vignette
    public Image overlay;                   // ScreenFX/BurnOverlay: ScreenFire flames, alpha = strength
    public ParticleSystem[] handFires;      // flames parented to the hands
    public Light[] handLights;              // small orange point lights in the hand fires
    public Image sunIcon;                   // HUD3D/SunIcon by the health bar: the "you're in sunlight" cue

    [Header("Burning")]
    public float igniteTime = 0.3f;         // seconds in light before catching fire
    public float lingerTime = 0.5f;         // keeps burning this long after reaching shade
    public float burnTickDamage = 5f;
    public float burnTickInterval = 0.5f;   // 5 every 0.5 s = 10 HP/s

    [Header("Look")]
    [Range(0f, 1f)] public float overlayAlpha = 0.3f;
    public float flicker = 0.15f;
    public float flickerSpeed = 8f;
    public float fadeSpeed = 4f;            // how fast the burn look follows the state
    public float handLightIntensity = 1.5f;

    [Header("Sun icon")]
    public Color iconShade = new Color(0.7f, 0.7f, 0.7f, 0.3f);
    public Color iconSun = new Color(1f, 0.95f, 0.6f, 1f);
    public Color iconFire = new Color(1f, 0.5f, 0.1f, 1f);
    public float iconPulse = 0.15f;         // scale pulse while in sun (stronger while burning)

    public bool IsLit { get; private set; }
    public bool OnFire { get; private set; }

    Collider body;
    float exposure;                         // 0..1 warming up towards igniting
    float lastLitTime;
    float nextTickTime;
    float look;                             // smoothed 0..1 visual intensity

    void Start()
    {
        if (player == null) player = GetComponent<Player>();
        body = GetComponent<Collider>();
        if (sun == null) sun = FindSun();

        // Scene/domain reload is off in this project — start clean
        exposure = 0f;
        look = 0f;
        OnFire = false;
        IsLit = false;
        SetHandFires(false, true);
        ApplyLook(0f);
        UpdateSunIcon();
    }

    void OnDisable()
    {
        OnFire = false;
        SetHandFires(false, true);
        ApplyLook(0f);
    }

    void Update()
    {
        IsLit = CheckLit();
        float dt = Time.deltaTime;

        exposure = Mathf.Clamp01(exposure + (IsLit ? dt : -dt) / Mathf.Max(igniteTime, 0.0001f));
        if (IsLit) lastLitTime = Time.time;

        if (!OnFire && exposure >= 1f) Ignite();
        else if (OnFire && !IsLit && Time.time - lastLitTime > lingerTime) Extinguish();

        if (OnFire && Time.time >= nextTickTime)
        {
            nextTickTime = Time.time + burnTickInterval;
            if (player != null) player.TakeDamage(burnTickDamage);   // HurtFeedback pulses on each tick
        }

        // screen grade only while actually burning — the sun icon is the warning
        look = Mathf.MoveTowards(look, OnFire ? 1f : 0f, fadeSpeed * Time.unscaledDeltaTime);
        float f = (Mathf.PerlinNoise(Time.unscaledTime * flickerSpeed, 0.37f) - 0.5f) * 2f * flicker;
        ApplyLook(Mathf.Clamp01(look + f * look));
        UpdateSunIcon();
    }

    void UpdateSunIcon()
    {
        if (sunIcon == null) return;

        float t = Time.unscaledTime;
        Color c;
        float pulse;
        if (OnFire)
        {
            c = iconFire;
            pulse = iconPulse * 1.5f * Mathf.Abs(Mathf.Sin(t * 10f));
        }
        else if (IsLit)
        {
            c = Color.Lerp(iconShade, iconSun, 0.6f + 0.4f * exposure);
            pulse = iconPulse * Mathf.Abs(Mathf.Sin(t * Mathf.Lerp(6f, 14f, exposure)));   // quicker as you heat up
        }
        else
        {
            c = Color.Lerp(iconShade, iconSun, exposure);   // cools down as exposure drains
            pulse = 0f;
        }
        sunIcon.color = c;
        sunIcon.rectTransform.localScale = Vector3.one * (1f + pulse);
    }

    void Ignite()
    {
        OnFire = true;
        nextTickTime = Time.time;   // first tick straight away so catching fire is felt
        SetHandFires(true, false);
    }

    void Extinguish()
    {
        OnFire = false;
        exposure = 0f;
        SetHandFires(false, false);
    }

    /// <summary>Head or chest can see the sun (own collider and triggers ignored).</summary>
    bool CheckLit()
    {
        if (sun == null || !sun.isActiveAndEnabled || body == null) return false;
        Vector3 toSun = -sun.transform.forward;
        int mask = ~(1 << gameObject.layer);
        Bounds b = body.bounds;
        Vector3 head = b.center + Vector3.up * (b.extents.y * 0.8f);
        Vector3 chest = b.center + Vector3.up * (b.extents.y * 0.2f);
        return !Physics.Raycast(head, toSun, 200f, mask, QueryTriggerInteraction.Ignore)
            || !Physics.Raycast(chest, toSun, 200f, mask, QueryTriggerInteraction.Ignore);
    }

    void SetHandFires(bool on, bool clear)
    {
        if (handFires == null) return;
        foreach (var ps in handFires)
        {
            if (ps == null) continue;
            if (on) ps.Play(true);
            else ps.Stop(true, clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
        }
    }

    void ApplyLook(float amount)
    {
        if (volume != null) volume.weight = amount;
        if (overlay != null)
        {
            Color c = overlay.color;
            c.a = overlayAlpha * amount;
            overlay.color = c;
        }
        if (handLights != null)
            foreach (var l in handLights)
            {
                if (l == null) continue;
                l.intensity = handLightIntensity * amount;
                l.enabled = amount > 0.01f;
            }
    }

    static Light FindSun()
    {
        if (RenderSettings.sun != null) return RenderSettings.sun;
        foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            if (l.type == LightType.Directional) return l;
        return null;
    }
}
