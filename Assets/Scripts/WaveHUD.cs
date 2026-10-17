using TMPro;
using UnityEngine;

/// <summary>
/// The wave HUD: the wave number and enemies left up top, and a big banner for announcements ("WAVE 3",
/// "WAVE CLEARED", "YOU DIED") that punches in, holds and fades, and the run's kills top right (a punch on each).
/// Driven by <see cref="WaveDirector"/>.
/// </summary>
public class WaveHUD : MonoBehaviour
{
    public TMP_Text waveLabel;      // "WAVE 3"
    public TMP_Text leftLabel;      // "4 LEFT" / "NEXT WAVE IN 5"
    public TMP_Text banner;         // big announcement
    public TMP_Text bannerSub;      // line under it
    public TMP_Text killsLabel;     // "23 KILLS" / "5 EXECUTED", top right
    public Color executedColor = new Color(1f, 0.42f, 0.12f, 1f);

    [Header("Banner")]
    public float punchScale = 1.5f;     // starts this big...
    public float punchTime = 0.25f;     // ...and settles in this long
    public float fadeTime = 0.6f;

    float bannerStart = float.NegativeInfinity, bannerHold;
    Vector3 bannerScale = Vector3.one, subScale = Vector3.one, killsScale = Vector3.one;
    int kills = -1, executions = -1;
    float killPunch;

    void Awake()
    {
        if (banner != null) bannerScale = banner.rectTransform.localScale;
        if (bannerSub != null) subScale = bannerSub.rectTransform.localScale;
        if (killsLabel != null) killsScale = killsLabel.rectTransform.localScale;
    }

    /// <summary>The run's kill count (executions are kills too) and how many of them were executions.</summary>
    public void SetKills(int killCount, int executionCount)
    {
        if (killCount == kills && executionCount == executions) return;
        if (killCount > kills && kills >= 0) killPunch = 1f;
        kills = killCount;
        executions = executionCount;
        if (killsLabel == null) return;
        string hex = ColorUtility.ToHtmlStringRGB(executedColor);
        killsLabel.text = $"{kills} <size=55%>KILLS</size>\n<size=55%><color=#{hex}>{executions} EXECUTED</color></size>";
    }

    void OnEnable()
    {
        // no scene reload: start clean every run
        bannerStart = float.NegativeInfinity;
        kills = executions = -1;
        killPunch = 0f;
        SetKills(0, 0);
        SetStatus("", "");
        SetBannerAlpha(0f);
    }

    /// <summary>The top line and the one under it (empty hides them).</summary>
    public void SetStatus(string wave, string left)
    {
        if (waveLabel != null) waveLabel.text = wave;
        if (leftLabel != null) leftLabel.text = left;
    }

    /// <summary>A big announcement, held for <paramref name="hold"/> seconds and then faded out.</summary>
    public void Announce(string big, string small, float hold)
    {
        if (banner != null) banner.text = big;
        if (bannerSub != null) bannerSub.text = small;
        bannerStart = Time.time;
        bannerHold = hold;
    }

    /// <summary>Changes the line under the banner without restarting it (countdowns).</summary>
    public void SetBannerSub(string small)
    {
        if (bannerSub != null) bannerSub.text = small;
    }

    void Update()
    {
        if (killsLabel != null)
        {
            killPunch = Mathf.MoveTowards(killPunch, 0f, Time.unscaledDeltaTime / 0.25f);
            killsLabel.rectTransform.localScale = killsScale * (1f + 0.35f * killPunch * killPunch);
        }

        float t = Time.time - bannerStart;
        if (t < 0f || t > bannerHold + fadeTime) { SetBannerAlpha(0f); return; }

        float punch = Mathf.Clamp01(t / Mathf.Max(0.01f, punchTime));
        float ease = 1f - (1f - punch) * (1f - punch) * (1f - punch);   // fast in, soft landing
        float s = Mathf.Lerp(punchScale, 1f, ease);
        if (banner != null) banner.rectTransform.localScale = bannerScale * s;
        if (bannerSub != null) bannerSub.rectTransform.localScale = subScale * Mathf.Lerp(1.15f, 1f, ease);

        float alpha = Mathf.Min(punch * 2f, 1f);
        if (t > bannerHold) alpha *= 1f - (t - bannerHold) / Mathf.Max(0.01f, fadeTime);
        SetBannerAlpha(alpha);
    }

    void SetBannerAlpha(float a)
    {
        if (banner != null) banner.alpha = a;
        if (bannerSub != null) bannerSub.alpha = a;
    }
}
