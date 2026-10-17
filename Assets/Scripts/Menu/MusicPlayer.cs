using UnityEngine;

/// <summary>
/// The music: one track on loop, from the menu into the game and back without restarting (it lives across
/// scene loads), fading in when it starts. Made by itself when the game starts; its volume is the Music
/// setting (Master Volume on top, through the listener). It keeps playing in the pause menu.
/// </summary>
public class MusicPlayer : MonoBehaviour
{
    const string TrackPath = "Music/CountDracula";   // Assets/Resources/Music/CountDracula.mp3
    const float FadeIn = 2.5f;

    static MusicPlayer instance;
    AudioSource source;
    float fade;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;   // no domain reload: last play's player is gone

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        if (instance != null) return;
        GameSettings.Load();   // the music volume, before anything else asks
        var clip = Resources.Load<AudioClip>(TrackPath);
        if (clip == null) { Debug.LogWarning($"MusicPlayer: no music at Resources/{TrackPath}"); return; }
        var go = new GameObject("Music");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<MusicPlayer>();
        var s = instance.source = go.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = true;
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.priority = 0;                 // never cut for effects
        s.ignoreListenerPause = true;   // plays on in the pause menu
        s.volume = 0f;
        clip.LoadAudioData();   // streamed: starts once it's ready (Update)
    }

    void Update()
    {
        // start when the stream is ready, and again if anything ever stops it
        if (source != null && !source.isPlaying && source.clip != null && source.clip.loadState == AudioDataLoadState.Loaded) source.Play();
        fade = Mathf.MoveTowards(fade, 1f, Time.unscaledDeltaTime / FadeIn);
        if (source != null) source.volume = GameSettings.MusicVolume * fade * fade;
    }
}
