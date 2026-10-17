using System.Collections.Generic;
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
        knockPending = false;
        if (anim != null) anim.enabled = !on;
        foreach (var c in colliders) c.enabled = on;
        foreach (var b in bodies)
        {
            b.isKinematic = !on;
            b.interpolation = on ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        }
    }

    /// <summary>
    /// Lets go of <paramref name="bone"/> and everything under it (it was cut off): their joints, bodies and
    /// colliders are removed, so the bones just follow their parent.
    /// </summary>
    public void Detach(Transform bone)
    {
        var gone = new HashSet<Rigidbody>(bone.GetComponentsInChildren<Rigidbody>(true));
        foreach (var j in GetComponentsInChildren<Joint>(true))
            if (gone.Contains(j.GetComponent<Rigidbody>()) || (j.connectedBody != null && gone.Contains(j.connectedBody)))
                DestroyImmediate(j);   // joints before the bodies they need
        foreach (var c in bone.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (var b in gone) DestroyImmediate(b);
        bodies = System.Array.FindAll(bodies, b => b != null);
        colliders = System.Array.FindAll(colliders, c => c != null);
    }

    /// <summary>
    /// Go limp and get knocked along <paramref name="direction"/>. Every part gets
    /// <paramref name="bodySpeed"/> (+ <paramref name="lift"/> upwards) so the whole body is thrown,
    /// and the part nearest the hit gets <paramref name="hitSpeed"/> on top. Speeds are velocity
    /// changes (m/s), so they don't depend on the parts' masses.
    /// </summary>
    public void Activate(Vector3 point, Vector3 direction, float bodySpeed, float hitSpeed, float lift)
    {
        SetRagdoll(true);

        // Forces added in the same frame the bodies stop being kinematic get dropped by physics,
        // so queue the knock-back for the next physics step.
        knockDir = direction.normalized;
        knockPoint = point;
        knockBody = bodySpeed;
        knockHit = hitSpeed;
        knockLift = lift;
        knockPending = true;
    }

    bool knockPending;
    Vector3 knockDir, knockPoint;
    float knockBody, knockHit, knockLift;

    void FixedUpdate()
    {
        if (!knockPending || !IsActive) return;
        knockPending = false;

        Rigidbody nearest = null;
        float best = float.MaxValue;
        foreach (var b in bodies)
        {
            b.AddForce(knockDir * knockBody + Vector3.up * knockLift, ForceMode.VelocityChange);
            float d = (b.worldCenterOfMass - knockPoint).sqrMagnitude;
            if (d < best) { best = d; nearest = b; }
        }
        if (nearest != null) nearest.AddForce(knockDir * knockHit, ForceMode.VelocityChange);
    }
}
