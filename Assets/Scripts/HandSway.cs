using UnityEngine;

/// <summary>
/// Slight hand sway: the hands lag a little behind mouse look and movement, then settle. Both hands
/// swing together around the view (Camera Head space). Off while guarding, executing or with look
/// disabled. Applied after the Animator; if the Animator didn't rewrite a hand this frame, last frame's
/// sway is taken off first, so it never stacks up.
/// </summary>
[DefaultExecutionOrder(100)]   // after the guard / movement scripts
public class HandSway : MonoBehaviour
{
    public PlayerMovement movement;
    public PlayerBlock block;
    public Transform[] hands;               // RHand, LHand (children of Camera Head)

    [Header("Look sway")]
    public float lookAmount = 1.2f;         // degrees per unit of mouse movement
    public float lookMax = 3f;              // degrees
    public float lookShift = 0.012f;        // metres per unit of mouse movement
    public float lookShiftMax = 0.03f;

    [Header("Move sway")]
    public float moveShift = 0.004f;        // metres per m/s (opposite to the movement)
    public float moveShiftMax = 0.035f;
    public float moveRoll = 0.35f;          // degrees per m/s of strafing

    [Header("Feel")]
    public float smooth = 9f;               // how quickly the sway follows (higher = tighter)

    Vector3 swayPos, swayRot;               // current offset (Camera Head space) and rotation (deg)
    readonly Vector3[] lastPos = new Vector3[2];
    readonly Quaternion[] lastRot = new Quaternion[2];
    readonly Vector3[] lastWrittenPos = new Vector3[2];
    readonly Quaternion[] lastWrittenRot = new Quaternion[2];
    readonly bool[] written = new bool[2];

    void Start()
    {
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (block == null) block = GetComponent<PlayerBlock>();
        swayPos = Vector3.zero;
        swayRot = Vector3.zero;
        for (int i = 0; i < written.Length; i++) written[i] = false;
    }

    void OnDisable() => RemoveSway();

    void LateUpdate()
    {
        if (hands == null || movement == null) return;

        bool active = movement.LookEnabled && !PlayerExecution.IsExecuting && (block == null || !block.isGuarding);
        Vector3 wantPos = Vector3.zero, wantRot = Vector3.zero;
        if (active)
        {
            float mx = Input.GetAxis("Mouse X"), my = Input.GetAxis("Mouse Y");
            // look: hands trail the turn (turn right -> hands drift left and yaw back)
            wantRot = new Vector3(Mathf.Clamp(my * lookAmount, -lookMax, lookMax), Mathf.Clamp(-mx * lookAmount, -lookMax, lookMax), 0f);
            wantPos = new Vector3(Mathf.Clamp(-mx * lookShift, -lookShiftMax, lookShiftMax), Mathf.Clamp(-my * lookShift, -lookShiftMax, lookShiftMax), 0f);

            // move: hands lag against the movement, roll a little into strafes
            Transform head = movement.cameraHead;
            Vector3 v = head != null ? head.InverseTransformDirection(movement.velocity) : Vector3.zero;
            wantPos += Vector3.ClampMagnitude(-v * moveShift, moveShiftMax);
            wantRot.z += Mathf.Clamp(-v.x * moveRoll, -lookMax, lookMax);
        }
        float k = 1f - Mathf.Exp(-smooth * Time.unscaledDeltaTime);
        swayPos = Vector3.Lerp(swayPos, wantPos, k);
        swayRot = Vector3.Lerp(swayRot, wantRot, k);

        Quaternion q = Quaternion.Euler(swayRot);
        bool none = swayPos.sqrMagnitude < 1e-8f && swayRot.sqrMagnitude < 1e-4f;
        for (int i = 0; i < hands.Length && i < 2; i++)
        {
            Transform h = hands[i];
            if (h == null) continue;
            // the pose the Animator gave this frame (or ours from last frame, minus our sway)
            Vector3 basePos = h.localPosition;
            Quaternion baseRot = h.localRotation;
            if (written[i] && (h.localPosition - lastWrittenPos[i]).sqrMagnitude < 1e-10f && Quaternion.Angle(h.localRotation, lastWrittenRot[i]) < 0.001f)
            {
                basePos = lastPos[i];
                baseRot = lastRot[i];
            }
            if (none)
            {
                if (written[i]) { h.localPosition = basePos; h.localRotation = baseRot; }
                written[i] = false;
                continue;
            }
            // swing both hands together around the eye
            h.localPosition = q * basePos + swayPos;
            h.localRotation = q * baseRot;
            lastPos[i] = basePos; lastRot[i] = baseRot;
            lastWrittenPos[i] = h.localPosition; lastWrittenRot[i] = h.localRotation;
            written[i] = true;
        }
    }

    void RemoveSway()
    {
        if (hands == null) return;
        for (int i = 0; i < hands.Length && i < 2; i++)
        {
            Transform h = hands[i];
            if (h == null || !written[i]) continue;
            if ((h.localPosition - lastWrittenPos[i]).sqrMagnitude < 1e-10f) { h.localPosition = lastPos[i]; h.localRotation = lastRot[i]; }
            written[i] = false;
        }
        swayPos = Vector3.zero;
        swayRot = Vector3.zero;
    }
}
