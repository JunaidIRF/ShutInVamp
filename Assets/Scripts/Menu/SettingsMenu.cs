using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The settings panel: each control shows the saved value and writes changes straight to <see cref="GameSettings"/>.</summary>
public class SettingsMenu : MonoBehaviour
{
    public Slider volume;
    public TMP_Text volumeValue;
    public Slider music;
    public TMP_Text musicValue;
    public Slider sensitivity;
    public TMP_Text sensitivityValue;
    public OptionSelector antiAliasing;     // OFF / FXAA / SMAA
    public OptionSelector msaa;             // OFF / 2X / 4X / 8X
    public OptionSelector vsync;            // OFF / ON
    public OptionSelector fullscreen;       // OFF / ON
    public Button resetButton;

    bool hooked;

    void OnEnable()
    {
        GameSettings.Load();
        if (!hooked) Hook();
        Refresh();
    }

    void Hook()
    {
        hooked = true;
        if (volume != null) volume.onValueChanged.AddListener(v => { GameSettings.SetMasterVolume(v); ShowNumbers(); });
        if (music != null) music.onValueChanged.AddListener(v => { GameSettings.SetMusicVolume(v); ShowNumbers(); });
        if (sensitivity != null) sensitivity.onValueChanged.AddListener(v => { GameSettings.SetSensitivity(v); ShowNumbers(); });
        if (antiAliasing != null) antiAliasing.Changed += i => GameSettings.SetAntiAliasing((GameSettings.PostAA)i);
        if (msaa != null) msaa.Changed += i => GameSettings.SetMsaa(GameSettings.MsaaSteps[i]);
        if (vsync != null) vsync.Changed += i => GameSettings.SetVSync(i == 1);
        if (fullscreen != null) fullscreen.Changed += i => GameSettings.SetFullscreen(i == 1);
        if (resetButton != null) resetButton.onClick.AddListener(() => { GameSettings.ResetToDefaults(); Refresh(); });
    }

    void Refresh()
    {
        if (volume != null) volume.SetValueWithoutNotify(GameSettings.MasterVolume);
        if (music != null) music.SetValueWithoutNotify(GameSettings.MusicVolume);
        if (sensitivity != null) sensitivity.SetValueWithoutNotify(GameSettings.Sensitivity);
        if (antiAliasing != null) antiAliasing.Show((int)GameSettings.AntiAliasing);
        if (msaa != null) msaa.Show(System.Array.IndexOf(GameSettings.MsaaSteps, GameSettings.Msaa));
        if (vsync != null) vsync.Show(GameSettings.VSync ? 1 : 0);
        if (fullscreen != null) fullscreen.Show(GameSettings.Fullscreen ? 1 : 0);
        ShowNumbers();
    }

    void ShowNumbers()
    {
        if (volumeValue != null) volumeValue.text = $"{Mathf.RoundToInt(GameSettings.MasterVolume * 100f)}%";
        if (musicValue != null) musicValue.text = $"{Mathf.RoundToInt(GameSettings.MusicVolume * 100f)}%";
        if (sensitivityValue != null) sensitivityValue.text = GameSettings.Sensitivity.ToString("0.0");
    }
}
