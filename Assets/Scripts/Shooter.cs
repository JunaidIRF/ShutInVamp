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
    public float throwReleaseDelay = 0.28f; // wind-up before the stake leaves the hand (the Throw clip's release frame)

    [Header("Spread")]
    // Each shot aims at a random point around the target's body outline (head, feet, sides...),
    // as a fraction of its half-size: 0 = dead centre, 1 = the very edge
    [Range(0f, 1f)] public float minSpread = 0.5f;
    [Range(0f, 1f)] public float maxSpread = 0.8f;

    [Header("Health")]
    public float maxHealth = 100f;
    public float health = 100f;
    public UnityEngine.UI.Image healthFill;   // world-space bar above the head (horizontal fill)
    public HealthBarFx healthBarFx;           // eased fill + white damage trail (drives healthFill when set)

    [Header("Stagger")]
    public float staggerTime = 2f;
    public HitFlash hitFlash;           // red overlay on hit, works on any model's materials
    public GameObject bloodFx;          // spawned at the impact point, spraying back towards the shot
    public float bloodFxLifetime = 9f;  // hard cleanup: drops fly up to 5 s, then sit on the floor up to 3.5 s
    public float bloodFxOffset = 0.15f; // start just outside our collider, or the drops collide with it and die at once

    [Header("Animation")]
    public Animator anim;               // on the model: Throw / Hit triggers, Stunned bool

    [Header("Death")]
    public Ragdoll ragdoll;             // on the model; switched on when health hits 0
    // knock-back along the killing shot, as velocity changes (m/s) so they don't depend on body mass
    public float deathKnockSpeed = 3.5f;  // whole body
    public float deathHitSpeed = 6f;      // extra on the body part that was hit
    public float deathLift = 1.2f;        // a little upward pop so it leaves the ground

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
    float releaseTime = -1f;            // when the current throw lets go of the stake (-1 = not throwing)
    float staggerEndTime;
    bool isStaggered;
    bool isDead;

    Vector3 graphicsBaseScale = Vector3.one;
    Vector3 graphicsBasePosition;
    float feetBelowPivot;               // so squashing keeps the feet on the ground, whatever the pivot
    float bounceStartTime = float.NegativeInfinity;
    bool bouncing;

    static readonly int ThrowHash = Animator.StringToHash("Throw");
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
        if (ragdoll == null) ragdoll = GetComponentInChildren<Ragdoll>();
        isDead = false;

        if (graphics != null)
        {
            graphicsBaseScale = graphics.localScale;
            graphicsBasePosition = graphics.localPosition;
            feetBelowPivot = FeetBelowPivot();
        }

        health = maxHealth;
        UpdateHealthBar();

        // Scene reload is off in this project — don't carry a stagger/bounce over from a previous run
        isStaggered = false;
        releaseTime = -1f;
        bounceStartTime = float.NegativeInfinity;
        bouncing = false;
        ScheduleNextShot();
    }

    void Update()
    {
        if (isDead) return;
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

        if (releaseTime >= 0f)
        {
            if (Time.time >= releaseTime)
            {
                releaseTime = -1f;
                Release();
            }
            return;
        }

        if (Time.time >= nextFireTime)
        {
            // wind up first; the stake leaves the hand at the release frame
            if (anim != null) anim.SetTrigger(ThrowHash);
            releaseTime = Time.time + throwReleaseDelay;
            ScheduleNextShot();
        }
    }

    void Release()
    {
        if (bulletPrefab == null || muzzle == null) return;

        Vector3 aim = targetCollider != null ? AimAroundBody(targetCollider.bounds) : target.position;
        Bullet bullet = Instantiate(bulletPrefab, muzzle.position, Quaternion.identity);
        bullet.Launch(aim - muzzle.position, bulletSpeed, damage, this);
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
    /// <param name="damage">Damage the parried bullet deals.</param>
    public void Stagger(Vector3 point, Vector3 direction, float damage)
    {
        if (isDead) return;

        health = Mathf.Max(health - damage, 0f);
        UpdateHealthBar();

        // The blood effect sprays along its +Z, so point it back where the shot came from
        // (its stop action doesn't reliably fire with the droplet sub-emitter, so also destroy it on a timer)
        if (bloodFx != null && direction.sqrMagnitude > 0.0001f)
        {
            Vector3 back = -direction.normalized;
            Destroy(Instantiate(bloodFx, point + back * bloodFxOffset, Quaternion.LookRotation(back)), bloodFxLifetime);
        }
        if (hitFlash != null) hitFlash.Flash();

        if (health <= 0f)
        {
            Die(point, direction);
            return;
        }

        isStaggered = true;
        staggerEndTime = Time.time + staggerTime;
        bounceStartTime = Time.time;
        bouncing = true;
        releaseTime = -1f;                 // a hit cancels a throw that hasn't let go yet
        if (anim != null)
        {
            anim.ResetTrigger(ThrowHash);
            anim.SetTrigger(HitHash);
            anim.SetBool(StunnedHash, true);
        }
    }

    void UpdateHealthBar()
    {
        if (healthBarFx != null) healthBarFx.SetValue(health / maxHealth);
        else if (healthFill != null) healthFill.fillAmount = health / maxHealth;
    }

    /// <summary>Go ragdoll, knocked along the killing shot. The shooter stays dead (no respawn yet).</summary>
    void Die(Vector3 point, Vector3 direction)
    {
        isDead = true;
        isStaggered = false;
        releaseTime = -1f;

        // physics must see the real, unsquashed model
        bouncing = false;
        if (graphics != null)
        {
            graphics.localScale = graphicsBaseScale;
            graphics.localPosition = graphicsBasePosition;
        }

        if (ownCollider != null) ownCollider.enabled = false;   // the ragdoll's own colliders take over
        if (healthFill != null) healthFill.canvas.gameObject.SetActive(false);
        if (ragdoll != null) ragdoll.Activate(point, direction, deathKnockSpeed, deathHitSpeed, deathLift);
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
