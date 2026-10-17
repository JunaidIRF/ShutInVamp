using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// The player's settings: kept in PlayerPrefs and applied to every scene as it loads (volume, music volume (read by
/// <see cref="MusicPlayer"/>), mouse
/// sensitivity on the player, anti-aliasing on the cameras, MSAA on the render pipeline, vsync, fullscreen).
/// Change a value through its setter and it's applied and saved at once.
/// </summary>
public static class GameSettings
{
    public enum PostAA { Off, FXAA, SMAA }

    public const float DefaultSensitivity = 2f;
    public const float DefaultMusicVolume = 0.25f;   // low: under the game's sounds
    public static readonly int[] MsaaSteps = { 1, 2, 4, 8 };

    public static float MasterVolume { get; private set; } = 1f;     // 0..1
    public static float MusicVolume { get; private set; } = DefaultMusicVolume;   // 0..1, under the master
    public static float Sensitivity { get; private set; } = DefaultSensitivity;
    public static PostAA AntiAliasing { get; private set; } = PostAA.FXAA;
    public static int Msaa { get; private set; } = 1;                  // 1 = off, 2, 4, 8
    public static bool VSync { get; private set; } = true;
    public static bool Fullscreen { get; private set; } = true;

    static bool loaded;
#if UNITY_EDITOR
    static int editorMsaa = -1;                                          // the pipeline asset's own value, put back after play
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        // no domain reload: hook once per play
        loaded = false;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged -= RestoreEditorAsset;
        UnityEditor.EditorApplication.playModeStateChanged += RestoreEditorAsset;
#endif
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { Load(); Apply(); }

    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        MasterVolume = PlayerPrefs.GetFloat("settings.volume", 1f);
        MusicVolume = PlayerPrefs.GetFloat("settings.music", DefaultMusicVolume);
        Sensitivity = PlayerPrefs.GetFloat("settings.sensitivity", DefaultSensitivity);
        AntiAliasing = (PostAA)Mathf.Clamp(PlayerPrefs.GetInt("settings.aa", (int)PostAA.FXAA), 0, 2);
        Msaa = PlayerPrefs.GetInt("settings.msaa", 1);
        if (System.Array.IndexOf(MsaaSteps, Msaa) < 0) Msaa = 1;
        VSync = PlayerPrefs.GetInt("settings.vsync", 1) == 1;
        Fullscreen = PlayerPrefs.GetInt("settings.fullscreen", 1) == 1;
    }

    public static void SetMasterVolume(float v) { MasterVolume = Mathf.Clamp01(v); Save(); Apply(); }
    public static void SetMusicVolume(float v) { MusicVolume = Mathf.Clamp01(v); Save(); }   // MusicPlayer reads it every frame
    public static void SetSensitivity(float v) { Sensitivity = Mathf.Clamp(v, 0.1f, 10f); Save(); Apply(); }
    public static void SetAntiAliasing(PostAA v) { AntiAliasing = v; Save(); Apply(); }
    public static void SetMsaa(int v) { Msaa = System.Array.IndexOf(MsaaSteps, v) >= 0 ? v : 1; Save(); Apply(); }
    public static void SetVSync(bool v) { VSync = v; Save(); Apply(); }
    public static void SetFullscreen(bool v) { Fullscreen = v; Save(); Apply(); }

    public static void ResetToDefaults()
    {
        MasterVolume = 1f; MusicVolume = DefaultMusicVolume; Sensitivity = DefaultSensitivity; AntiAliasing = PostAA.FXAA; Msaa = 1; VSync = true; Fullscreen = true;
        Save(); Apply();
    }

    static void Save()
    {
        PlayerPrefs.SetFloat("settings.volume", MasterVolume);
        PlayerPrefs.SetFloat("settings.music", MusicVolume);
        PlayerPrefs.SetFloat("settings.sensitivity", Sensitivity);
        PlayerPrefs.SetInt("settings.aa", (int)AntiAliasing);
        PlayerPrefs.SetInt("settings.msaa", Msaa);
        PlayerPrefs.SetInt("settings.vsync", VSync ? 1 : 0);
        PlayerPrefs.SetInt("settings.fullscreen", Fullscreen ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// <summary>Everything onto what's loaded now.</summary>
    public static void Apply()
    {
        Load();
        AudioListener.volume = MasterVolume;
        QualitySettings.vSyncCount = VSync ? 1 : 0;
        if (!Application.isEditor && Screen.fullScreen != Fullscreen) Screen.fullScreen = Fullscreen;

        foreach (var m in Object.FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include)) m.mouseSensitivity = Sensitivity;

        var mode = AntiAliasing == PostAA.FXAA ? AntialiasingMode.FastApproximateAntialiasing
                 : AntiAliasing == PostAA.SMAA ? AntialiasingMode.SubpixelMorphologicalAntiAliasing
                 : AntialiasingMode.None;
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
        {
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.renderType != CameraRenderType.Base) continue;
            data.antialiasing = mode;
            data.antialiasingQuality = AntialiasingQuality.High;
            cam.allowMSAA = Msaa > 1;
        }

        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
        {
#if UNITY_EDITOR
            if (editorMsaa < 0) editorMsaa = urp.msaaSampleCount;
#endif
            urp.msaaSampleCount = Msaa;
        }
    }

#if UNITY_EDITOR
    /// <summary>MSAA lives on the pipeline asset: in the editor it goes back to what the project had after play.</summary>
    static void RestoreEditorAsset(UnityEditor.PlayModeStateChange change)
    {
        if (change != UnityEditor.PlayModeStateChange.ExitingPlayMode || editorMsaa < 0) return;
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) urp.msaaSampleCount = editorMsaa;
        editorMsaa = -1;
    }
#endif
}
