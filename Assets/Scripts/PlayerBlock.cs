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

    [Header("Animation")]
    public Animator anim;                   // Camera Head animator: Block<Dir> / Parry<Dir> states
    public string parryTag = "Parry";       // tag on the parry states — a missed parry locks blocking until it ends
    public GuardJuice juice;                // slow motion / chromatic aberration / shake / ring pulse

    [Header("UI")]
    public GameObject guardVisuals;         // ring base + guard arc, shown while guarding
    public Image guardArc;
    public Image incomingTemplate;          // red arc cloned per incoming bullet
    public float incomingArc = 40f;
    public float kickArc = 60f;             // the kick warning at the bottom of the ring
    public float warnTime = 1.5f;           // indicator reaches full strength this close to impact
    public Color guardColor = Color.white;
    public Color blockFlashColor = new Color(0.5f, 0.8f, 1f);
    public Color parryFlashColor = new Color(1f, 0.85f, 0.2f);
    public Color lockedColor = new Color(1f, 1f, 1f, 0.25f);   // guard while a missed parry recovers
    public float flashTime = 0.25f;

    [HideInInspector] public bool isGuarding;
    [HideInInspector] public float guardAngle;

    Vector2 guardStick = Vector2.up;
    float lastParryPress = float.NegativeInfinity;
    bool parryLanded;
    Collider body;
    float flashEndTime;
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
        lastParryPress = float.NegativeInfinity;
        parryLanded = false;
        flashEndTime = 0f;
        guardStick = Vector2.up;

        if (incomingTemplate != null) incomingTemplate.gameObject.SetActive(false);
        SetGuarding(false);
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
    /// A melee hit (the enemy kick) lands now from ring <paramref name="angle"/> (180 = below): only a parry
    /// pressed within <paramref name="window"/> s with the guard over that angle counts — blocking does
    /// nothing. True = parried.
    /// </summary>
    public bool TryParryMelee(float window, float angle)
    {
        bool covered = isGuarding && Mathf.Abs(Mathf.DeltaAngle(angle, guardAngle)) <= blockArc * 0.5f;
        if (!covered || Time.time - lastParryPress > window) return false;
        // like stakes: one parry covers every kick landing in its window
        if (!parryLanded)
        {
            Flash(parryFlashColor);
            if (juice != null) juice.Parry();
        }
        parryLanded = true;
        return true;
    }
    public void ResolveHit(Bullet bullet)
    {
        // Where on our capsule the bullet actually struck
        float hitAngle = BodyAngle(bullet.transform.position - Center);
        bool covered = isGuarding && Mathf.Abs(Mathf.DeltaAngle(hitAngle, guardAngle)) <= blockArc * 0.5f;

        // a parry stays active for its whole window: every stake the guard covers in it is reflected
        // (stakes from another side need their own parry — press again once one has landed)
        if (covered && InParryWindow)
        {
            bool first = !parryLanded;
            parryLanded = true; // a landed parry frees you straight away
            bullet.Reflect();
            if (first)
            {
                Flash(parryFlashColor);
                if (juice != null) juice.Parry();   // slow-mo etc. once per parry, not per stake
            }
            return;
        }

        if (covered && !BlockLocked)
        {
            Flash(blockFlashColor);
            PlayDirectional("Block", "Parry");
            if (juice != null) juice.Block();
            player.TakeDamage(bullet.damage * chipDamage);
        }
        else
        {
            player.TakeDamage(bullet.damage);
        }

        Destroy(bullet.gameObject);
    }

    Vector3 Center => body != null ? body.bounds.center : transform.position;

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
    }

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

            if (used == incomingPool.Count)
                incomingPool.Add(Instantiate(incomingTemplate, incomingTemplate.transform.parent));

            // Point where the shot is heading on us: the closest pass of its path to our centre
            Vector3 passPoint = bullet.transform.position + dir * along;
            Image arc = incomingPool[used++];
            arc.gameObject.SetActive(true);
            PlaceArc(arc, BodyAngle(passPoint - Center), incomingArc);

            // Fade in as it gets closer to impact
            float timeToImpact = along / bullet.Speed;
            Color c = incomingTemplate.color;
            c.a = Mathf.Lerp(1f, 0.25f, Mathf.Clamp01(timeToImpact / warnTime));
            arc.color = c;
        }

        // kicks: a red warning at the bottom of the ring for the whole wind-up, strongest at impact
        foreach (Shooter s in Shooter.Kicking)
        {
            if (s == null) continue;
            if (used == incomingPool.Count)
                incomingPool.Add(Instantiate(incomingTemplate, incomingTemplate.transform.parent));
            Image arc = incomingPool[used++];
            arc.gameObject.SetActive(true);
            PlaceArc(arc, 180f, kickArc);
            Color c = incomingTemplate.color;
            float k = 1f - Mathf.Clamp01(s.KickTimeToImpact / Mathf.Max(s.kickWindup, 0.0001f));
            c.a = Mathf.Lerp(0.45f, 1f, k);
            arc.color = c;
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

    void Flash(Color color)
    {
        flashColor = color;
        flashEndTime = Time.time + flashTime;
    }
}
