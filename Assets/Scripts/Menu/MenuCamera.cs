using UnityEngine;

/// <summary>
/// The menu camera: turns a few degrees after the mouse (smoothly, so the scene feels alive and deep)
/// and breathes a little on its own.
/// </summary>
public class MenuCamera : MonoBehaviour
{
    public Vector2 maxAngle = new Vector2(4f, 2.5f);    // yaw, pitch at the screen's edges
    public float follow = 3f;                            // how quickly it catches up with the mouse
    public float swayAngle = 0.4f;                       // idle drift
    public float swaySpeed = 0.25f;

    Quaternion rest;
    Vector2 current;

    void Awake() => rest = transform.localRotation;

    void OnEnable() => current = Vector2.zero;

    void Update()
    {
        Vector2 m = Vector2.zero;
        if (Screen.width > 0 && Screen.height > 0)
        {
            Vector3 p = Input.mousePosition;
            m = new Vector2(Mathf.Clamp(p.x / Screen.width, 0f, 1f) * 2f - 1f, Mathf.Clamp(p.y / Screen.height, 0f, 1f) * 2f - 1f);
        }
        Vector2 target = new Vector2(m.x * maxAngle.x, -m.y * maxAngle.y);
        current = Vector2.Lerp(current, target, 1f - Mathf.Exp(-follow * Time.unscaledDeltaTime));

        float t = Time.unscaledTime * swaySpeed;
        float swayYaw = (Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 2f * swayAngle;
        float swayPitch = (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f * swayAngle * 0.6f;
        transform.localRotation = rest * Quaternion.Euler(current.y + swayPitch, current.x + swayYaw, 0f);
    }
}
