using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The main menu: Start fades to black and loads the game, Settings swaps the main panel for the settings one
/// (Back or Esc returns), Quit quits. The menu comes up with a fade from black and the cursor free.
/// </summary>
public class MainMenu : MonoBehaviour
{
    public string gameScene = "RuinComp";
    public CanvasGroup mainPanel;
    public CanvasGroup settingsPanel;
    public Button startButton, settingsButton, quitButton, backButton;
    public Image fader;                     // full-screen black on an overlay canvas
    public float fadeIn = 1.5f;
    public float fadeOut = 0.8f;
    public float panelFade = 0.2f;

    bool starting;
    Coroutine swapping;

    void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 1f;
        starting = false;
        GameSettings.Apply();
        if (startButton != null) startButton.onClick.AddListener(StartGame);
        if (settingsButton != null) settingsButton.onClick.AddListener(() => Show(settingsPanel));
        if (backButton != null) backButton.onClick.AddListener(() => Show(mainPanel));
        if (quitButton != null) quitButton.onClick.AddListener(Quit);
        SetPanel(mainPanel, 1f, true);
        SetPanel(settingsPanel, 0f, false);
        StartCoroutine(Fade(1f, 0f, fadeIn));
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && settingsPanel != null && settingsPanel.interactable) Show(mainPanel);
    }

    void Show(CanvasGroup panel)
    {
        if (starting) return;
        if (swapping != null) StopCoroutine(swapping);
        swapping = StartCoroutine(Swap(panel));
    }

    IEnumerator Swap(CanvasGroup to)
    {
        CanvasGroup from = to == mainPanel ? settingsPanel : mainPanel;
        SetPanel(from, from.alpha, false);
        for (float t = 0f; t < panelFade; t += Time.unscaledDeltaTime) { SetPanel(from, 1f - t / panelFade, false); yield return null; }
        SetPanel(from, 0f, false);
        for (float t = 0f; t < panelFade; t += Time.unscaledDeltaTime) { SetPanel(to, t / panelFade, false); yield return null; }
        SetPanel(to, 1f, true);
    }

    static void SetPanel(CanvasGroup g, float alpha, bool active)
    {
        if (g == null) return;
        g.alpha = alpha;
        g.interactable = active;
        g.blocksRaycasts = active;
    }

    void StartGame()
    {
        if (starting) return;
        starting = true;
        SetPanel(mainPanel, mainPanel.alpha, false);
        StartCoroutine(LoadGame());
    }

    IEnumerator LoadGame()
    {
        yield return Fade(fader != null ? fader.color.a : 0f, 1f, fadeOut);
        SceneManager.LoadScene(gameScene);
    }

    IEnumerator Fade(float from, float to, float time)
    {
        if (fader == null) yield break;
        fader.raycastTarget = true;
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            float k = t / time;
            SetFader(Mathf.Lerp(from, to, k * k * (3f - 2f * k)));
            yield return null;
        }
        SetFader(to);
        fader.raycastTarget = to > 0.01f;
    }

    void SetFader(float a) { var c = fader.color; c.a = a; fader.color = c; }

    void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
