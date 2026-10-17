using UnityEngine;

/// <summary>
/// One stop on a run through the city: a patch of shade where a wave comes for you. The WaveDirector runs them
/// in order; you reach the next one across the sunlit street with the ward your kills gave you. Its position is
/// where you respawn after dying here, facing along its forward.
/// </summary>
public class ShadeZone : MonoBehaviour
{
    [Tooltip("You're in the zone within this many metres of its centre (on the ground)")]
    public float radius = 10f;

    public bool Contains(Vector3 p)
    {
        Vector3 d = p - transform.position;
        d.y = 0f;
        return d.sqrMagnitude <= radius * radius;
    }

    public float FlatDistance(Vector3 p)
    {
        Vector3 d = p - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.5f, 1f, 0.9f);
        Vector3 c = transform.position + Vector3.up * 0.15f;
        Vector3 last = c + Vector3.right * radius;
        for (int i = 1; i <= 48; i++)
        {
            float a = i * Mathf.PI * 2f / 48f;
            Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            Gizmos.DrawLine(last, p);
            last = p;
        }
        Gizmos.DrawLine(c, c + transform.forward * 2f);
    }
}
