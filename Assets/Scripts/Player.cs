using UnityEngine;
using UnityEngine.UI;

public class Player : MonoBehaviour
{
    public static Player Instance {  get; private set; }
    /// <summary>Health hit 0 (the wave director ends the run; without one we just refill).</summary>
    public static event System.Action Died;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Died = null;   // no domain reload: drop last session's listeners
    public string attackName = "Fist";
    public int lastAttack = 1;
    public Animator anim;
    [System.NonSerialized]
    public AnimatorStateInfo stateInfo;
    [HideInInspector]
    public bool isBusy;
    public int lastAttackCount = 0;

    [Header("Health")]
    public float maxHealth = 100;
    public float health = 100;
    public Image healthFill;            // HealthBar/Fill in HUD3D — vertical fill, 1 = full health
    public HealthBarFx healthBarFx;     // eased fill + white damage trail (drives healthFill when set)
    public HurtFeedback hurt;           // screen/camera feedback when we take damage

    [Header("Punch")]
    public Transform cam;               // Player Camera — punches hit what it looks at
    public GuardJuice juice;            // small shake when a punch lands
    public float punchDamage = 10f;
    public float punchRange = 1.7f;
    public float punchRadius = 0.35f;
    public float punchHitDelay = 0.1f;  // the fist clips' impact frame
    public float punchShake = 0.8f;     // degrees

    float pendingPunchTime = -1f;

    private void Start()
    {
        Instance = this;
        pendingPunchTime = -1f;
        health = maxHealth;
        UpdateHealthBar();
    }

    private void Update()
    {
        if (PlayerExecution.IsExecuting) { pendingPunchTime = -1f; return; }   // the execution owns the animator

        if (pendingPunchTime >= 0f && Time.time >= pendingPunchTime)
        {
            pendingPunchTime = -1f;
            PunchHit();
        }

        stateInfo = anim.GetCurrentAnimatorStateInfo(0);
        // A committed parry swipe can't be cancelled by punching/inspecting (that would skip its recovery)
        isBusy = stateInfo.IsTag("Busy") || stateInfo.IsTag("Parry");
        bool isInspecting = stateInfo.IsTag("Inspect");

        if (Input.GetMouseButton(0) && !isBusy)
        {
            anim.SetTrigger(attackName + lastAttackCount);
            pendingPunchTime = Time.time + punchHitDelay;

            if (lastAttackCount < lastAttack)
            {
                lastAttackCount++;
            }
            else
            {
                lastAttackCount = 0;
            }
        }

        if(Input.GetKeyDown(KeyCode.F) && !isBusy && !isInspecting)
        {
            anim.SetTrigger("Inspect");
        }
    }

    /// <summary>Hit whatever enemy is right in front of the camera.</summary>
    void PunchHit()
    {
        if (cam == null) return;
        int mask = ~(1 << gameObject.layer);
        if (!Physics.SphereCast(cam.position, punchRadius, cam.forward, out RaycastHit hit, punchRange, mask, QueryTriggerInteraction.Ignore))
            return;

        Shooter shooter = hit.collider.GetComponentInParent<Shooter>();
        if (shooter == null || shooter.IsDead) return;
        // hurts, but only parries stun (open executions); a killing slash cuts off the part it lands on.
        // A cast that starts inside the collider reports no point: any point on the aim line will do
        Vector3 point = hit.distance > 0f ? hit.point : cam.position;
        shooter.Hit(point, cam.forward, punchDamage, severOnKill: true);
        if (juice != null) juice.Shake(punchShake, 0.15f);
    }

    public void Heal(float amount)
    {
        if (amount <= 0f) return;
        health = Mathf.Min(health + amount, maxHealth);
        UpdateHealthBar();
    }

    public void TakeDamage(float dmg)
    {
        health = Mathf.Max(health - dmg, 0f);
        if (hurt != null && dmg > 0f) hurt.Hit(dmg);

        // dying is the wave director's business (run over); we refill either way
        if (health <= 0f)
        {
            health = maxHealth;
            Died?.Invoke();
        }

        UpdateHealthBar();
    }

    void UpdateHealthBar()
    {
        if (healthBarFx != null) healthBarFx.SetValue(health / maxHealth);
        else if (healthFill != null) healthFill.fillAmount = health / maxHealth;
    }
}
