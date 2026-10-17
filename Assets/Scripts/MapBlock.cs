using UnityEngine;

/// <summary>
/// One block of the city: an axis-aligned rectangle on the ground (centred on this transform). The WaveDirector
/// tracks which one the player is in and spawns enemies only in its neighbours.
/// </summary>
public class MapBlock : MonoBehaviour
{
    [Tooltip("Shown on the HUD (\"BLOCK 7\")")]
    public string label = "1";
    [Tooltip("Width (x) and depth (z) in metres")]
    public Vector2 size = new Vector2(28f, 24f);
    [Tooltip("Blocks next to this one: enemies come in from these while the player is here")]
    public MapBlock[] neighbours;

    public bool Contains(Vector3 p)
    {
        Vector3 d = p - transform.position;
        return Mathf.Abs(d.x) <= size.x * 0.5f && Mathf.Abs(d.z) <= size.y * 0.5f;
    }

    /// <summary>In one of the neighbouring blocks.</summary>
    public bool NextTo(Vector3 p)
    {
        if (neighbours == null) return false;
        foreach (var n in neighbours) if (n != null && n != this && n.Contains(p)) return true;
        return false;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.8f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.2f, new Vector3(size.x, 0.1f, size.y));
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.9f);
        if (neighbours != null)
            foreach (var n in neighbours)
                if (n != null) Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, n.transform.position + Vector3.up * 0.5f);
    }
}
