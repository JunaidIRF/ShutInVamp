using UnityEngine;

/// <summary>
/// Toggles a rigged model between animated and ragdoll. Put it on the model root (next to its Animator).
/// While animated, the bone rigidbodies are kinematic and their colliders are off, so they don't catch
/// projectiles or bump the character's gameplay collider.
/// </summary>
public class Ragdoll : MonoBehaviour
{
    Rigidbody[] bodies;
    Collider[] colliders;
    Animator anim;

    public bool IsActive { get; private set; }

    void Awake()
    {
        bodies = GetComponentsInChildren<Rigidbody>(true);
        colliders = GetComponentsInChildren<Collider>(true);
        anim = GetComponent<Animator>();
        SetRagdoll(false);
    }

    public void SetRagdoll(bool on)
    {
        IsActive = on;
        if (anim != null) anim.enabled = !on;
        foreach (var c in colliders) c.enabled = on;
        foreach (var b in bodies)
        {
            b.isKinematic = !on;
            b.interpolation = on ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        }
    }

    /// <summary>Go limp and knock the body part nearest the hit.</summary>
    public void Activate(Vector3 point, Vector3 impulse)
    {
        SetRagdoll(true);

        Rigidbody nearest = null;
        float best = float.MaxValue;
        foreach (var b in bodies)
        {
            float d = (b.worldCenterOfMass - point).sqrMagnitude;
            if (d < best) { best = d; nearest = b; }
        }
        if (nearest != null) nearest.AddForceAtPosition(impulse, point, ForceMode.Impulse);
    }
}
