using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class Shooter : MonoBehaviour
{
    /// <summary>Shooters whose kick is winding up right now (the player's guard ring shows a warning).</summary>
    public static readonly List<Shooter> Kicking = new List<Shooter>();
    /// <summary>Every enabled shooter (they keep some room from each other).</summary>
    public static readonly List<Shooter> Active = new List<Shooter>();
    /// <summary>A shooter just died (any way: claws, execution, stake...). The vampire feeds on it.</summary>
    public static event System.Action<Shooter> Killed;
    /// <summary>A shooter was just finished off by an execution (after <see cref="Killed"/>).</summary>
    public static event System.Action<Shooter> Executed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Kicking.Clear(); Active.Clear(); ThrowTurns.Clear(); StakeArrivals.Clear(); Killed = null; Executed = null; }

    // group fairness: only a few enemies throw at once, and no two stakes arrive at the same moment
    static readonly List<(Shooter who, float until)> ThrowTurns = new List<(Shooter, float)>();
    static readonly List<float> StakeArrivals = new List<float>();

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
    [Tooltip("A stake model under the right hand: shown from the draw to the release, then the real one flies from where it is")]
    public Transform heldStake;
    public float stakeDrawTime = 0.16f;     // into the throw when the hand has pulled the stake (the Throw clip's draw frame)

    [Header("Group")]
    // with several enemies: only maxThrowers throw at once (a turn lasts the wind-up + throwTurnHold), and
    // every stake is timed to reach the player at least minStakeGap after any other, so each can be parried
    public int maxThrowers = 2;
    public float throwTurnHold = 0.8f;
    public float minStakeGap = 0.6f;

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

    [Header("Movement")]
    // keeps about preferredDistance from the player on the NavMesh: walks in when too far (or can't
    // see them), backs off when they come too close (kick range is handled by the kick)
    public NavMeshAgent agent;          // on this root (baseOffset = half the capsule height)
    public float preferredDistance = 10f;
    public float tooFar = 12.5f;
    public float tooClose = 7f;
    public float moveSpeed = 3.5f;
    public float retreatSpeed = 2.5f;
    public float repathInterval = 0.25f;
    public float legsBlendSpeed = 6f;   // how fast the walk layer fades in/out
    public float runAnimSpeed = 3.5f;   // the Run clip's own speed: above it the run plays faster so feet stay planted
    public float hipTurnSpeed = 540f;   // deg/s the legs turn towards the movement direction
    public float backpedalAngle = 112f; // moving more than this away from where we face = backpedal
    public float spreadDistance = 4f;   // keep at least this far from other enemies
    public float strafeAngle = 30f;     // how far round the player we step to get clear (degrees)

    [Header("Kick")]
    // when the player gets close (and we're not stunned) we kick them away
    public float kickRange = 2.5f;       // starts a kick when the player is this close (flat distance)
    public float kickLungeTo = 1f;       // lunges in during the wind-up to stop this far from the player
    public float kickLungeMax = 2f;      // never lunges further than this
    public float kickReach = 1.7f;       // still connects at the impact frame if they're within this
    public float kickMaxAngle = 70f;     // ... and roughly in front of us
    public float kickWindupMin = 0.4f;   // each kick lands a random time into its wind-up (the clip is sped up /
    public float kickWindupMax = 0.65f;  // slowed down to match), so the timing has to be read off the leg
    public float kickClipImpact = 0.35f; // the Kick clip's impact frame at normal speed
    public float kickCooldown = 3f;
    public float kickDamage = 25f;
    public float kickKnockback = 10f;    // m/s backwards (~5 m with the player's knockback drag)
    public float kickLift = 3f;          // m/s upward hop
    public float kickParryWindow = 0.06f; // the right-click must land this close before impact (stakes: 0.2)
    [Range(0f, 1f)] public float kickParriedDistance = 0.6f;  // a parried kick still shoves you this fraction as far (no damage)

    [Header("Sound")]
    public Sfx throwSound;              // the stake leaving the hand (from the hand)
    public Sfx kickSound;               // a kick connecting (a parried one plays the player's parry instead)

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
    float kickWindup = 0.5f;            // this kick's wind-up (random per kick)
    float nextKickTime;
    Vector3 lungeFrom, lungeTo;

    /// <summary>Seconds until the current kick lands (only meaningful while in <see cref="Kicking"/>).</summary>
    public float KickTimeToImpact => kickImpactTime - Time.time;
    /// <summary>Seconds since the current kick started winding up.</summary>
    public float KickElapsed => kickWindup - KickTimeToImpact;
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
    static readonly int KickSpeedHash = Animator.StringToHash("KickSpeed");
    static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");
    static readonly int WalkRateHash = Animator.StringToHash("WalkRate");

    enum MoveMode { Hold, Approach, Retreat, Strafe }
    enum Sight { Clear, World, Ally }   // what's between us and the player
    int strafeSide = 1;
    MoveMode moveMode;
    float nextRepath;
    bool hasSight;
    int legsLayer = -1;
    float legYaw;                       // how far the legs are turned from the torso (deg, towards the movement)
    bool backpedal;
    Transform hipsBone, spineBone, spine1Bone, spine2Bone;
    Quaternion hipsRest;                // the hips' rest rotation (nothing animates it, so we own it)
    bool hipsTurned;
    float legsWeight;

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
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        isDead = false;
        isExecuted = false;

        // movement: start on the NavMesh, standing still
        moveMode = MoveMode.Hold;
        nextRepath = 0f;
        hasSight = false;
        legsWeight = 0f;
        legsLayer = anim != null ? anim.GetLayerIndex("Legs") : -1;
        legYaw = 0f;
        backpedal = false;
        if (anim != null)
        {
            hipsBone = FindDeep(anim.transform, "mixamorig:Hips");
            spineBone = FindDeep(anim.transform, "mixamorig:Spine");
            spine1Bone = FindDeep(anim.transform, "mixamorig:Spine1");
            spine2Bone = FindDeep(anim.transform, "mixamorig:Spine2");
            if (hipsBone != null) hipsRest = hipsBone.localRotation;
            hipsTurned = false;
        }
        if (legsLayer >= 0) anim.SetLayerWeight(legsLayer, 0f);
        if (agent != null)
        {
            agent.enabled = true;
            agent.updateRotation = false;   // we face the player ourselves
            agent.avoidancePriority = Random.Range(40, 60);   // so two enemies don't deadlock
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit onMesh, 3f, NavMesh.AllAreas)) agent.Warp(onMesh.position);
        }

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
            if (Time.time < staggerEndTime) { UpdateWalkAnim(); return; }

            isStaggered = false;
            if (anim != null) anim.SetBool(StunnedHash, false);
            ScheduleNextShot();
        }

        // the player is executing someone (not us): hold off — no new attacks, and a throw or kick
        // that hasn't landed yet is dropped (stakes already in the air keep flying)
        if (PlayerExecution.IsExecuting)
        {
            if (kickImpactTime >= 0f) EndKick();
            StopMoving();
            UpdateWalkAnim();
            releaseTime = -1f;
            if (anim != null) { anim.ResetTrigger(ThrowHash); anim.ResetTrigger(KickHash); }
            nextFireTime = Mathf.Max(nextFireTime, Time.time + Random.Range(0.6f, 1.2f));   // staggered restart
            nextKickTime = Mathf.Max(nextKickTime, Time.time + 0.6f);
            return;
        }

        if (kickImpactTime < 0f) UpdateMovement();   // keep our distance (also while throwing)

        if (kickImpactTime >= 0f)
        {
            // lunge in over the wind-up, along the NavMesh (so never through a wall)
            float k = 1f - Mathf.Clamp01(KickTimeToImpact / Mathf.Max(kickWindup, 0.0001f));
            Vector3 want = Vector3.Lerp(lungeFrom, lungeTo, Mathf.SmoothStep(0f, 1f, k));
            if (AgentReady) agent.Move(new Vector3(want.x - transform.position.x, 0f, want.z - transform.position.z));
            else transform.position = want;

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

        if (Time.time >= nextFireTime && hasSight)   // no stakes into walls
        {
            if (TryTakeThrowTurn())
            {
                // wind up first; the stake leaves the hand at the release frame
                if (anim != null) anim.SetTrigger(ThrowHash);
                releaseTime = Time.time + throwReleaseDelay;
                ScheduleNextShot();
            }
            else nextFireTime = Time.time + Random.Range(0.15f, 0.4f);   // someone else's turn: try again shortly
        }
    }

    /// <summary>
    /// May we throw now? Not if maxThrowers others are mid-turn, or if our stake would reach the player
    /// within minStakeGap of another one. Taking the turn books our slot and our stake's arrival time.
    /// </summary>
    bool TryTakeThrowTurn()
    {
        float now = Time.time;
        ThrowTurns.RemoveAll(t => t.who == null || t.who.isDead || t.until <= now);
        StakeArrivals.RemoveAll(a => a < now - minStakeGap);

        int others = 0;
        foreach (var t in ThrowTurns) if (t.who != this) others++;
        if (others >= maxThrowers) return false;

        Vector3 from = muzzle != null ? muzzle.position : transform.position;
        Vector3 to = targetCollider != null ? targetCollider.bounds.center : target.position;
        float arrival = now + throwReleaseDelay + Vector3.Distance(from, to) / Mathf.Max(bulletSpeed, 0.01f);
        foreach (float a in StakeArrivals)
            if (Mathf.Abs(a - arrival) < minStakeGap) return false;

        ThrowTurns.Add((this, now + throwReleaseDelay + throwTurnHold));
        StakeArrivals.Add(arrival);
        return true;
    }

    void StartKick()
    {
        kickWindup = Random.Range(kickWindupMin, Mathf.Max(kickWindupMin, kickWindupMax));
        if (anim != null)
        {
            anim.SetFloat(KickSpeedHash, kickClipImpact / Mathf.Max(kickWindup, 0.01f));
            anim.SetTrigger(KickHash);
        }
        kickImpactTime = Time.time + kickWindup;
        nextKickTime = Time.time + kickCooldown;
        if (!Kicking.Contains(this)) Kicking.Add(this);

        // the kick animates the legs: stop walking and drop the walk layer straight away
        StopMoving();
        SetLegsWeight(0f);

        // lunge to kickLungeTo from the player (moved along the NavMesh, so walls stop it)
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        float lunge = Mathf.Clamp(to.magnitude - kickLungeTo, 0f, kickLungeMax);
        Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : transform.forward;
        lungeFrom = transform.position;
        lungeTo = transform.position + dir * lunge;
    }

    // ══════════════════════════════════════════════════════════════════
    //  MOVEMENT — keep our distance on the NavMesh
    // ══════════════════════════════════════════════════════════════════

    bool AgentReady => agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;

    void UpdateMovement()
    {
        Sight sight = LineToTarget();
        hasSight = sight == Sight.Clear;
        if (!AgentReady) { UpdateWalkAnim(); return; }

        Vector3 to = target.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        Shooter crowd = NearestAlly(out float allyDist);

        // decide (with hysteresis so it doesn't twitch at the edges)
        if (dist > tooFar || sight == Sight.World) moveMode = MoveMode.Approach;            // too far / behind a wall
        else if (dist < tooClose && dist > kickRange) moveMode = MoveMode.Retreat;          // too close
        else if (sight == Sight.Ally || (crowd != null && allyDist < spreadDistance))      // another enemy in the way / too near
        {
            if (moveMode != MoveMode.Strafe) strafeSide = StrafeSideAwayFrom(crowd);
            moveMode = MoveMode.Strafe;
        }
        if (moveMode == MoveMode.Approach && hasSight && dist <= preferredDistance) moveMode = MoveMode.Hold;
        if (moveMode == MoveMode.Retreat && (dist >= preferredDistance - 1f || dist <= kickRange)) moveMode = MoveMode.Hold;
        if (moveMode == MoveMode.Strafe && sight != Sight.Ally && (crowd == null || allyDist > spreadDistance + 0.5f)) moveMode = MoveMode.Hold;

        if (Time.time >= nextRepath)
        {
            nextRepath = Time.time + repathInterval;
            switch (moveMode)
            {
                case MoveMode.Approach:
                    agent.speed = moveSpeed;
                    agent.isStopped = false;
                    agent.SetDestination(target.position);
                    break;
                case MoveMode.Retreat:
                    agent.speed = retreatSpeed;
                    if (FindRetreatPoint(dist > 0.001f ? to / dist : transform.forward, preferredDistance - dist, out Vector3 point))
                    {
                        agent.isStopped = false;
                        agent.SetDestination(point);
                    }
                    else { StopMoving(); moveMode = MoveMode.Hold; }   // cornered: stand and fight
                    break;
                case MoveMode.Strafe:
                    agent.speed = retreatSpeed;
                    if (FindStrafePoint(out Vector3 side))
                    {
                        agent.isStopped = false;
                        agent.SetDestination(side);
                    }
                    else strafeSide = -strafeSide;   // blocked that way: try the other side next time
                    break;
                default:
                    StopMoving();
                    break;
            }
        }
        UpdateWalkAnim();
    }

    /// <summary>A reachable spot about <paramref name="amount"/> m away from the player: straight back, else diagonally.</summary>
    bool FindRetreatPoint(Vector3 toPlayer, float amount, out Vector3 point)
    {
        Vector3 back = -toPlayer;
        foreach (float angle in new[] { 0f, 35f, -35f, 70f, -70f })
        {
            Vector3 want = transform.position + Quaternion.Euler(0f, angle, 0f) * back * (Mathf.Max(amount, 1f) + 0.5f);
            if (NavMesh.SamplePosition(want, out NavMeshHit hit, 1f, NavMesh.AllAreas) &&
                !NavMesh.Raycast(agent.nextPosition, hit.position, out _, NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }
        }
        point = transform.position;
        return false;
    }

    /// <summary>What's between our chest and the player's: nothing, the level, or another enemy.</summary>
    Sight LineToTarget()
    {
        Vector3 from = ownCollider != null ? ownCollider.bounds.center : transform.position;
        Vector3 to = targetCollider != null ? targetCollider.bounds.center : target.position;
        // our own capsule doesn't count (the line starts inside it); other enemies do
        if (!Physics.Linecast(from, to, out RaycastHit hit, ~(1 << 2), QueryTriggerInteraction.Ignore)) return Sight.Clear;
        if (hit.collider.transform.IsChildOf(target)) return Sight.Clear;
        Shooter other = hit.collider.GetComponentInParent<Shooter>();
        if (other == this) return Sight.Clear;
        return other != null ? Sight.Ally : Sight.World;
    }

    /// <summary>The closest other living enemy (null if none).</summary>
    Shooter NearestAlly(out float distance)
    {
        Shooter best = null;
        distance = float.MaxValue;
        foreach (Shooter s in Active)
        {
            if (s == this || s == null || s.isDead || s.isExecuted) continue;
            Vector3 d = s.transform.position - transform.position;
            d.y = 0f;
            if (d.magnitude < distance) { distance = d.magnitude; best = s; }
        }
        return best;
    }

    /// <summary>+1 / -1: which way round the player moves us away from <paramref name="ally"/>.</summary>
    int StrafeSideAwayFrom(Shooter ally)
    {
        if (ally == null) return Random.value < 0.5f ? 1 : -1;
        Vector3 fromPlayer = transform.position - target.position;
        Vector3 allyFromPlayer = ally.transform.position - target.position;
        float cross = Vector3.Cross(allyFromPlayer, fromPlayer).y;
        return cross >= 0f ? 1 : -1;
    }

    /// <summary>A spot strafeAngle further round the player at our current distance (on the NavMesh, reachable).</summary>
    bool FindStrafePoint(out Vector3 point)
    {
        Vector3 fromPlayer = transform.position - target.position;
        fromPlayer.y = 0f;
        float radius = Mathf.Clamp(fromPlayer.magnitude, tooClose, tooFar);
        Vector3 want = target.position + Quaternion.Euler(0f, strafeSide * strafeAngle, 0f) * fromPlayer.normalized * radius;
        if (NavMesh.SamplePosition(want, out NavMeshHit hit, 1.5f, NavMesh.AllAreas) &&
            !NavMesh.Raycast(agent.nextPosition, hit.position, out _, NavMesh.AllAreas))
        {
            point = hit.position;
            return true;
        }
        point = transform.position;
        return false;
    }

    void StopMoving()
    {
        if (AgentReady)
        {
            agent.ResetPath();
            agent.velocity = Vector3.zero;
        }
    }

    /// <summary>
    /// Legs follow how we actually move: the gait blend gets the signed speed in m/s (walk/run, forward or
    /// backpedal) and the legs turn towards the movement direction (applied in LateUpdate), so any direction
    /// is a forward run or a backpedal with the hips turned — while the torso keeps facing the player.
    /// </summary>
    void UpdateWalkAnim()
    {
        if (anim == null || legsLayer < 0) return;
        Vector3 v = AgentReady ? agent.velocity : Vector3.zero;
        v.y = 0f;
        float speed = v.magnitude;

        float yawTarget = 0f;
        if (speed > 0.2f)
        {
            float phi = Vector3.SignedAngle(transform.forward, v, Vector3.up);
            // switch forward <-> backpedal with a little hysteresis so it doesn't flicker at the edge
            if (backpedal && Mathf.Abs(phi) < backpedalAngle - 10f) backpedal = false;
            else if (!backpedal && Mathf.Abs(phi) > backpedalAngle + 10f) backpedal = true;
            yawTarget = backpedal ? Mathf.DeltaAngle(0f, phi - 180f) : phi;
        }
        legYaw = Mathf.MoveTowardsAngle(legYaw, yawTarget, hipTurnSpeed * Time.deltaTime);

        anim.SetFloat(MoveSpeedHash, backpedal ? -speed : speed, 0.1f, Time.deltaTime);
        anim.SetFloat(WalkRateHash, Mathf.Max(1f, speed / Mathf.Max(runAnimSpeed, 0.01f)));
        float want = Mathf.Clamp01(speed / 0.5f);   // the blend tree handles walk vs run; this just fades standing
        legsWeight = Mathf.MoveTowards(legsWeight, want, legsBlendSpeed * Time.deltaTime);
        anim.SetLayerWeight(legsLayer, legsWeight);
    }

    void SetLegsWeight(float w)
    {
        legsWeight = w;
        if (w <= 0f) legYaw = 0f;
        if (anim != null && legsLayer >= 0) anim.SetLayerWeight(legsLayer, w);
    }

    /// <summary>After the Animator: turn the legs (hips) towards the movement and untwist the spine so
    /// chest, arms and head stay aimed at the player. The Animator never writes the hips' rotation (no clip
    /// changes it), so it's set absolutely from its rest each frame — never added on top of last frame's.
    /// The spine is rewritten by the Animator every frame, so its counter-turn is relative.</summary>
    void LateUpdate()
    {
        UpdateHeldStake();   // after the Animator has posed the hand
        if (isDead || isExecuted || hipsBone == null) return;
        float yaw = legYaw * legsWeight;
        if (Mathf.Abs(yaw) < 0.01f)
        {
            ResetHips();
            return;
        }
        Vector3 up = transform.up;
        hipsBone.localRotation = hipsRest;
        hipsBone.rotation = Quaternion.AngleAxis(yaw, up) * hipsBone.rotation;
        hipsTurned = true;
        if (spineBone != null) spineBone.rotation = Quaternion.AngleAxis(-yaw * 0.5f, up) * spineBone.rotation;
        if (spine1Bone != null) spine1Bone.rotation = Quaternion.AngleAxis(-yaw * 0.3f, up) * spine1Bone.rotation;
        if (spine2Bone != null) spine2Bone.rotation = Quaternion.AngleAxis(-yaw * 0.2f, up) * spine2Bone.rotation;
    }

    void ResetHips()
    {
        if (!hipsTurned || hipsBone == null) return;
        hipsBone.localRotation = hipsRest;
        hipsTurned = false;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            Transform f = FindDeep(c, name);
            if (f != null) return f;
        }
        return null;
    }

    /// <summary>The kick is over (landed, or cancelled by a stun / execution / death).</summary>
    void EndKick()
    {
        kickImpactTime = -1f;
        Kicking.Remove(this);
    }

    void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }

    void OnDisable()
    {
        Kicking.Remove(this);
        Active.Remove(this);
    }

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
        // who this kick hits, for the whole impact: the damage can kill them, and dying clears our target
        // (the wave director stops everyone)
        Transform victim = target;

        var block = victim.GetComponent<PlayerBlock>();
        bool parried = block != null && block.TryParryMelee(kickParryWindow, 180f, transform.position);   // a kick comes in low: guard down

        // parried: we're stunned and the player takes no damage, but is still pushed back a bit
        if (parried) Stun(staggerTime);
        else
        {
            var player = victim.GetComponent<Player>();
            kickSound?.PlayAt(Vector3.Lerp(transform.position, victim.position, 0.5f));
            if (player != null) player.TakeDamage(kickDamage);
            if (block != null) { block.HitPop(); block.HitFrom(transform.position); }
        }

        var move = victim.GetComponent<PlayerMovement>();
        Vector3 away = victim.position - transform.position;
        away.y = 0f;
        float push = parried ? Mathf.Sqrt(kickParriedDistance) : 1f;   // slide distance goes with speed squared
        if (move != null) move.Knockback((away.normalized * kickKnockback + Vector3.up * kickLift) * push);
    }

    void Release()
    {
        if (bulletPrefab == null || muzzle == null) return;

        // from the hand that holds it (the muzzle if there's no stake model)
        Vector3 from = heldStake != null && heldStake.gameObject.activeInHierarchy ? heldStake.position : muzzle.position;
        Vector3 aim = targetCollider != null ? AimAroundBody(targetCollider.bounds, from) : target.position;
        Bullet bullet = Instantiate(bulletPrefab, from, Quaternion.LookRotation(aim - from));
        throwSound?.PlayAt(from);
        bullet.Launch(aim - from, bulletSpeed, damage, this);
        if (heldStake != null) heldStake.gameObject.SetActive(false);
    }

    /// <summary>
    /// The stake in the hand: there from the draw until it's thrown (or the throw is cut short). Its tip turns from
    /// pointing down (just pulled from the hip) to aimed at the target as the arm comes up.
    /// </summary>
    void UpdateHeldStake()
    {
        if (heldStake == null) return;
        float drawn = releaseTime - throwReleaseDelay + stakeDrawTime;   // when the hand got it
        bool show = releaseTime >= 0f && !isDead && !isExecuted && Time.time >= drawn;
        if (heldStake.gameObject.activeSelf != show) heldStake.gameObject.SetActive(show);
        if (!show) return;
        float k = Mathf.SmoothStep(0f, 1f, (Time.time - drawn) / Mathf.Max(0.01f, (throwReleaseDelay - stakeDrawTime) * 0.6f));
        Vector3 toTarget = target != null ? (targetCollider != null ? targetCollider.bounds.center : target.position) - heldStake.position : transform.forward;
        Vector3 down = Vector3.down - transform.forward * 0.3f;
        Vector3 dir = Vector3.Slerp(down.normalized, toTarget.normalized, k);
        if (dir.sqrMagnitude > 0.0001f) heldStake.rotation = Quaternion.LookRotation(dir, transform.forward);
    }

    /// <summary>Random point on the target's silhouette as seen from <paramref name="from"/>, in any direction from its centre.</summary>
    Vector3 AimAroundBody(Bounds body, Vector3 from)
    {
        Vector3 toTarget = body.center - from;
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
        StopMoving();
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

    /// <summary>
    /// A hit that hurts but doesn't stun (punches): damage, blood, flash and a squash; we keep fighting.
    /// Only parried stakes / kicks stun us (and so open executions). A killing blow with
    /// <paramref name="severOnKill"/> cuts off the part it lands on (head, arm, leg).
    /// </summary>
    public void Hit(Vector3 point, Vector3 direction, float damage, bool severOnKill = false)
    {
        if (isDead || isExecuted) return;
        health = Mathf.Max(health - damage, 0f);
        UpdateHealthBar();
        Bleed(point, direction);
        if (hitFlash != null) hitFlash.Flash();
        if (health <= 0f)
        {
            // pick the part in the pose the blow landed on, before dying changes it
            Dismemberment cut = severOnKill ? FindDismemberment() : null;
            Dismemberment.Part part = cut != null ? cut.PartAt(point, direction) : null;
            Die(point, direction);
            if (part != null) cut.Sever(part, direction);
            return;
        }
        bounceStartTime = Time.time;
        bouncing = true;
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
        StopMoving();
        SetLegsWeight(0f);   // the execution clip owns the legs
        ResetHips();
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
    public void FinishExecution(Vector3 point, Vector3 direction, float bodySpeed, float hitSpeed, float lift, bool decapitate = false)
    {
        if (isDead) return;
        health = 0f;
        UpdateHealthBar();
        Die(point, direction, bodySpeed, hitSpeed, lift);
        if (decapitate) { var cut = FindDismemberment(); if (cut != null) cut.Sever(Dismemberment.BodyPart.Head, direction); }
        Executed?.Invoke(this);
    }

    /// <summary>Cuts <paramref name="part"/> off (alive or dead), if the model has a Dismemberment.</summary>
    public bool Sever(Dismemberment.BodyPart part, Vector3 direction)
    {
        var cut = FindDismemberment();
        return cut != null && cut.Sever(part, direction);
    }

    /// <summary>The bone <paramref name="part"/> comes off at on this model (null if there's none).</summary>
    public Transform PartBone(Dismemberment.BodyPart part)
    {
        var cut = FindDismemberment();
        return cut != null ? cut.BoneOf(part) : null;
    }

    Dismemberment FindDismemberment()
    {
        Dismemberment d = anim != null ? anim.GetComponent<Dismemberment>() : null;
        return d != null ? d : GetComponentInChildren<Dismemberment>();
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
        Killed?.Invoke(this);
        isExecuted = false;
        isStaggered = false;
        releaseTime = -1f;
        EndKick();
        StopMoving();
        if (agent != null) agent.enabled = false;   // the ragdoll moves on its own now
        SetLegsWeight(0f);
        ResetHips();

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
        // parts cut off before death (execution events) must not collide with the ragdoll's colliders now on
        var cut = FindDismemberment();
        if (cut != null) cut.IgnoreBody();
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
