using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Health bar feel: the fill eases quickly to the new value, and a white "damage trail" behind it holds
/// the old value for a moment, then drains down to catch up — so you can see how much a hit took.
/// Healing raises the trail straight away and lets the fill ease up into it. Works for any Filled
/// image (vertical or horizontal); the trail uses the same fill settings. Unscaled time, so slow
/// motion doesn't stall it.
/// </summary>
public class HealthBarFx : MonoBehaviour
{
    public Image fill;                      // the coloured bar
    public Image trail;                     // white bar behind it, same rect and fill settings

    public float fillSharpness = 14f;       // how quickly the fill eases to the target (higher = snappier)
    public float trailDelay = 0.35f;        // how long the white holds after a hit
    public float trailSpeed = 0.9f;         // drain speed afterwards (fraction of the bar per second)

    float target = 1f;
    float shown = 1f;
    float trailValue = 1f;
    float trailHoldUntil;

    void Awake()
    {
        float v = fill != null ? fill.fillAmount : 1f;
        target = shown = trailValue = v;
        Apply();
    }

    /// <summary>New health, 0..1.</summary>
    public void SetValue(float value)
    {
        value = Mathf.Clamp01(value);
        if (value < target)
        {
            // damage: trail stays where the bar was, then catches up after a short hold
            trailValue = Mathf.Max(trailValue, shown);
            trailHoldUntil = Time.unscaledTime + trailDelay;
        }
        else
        {
            trailValue = value;             // heal: white jumps up, fill eases into it
        }
        target = value;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        shown = Mathf.Lerp(shown, target, 1f - Mathf.Exp(-fillSharpness * dt));
        if (Mathf.Abs(shown - target) < 0.001f) shown = target;

        if (Time.unscaledTime >= trailHoldUntil)
            trailValue = Mathf.MoveTowards(trailValue, shown, trailSpeed * dt);
        trailValue = Mathf.Max(trailValue, shown);   // never behind the fill

        Apply();
    }

    void Apply()
    {
        if (fill != null) fill.fillAmount = shown;
        if (trail != null) trail.fillAmount = trailValue;
    }
}
