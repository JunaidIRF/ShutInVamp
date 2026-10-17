using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Vampire sunlight. If the sun can reach the player's head or chest (a ray towards the sun isn't blocked),
/// the player warms up and, after a short moment, catches fire: steady burn damage while lit, lingering
/// briefly after reaching shade. Safe exactly where the scene draws shadow.
/// Drives a warm grade + flickering edge glow (a warning while warming up) and flames on the hands.
/// Bare, the sun kills in a couple of seconds. Every execution (not a plain kill) feeds a sun ward (seconds of protection, capped):
/// while it lasts you don't burn at all (no fire, no damage) and it drains while you're in the light — enough to
/// dash across a street. In the sun on the ward the screen edges glow in its colour, blue when full through green
/// and orange to red as it runs out (the ward bar and sun icon follow); then the burn's own orange and the fire.
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
    public float burnTickDamage = 15f;      // bare skin...
    public float burnTickInterval = 0.2f;   // ...15 every 0.2 s = 75 HP/s: a sunlit street is death without the ward

    [Header("Sun ward (executions buy time in the sun)")]
    [FormerlySerializedAs("wardPerKill")]
    public float wardPerExecution = 4f;     // s of protection each execution gives (plain kills give none)...
    public float wardMax = 8f;              // ...stacking up to this
    public HealthBarFx wardBar;             // HUD3D/WardBar: eased fill + white trail of what the sun just took
    public Graphic wardFillGraphic;         // flashes bright when an execution tops it up
    [Tooltip("The ward's colour by how much is left (right = full): screen edges, bar and sun icon")]
    public Gradient wardColors = DefaultWardColors();
    public Color wardGainColor = new Color(1f, 0.9f, 0.55f, 1f);

    [Header("Sound")]
    public Sfx burnLoop;                    // loops while on fire (its volume is the loop's full volume)
    public Sfx burnTick;                    // every damage tick
    public float loopFadeIn = 6f;           // volume per second up when catching fire...
    public float loopFadeOut = 2.5f;        // ...and down when it goes out

    [Header("Look")]
    [Range(0f, 1f)] public float overlayAlpha = 0.3f;
    [Range(0f, 1f)] public float wardVignette = 0.42f;      // how dark the coloured edges get in the sun on the ward...
    [Range(0.01f, 1f)] public float wardVignetteSmoothness = 0.45f;   // ...and how far in they reach
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
    /// <summary>Seconds of sun the ward still holds off.</summary>
    public float Ward { get; private set; }

    Collider body;
    AudioSource loopSource;
    float exposure;                         // 0..1 warming up towards igniting
    float lastLitTime;
    float nextTickTime;
    float look;                             // smoothed 0..1 orange screen glow
    float fireLook;                         // smoothed 0..1 flames on screen + hand lights (only when on fire)
    Vignette vignette;                      // the burn volume's own (a runtime copy of the profile)
    Color burnVignetteColor;
    float burnVignetteIntensity, burnVignetteSmoothness;
    float wardFlash;                        // 1 on a kill, fades

    void Start()
    {
        if (player == null) player = GetComponent<Player>();
        body = GetComponent<Collider>();
        if (sun == null) sun = FindSun();

        // Scene/domain reload is off in this project — start clean
        exposure = 0f;
        look = 0f;
        fireLook = 0f;
        OnFire = false;
        IsLit = false;
        SetHandFires(false, true);
        ApplyLook(0f, 0f);
        UpdateSunIcon();
        SetWard(0f);

        // the vignette changes colour with the ward: work on this volume's own copy, not the shared asset
        vignette = null;
        if (volume != null && volume.profile != null && volume.profile.TryGet(out vignette))
        {
            burnVignetteColor = vignette.color.value;
            burnVignetteIntensity = vignette.intensity.value;
            burnVignetteSmoothness = vignette.smoothness.value;
        }

        // the burning loop: its own source (made at runtime, reused if it's already there)
        if (loopSource == null)
        {
            Transform t = transform.Find("Burn Loop");
            loopSource = t != null ? t.GetComponent<AudioSource>() : null;
            if (loopSource == null)
            {
                var go = new GameObject("Burn Loop");
                go.transform.SetParent(transform, false);
                loopSource = go.AddComponent<AudioSource>();
            }
        }
        loopSource.playOnAwake = false;
        loopSource.loop = true;
        loopSource.spatialBlend = 0f;
        loopSource.volume = 0f;
        loopSource.Stop();
    }

    void OnEnable() => Shooter.Executed += OnExecution;

    void OnExecution(Shooter _)
    {
        Ward = Mathf.Min(Ward + wardPerExecution, wardMax);
        wardFlash = 1f;
        UpdateWardBar();
    }

    /// <summary>Set the ward outright (a respawn starts with none).</summary>
    public void SetWard(float seconds)
    {
        Ward = Mathf.Clamp(seconds, 0f, wardMax);
        wardFlash = 0f;
        UpdateWardBar();
    }

    /// <summary>The ward's colour for what's left of it.</summary>
    public Color WardColor => wardColors.Evaluate(wardMax > 0f ? Ward / wardMax : 0f);

    static Gradient DefaultWardColors()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.08f, 0.05f), 0f),     // red: about to go
                new GradientColorKey(new Color(1f, 0.5f, 0.08f), 0.3f),    // orange
                new GradientColorKey(new Color(0.2f, 0.95f, 0.35f), 0.62f),// green
                new GradientColorKey(new Color(0.2f, 0.55f, 1f), 1f),      // blue: full
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    void UpdateWardBar()
    {
        if (wardBar != null) wardBar.SetValue(wardMax > 0f ? Ward / wardMax : 0f);
        if (wardFillGraphic != null) wardFillGraphic.color = Color.Lerp(WardColor, wardGainColor, wardFlash);
    }

    void OnDisable()
    {
        Shooter.Executed -= OnExecution;
        OnFire = false;
        SetHandFires(false, true);
        ApplyLook(0f, 0f);
        if (loopSource != null) { loopSource.Stop(); loopSource.volume = 0f; }
    }

    /// <summary>The burning loop follows being on fire: fades in fast, out a little slower.</summary>
    void UpdateLoop()
    {
        if (loopSource == null || burnLoop == null || burnLoop.clip == null) return;
        float target = OnFire ? burnLoop.volume : 0f;
        float rate = OnFire ? loopFadeIn : loopFadeOut;
        loopSource.volume = Mathf.MoveTowards(loopSource.volume, target, rate * burnLoop.volume * Time.unscaledDeltaTime);
        if (loopSource.volume > 0f && !loopSource.isPlaying)
        {
            loopSource.clip = burnLoop.clip;
            loopSource.pitch = burnLoop.RandomPitch();
            loopSource.Play();
        }
        else if (loopSource.volume <= 0f && loopSource.isPlaying) loopSource.Stop();
    }

    void Update()
    {
        IsLit = CheckLit();
        float dt = Time.deltaTime;

        // the ward takes the sun while it lasts: it drains instead of you warming up
        if (IsLit && Ward > 0f) Ward = Mathf.Max(0f, Ward - dt);
        bool burning = IsLit && Ward <= 0f;
        wardFlash = Mathf.MoveTowards(wardFlash, 0f, Time.unscaledDeltaTime * 2.5f);
        UpdateWardBar();

        exposure = Mathf.Clamp01(exposure + (burning ? dt : -dt) / Mathf.Max(igniteTime, 0.0001f));
        if (burning) lastLitTime = Time.time;

        if (!OnFire && exposure >= 1f) Ignite();
        else if (OnFire && Ward > 0f) Extinguish();   // an execution while burning puts it out
        else if (OnFire && !burning && Time.time - lastLitTime > lingerTime) Extinguish();

        if (OnFire && Time.time >= nextTickTime)
        {
            nextTickTime = Time.time + burnTickInterval;
            if (player != null) player.TakeDamage(burnTickDamage);   // HurtFeedback pulses on each tick
            burnTick?.Play();
        }
        UpdateLoop();

        // the orange glow while burning, softer in the sun on the ward; flames only while actually burning
        bool warded = !OnFire && IsLit && Ward > 0f;
        float glow = OnFire || warded || (IsLit && exposure > 0f) ? 1f : 0f;   // (no dip between the ward running out and catching fire)
        look = Mathf.MoveTowards(look, glow, fadeSpeed * Time.unscaledDeltaTime);
        if (vignette != null)
        {
            vignette.color.value = warded ? WardColor : burnVignetteColor;
            vignette.intensity.value = warded ? wardVignette : burnVignetteIntensity;
            vignette.smoothness.value = warded ? wardVignetteSmoothness : burnVignetteSmoothness;
        }
        fireLook = Mathf.MoveTowards(fireLook, OnFire ? 1f : 0f, fadeSpeed * Time.unscaledDeltaTime);
        float f = (Mathf.PerlinNoise(Time.unscaledTime * flickerSpeed, 0.37f) - 0.5f) * 2f * flicker;
        ApplyLook(Mathf.Clamp01(look + f * look), Mathf.Clamp01(fireLook + f * fireLook));
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
        else if (IsLit && Ward > 0f)
        {
            c = WardColor;
            pulse = iconPulse * 0.5f * Mathf.Abs(Mathf.Sin(t * 4f));   // calm: the ward has it
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

    /// <summary>Head or chest can see the sun (own collider, triggers and Ignore Raycast — the invisible map bounds — ignored).</summary>
    bool CheckLit()
    {
        if (sun == null || !sun.isActiveAndEnabled || body == null) return false;
        Vector3 toSun = -sun.transform.forward;
        int mask = Physics.DefaultRaycastLayers & ~(1 << gameObject.layer);
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

    /// <summary><paramref name="glow"/>: the orange grade; <paramref name="fire"/>: flames on screen and the hand lights.</summary>
    void ApplyLook(float glow, float fire)
    {
        if (volume != null) volume.weight = glow;
        if (overlay != null)
        {
            Color c = overlay.color;
            c.a = overlayAlpha * fire;
            overlay.color = c;
        }
        if (handLights != null)
            foreach (var l in handLights)
            {
                if (l == null) continue;
                l.intensity = handLightIntensity * fire;
                l.enabled = fire > 0.01f;
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
