using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// "YOU DIED": the screen fades to black, the title and the run's numbers come up, then a Retry button
/// (R or Enter work too). The cursor is free while it's up. The WaveDirector shows it and hears <see cref="Retry"/>.
/// </summary>
public class DeathScreen : MonoBehaviour
{
    public CanvasGroup group;           // the whole screen (fades in)
    public Image black;                 // full-screen backdrop
    public TMP_Text title;
    public TMP_Text stats;
    public Button retryButton;

    public float fadeTime = 1.2f;       // to black
    [Range(0f, 1f)] public float blackAlpha = 0.92f;
    public float buttonDelay = 0.9f;    // the button comes up this long after dying

    public event System.Action Retry;
    public bool Showing { get; private set; }

    float shownAt;

    void Awake()
    {
        if (retryButton != null)
        {
            retryButton.onClick.RemoveListener(OnRetry);
            retryButton.onClick.AddListener(OnRetry);
        }
    }

    void OnEnable()
    {
        // no scene reload: hidden at the start of every run
        Showing = false;
        SetAlpha(0f, 0f);
    }

    public void Show(string statsText)
    {
        Showing = true;
        shownAt = Time.unscaledTime;
        if (stats != null) stats.text = statsText;
        if (retryButton != null) retryButton.interactable = false;
        EnsureEventSystem();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Hide()
    {
        Showing = false;
        SetAlpha(0f, 0f);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (!Showing) return;
        float t = Time.unscaledTime - shownAt;
        float fade = Mathf.Clamp01(t / Mathf.Max(0.01f, fadeTime));
        float button = Mathf.Clamp01((t - buttonDelay) / 0.35f);
        SetAlpha(fade * fade * (3f - 2f * fade), button);
        if (retryButton != null) retryButton.interactable = button >= 1f;
        if (button >= 1f && (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Return))) OnRetry();
    }

    void OnRetry()
    {
        if (!Showing || Time.unscaledTime - shownAt < buttonDelay) return;   // not before the button is up
        Retry?.Invoke();
    }

    void SetAlpha(float screen, float button)
    {
        if (group != null)
        {
            group.alpha = screen > 0f ? 1f : 0f;
            group.blocksRaycasts = screen > 0f;
            group.interactable = screen > 0f;
        }
        if (black != null) { var c = black.color; c.a = blackAlpha * screen; black.color = c; }
        if (title != null) title.alpha = screen;
        if (stats != null) stats.alpha = Mathf.Clamp01(screen * 1.5f - 0.5f);
        if (retryButton != null)
        {
            var cg = retryButton.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = button;
        }
    }

    /// <summary>The button needs an EventSystem; the scenes have none, so one is made the first time.</summary>
    static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }
}
