using UnityEngine;

public class Shooter : MonoBehaviour
{
    [Header("References")]
    public Transform target;            // falls back to the Player in the scene
    public Transform muzzle;
    public Bullet bulletPrefab;

    [Header("Shooting")]
    public float minFireInterval = 1.2f;
    public float maxFireInterval = 2.4f;
    public float bulletSpeed = 15f;
    public float damage = 20f;

    [Header("Spread")]
    // Each shot aims at a random point around the target's body outline (head, feet, sides...),
    // as a fraction of its half-size: 0 = dead centre, 1 = the very edge
    [Range(0f, 1f)] public float minSpread = 0.5f;
    [Range(0f, 1f)] public float maxSpread = 0.8f;

    [Header("Stagger")]
    public float staggerTime = 2f;
    public HitFlash hitFlash;           // red overlay on hit, works on any model's materials
    public GameObject bloodFx;          // spawned at the impact point, spraying back towards the shot

    [Header("Animation")]
    public Animator anim;               // on the model: Shoot / Hit triggers, Stunned bool

    [Header("Hit bounce")]
    // Visual only — the collider stays on the root, unscaled. For a rigged model make this a plain
    // wrapper object above the model (and its Animator), so nothing else drives its scale/position.
    public Transform graphics;
    public float bounceAmount = 0.35f;  // how hard it squashes on impact
    public float bounceFrequency = 18f; // wobble speed (rad/s)
    public float bounceDecay = 7f;      // how fast the wobble settles

    Collider targetCollider;
    Collider ownCollider;
    float nextFireTime;
    float staggerEndTime;
    bool isStaggered;

    Vector3 graphicsBaseScale = Vector3.one;
    Vector3 graphicsBasePosition;
    float feetBelowPivot;               // so squashing keeps the feet on the ground, whatever the pivot
    float bounceStartTime = float.NegativeInfinity;
    bool bouncing;

    static readonly int ShootHash = Animator.StringToHash("Shoot");
    static readonly int HitHash = Animator.StringToHash("Hit");
    static readonly int StunnedHash = Animator.StringToHash("Stunned");

    /// <summary>Where reflected bullets aim — the middle of our capsule.</summary>
    public Vector3 AimPoint => ownCollider != null ? ownCollider.bounds.center : transform.position;

    void Start()
    {
        if (target == null)
        {
            Player player = FindAnyObjectByType<Player>();
            if (player != null) target = player.transform;
        }

        if (target != null)
            targetCollider = target.GetComponent<Collider>();

        ownCollider = GetComponent<Collider>();
        if (hitFlash == null) hitFlash = GetComponent<HitFlash>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (anim != null) anim.SetBool(StunnedHash, false);

        if (graphics != null)
        {
            graphicsBaseScale = graphics.localScale;
            graphicsBasePosition = graphics.localPosition;
            feetBelowPivot = FeetBelowPivot();
        }

        // Scene reload is off in this project — don't carry a stagger/bounce over from a previous run
        isStaggered = false;
        bounceStartTime = float.NegativeInfinity;
        bouncing = false;
        ScheduleNextShot();
    }

    void Update()
    {
        UpdateBounce();

        if (target == null) return;

        // Face the target (yaw only)
        Vector3 flat = target.position - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(flat);

        if (isStaggered)
        {
            if (Time.time < staggerEndTime) return;

            isStaggered = false;
            if (anim != null) anim.SetBool(StunnedHash, false);
            ScheduleNextShot();
        }

        if (Time.time >= nextFireTime)
        {
            Fire();
            ScheduleNextShot();
        }
    }

    void Fire()
    {
        if (bulletPrefab == null || muzzle == null) return;

        Vector3 aim = targetCollider != null ? AimAroundBody(targetCollider.bounds) : target.position;
        Bullet bullet = Instantiate(bulletPrefab, muzzle.position, Quaternion.identity);
        bullet.Launch(aim - muzzle.position, bulletSpeed, damage, this);
        if (anim != null) anim.SetTrigger(ShootHash);
    }

    /// <summary>Random point on the target's silhouette as seen from the muzzle, in any direction from its centre.</summary>
    Vector3 AimAroundBody(Bounds body)
    {
        Vector3 toTarget = body.center - muzzle.position;
        toTarget.y = 0f;
        Vector3 side = Vector3.Cross(Vector3.up, toTarget).normalized;

        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float amount = Random.Range(minSpread, maxSpread);
        Vector3 offset = side * (Mathf.Sin(angle) * body.extents.x) + Vector3.up * (Mathf.Cos(angle) * body.extents.y);

        return body.center + offset * amount;
    }

    void ScheduleNextShot()
    {
        nextFireTime = Time.time + Random.Range(minFireInterval, maxFireInterval);
    }

    /// <summary>Called when a parried bullet hits us — stop firing for a moment.</summary>
    /// <param name="point">Where the bullet hit our collider.</param>
    /// <param name="direction">The bullet's travel direction.</param>
    public void Stagger(Vector3 point, Vector3 direction)
    {
        // The blood effect sprays along its +Z, so point it back where the shot came from
        if (bloodFx != null && direction.sqrMagnitude > 0.0001f)
            Instantiate(bloodFx, point, Quaternion.LookRotation(-direction));

        isStaggered = true;
        staggerEndTime = Time.time + staggerTime;
        bounceStartTime = Time.time;
        bouncing = true;
        if (hitFlash != null) hitFlash.Flash();
        if (anim != null)
        {
            anim.ResetTrigger(ShootHash);
            anim.SetTrigger(HitHash);
            anim.SetBool(StunnedHash, true);
        }
    }

    /// <summary>Squash-and-stretch on the visual only: squash on impact, overshoot, settle. Feet stay planted.</summary>
    void UpdateBounce()
    {
        if (graphics == null || !bouncing) return;   // only touches the transform while bouncing

        float t = Time.time - bounceStartTime;
        float s = 0f;
        if (t < 5f / bounceDecay)
            s = bounceAmount * Mathf.Exp(-bounceDecay * t) * Mathf.Cos(bounceFrequency * t);
        else
            bouncing = false;                          // this frame writes the rest pose back once

        // squash Y, bulge X/Z so it keeps its volume
        float sy = 1f - s;
        float sxz = 1f + s * 0.5f;
        graphics.localScale = Vector3.Scale(graphicsBaseScale, new Vector3(sxz, sy, sxz));
        graphics.localPosition = graphicsBasePosition + Vector3.down * ((1f - sy) * feetBelowPivot);
    }

    /// <summary>
    /// How far the model's lowest point sits below the graphics pivot, in the parent's space. Centre-pivot
    /// meshes need lifting back down as they squash; feet-pivot rigs come out as ~0 and need nothing.
    /// </summary>
    float FeetBelowPivot()
    {
        // enabled only: hidden HitFlash overlays (and other disabled renderers) can report empty bounds
        var renderers = System.Array.FindAll(graphics.GetComponentsInChildren<Renderer>(), r => r.enabled);
        if (renderers.Length == 0) return 0f;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

        float parentScale = graphics.parent != null ? graphics.parent.lossyScale.y : 1f;
        return Mathf.Max(0f, (graphics.position.y - b.min.y) / Mathf.Max(parentScale, 0.0001f));
    }
}
