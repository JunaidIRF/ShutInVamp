using System.Collections.Generic;
using UnityEngine;

public class Bullet : MonoBehaviour
{
    /// <summary>Every live bullet — PlayerBlock reads this to draw the incoming indicators.</summary>
    public static readonly List<Bullet> Active = new List<Bullet>();

    // Domain reload is off in this project, so statics survive between play sessions
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Active.Clear();

    public float lifetime = 5f;
    public float reflectSpeedMultiplier = 1.5f;

    [HideInInspector] public Vector3 velocity;
    [HideInInspector] public float damage;
    [HideInInspector] public bool reflected;
    [HideInInspector] public Shooter owner;

    public float Speed => velocity.magnitude;

    void OnEnable() => Active.Add(this);
    void OnDisable() => Active.Remove(this);

    void Start()
    {
        Destroy(gameObject, lifetime);
    }

    public void Launch(Vector3 direction, float speed, float dmg, Shooter shooter)
    {
        velocity = direction.normalized * speed;
        damage = dmg;
        owner = shooter;
        FaceVelocity();
    }

    /// <summary>The projectile model (a stake) points along +Z, so keep it aimed where it's flying.</summary>
    void FaceVelocity()
    {
        if (velocity.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(velocity);
    }

    void Update()
    {
        Vector3 from = transform.position;
        Vector3 to = from + velocity * Time.deltaTime;

        // Linecast so fast bullets can't tunnel through the player's capsule
        if (Physics.Linecast(from, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!reflected)
            {
                PlayerBlock block = hit.collider.GetComponentInParent<PlayerBlock>();
                if (block != null)
                {
                    transform.position = hit.point;
                    block.ResolveHit(this);
                    return;
                }
            }
            else
            {
                Shooter shooter = hit.collider.GetComponentInParent<Shooter>();
                if (shooter != null)
                {
                    shooter.Stagger(hit.point, velocity.normalized, damage);
                    Destroy(gameObject);
                    return;
                }

                // Reflected bullets fly out of the player's capsule — don't let it eat them
                if (hit.collider.GetComponentInParent<PlayerBlock>() != null)
                {
                    transform.position = to;
                    return;
                }
            }

            Destroy(gameObject);
            return;
        }

        transform.position = to;
    }

    /// <summary>Send the bullet back at whoever fired it.</summary>
    public void Reflect()
    {
        reflected = true;

        Vector3 target = owner != null ? owner.AimPoint : transform.position - velocity;
        velocity = (target - transform.position).normalized * Speed * reflectSpeedMultiplier;
        FaceVelocity();
    }
}
