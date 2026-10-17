using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A sound effect: its clip, volume and a random pitch range (so repeats never sound identical), heard
/// either the same everywhere (the player's own sounds) or from where it happens (enemies, wounds).
/// </summary>
[System.Serializable]
public class Sfx
{
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("Each play picks a pitch in this range")]
    public Vector2 pitch = new Vector2(0.95f, 1.05f);
    [Tooltip("0 = heard the same everywhere (the player's own sounds), 1 = from where it happens")]
    [Range(0f, 1f)] public float spatial = 0f;

    /// <summary>Plays it (for the player's own sounds).</summary>
    public void Play() => SfxPlayer.Play(this, Vector3.zero);

    /// <summary>Plays it from <paramref name="at"/> (3D if <see cref="spatial"/> is up).</summary>
    public void PlayAt(Vector3 at) => SfxPlayer.Play(this, at);

    public float RandomPitch() => Random.Range(Mathf.Min(pitch.x, pitch.y), Mathf.Max(pitch.x, pitch.y));
}

/// <summary>One-shot sound effects from a small pool of AudioSources, made on first use.</summary>
public static class SfxPlayer
{
    const int MaxVoices = 32;
    static readonly List<AudioSource> pool = new List<AudioSource>();
    static Transform root;
    static int next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { pool.Clear(); root = null; next = 0; }   // no domain reload

    public static AudioSource Play(Sfx sfx, Vector3 at)
    {
        if (sfx == null || sfx.clip == null || !Application.isPlaying) return null;
        AudioSource src = Voice();
        src.transform.position = at;
        src.clip = sfx.clip;
        src.volume = sfx.volume;
        src.pitch = sfx.RandomPitch();
        src.spatialBlend = sfx.spatial;
        src.loop = false;
        src.Play();
        return src;
    }

    static AudioSource Voice()
    {
        if (root == null)
        {
            var go = new GameObject("SFX");
            Object.DontDestroyOnLoad(go);
            root = go.transform;
            pool.Clear();
        }
        foreach (var a in pool) if (a != null && !a.isPlaying) return a;
        if (pool.Count >= MaxVoices)
        {
            // all busy: take over the oldest
            next = (next + 1) % pool.Count;
            if (pool[next] != null) return pool[next];
        }
        var voice = new GameObject("Voice").AddComponent<AudioSource>();
        voice.transform.SetParent(root, false);
        voice.playOnAwake = false;
        voice.dopplerLevel = 0f;
        voice.rolloffMode = AudioRolloffMode.Logarithmic;
        voice.minDistance = 2f;
        voice.maxDistance = 40f;
        pool.Add(voice);
        return voice;
    }
}
