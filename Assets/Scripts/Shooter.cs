using System.Collections.Generic;
using UnityEngine;

public class Shooter : MonoBehaviour
{
    /// <summary>Shooters whose kick is winding up right now (the player's guard ring shows a warning).</summary>
    public static readonly List<Shooter> Kicking = new List<Shooter>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Kicking.Clear();

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

    [Header("Kick")]
    // when the player gets close (and we're not stunned) we kick them away
    public float kickRange = 2.5f;       // starts a kick when the player is this close (flat distance)
    public float kickLungeTo = 1f;       // lunges in during the wind-up to stop this far from the player
    public float kickLungeMax = 2f;      // never lunges further than this
    public float kickReach = 1.7f;       // still connects at the impact frame if they're within this
    public float kickMaxAngle = 70f;     // ... and roughly in front of us
    public float kickWindup = 0.5f;      // the Kick state's impact moment (clip impact 0.35 s at state speed 0.7)
    public float kickCooldown = 3f;
    public float kickDamage = 25f;
    public float kickKnockback = 10f;    // m/s backwards (~5 m with the player's knockback drag)
    public float kickLift = 3f;          // m/s upward hop
    public float kickParryWindow = 0.1f; // the right-click must land this close before impact (stakes: 0.2)

    [Header("Animation")]
    public Animator anim;               // on the model: Throw / Hit / Kick triggers, Stunned bool

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
    float kickImpactTime = -1f;         // when the current kick lands (-1 = not kicking)
    float nextKickTime;
    Vector3 lungeFrom, lungeTo;

    /// <summary>Seconds until the current kick lands (only meaningful while in <see cref="Kicking"/>).</summary>
    public float KickTimeToImpact => kickImpactTime - Time.time;
    float staggerEndTime;
    bool isStaggered;
    bool isDead;
    bool isExecuted;                    // a player execution is playing on us (it drives the animator)

    Vector3 graphicsBaseScale = Vector3.one;
    Vector3 graphicsBasePosition;
    float feetBelowPivot;               // so squashing keeps the feet on the ground, whatever the pivot
    float bounceStartTime = float.NegativeInfinity;
    bool bouncing;

    static readonly int ThrowHash = Animator.StringToHash("Throw");
    static readonly int HitHash = Animator.StringToHash("Hit");
    static readonly int StunnedHash = Animator.StringToHash("Stunned");
    static readonly int KickHash = Animator.StringToHash("Kick");

    /// <summary>Where reflected bullets aim — the middle of our capsule.</summary>
    public Vector3 AimPoint => ownCollider != null ? ownCollider.bounds.center : transform.position;

    public bool IsDead => isDead;
    /// <summary>Executions need us stunned first (a parried stake or a punch).</summary>
    public bool CanBeExecuted => !isDead && !isExecuted && isStaggered;

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
        isExecuted = false;

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
        EndKick();
        nextKickTime = 0f;
        bounceStartTime = float.NegativeInfinity;
        bouncing = false;
        ScheduleNextShot();
    }

    void Update()
    {
        if (isDead || isExecuted) return;
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

        // the player is executing someone (not us): hold off — no new attacks, and a throw or kick
        // that hasn't landed yet is dropped (stakes already in the air keep flying)
        if (PlayerExecution.IsExecuting)
        {
            if (kickImpactTime >= 0f) EndKick();
            releaseTime = -1f;
            if (anim != null) { anim.ResetTrigger(ThrowHash); anim.ResetTrigger(KickHash); }
            nextFireTime = Mathf.Max(nextFireTime, Time.time + Random.Range(0.6f, 1.2f));   // staggered restart
            nextKickTime = Mathf.Max(nextKickTime, Time.time + 0.6f);
            return;
        }

        if (kickImpactTime >= 0f)
        {
            // lunge in over the wind-up
            float k = 1f - Mathf.Clamp01(KickTimeToImpact / Mathf.Max(kickWindup, 0.0001f));
            transform.position = Vector3.Lerp(lungeFrom, lungeTo, Mathf.SmoothStep(0f, 1f, k));

            if (Time.time >= kickImpactTime)
            {
                EndKick();
                KickImpact();
            }
            return;
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

        if (Time.time >= nextKickTime && TargetInFront(kickRange))
        {
            StartKick();
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

    void StartKick()
    {
        if (anim != null) anim.SetTrigger(KickHash);
        kickImpactTime = Time.time + kickWindup;
        nextKickTime = Time.time + kickCooldown;
        if (!Kicking.Contains(this)) Kicking.Add(this);

        // lunge to kickLungeTo from the player, but not through walls (the player and we don't count)
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        float lunge = Mathf.Clamp(to.magnitude - kickLungeTo, 0f, kickLungeMax);
        Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : transform.forward;
        Vector3 chest = ownCollider != null ? ownCollider.bounds.center : transform.position + Vector3.up;
        int mask = ~((1 << gameObject.layer) | (1 << target.gameObject.layer));
        if (lunge > 0f && Physics.SphereCast(chest, 0.3f, dir, out RaycastHit hit, lunge + 0.5f, mask, QueryTriggerInteraction.Ignore))
            lunge = Mathf.Max(0f, hit.distance - 0.5f);
        lungeFrom = transform.position;
        lungeTo = transform.position + dir * lunge;
    }

    /// <summary>The kick is over (landed, or cancelled by a stun / execution / death).</summary>
    void EndKick()
    {
        kickImpactTime = -1f;
        Kicking.Remove(this);
    }

    void OnDisable() => Kicking.Remove(this);

    bool TargetInFront(float range)
    {
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        return to.magnitude <= range && Vector3.Angle(transform.forward, to) <= kickMaxAngle;
    }

    /// <summary>The kick's impact frame: parried = we're stunned; otherwise damage + knockback (if still in reach).</summary>
    void KickImpact()
    {
        ScheduleNextShot();   // don't throw straight after a kick
        if (target == null || !TargetInFront(kickReach)) return;   // they stepped back: whiff

        var block = target.GetComponent<PlayerBlock>();
        if (block != null && block.TryParryMelee(kickParryWindow, 180f))   // a kick comes in low: guard down
        {
            Stun(staggerTime);
            return;
        }

        var player = target.GetComponent<Player>();
        if (player != null) player.TakeDamage(kickDamage);
        var move = target.GetComponent<PlayerMovement>();
        Vector3 away = target.position - transform.position;
        away.y = 0f;
        if (move != null) move.Knockback(away.normalized * kickKnockback + Vector3.up * kickLift);
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
    /// <param name="stunTime">How long we stay stunned; negative = <see cref="staggerTime"/>.</param>
    public void Stagger(Vector3 point, Vector3 direction, float damage, float stunTime = -1f)
    {
        if (isDead || isExecuted) return;

        health = Mathf.Max(health - damage, 0f);
        UpdateHealthBar();

        Bleed(point, direction);
        if (hitFlash != null) hitFlash.Flash();

        if (health <= 0f)
        {
            Die(point, direction);
            return;
        }

        Stun(stunTime >= 0f ? stunTime : staggerTime);
    }

    /// <summary>Stunned for <paramref name="time"/> s: hit reaction, bounce, any throw or kick in progress cancelled.</summary>
    void Stun(float time)
    {
        isStaggered = true;
        staggerEndTime = Time.time + time;
        bounceStartTime = Time.time;
        bouncing = true;
        releaseTime = -1f;                 // a hit cancels a throw that hasn't let go yet
        EndKick();                         // ... and a kick that hasn't landed
        if (anim != null)
        {
            anim.ResetTrigger(ThrowHash);
            anim.ResetTrigger(KickHash);
            anim.SetTrigger(HitHash);
            anim.SetBool(StunnedHash, true);
        }
    }

    /// <summary>Blood at <paramref name="point"/>, spraying back against <paramref name="direction"/>.</summary>
    public void Bleed(Vector3 point, Vector3 direction)
    {
        // The blood effect sprays along its +Z, so point it back where the hit came from
        // (its stop action doesn't reliably fire with the droplet sub-emitter, so also destroy it on a timer)
        if (bloodFx == null || direction.sqrMagnitude < 0.0001f) return;
        Vector3 back = -direction.normalized;
        Destroy(Instantiate(bloodFx, point + back * bloodFxOffset, Quaternion.LookRotation(back)), bloodFxLifetime);
    }

    // ══════════════════════════════════════════════════════════════════
    //  EXECUTIONS — PlayerExecution plays its victim state on our animator
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Stop all AI and visual effects that would fight the execution animation.</summary>
    public void BeginExecution()
    {
        isExecuted = true;
        isStaggered = false;
        releaseTime = -1f;
        EndKick();
        bouncing = false;
        if (ownCollider != null) ownCollider.enabled = false;   // the player stands closer than our capsule allows
        if (graphics != null)
        {
            graphics.localScale = graphicsBaseScale;
            graphics.localPosition = graphicsBasePosition;
        }
        if (anim != null)
        {
            anim.ResetTrigger(ThrowHash);
            anim.ResetTrigger(HitHash);
            anim.ResetTrigger(KickHash);
            anim.SetBool(StunnedHash, false);
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
    }

    /// <summary>The execution's kill moment: hand over to the ragdoll, pushed along <paramref name="direction"/>
    /// (speeds are velocity changes in m/s, like the death knock-back).</summary>
    public void FinishExecution(Vector3 point, Vector3 direction, float bodySpeed, float hitSpeed, float lift)
    {
        if (isDead) return;
        health = 0f;
        UpdateHealthBar();
        Die(point, direction, bodySpeed, hitSpeed, lift);
    }

    void UpdateHealthBar()
    {
        if (healthBarFx != null) healthBarFx.SetValue(health / maxHealth);
        else if (healthFill != null) healthFill.fillAmount = health / maxHealth;
    }

    /// <summary>Go ragdoll, knocked along the killing shot. The shooter stays dead (no respawn yet).</summary>
    void Die(Vector3 point, Vector3 direction) => Die(point, direction, deathKnockSpeed, deathHitSpeed, deathLift);

    void Die(Vector3 point, Vector3 direction, float bodySpeed, float hitSpeed, float lift)
    {
        isDead = true;
        isExecuted = false;
        isStaggered = false;
        releaseTime = -1f;
        EndKick();

        // physics must see the real, unsquashed model
        bouncing = false;
        if (graphics != null)
        {
            graphics.localScale = graphicsBaseScale;
            graphics.localPosition = graphicsBasePosition;
        }

        if (ownCollider != null) ownCollider.enabled = false;   // the ragdoll's own colliders take over
        if (healthFill != null) healthFill.canvas.gameObject.SetActive(false);
        if (ragdoll != null) ragdoll.Activate(point, direction, bodySpeed, hitSpeed, lift);
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
