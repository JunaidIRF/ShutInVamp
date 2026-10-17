using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A "&lt; VALUE &gt;" row: the arrows step through the options (wrapping), the value text shows the current one.</summary>
public class OptionSelector : MonoBehaviour
{
    public TMP_Text value;
    public Button previous, next;
    public string[] options = { "OFF", "ON" };

    public int Index { get; private set; }
    public event System.Action<int> Changed;

    void Awake()
    {
        if (previous != null) previous.onClick.AddListener(() => Step(-1));
        if (next != null) next.onClick.AddListener(() => Step(1));
    }

    /// <summary>Show an option without raising <see cref="Changed"/>.</summary>
    public void Show(int index)
    {
        Index = options.Length > 0 ? Mathf.Clamp(index, 0, options.Length - 1) : 0;
        if (value != null && options.Length > 0) value.text = options[Index];
    }

    void Step(int by)
    {
        if (options.Length == 0) return;
        Show((Index + by + options.Length) % options.Length);
        Changed?.Invoke(Index);
    }
}
