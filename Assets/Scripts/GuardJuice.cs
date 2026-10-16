using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Feedback for blocks and parries. A parry drops into brief slow motion that eases back to normal,
/// pulses chromatic aberration, gives a strong camera shake and a pulse on the guard ring.
/// A block only gets a small shake. Runs on unscaled time so its own slow motion doesn't slow it.
/// </summary>
public class GuardJuice : MonoBehaviour
{
    [Header("References")]
    public Animator anim;                   // hand animator (unscaled) — slowed along with the world
    public Transform shakeTarget;           // Player Camera (CamParent is driven by the animations)
    public RectTransform ringPulse;         // HUD BlockRing
    public Volume parryVolume;              // global volume with chromatic aberration, weight pulsed on parry
    public Transform cameraHead;            // only used to find where hit effects start — they aren't parented

    [Header("Hit effects")]
    public GameObject parryFx;
    public GameObject blockFx;
    public Vector3 fxOffset = new Vector3(0f, 0f, 2f);  // in front of the view, Camera Head space

    [Header("Slow motion")]
    [Range(0.01f, 1f)] public float slowScale = 0.03f;
    public float slowHold = 0.04f;          // real seconds at full slow
    public float slowRecover = 0.12f;       // real seconds easing smoothly back to normal
    public string parryTag = "Parry";
    [Range(0f, 1f)] public float contactPose = 0.34f;  // normalized time of the swipe's impact pose (0.17 s of 0.5 s)

    [Header("Chromatic aberration")]
    public float aberrationIn = 0.03f;      // real seconds to full
    public float aberrationOut = 0.35f;     // real seconds to fade away

    [Header("Shake (degrees)")]
    public float parryShake = 2.5f;
    public float parryShakeTime = 0.3f;
    public float blockShake = 0.35f;
    public float blockShakeTime = 0.12f;
    public float shakeFrequency = 28f;

    [Header("Ring")]
    public float ringPulseScale = 0.25f;
    public float ringPulseTime = 0.15f;

    bool slowing;
    float slowStartTime;

    float aberrationStartTime = float.NegativeInfinity;

    float shakeAmount, shakeDuration, shakeEndTime;
    Quaternion shakeBaseRotation = Quaternion.identity;
    float shakeSeed;

    float pulseEndTime;

    void Start()
    {
        if (shakeTarget != null) shakeBaseRotation = shakeTarget.localRotation;
        if (parryVolume != null) parryVolume.weight = 0f;
        shakeSeed = Random.value * 100f;
    }

    public void Parry()
    {
        SnapToContactPose();
        slowing = true;
        slowStartTime = Time.unscaledTime;
        SetTimeScale(slowScale);

        aberrationStartTime = Time.unscaledTime;
        Shake(parryShake, parryShakeTime);
        pulseEndTime = Time.unscaledTime + ringPulseTime;
        SpawnFx(parryFx);
    }

    public void Block()
    {
        Shake(blockShake, blockShakeTime);
        SpawnFx(blockFx);
    }

    /// <summary>Spawned in the world in front of the view — stays where it was spawned, doesn't follow the camera.</summary>
    void SpawnFx(GameObject prefab)
    {
        if (prefab == null || cameraHead == null) return;
        Instantiate(prefab, cameraHead.TransformPoint(fxOffset), cameraHead.rotation);
    }

    void Update()
    {
        UpdateSlowMotion();
        UpdateAberration();
        UpdateShake();
        UpdateRingPulse();
    }

    void OnDisable()
    {
        // Never leave the game slowed, tinted or the camera tilted (domain/scene reload is off in this project)
        if (slowing) { slowing = false; SetTimeScale(1f); }
        if (parryVolume != null) parryVolume.weight = 0f;
        if (shakeTarget != null) shakeTarget.localRotation = shakeBaseRotation;
        if (ringPulse != null) ringPulse.localScale = Vector3.one;
        aberrationStartTime = float.NegativeInfinity;
        shakeEndTime = pulseEndTime = 0f;
    }

    // ══════════════════════════════════════════════════════════════════
    //  SLOW MOTION
    // ══════════════════════════════════════════════════════════════════
    void UpdateSlowMotion()
    {
        if (!slowing) return;

        float t = Time.unscaledTime - slowStartTime - slowHold;
        if (t <= 0f) return;

        float k = Mathf.Clamp01(t / slowRecover);
        SetTimeScale(Mathf.Lerp(slowScale, 1f, Mathf.SmoothStep(0f, 1f, k))); // smooth both ends, no snap back

        if (k >= 1f) slowing = false;
    }

    /// <summary>
    /// The bullet can land anywhere in the parry window — before the hand reaches the hit or after it.
    /// Put the swipe on its impact pose so the slow motion starts exactly on the hit.
    /// </summary>
    void SnapToContactPose()
    {
        if (anim == null) return;

        anim.Update(0f); // apply a parry trigger set this same frame
        AnimatorStateInfo state = anim.GetCurrentAnimatorStateInfo(0);
        if (!state.IsTag(parryTag)) return;

        anim.Play(state.fullPathHash, 0, contactPose);
        anim.Update(0f);
    }

    void SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        if (anim != null) anim.speed = scale; // the hand animator runs on unscaled time
    }

    // ══════════════════════════════════════════════════════════════════
    //  CHROMATIC ABERRATION
    // ══════════════════════════════════════════════════════════════════
    void UpdateAberration()
    {
        if (parryVolume == null) return;

        float t = Time.unscaledTime - aberrationStartTime;
        float w = t < aberrationIn ? t / aberrationIn
                : 1f - Mathf.Clamp01((t - aberrationIn) / aberrationOut);
        parryVolume.weight = Mathf.Clamp01(w) * Mathf.Clamp01(w); // fast punch, soft tail
    }

    // ══════════════════════════════════════════════════════════════════
    //  SHAKE
    // ══════════════════════════════════════════════════════════════════
    public void Shake(float amount, float duration)
    {
        // a stronger shake overrides a weaker one still playing
        float remaining = Mathf.Max(0f, shakeEndTime - Time.unscaledTime) / Mathf.Max(shakeDuration, 0.0001f);
        if (amount < shakeAmount * remaining) return;

        shakeAmount = amount;
        shakeDuration = duration;
        shakeEndTime = Time.unscaledTime + duration;
    }

    void UpdateShake()
    {
        if (shakeTarget == null) return;

        float left = shakeEndTime - Time.unscaledTime;
        if (left <= 0f)
        {
            shakeTarget.localRotation = shakeBaseRotation;
            return;
        }

        float fade = left / shakeDuration;
        float strength = shakeAmount * fade * fade;
        float t = Time.unscaledTime * shakeFrequency;
        Vector3 offset = new Vector3(
            (Mathf.PerlinNoise(shakeSeed, t) - 0.5f) * 2f,
            (Mathf.PerlinNoise(shakeSeed + 10f, t) - 0.5f) * 2f,
            (Mathf.PerlinNoise(shakeSeed + 20f, t) - 0.5f) * 2f) * strength;

        shakeTarget.localRotation = shakeBaseRotation * Quaternion.Euler(offset);
    }

    // ══════════════════════════════════════════════════════════════════
    //  RING
    // ══════════════════════════════════════════════════════════════════
    void UpdateRingPulse()
    {
        if (ringPulse == null) return;

        float left = pulseEndTime - Time.unscaledTime;
        float k = left > 0f ? left / ringPulseTime : 0f;
        ringPulse.localScale = Vector3.one * (1f + ringPulseScale * k * k);
    }
}
