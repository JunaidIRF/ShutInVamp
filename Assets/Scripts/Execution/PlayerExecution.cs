using System.Collections;
using UnityEngine;

/// <summary>
/// First-person executions. Shows a prompt when a stunned enemy is close and in front; on the key
/// press it locks the player's controls, glides into place, then starts the player's and the
/// victim's animator states on the same frame and fires the <see cref="ExecutionData"/> events.
/// </summary>
public class PlayerExecution : MonoBehaviour
{
    /// <summary>True while an execution owns the camera, hands and controls.</summary>
    public static bool IsExecuting { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => IsExecuting = false;

    [Header("References")]
    public Player player;
    public PlayerMovement movement;
    public Animator anim;                   // Camera Head animator
    public Transform cam;                   // Player Camera
    public GuardJuice juice;                // camera shake
    public GameObject prompt;               // HUD "[E] Execute"

    [Header("Executions")]
    public ExecutionData[] executions;      // a random one that both animators support is played
    public KeyCode key = KeyCode.E;
    public float range = 1.6f;              // flat distance to the enemy
    public float maxAngle = 55f;            // enemy must be this close to where we look

    [Header("Sound")]
    public Sfx executionSound;              // when the execution animation starts
    public Sfx bloodSound;                  // an event with Blood on (from the bone)

    [Header("Hands layer")]
    public Transform[] hands;               // RHand / LHand (found under the animator if empty)
    public int worldHandsLayer = 0;         // "World" events put the hands here (Default: drawn by the main camera)

    Shooter candidate;
    // every object under the hands with its own layer (hands sit on the overlay layer, so does their fire)
    readonly System.Collections.Generic.List<(GameObject go, int layer)> handLayers = new System.Collections.Generic.List<(GameObject, int)>();

    void Start()
    {
        IsExecuting = false;
        candidate = null;
        SetPrompt(false);
    }

    void OnDisable()
    {
        StopAllCoroutines();
        RestoreHands();
        if (IsExecuting && movement != null) { movement.Hold(false); movement.SetPlayerControlsEnabled(true); }
        IsExecuting = false;
        SetPrompt(false);
    }

    void Update()
    {
        if (IsExecuting) return;

        candidate = FindTarget();
        SetPrompt(candidate != null);

        if (candidate != null && Input.GetKeyDown(key) && !player.isBusy)
            TryExecute();
    }

    /// <summary>Execute the enemy in front of us, if there is one that can be executed.</summary>
    public bool TryExecute()
    {
        if (IsExecuting) return false;
        Shooter victim = candidate != null ? candidate : FindTarget();
        if (victim == null || victim.anim == null) return false;

        ExecutionData data = PickExecution(victim.anim);
        if (data == null) return false;

        StartCoroutine(Run(victim, data));
        return true;
    }

    ExecutionData PickExecution(Animator victimAnim)
    {
        if (executions == null || executions.Length == 0) return null;
        int start = Random.Range(0, executions.Length);
        for (int i = 0; i < executions.Length; i++)
        {
            ExecutionData d = executions[(start + i) % executions.Length];
            int hash = Animator.StringToHash(d != null ? d.stateName : "");
            if (d != null && anim.HasState(0, hash) && victimAnim.HasState(0, hash)) return d;
        }
        return null;
    }

    Shooter FindTarget()
    {
        if (cam == null) return null;
        Vector3 look = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        Shooter best = null;
        float bestDist = range;

        foreach (Shooter s in FindObjectsByType<Shooter>(FindObjectsInactive.Exclude))
        {
            if (!s.CanBeExecuted) continue;
            Vector3 to = s.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > bestDist || Vector3.Angle(look, to) > maxAngle) continue;
            best = s;
            bestDist = dist;
        }
        return best;
    }

    IEnumerator Run(Shooter victim, ExecutionData data)
    {
        IsExecuting = true;
        SetPrompt(false);
        movement.SetPlayerControlsEnabled(false);
        movement.Hold(true);   // mid-jump / sliding / crouched: no falling, standing, like it was animated
        victim.BeginExecution();
        if (data.worldHandsAtStart) SetHandsWorld();

        Animator victimAnim = victim.anim;
        Transform bone = FindDeep(victimAnim.transform, data.focusBone);
        if (bone == null) bone = victim.transform;

        // 1. glide into place, turning to look at the focus bone
        Vector3 startPos = transform.position;
        Vector3 startLook = cam.forward;
        Vector3 goal = victim.transform.TransformPoint(data.playerOffset);
        goal.y = GroundY(goal, startPos.y, victim.transform);   // on the ground, not mid-jump (as it was animated)
        for (float t = 0f; t < data.alignTime; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / data.alignTime);
            movement.Teleport(Vector3.Lerp(startPos, goal, k), Vector3.Slerp(startLook, (bone.position - cam.position).normalized, k));
            yield return null;
        }
        movement.Teleport(goal, (bone.position - cam.position).normalized);

        // 2. both states start on the same frame with no blend, so they advance in lockstep
        anim.Play(data.stateName, 0, 0f);
        victimAnim.Play(data.stateName, 0, 0f);
        executionSound?.Play();

        // 3. timed events on the same clock the animators use (scaled time)
        var events = data.events != null ? (ExecutionData.Event[])data.events.Clone() : new ExecutionData.Event[0];
        System.Array.Sort(events, (a, b) => a.time.CompareTo(b.time));
        int next = 0;
        bool killed = false;
        float time = 0f;
        while (time < data.duration)
        {
            yield return null;
            time += Time.deltaTime;

            while (next < events.Length && time >= events[next].time)
            {
                ExecutionData.Event e = events[next++];
                Transform at = string.IsNullOrEmpty(e.bone) ? bone : FindDeep(victimAnim.transform, e.bone);
                Fire(e, victim, at != null ? at : bone);
            }

            if (!killed && time >= data.killTime)
            {
                killed = true;
                Kill(victim, data, bone);
            }
        }
        if (!killed) Kill(victim, data, bone);

        // the player state blends back to Idle on its own (exit-time transition)
        RestoreHands();
        IsExecuting = false;
        movement.Hold(false);
        movement.SetPlayerControlsEnabled(true);
    }

    /// <summary>
    /// Where our feet go at <paramref name="at"/>: the ground under it (what we stand on: our ground layers,
    /// enemies and ourselves aside), so a standing start is unchanged and a jump or slide ends up there too.
    /// No ground found: the victim's feet.
    /// </summary>
    float GroundY(Vector3 at, float fromY, Transform victim)
    {
        float top = Mathf.Max(fromY, victim.position.y) + 1.5f;
        int mask = movement.groundMask & ~(1 << gameObject.layer) & ~(1 << victim.gameObject.layer);
        if (Physics.Raycast(new Vector3(at.x, top, at.z), Vector3.down, out RaycastHit hit, top - victim.position.y + 4f, mask, QueryTriggerInteraction.Ignore))
            return hit.point.y + FeetBelowPivot(transform);
        return FeetY(victim) + FeetBelowPivot(transform);
    }

    /// <summary>Height of the bottom of <paramref name="t"/>'s capsule (its feet).</summary>
    static float FeetY(Transform t) => t.position.y - FeetBelowPivot(t);

    /// <summary>How far below its pivot a character's feet are (the Animate stage uses the same rule).</summary>
    static float FeetBelowPivot(Transform t)
    {
        var cc = t.GetComponent<CharacterController>();
        if (cc != null) return (cc.height * 0.5f - cc.center.y) * t.lossyScale.y;
        var cap = t.GetComponent<CapsuleCollider>();
        if (cap != null) return (cap.height * 0.5f - cap.center.y) * t.lossyScale.y;
        return 0f;
    }

    void Kill(Shooter victim, ExecutionData data, Transform bone) =>
        victim.FinishExecution(bone.position, BiteDirection(bone), data.killBodySpeed, data.killHitSpeed, data.killLift, data.decapitateOnKill);

    void Fire(ExecutionData.Event e, Shooter victim, Transform bone)
    {
        if (e.blood)
        {
            victim.Bleed(bone.position, BiteDirection(bone));
            if (e.sever == Dismemberment.BodyPart.None) bloodSound?.PlayAt(bone.position);   // a cut plays its own
        }
        if (e.sever != Dismemberment.BodyPart.None)
        {
            Transform cut = victim.PartBone(e.sever);
            if (cut != null) victim.Sever(e.sever, BiteDirection(cut));   // flies off away from us
            else Debug.LogWarning($"Execution: the victim has no bone for '{e.sever}' (Dismemberment > Parts)");
        }
        if (e.shake > 0f && juice != null) juice.Shake(e.shake, e.shakeTime);
        if (e.heal > 0f && player != null) player.Heal(e.heal);
        if (e.hands == ExecutionData.HandsLayer.World) SetHandsWorld();
        else if (e.hands == ExecutionData.HandsLayer.Overlay) RestoreHands();
    }

    /// <summary>Hands (and their fire) onto the normal scene layer, so the victim's body can cover them.</summary>
    void SetHandsWorld()
    {
        if (handLayers.Count == 0)
        {
            if ((hands == null || hands.Length == 0) && anim != null)
                hands = new[] { anim.transform.Find("RHand"), anim.transform.Find("LHand") };
            if (hands == null) return;
            foreach (Transform h in hands)
                if (h != null)
                    foreach (Transform t in h.GetComponentsInChildren<Transform>(true))
                        handLayers.Add((t.gameObject, t.gameObject.layer));
        }
        foreach (var (go, _) in handLayers)
            if (go != null) go.layer = worldHandsLayer;
    }

    /// <summary>Hands back on the layers they had (the overlay). Safe to call any time.</summary>
    void RestoreHands()
    {
        foreach (var (go, layer) in handLayers)
            if (go != null) go.layer = layer;
        handLayers.Clear();
    }

    Vector3 BiteDirection(Transform bone) => (bone.position - cam.position).normalized;

    void SetPrompt(bool on)
    {
        if (prompt != null && prompt.activeSelf != on) prompt.SetActive(on);
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
