using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The threat ring: a wider ring round the crosshair, apart from the guard ring, for attacks the guard can't
/// cover (from behind or far to the side, see <see cref="PlayerBlock.guardFacing"/>). Each threat is a thin
/// glowing arc with a chevron pointing out towards it, seen from above: top = in front, right = our right,
/// bottom = behind. Stakes red, kicks orange; they fade in and pulse faster as they get close. A hit from out
/// there flashes once where it came from.
/// </summary>
public class ThreatIndicator : MonoBehaviour
{
    [Header("References")]
    public PlayerBlock block;
    public Image arcTemplate;       // thin ring, radial-filled from the top, clockwise (inactive template)
    public Image chevronTemplate;   // chevron pointing up, under a pivot at the ring's centre (inactive template)

    [Header("Shape")]
    public float radius = 0.21f;            // m on the HUD (the guard ring is ~0.12)
    public float arcWidth = 34f;            // degrees
    public float chevronOut = 0.028f;       // chevron this far outside the arc
    public float stakeMissDistance = 1.2f;  // stakes passing further from us than this aren't threats

    [Header("Look")]
    public Color stakeColor = new Color(1f, 0.12f, 0.08f, 1f);
    public Color kickColor = new Color(1f, 0.55f, 0.05f, 1f);
    public Color hitColor = new Color(1f, 0.25f, 0.2f, 1f);
    public float warnTime = 1.5f;           // a stake shows from this long before it lands
    public float pulseSlow = 3f, pulseFast = 14f;   // pulses/s far away / about to land
    public float hitTime = 0.8f;            // a hit's flash fades over this

    struct Threat { public float angle, urgency; public Color color; public bool hit; }

    readonly List<Threat> threats = new List<Threat>();
    readonly List<(float angle, float time)> hits = new List<(float, float)>();
    readonly List<Image> arcs = new List<Image>();
    readonly List<Image> chevrons = new List<Image>();

    void OnEnable()
    {
        hits.Clear();   // no scene reload: nothing carried over
        if (arcTemplate != null) arcTemplate.gameObject.SetActive(false);
        if (chevronTemplate != null) chevronTemplate.transform.parent.gameObject.SetActive(false);
    }

    /// <summary>We were just hit from <paramref name="toward"/> (a direction from us): flash it.</summary>
    public void Flash(Vector3 toward)
    {
        hits.Add((AngleOf(toward), Time.time));
    }

    void LateUpdate()
    {
        threats.Clear();
        if (block != null)
        {
            Vector3 me = block.Center;
            foreach (Bullet b in Bullet.Active)
            {
                if (b == null || b.reflected || b.Speed < 0.01f) continue;
                Vector3 dir = b.velocity / b.Speed;
                float along = Vector3.Dot(me - b.transform.position, dir);
                if (along <= 0f) continue;   // flew past
                Vector3 pass = b.transform.position + dir * along;
                if ((pass - me).magnitude > stakeMissDistance) continue;   // not coming at us
                if (block.CanGuard(-dir)) continue;   // the guard ring shows it
                float k = 1f - Mathf.Clamp01(along / b.Speed / Mathf.Max(0.01f, warnTime));
                if (k <= 0f) continue;
                threats.Add(new Threat { angle = AngleOf(-dir), urgency = k, color = stakeColor });
            }
            foreach (Shooter s in Shooter.Kicking)
            {
                if (s == null) continue;
                Vector3 to = s.transform.position - me;
                if (block.CanGuard(to)) continue;
                float k = Mathf.Clamp01(0.5f + 0.5f * s.KickElapsed / Mathf.Max(0.01f, s.KickElapsed + s.KickTimeToImpact));
                threats.Add(new Threat { angle = AngleOf(to), urgency = k, color = kickColor });
            }
        }
        for (int i = hits.Count - 1; i >= 0; i--)
        {
            float age = Time.time - hits[i].time;
            if (age > hitTime) { hits.RemoveAt(i); continue; }
            threats.Add(new Threat { angle = hits[i].angle, urgency = 1f - age / hitTime, color = hitColor, hit = true });
        }

        for (int i = 0; i < threats.Count; i++) Draw(i, threats[i]);
        for (int i = threats.Count; i < arcs.Count; i++) { arcs[i].gameObject.SetActive(false); chevrons[i].transform.parent.gameObject.SetActive(false); }
    }

    void Draw(int i, Threat t)
    {
        if (arcTemplate == null || chevronTemplate == null) return;
        while (arcs.Count <= i)
        {
            arcs.Add(Instantiate(arcTemplate, arcTemplate.transform.parent));
            var copy = Instantiate(chevronTemplate.transform.parent.gameObject, chevronTemplate.transform.parent.parent);
            chevrons.Add(copy.GetComponentInChildren<Image>(true));
        }
        Image arc = arcs[i], chevron = chevrons[i];
        arc.gameObject.SetActive(true);
        chevron.transform.parent.gameObject.SetActive(true);

        // a hit: one bright flash that fades and swells; a threat: fades in, pulses faster as it closes in
        float k = t.urgency;
        float alpha, swell;
        if (t.hit) { alpha = k; swell = 1f + 0.25f * (1f - k); }
        else
        {
            float rate = Mathf.Lerp(pulseSlow, pulseFast, k * k);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * rate * Mathf.PI * 2f);
            alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 2f)) * Mathf.Lerp(0.55f, 1f, pulse);
            swell = 1f + 0.08f * pulse * k;
        }
        float width = t.hit ? arcWidth * 1.4f : Mathf.Lerp(arcWidth * 0.8f, arcWidth * 1.15f, k);

        arc.fillAmount = width / 360f;
        arc.rectTransform.localEulerAngles = new Vector3(0f, 0f, -(t.angle - width * 0.5f));
        arc.rectTransform.localScale = Vector3.one * swell;
        Color c = t.color; c.a *= alpha;
        arc.color = c;

        var pivot = chevron.transform.parent;
        pivot.localEulerAngles = new Vector3(0f, 0f, -t.angle);
        chevron.rectTransform.anchoredPosition = new Vector2(0f, radius + chevronOut + 0.01f * k);
        chevron.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1.15f, k) * swell;
        chevron.color = c;
    }

    /// <summary>Clockwise from straight ahead, seen from above (0 = in front, 90 = right, 180 = behind).</summary>
    float AngleOf(Vector3 toward)
    {
        Transform me = block != null ? block.transform : transform;
        Vector3 f = me.forward; f.y = 0f;
        toward.y = 0f;
        if (f.sqrMagnitude < 0.0001f || toward.sqrMagnitude < 0.0001f) return 0f;
        return Vector3.SignedAngle(f, toward, Vector3.up);
    }
}
