using UnityEngine;

/// <summary>
/// Keeps this object (e.g. a world-space canvas) facing the camera. Matches the camera's rotation
/// rather than looking at its position, so flat UI stays square-on to the screen with no skew.
/// </summary>
public class Billboard : MonoBehaviour
{
    public Camera cam;   // falls back to the main camera

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        transform.rotation = cam.transform.rotation;
    }
}
