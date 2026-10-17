using UnityEngine;

/// <summary>A string field holding a bone name, drawn as a Transform picker (drag the bone in).</summary>
public class BonePickerAttribute : PropertyAttribute { }

/// <summary>
/// One first-person execution: a synced pair of animator states plus timed events.
///
/// To add a new execution:
///   1. Make a player clip (animates RHand / LHand / CamParent under Camera Head) and a victim clip
///      (the enemy's bones), both <see cref="duration"/> long.
///   2. Add a state named <see cref="stateName"/> to BOTH controllers: Camera Head.controller (tag
///      "Busy", exit-time transition back to Idle) and the enemy's controller (no transitions needed).
///   3. Create one of these assets (Create > ShutInVamp > Execution) and add it to PlayerExecution.
/// Both states are started on the same frame with no blend, so they stay in sync.
/// </summary>
[CreateAssetMenu(menuName = "ShutInVamp/Execution", fileName = "Execution")]
public class ExecutionData : ScriptableObject
{
    /// <summary>Which layer the hands render on from this event on.</summary>
    public enum HandsLayer
    {
        Keep,       // no change
        World,      // normal scene layer: the victim's body can cover the hands (no drawing on top)
        Overlay     // back to the first-person overlay layer (always restored at the end anyway)
    }

    [System.Serializable]
    public struct Event
    {
        public float time;          // seconds from the start of the animations
        public bool blood;          // spray blood at this event's bone (or the focus bone)
        public float shake;         // camera shake in degrees (0 = none)
        public float shakeTime;
        public float heal;          // health given to the player
        public HandsLayer hands;    // switch the hands' render layer
        [BonePicker, Tooltip("Where this event's blood comes from. Empty = the focus bone")]
        public string bone;
    }

    [Tooltip("State name used in both the player's and the victim's animator controllers")]
    public string stateName = "ExecBite";
    public float duration = 1.6f;

    [Header("Alignment")]
    [Tooltip("How long the player glides into place before the animations start")]
    public float alignTime = 0.2f;
    [Tooltip("Where the player stands, in the victim's local space (victim faces +Z)")]
    public Vector3 playerOffset = new Vector3(-0.2f, 0f, 0.55f);
    [BonePicker, Tooltip("Bone on the victim the camera lines up on; also the default blood spot and the kill push point")]
    public string focusBone = "mixamorig:LeftShoulder";

    [Header("Hands")]
    [Tooltip("On: the hands start on the world layer (the victim's body can cover them) for the whole execution, without an event. Events with Hands set can still switch them. Always back to the overlay at the end.")]
    public bool worldHandsAtStart;

    [Header("Timing")]
    [Tooltip("When the victim dies and drops into its ragdoll")]
    public float killTime = 1.3f;

    [Header("Kill push (ragdoll)")]
    [Tooltip("Velocity change (m/s) on every body part, away from the player")]
    public float killBodySpeed = 1.05f;
    [Tooltip("Extra velocity change (m/s) on the body part nearest the focus bone")]
    public float killHitSpeed = 1.8f;
    [Tooltip("Upward velocity change (m/s) so it leaves the ground a little")]
    public float killLift = 0.36f;

    [Header("Events")]
    public Event[] events;
}
