using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Radial guard. Hold the guard key to freeze the camera and steer the guard arc around the
/// crosshair with the mouse. A bullet that lands inside the arc is blocked (chip damage);
/// right-click shortly before it lands parries it back at the shooter. A parry commits to its
/// swipe animation: if nothing is parried you can't block until the swipe has finished.
/// Angles say where on the body a shot lands, clockwise from the top of the ring:
/// 0 = head (high), 90 = right side, ±180 = feet (low), -90 = left side.
/// </summary>
public class PlayerBlock : MonoBehaviour
{
    [Header("References")]
    public Player player;
    public PlayerMovement movement;

    [Header("Input")]
    public KeyCode guardKey = KeyCode.LeftAlt;
    public int parryMouseButton = 1;
    public float guardSensitivity = 0.15f;  // how fast the mouse pushes the guard around the ring
    public float guardDeadzone = 0.3f;

    [Header("Block / Parry")]
    public float blockArc = 60f;            // width of the guard, in degrees
    public float parryWindow = 0.2f;        // right-click must land this long before the hit
    [Range(0f, 1f)] public float chipDamage = 0.2f;
    public float parryHeal = 5f;            // every parried stake / kick gives this much health back
    [Tooltip("Only attacks coming from within this many degrees of where you look (each side) can be blocked or parried; anything from behind just hits")]
    [Range(0f, 180f)] public float guardFacing = 70f;

    [Header("Animation")]
    public Animator anim;                   // Camera Head animator: Block<Dir> / Parry<Dir> states
    public string parryTag = "Parry";       // tag on the parry states — a missed parry locks blocking until it ends
    public GuardJuice juice;                // slow motion / chromatic aberration / shake / ring pulse

    [Header("Sound")]
    public Sfx parrySwingSound;             // every parry press: the swipe through the air
    public Sfx parrySound;                  // a parry landed (once per parry, however many stakes / kicks it catches)
    public Sfx blockSound;                  // a stake taken on the guard
    public Sfx stakeHitSound;               // a stake getting through: we're hit

    [Header("UI")]
    public ThreatIndicator threats;         // the outer ring for attacks from outside the guard (behind / far to the side)
    public GameObject guardVisuals;         // ring base + guard arc, shown while guarding
    public Image guardArc;
    public Image incomingTemplate;          // red arc cloned per incoming bullet
    public float incomingArc = 40f;
    public float kickArc = 60f;             // the kick warning at the bottom of the ring
    public Color kickColor = new Color(1f, 0.55f, 0.05f, 1f);   // orange, so it never reads as a stake
    public float kickPulseSpeed = 7f;       // the warning pulses steadily (it doesn't count down to impact)
    public float warnTime = 1.5f;           // indicator reaches full strength this close to impact
    public Color guardColor = Color.white;
    public Color blockFlashColor = new Color(0.5f, 0.8f, 1f);
    public Color parryFlashColor = Color.white;
    public Color hitFlashColor = new Color(1f, 0.15f, 0.1f);
    public Color lockedColor = new Color(1f, 1f, 1f, 0.25f);   // guard while a missed parry recovers
    public float flashTime = 0.25f;

    [Header("Guard lean")]
    // while guarding, the view leans a little towards the guard: up = look up, right = turn + roll right
    public float guardTilt = 1.75f;         // degrees of pitch / yaw at full guard
    public float guardRoll = 1.2f;          // degrees of roll for a side guard
    public float leanSpeed = 10f;           // how fast it eases in / out
    public float popScale = 0.28f;          // the guard arc pops this much bigger on a block / parry / hit
    public float popTime = 0.2f;

    [Header("Incoming look")]
    // stake markers start small, thin and pale, and grow, thicken and darken as the stake closes in;
    // inside the parry window they snap to a bright flash (the moment to right-click)
    public Color incomingFar = new Color(1f, 0.45f, 0.45f, 0.35f);
    public Color incomingNear = new Color(0.6f, 0.02f, 0.04f, 1f);
    public Color parryCueColor = new Color(1f, 0.95f, 0.9f, 1f);
    public float incomingScaleFar = 0.85f, incomingScaleNear = 1.12f, parryCueScale = 1.2f;
    public float kickGrowTime = 0.3f;       // the kick marker swells over this (shorter than any wind-up, so it's no countdown)

    [HideInInspector] public bool isGuarding;
    [HideInInspector] public float guardAngle;

    Vector2 guardStick = Vector2.up;
    float lastParryPress = float.NegativeInfinity;
    bool parryLanded;
    Collider body;
    float flashEndTime;
    float popStartTime = float.NegativeInfinity;
    Color flashColor;
    readonly List<Image> incomingPool = new List<Image>();

    void Start()
    {
        if (player == null) player = GetComponent<Player>();
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (anim == null && player != null) anim = player.anim;
        body = GetComponent<Collider>();

        // Enter Play Mode skips scene/domain reload in this project, so fields keep their
        // values between runs — start from a clean state (stale pool entries were destroyed).
        incomingPool.Clear();
        camParent = anim != null ? anim.transform.Find("CamParent") : null;
        lean = Vector3.zero;
        leanWritten = false;
        lastParryPress = float.NegativeInfinity;
        parryLanded = false;
        flashEndTime = 0f;
        guardStick = Vector2.up;

        if (incomingTemplate != null) incomingTemplate.gameObject.SetActive(false);
        SetGuarding(false);
    }

    Transform camParent;
    Vector3 lean;                           // current lean (pitch, yaw, roll)
    Quaternion lastLean = Quaternion.identity, lastWritten;
    bool leanWritten;

    /// <summary>
    /// After the Animator (which drives CamParent for inspect / punches / blocks / executions): lean the view
    /// towards the guard. If the Animator didn't rewrite CamParent this frame, last frame's lean is taken
    /// off first, so it never stacks up.
    /// </summary>
    void LateUpdate()
    {
        if (camParent == null) return;
        Vector3 want = Vector3.zero;
        if (isGuarding && !PlayerExecution.IsExecuting)
        {
            float a = guardAngle * Mathf.Deg2Rad;
            float amount = Mathf.Clamp01(guardStick.magnitude);
            float x = Mathf.Sin(a) * amount, y = Mathf.Cos(a) * amount;
            want = new Vector3(-y * guardTilt, x * guardTilt, -x * guardRoll);
        }
        lean = Vector3.Lerp(lean, want, 1f - Mathf.Exp(-leanSpeed * Time.unscaledDeltaTime));   // unscaled: parry slow-mo doesn't freeze it

        Quaternion current = camParent.localRotation;
        Quaternion baseRot = leanWritten && Quaternion.Angle(current, lastWritten) < 0.001f
            ? current * Quaternion.Inverse(lastLean)   // untouched since last frame: remove our old lean
            : current;                                // the Animator rewrote it
        if (lean.sqrMagnitude < 0.0001f && want == Vector3.zero)
        {
            if (leanWritten) camParent.localRotation = baseRot;
            leanWritten = false;
            lastLean = Quaternion.identity;
            return;
        }
        Quaternion q = Quaternion.Euler(lean);
        camParent.localRotation = baseRot * q;
        lastLean = q;
        lastWritten = camParent.localRotation;
        leanWritten = true;
    }

    void OnDisable()
    {
        if (camParent != null && leanWritten && Quaternion.Angle(camParent.localRotation, lastWritten) < 0.001f)
            camParent.localRotation = camParent.localRotation * Quaternion.Inverse(lastLean);
        leanWritten = false;
        lean = Vector3.zero;
    }

    void Update()
    {
        HandleGuardInput();
        UpdateGuardVisuals();
        UpdateIncomingIndicators();
    }

    // ══════════════════════════════════════════════════════════════════
    //  INPUT
    // ══════════════════════════════════════════════════════════════════
    void HandleGuardInput()
    {
        // An execution owns the camera and hands: drop the guard without touching mouse look
        if (PlayerExecution.IsExecuting)
        {
            if (isGuarding)
            {
                isGuarding = false;
                if (guardVisuals != null) guardVisuals.SetActive(false);
            }
            return;
        }

        // Polled (and ignored while unfocused) so alt-tabbing can't leave the guard stuck on
        bool held = Application.isFocused && Input.GetKey(guardKey);
        if (held != isGuarding) SetGuarding(held);

        if (!isGuarding) return;

        // Mouse acts like a virtual thumbstick pushed around the ring
        Vector2 mouse = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        guardStick = Vector2.ClampMagnitude(guardStick + mouse * guardSensitivity, 1f);

        if (guardStick.magnitude > guardDeadzone)
            guardAngle = Mathf.Atan2(guardStick.x, guardStick.y) * Mathf.Rad2Deg;

        // One parry at a time — the swipe has to play out (or land) before the next
        if (Input.GetMouseButtonDown(parryMouseButton) && !ParryCommitted)
        {
            lastParryPress = Time.time;
            parryLanded = false;
            PlayDirectional("Parry", "Block");
            parrySwingSound?.Play();
        }
    }

    bool InParryWindow => Time.time - lastParryPress <= parryWindow;

    bool InParryAnimation => anim != null && anim.GetCurrentAnimatorStateInfo(0).IsTag(parryTag);

    /// <summary>A parry was pressed and hasn't landed yet: its window is open or its swipe is still playing.</summary>
    bool ParryCommitted => !parryLanded && (InParryWindow || InParryAnimation);

    /// <summary>A missed parry still recovering — no blocking until the swipe finishes.</summary>
    bool BlockLocked => ParryCommitted && !InParryWindow;

    /// <summary>Play the swipe for the current guard direction (8-way).</summary>
    void PlayDirectional(string trigger, string otherTrigger)
    {
        if (anim == null) return;
        anim.ResetTrigger(otherTrigger); // a stale trigger would cut this animation short next frame
        anim.SetInteger("GuardDir", DirectionIndex(guardAngle));
        anim.SetTrigger(trigger);
    }

    /// <summary>0 Up, 1 TopRight, 2 Right, 3 BottomRight, 4 Down, 5 BottomLeft, 6 Left, 7 TopLeft.</summary>
    public static int DirectionIndex(float angle) => ((Mathf.RoundToInt(angle / 45f) % 8) + 8) % 8;

    void SetGuarding(bool guarding)
    {
        isGuarding = guarding;
        if (movement != null) movement.SetLookEnabled(!guarding);
        if (guardVisuals != null) guardVisuals.SetActive(guarding);
    }

    // ══════════════════════════════════════════════════════════════════
    //  HIT RESOLUTION — called by Bullet when it reaches our capsule
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// A melee hit (the enemy kick) lands now from ring <paramref name="angle"/> (180 = below), thrown from
    /// <paramref name="attacker"/>: only a parry pressed within <paramref name="window"/> s with the guard over
    /// that angle, facing the attacker, counts — blocking does nothing. True = parried.
    /// </summary>
    public bool TryParryMelee(float window, float angle, Vector3 attacker)
    {
        bool covered = isGuarding && Mathf.Abs(Mathf.DeltaAngle(angle, guardAngle)) <= blockArc * 0.5f
                       && Facing(attacker - Center);
        if (!covered || Time.time - lastParryPress > window) return false;
        // like stakes: one parry covers every kick landing in its window
        if (!parryLanded)
        {
            Flash(parryFlashColor);
            if (juice != null) juice.Parry();
            parrySound?.Play();
        }
        parryLanded = true;
        if (player != null) player.Heal(parryHeal);
        return true;
    }
    public void ResolveHit(Bullet bullet)
    {
        // Where on our capsule the bullet actually struck
        float hitAngle = BodyAngle(bullet.transform.position - Center);
        // where it comes from: back along its flight (or its thrower)
        Vector3 from = bullet.velocity.sqrMagnitude > 0.0001f ? -bullet.velocity
                     : bullet.owner != null ? bullet.owner.transform.position - Center : transform.forward;
        bool covered = isGuarding && Mathf.Abs(Mathf.DeltaAngle(hitAngle, guardAngle)) <= blockArc * 0.5f
                       && Facing(from);

        // a parry stays active for its whole window: every stake the guard covers in it is reflected
        // (stakes from another side need their own parry — press again once one has landed)
        if (covered && InParryWindow)
        {
            bool first = !parryLanded;
            parryLanded = true; // a landed parry frees you straight away
            bullet.Reflect();
            if (player != null) player.Heal(parryHeal);
            if (first)
            {
                Flash(parryFlashColor);
                if (juice != null) juice.Parry();   // slow-mo etc. once per parry, not per stake
                parrySound?.Play();
            }
            return;
        }

        if (covered && !BlockLocked)
        {
            Flash(blockFlashColor);
            PlayDirectional("Block", "Parry");
            if (juice != null) juice.Block();
            blockSound?.Play();
            player.TakeDamage(bullet.damage * chipDamage);
        }
        else
        {
            stakeHitSound?.Play();
            player.TakeDamage(bullet.damage);
            HitPop();
            HitFromDirection(from);
        }

        Destroy(bullet.gameObject);
    }

    public Vector3 Center => body != null ? body.bounds.center : transform.position;

    /// <summary>Can an attack coming from <paramref name="toward"/> (a direction from us) be blocked or parried at all?</summary>
    public bool CanGuard(Vector3 toward) => Facing(toward);

    /// <summary>
    /// An attack from <paramref name="attacker"/> (world position) just hit us. One from outside the guard
    /// flashes on the threat ring, pointing at it.
    /// </summary>
    public void HitFrom(Vector3 attacker) => HitFromDirection(attacker - Center);

    void HitFromDirection(Vector3 toward)
    {
        if (threats != null && !Facing(toward)) threats.Flash(toward);
    }

    /// <summary>Is <paramref name="toward"/> (flat) within <see cref="guardFacing"/> of where we're facing? The guard only covers the front.</summary>
    bool Facing(Vector3 toward)
    {
        toward.y = 0f;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (toward.sqrMagnitude < 0.0001f || forward.sqrMagnitude < 0.0001f) return true;   // right on top of us / looking straight down
        return Vector3.Angle(forward, toward) <= guardFacing;
    }

    /// <summary>Ring angle of an offset from our centre, seen face-on: up = head, right = our right.</summary>
    float BodyAngle(Vector3 offset)
    {
        float x = Vector3.Dot(offset, transform.right);
        float y = Vector3.Dot(offset, Vector3.up);
        return Mathf.Atan2(x, y) * Mathf.Rad2Deg;
    }

    // ══════════════════════════════════════════════════════════════════
    //  UI
    // ══════════════════════════════════════════════════════════════════
    void UpdateGuardVisuals()
    {
        if (guardArc == null) return;

        PlaceArc(guardArc, guardAngle, blockArc);
        guardArc.color = Time.time < flashEndTime ? flashColor
                       : BlockLocked ? lockedColor
                       : guardColor;

        // pop on block / parry / hit: quick swell that settles back
        float t = (Time.time - popStartTime) / Mathf.Max(popTime, 0.0001f);
        float pop = t >= 0f && t < 1f ? popScale * (1f - t) * (1f - t) : 0f;
        guardArc.rectTransform.localScale = Vector3.one * (1f + pop);
    }

    /// <summary>Unblocked stake or kick landed: red pop on the guard arc.</summary>
    public void HitPop() => Flash(hitFlashColor);

    void UpdateIncomingIndicators()
    {
        if (incomingTemplate == null) return;

        int used = 0;
        foreach (Bullet bullet in Bullet.Active)
        {
            if (bullet.reflected || bullet.Speed < 0.01f) continue;

            Vector3 dir = bullet.velocity / bullet.Speed;
            float along = Vector3.Dot(Center - bullet.transform.position, dir);
            if (along <= 0f) continue; // already flew past
            if (!Facing(-dir)) continue;  // from outside the guard: the threat ring shows it instead

            if (used == incomingPool.Count)
                incomingPool.Add(Instantiate(incomingTemplate, incomingTemplate.transform.parent));

            // Point where the shot is heading on us: the closest pass of its path to our centre
            Vector3 passPoint = bullet.transform.position + dir * along;
            Image arc = incomingPool[used++];
            arc.gameObject.SetActive(true);

            // build up as it closes in: grow, thicken, darken; flash inside the parry window
            float timeToImpact = along / bullet.Speed;
            float k = 1f - Mathf.Clamp01(timeToImpact / warnTime);
            k *= k;   // most of the build-up happens near the end
            bool cue = timeToImpact <= parryWindow;
            float width = Mathf.Lerp(incomingArc * 0.7f, incomingArc * 1.15f, k);
            PlaceArc(arc, BodyAngle(passPoint - Center), width);
            Color c = cue ? parryCueColor : Color.Lerp(incomingFar, incomingNear, k);
            float scale = cue ? parryCueScale : Mathf.Lerp(incomingScaleFar, incomingScaleNear, k);
            StyleArc(arc, c, scale, cue ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, k)));
        }

        // kicks: an orange warning at the bottom of the ring for the whole wind-up, pulsing steadily
        // (no countdown: when to parry has to be read off the kick itself)
        foreach (Shooter s in Shooter.Kicking)
        {
            if (s == null || !Facing(s.transform.position - Center)) continue;   // behind us: the threat ring
            if (used == incomingPool.Count)
                incomingPool.Add(Instantiate(incomingTemplate, incomingTemplate.transform.parent));
            Image arc = incomingPool[used++];
            arc.gameObject.SetActive(true);
            PlaceArc(arc, 180f, kickArc);
            Color c = kickColor;
            c.a = kickColor.a * Mathf.Lerp(0.55f, 1f, Mathf.PingPong(Time.time * kickPulseSpeed, 1f));
            // swells to full size early in the wind-up (no countdown to the impact itself)
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s.KickElapsed / Mathf.Max(kickGrowTime, 0.0001f)));
            StyleArc(arc, c, Mathf.Lerp(incomingScaleFar, incomingScaleNear, k), k);
        }

        for (int i = used; i < incomingPool.Count; i++)
            incomingPool[i].gameObject.SetActive(false);
    }

    /// <summary>Centre a radial-filled ring image (origin Top, clockwise) on the given angle.</summary>
    static void PlaceArc(Image arc, float angle, float width)
    {
        arc.fillAmount = width / 360f;
        arc.rectTransform.localEulerAngles = new Vector3(0f, 0f, -(angle - width * 0.5f));
    }

    /// <summary>Colour + size of an incoming marker; its Inner/Outer child rings fade in with
    /// <paramref name="thick"/> to make the band thicker.</summary>
    static void StyleArc(Image arc, Color color, float scale, float thick)
    {
        arc.color = color;
        arc.rectTransform.localScale = Vector3.one * scale;
        foreach (Transform child in arc.transform)
        {
            if (!child.TryGetComponent(out Image band)) continue;
            band.fillAmount = arc.fillAmount;
            Color c = color;
            c.a *= thick;
            band.color = c;
        }
    }

    void Flash(Color color)
    {
        flashColor = color;
        flashEndTime = Time.time + flashTime;
        popStartTime = Time.time;
    }
}
