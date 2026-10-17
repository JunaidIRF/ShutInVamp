using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A menu entry's feel: on hover the text slides right a touch, turns blood red and a red bar grows in
/// front of it; a soft swish on hover and on click. Works with any Selectable (Button, the selector arrows).
/// </summary>
public class MenuButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public TMP_Text label;
    public RectTransform bar;                 // the red accent that grows on hover (optional)
    public Color normal = new Color(0.86f, 0.86f, 0.84f, 1f);
    public Color hover = new Color(0.9f, 0.18f, 0.14f, 1f);
    public float slide = 18f;                 // px the text moves right on hover
    public float speed = 14f;
    public AudioClip hoverSound, clickSound;
    [Range(0f, 1f)] public float volume = 0.35f;

    bool over;
    float t;
    Vector2 labelRest;
    Selectable selectable;
    static AudioSource source;

    void Awake()
    {
        if (label != null) labelRest = label.rectTransform.anchoredPosition;
        selectable = GetComponent<Selectable>();
        if (selectable is Button b) b.onClick.AddListener(() => Play(clickSound, 1f));
    }

    void OnEnable() { over = false; t = 0f; Apply(); }

    public void OnPointerEnter(PointerEventData e) => Enter();
    public void OnPointerExit(PointerEventData e) => over = false;
    public void OnSelect(BaseEventData e) => Enter();
    public void OnDeselect(BaseEventData e) => over = false;

    void Enter()
    {
        if (selectable != null && !selectable.interactable) return;
        if (!over) Play(hoverSound, Random.Range(1.1f, 1.25f));
        over = true;
    }

    void Update()
    {
        t = Mathf.MoveTowards(t, over ? 1f : 0f, speed * Time.unscaledDeltaTime * 0.25f);
        Apply();
    }

    void Apply()
    {
        float e = 1f - (1f - t) * (1f - t);
        if (label != null)
        {
            label.color = Color.Lerp(normal, hover, e);
            label.rectTransform.anchoredPosition = labelRest + Vector2.right * slide * e;
        }
        if (bar != null) bar.localScale = new Vector3(e, 1f, 1f);
    }

    void Play(AudioClip clip, float pitch)
    {
        if (clip == null) return;
        if (source == null)
        {
            var go = new GameObject("Menu Sounds");
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }
        source.pitch = pitch;
        source.PlayOneShot(clip, volume);
    }
}
