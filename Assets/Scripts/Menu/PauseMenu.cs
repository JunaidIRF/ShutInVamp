using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Esc in the game: everything stops (time, the game's sound), the screen dims and RESUME / SETTINGS /
/// MAIN MENU / QUIT come up with the cursor free. The player's input scripts are switched off while
/// paused and put back exactly as they were (an execution keeps going: frozen time holds it, and its key is
/// ignored while paused). Esc again goes back (out of settings, then resumes).
/// Not while the death screen is up.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    public CanvasGroup screen;              // the whole pause screen
    public CanvasGroup mainPanel;
    public CanvasGroup settingsPanel;
    public Button resumeButton, settingsButton, menuButton, quitButton, backButton;
    public string menuScene = "MainMenu";
    public float fadeTime = 0.15f;

    public static bool Paused { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Paused = false;

    DeathScreen death;
    Behaviour[] controls;
    bool[] wereOn;
    float timeScaleBefore = 1f;
    float shown;                            // 0..1 fade

    void Awake()
    {
        if (resumeButton != null) resumeButton.onClick.AddListener(Resume);
        if (settingsButton != null) settingsButton.onClick.AddListener(() => ShowSettings(true));
        if (backButton != null) backButton.onClick.AddListener(() => ShowSettings(false));
        if (menuButton != null) menuButton.onClick.AddListener(ToMainMenu);
        if (quitButton != null) quitButton.onClick.AddListener(Quit);
    }

    void Start()
    {
        Transform player = transform.root;
        death = player.GetComponentInChildren<DeathScreen>(true);
        controls = new Behaviour[]
        {
            player.GetComponent<PlayerMovement>(), player.GetComponent<Player>(), player.GetComponent<PlayerBlock>(),
            player.GetComponentInChildren<HandSway>(true),   // not PlayerExecution: switching it off cancels a running execution (it just ignores the key while paused)
        };
        Paused = false;
        shown = 0f;
        Apply();
    }

    void OnDisable()
    {
        if (Paused) Unfreeze();   // never leave the game frozen
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (!Paused) { if (death == null || !death.Showing) Pause(); }
            else if (settingsPanel != null && settingsPanel.interactable) ShowSettings(false);
            else Resume();
        }
        shown = Mathf.MoveTowards(shown, Paused ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeTime));
        if (screen != null) screen.alpha = shown;
    }

    public void Pause()
    {
        if (Paused) return;
        Paused = true;
        timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        AudioListener.pause = true;
        wereOn = new bool[controls.Length];
        for (int i = 0; i < controls.Length; i++)
        {
            if (controls[i] == null) continue;
            wereOn[i] = controls[i].enabled;
            controls[i].enabled = false;
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (UnityEngine.EventSystems.EventSystem.current == null && FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
        ShowSettings(false);
        Apply();
    }

    public void Resume()
    {
        if (!Paused) return;
        Unfreeze();
        for (int i = 0; i < controls.Length; i++)
            if (controls[i] != null && wereOn != null) controls[i].enabled = wereOn[i];
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        Apply();
    }

    void Unfreeze()
    {
        Paused = false;
        Time.timeScale = timeScaleBefore;
        AudioListener.pause = false;
    }

    void ShowSettings(bool on)
    {
        Set(mainPanel, !on);
        Set(settingsPanel, on);
    }

    /// <summary>Raycasts and clicks only while paused.</summary>
    void Apply()
    {
        if (screen != null) { screen.blocksRaycasts = Paused; screen.interactable = Paused; }
    }

    static void Set(CanvasGroup g, bool on)
    {
        if (g == null) return;
        g.alpha = on ? 1f : 0f;
        g.interactable = on;
        g.blocksRaycasts = on;
    }

    void ToMainMenu()
    {
        Unfreeze();
        SceneManager.LoadScene(menuScene);
    }

    void Quit()
    {
#if UNITY_EDITOR
        Unfreeze();
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
