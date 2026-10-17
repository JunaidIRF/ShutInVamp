using UnityEngine;

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
        public bool blood;          // spray blood at the focus bone
        public float shake;         // camera shake in degrees (0 = none)
        public float shakeTime;
        public float heal;          // health given to the player
        public HandsLayer hands;    // switch the hands' render layer
    }

    [Tooltip("State name used in both the player's and the victim's animator controllers")]
    public string stateName = "ExecBite";
    public float duration = 1.6f;

    [Header("Alignment")]
    [Tooltip("How long the player glides into place before the animations start")]
    public float alignTime = 0.2f;
    [Tooltip("Where the player stands, in the victim's local space (victim faces +Z)")]
    public Vector3 playerOffset = new Vector3(-0.2f, 0f, 0.55f);
    [Tooltip("Bone on the victim the camera looks at and where blood comes from")]
    public string focusBone = "mixamorig:LeftShoulder";

    [Header("Hands")]
    [Tooltip("On: the hands start on the world layer (the victim's body can cover them) for the whole execution, without an event. Events with Hands set can still switch them. Always back to the overlay at the end.")]
    public bool worldHandsAtStart;

    [Header("Timing")]
    [Tooltip("When the victim dies and drops into its ragdoll")]
    public float killTime = 1.3f;
    public Event[] events;
}
