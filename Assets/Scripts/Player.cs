using UnityEngine;
using UnityEngine.UI;

public class Player : MonoBehaviour
{
    public static Player Instance {  get; private set; }
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

    private void Start()
    {
        Instance = this;
        health = maxHealth;
        UpdateHealthBar();
    }

    private void Update()
    {
        stateInfo = anim.GetCurrentAnimatorStateInfo(0);
        // A committed parry swipe can't be cancelled by punching/inspecting (that would skip its recovery)
        isBusy = stateInfo.IsTag("Busy") || stateInfo.IsTag("Parry");
        bool isInspecting = stateInfo.IsTag("Inspect");

        if (Input.GetMouseButton(0) && !isBusy)
        {
            anim.SetTrigger(attackName + lastAttackCount);

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

    public void TakeDamage(float dmg)
    {
        health = Mathf.Max(health - dmg, 0f);

        // No death for now — refill so we can keep practicing
        if (health <= 0f)
            health = maxHealth;

        UpdateHealthBar();
    }

    void UpdateHealthBar()
    {
        if (healthFill == null) return;

        healthFill.fillAmount = health / maxHealth;
    }
}
