using System;
using System.Collections.Generic;
using UnityEngine;

namespace JunaidIRF.SyncedInteractions
{
    /// <summary>Where the slots of an interaction are measured from.</summary>
    public enum AnchorMode
    {
        /// <summary>A role's character, where it stands when the interaction starts (usually the one that stays put).</summary>
        Role,
        /// <summary>A pose given when it's played (<see cref="InteractionOptions.anchor"/>), e.g. a bench or a doorway.</summary>
        World,
    }

    /// <summary>How the characters get to their slots before the animations start.</summary>
    public enum AlignMode
    {
        /// <summary>Straight there, the same frame.</summary>
        Warp,
        /// <summary>A short eased slide and turn over <see cref="InteractionAsset.alignTime"/>.</summary>
        Glide,
        /// <summary>They start where they are (the slots only set nothing).</summary>
        None,
    }

    /// <summary>
    /// One synced interaction: an animation clip per role, where each role stands relative to an anchor, and
    /// timed events. Played with <see cref="Interaction.Play(InteractionAsset, InteractionCast, InteractionOptions)"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Synced Interactions/Interaction", fileName = "Interaction", order = 300)]
    public class InteractionAsset : ScriptableObject
    {
        [Tooltip("The characters taking part. Two is the usual (Initiator, Receiver); any number works")]
        public List<InteractionRole> roles = new List<InteractionRole>
        {
            new InteractionRole("Initiator") { offset = new Vector3(0f, 0f, 0.8f), yaw = 180f },
            new InteractionRole("Receiver"),
        };

        [Header("Placement")]
        public AnchorMode anchor = AnchorMode.Role;
        [Tooltip("With Anchor: Role, the role the slots are measured from (it stays where it is)")]
        public string anchorRole = "Receiver";
        public AlignMode align = AlignMode.Glide;
        [Min(0f)] public float alignTime = 0.25f;
        [Tooltip("Slots are dropped onto the ground below them (a raycast), so nobody floats or sinks")]
        public bool snapToGround = true;
        public LayerMask groundMask = ~0;

        [Header("Timing")]
        [Tooltip("How long it plays. 0 = the longest role's clip")]
        [Min(0f)] public float duration;
        [Tooltip("Cancel() is allowed (gameplay can stop it). Off: only a participant being destroyed or disabled stops it")]
        public bool cancellable = true;

        [Header("Events")]
        public List<InteractionEvent> events = new List<InteractionEvent>();

        /// <summary>How long it plays: <see cref="duration"/>, or the longest clip.</summary>
        public float Length
        {
            get
            {
                if (duration > 0f) return duration;
                float longest = 0f;
                foreach (var r in roles) if (r != null && r.clip != null) longest = Mathf.Max(longest, r.clip.length);
                return longest;
            }
        }

        /// <summary>Index of the role called <paramref name="name"/> (case-insensitive), -1 if there's none.</summary>
        public int RoleIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < roles.Count; i++)
                if (roles[i] != null && string.Equals(roles[i].name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public InteractionRole Role(string name)
        {
            int i = RoleIndex(name);
            return i >= 0 ? roles[i] : null;
        }

        /// <summary>Problems that would stop it playing or make it play wrong (empty when it's fine).</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (roles.Count == 0) problems.Add("It has no roles.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < roles.Count; i++)
            {
                var r = roles[i];
                if (r == null) { problems.Add($"Role {i + 1} is empty."); continue; }
                if (string.IsNullOrWhiteSpace(r.name)) problems.Add($"Role {i + 1} has no name.");
                else if (!names.Add(r.name)) problems.Add($"Two roles are called '{r.name}'.");
                if (r.clip == null) problems.Add($"Role '{r.name}' has no animation clip.");
                if (r.releaseTime > 0f && r.releaseTime > Length) problems.Add($"Role '{r.name}' is released at {r.releaseTime:0.##} s, after the end ({Length:0.##} s).");
            }
            if (anchor == AnchorMode.Role && RoleIndex(anchorRole) < 0) problems.Add($"The anchor role '{anchorRole}' isn't one of the roles.");
            if (Length <= 0f) problems.Add("It has no length (no clips, and Duration is 0).");
            foreach (var e in events)
            {
                if (e == null) continue;
                if (e.time < 0f || e.time > Length + 1e-4f) problems.Add($"Event '{e.Label}' at {e.time:0.##} s is outside 0..{Length:0.##} s.");
                if (!string.IsNullOrEmpty(e.role) && RoleIndex(e.role) < 0) problems.Add($"Event '{e.Label}' is for role '{e.role}', which doesn't exist.");
                if (e.actions != null)
                    foreach (var a in e.actions) if (a == null) problems.Add($"Event '{e.Label}' has an empty action.");
            }
            return problems;
        }

        void OnValidate()
        {
            foreach (var e in events) if (e != null && string.IsNullOrEmpty(e.id)) e.id = Guid.NewGuid().ToString("N").Substring(0, 8);
        }
    }

    /// <summary>One character's part in an interaction.</summary>
    [Serializable]
    public class InteractionRole
    {
        public string name = "Role";
        [Tooltip("This role's animation (Humanoid clips retarget to any humanoid; Generic clips need matching bone names)")]
        public AnimationClip clip;

        [Header("Slot (relative to the anchor; the anchor faces +Z)")]
        public Vector3 offset;
        [Tooltip("Which way this role faces, in degrees around the anchor's up")]
        public float yaw;
        [Tooltip("Moved onto its slot before it starts. Off: stays where it is (the anchor role always does)")]
        public bool moveToSlot = true;
        [Tooltip("Turn to look at another role's bone while lining up (first-person cameras, eye contact). Empty = keep the slot's facing")]
        public string lookAtRole;
        [InteractionBone, Tooltip("The bone to look at on Look At Role (empty = its root, at head height)")]
        public string lookAtBone;

        [Header("Playback")]
        [Min(0f)] public float blendIn = 0.1f;
        [Min(0f)] public float blendOut = 0.2f;
        [Tooltip("The clip's root motion moves the character (else it stays exactly on its slot)")]
        public bool applyRootMotion;
        [Tooltip("This role is let go early, at this time (e.g. a victim handed to its ragdoll). 0 = at the end")]
        [Min(0f)] public float releaseTime;

        [Header("Editor preview")]
        [Tooltip("The character shown for this role when previewing / editing (any prefab with an Animator)")]
        public GameObject previewPrefab;

        public InteractionRole() { }
        public InteractionRole(string name) { this.name = name; }

        /// <summary>The slot as a pose in anchor space.</summary>
        public Pose Slot => new Pose(offset, Quaternion.Euler(0f, yaw, 0f));
    }

    /// <summary>Something that happens at a moment of an interaction: its actions run then.</summary>
    [Serializable]
    public class InteractionEvent
    {
        [Tooltip("What it's called (shown on markers, passed to EventFired listeners)")]
        public string name = "Event";
        [HideInInspector] public string id;
        [Min(0f)] public float time;
        [Tooltip("The role it happens to (its character is the actions' target). Empty = the first role")]
        public string role;
        [InteractionBone, Tooltip("Where on that character it happens (sounds, effects). Empty = the character's root")]
        public string bone;
        [SerializeReference] public List<InteractionAction> actions = new List<InteractionAction>();

        public string Label => string.IsNullOrEmpty(name) ? id : name;
    }

    /// <summary>A string holding a bone name: a bone picker in the inspector (humanoid bones by name, or any child's name).</summary>
    public class InteractionBoneAttribute : PropertyAttribute { }
}
