using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace JunaidIRF.SyncedInteractions
{
    /// <summary>
    /// Implement on any component of a character to hear about its interactions: stop the AI, drop a weapon,
    /// switch off input... Found on the actor's GameObject and its Animator's GameObject.
    /// </summary>
    public interface IInteractionParticipant
    {
        /// <summary>It's taking part, as <paramref name="role"/> (before it's moved onto its slot).</summary>
        void OnInteractionStart(InteractionHandle handle, string role);
        /// <summary>Its part is over: the interaction finished, was cancelled, or this role was let go early.</summary>
        void OnInteractionEnd(InteractionHandle handle, string role, bool cancelled);
    }

    /// <summary>
    /// Implement on any component of a character to veto an interaction before it starts
    /// (e.g. enemy is not stunned, character is dead, busy with an action...).
    /// Found on the actor's GameObject and its Animator's GameObject.
    /// </summary>
    public interface IInteractionVeto
    {
        /// <summary>Whether this character allows taking part in <paramref name="asset"/> with <paramref name="partner"/> as <paramref name="role"/>.</summary>
        bool CanInteract(InteractionActor partner, InteractionAsset asset, string role, out string reason);
    }

    /// <summary>
    /// A character that can take part in interactions. Needs an Animator (any controller, Humanoid or Generic).
    /// How it's moved onto its slot is picked from what it has (see <see cref="IInteractionMover"/>).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Synced Interactions/Interaction Actor")]
    public class InteractionActor : MonoBehaviour
    {
        [Serializable]
        public class NamedEvent
        {
            public string name;
            public UnityEvent onFired = new UnityEvent();
        }

        [Tooltip("The Animator the interactions play on (empty = the first one on this object or below)")]
        public Animator animator;
        [Tooltip("Can be picked by interaction queries and triggers (off: only by code)")]
        public bool available = true;

        [Header("Events")]
        public UnityEvent onInteractionStart = new UnityEvent();
        public UnityEvent onInteractionEnd = new UnityEvent();
        [Tooltip("Fired by an event's 'Invoke Actor Event' action with the same name")]
        public List<NamedEvent> namedEvents = new List<NamedEvent>();

        static readonly List<InteractionActor> all = new List<InteractionActor>();
        /// <summary>Every enabled actor.</summary>
        public static IReadOnlyList<InteractionActor> All
        {
            get
            {
                all.RemoveAll(a => a == null);
                if (!Application.isPlaying && all.Count == 0)
                    all.AddRange(FindObjectsByType<InteractionActor>(FindObjectsInactive.Exclude));
                return all;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => all.Clear();

        /// <summary>The interaction it's in right now (null if none).</summary>
        public InteractionHandle Current { get; internal set; }
        /// <summary>The role it plays in <see cref="Current"/>.</summary>
        public string CurrentRole { get; internal set; }
        public bool Busy => Current != null;

        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        IInteractionMover mover;

        public Animator Animator
        {
            get
            {
                if (animator == null) animator = GetComponentInChildren<Animator>(true);
                return animator;
            }
        }

        public IInteractionMover Mover
        {
            get
            {
                if (mover == null) mover = InteractionMovers.For(this);
                return mover;
            }
        }

        void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        void OnDisable()
        {
            all.Remove(this);
            if (Current != null) Current.ActorLost(this);
        }

        /// <summary>
        /// A bone by name: a humanoid bone ("Head", "RightHand", any HumanBodyBones name) on a humanoid, or the
        /// first transform under the Animator with that name (or that path). The root if empty; null if not found.
        /// </summary>
        public Transform Bone(string name)
        {
            if (string.IsNullOrEmpty(name)) return Animator != null ? Animator.transform : transform;
            if (bones.TryGetValue(name, out var cached) && cached != null) return cached;
            Transform found = null;
            var a = Animator;
            if (a != null && a.isHuman && Enum.TryParse(name, true, out HumanBodyBones hb) && hb != HumanBodyBones.LastBone)
                found = a.GetBoneTransform(hb);
            Transform root = a != null ? a.transform : transform;
            if (found == null && name.Contains("/")) found = root.Find(name);
            if (found == null) found = FindDeep(root, name);
            bones[name] = found;
            return found;
        }

        /// <summary>How far below this object's pivot its feet are (a CharacterController / capsule's bottom; 0 if it has neither).</summary>
        public float FeetBelowPivot
        {
            get
            {
                var cc = GetComponent<CharacterController>();
                if (cc != null) return (cc.height * 0.5f - cc.center.y) * transform.lossyScale.y;
                var cap = GetComponent<CapsuleCollider>();
                if (cap != null) return (cap.height * 0.5f - cap.center.y) * transform.lossyScale.y;
                return 0f;
            }
        }

        internal void FireNamed(string eventName)
        {
            foreach (var e in namedEvents)
                if (e != null && string.Equals(e.name, eventName, StringComparison.OrdinalIgnoreCase)) e.onFired?.Invoke();
        }

        internal void NotifyStart(InteractionHandle handle, string role)
        {
            foreach (var p in Participants()) p.OnInteractionStart(handle, role);
            onInteractionStart?.Invoke();
        }

        internal void NotifyEnd(InteractionHandle handle, string role, bool cancelled)
        {
            foreach (var p in Participants())
            {
                try { p.OnInteractionEnd(handle, role, cancelled); }
                catch (Exception ex) { Debug.LogException(ex, this); }   // one listener failing mustn't leave the rest stuck
            }
            onInteractionEnd?.Invoke();
        }

        List<IInteractionParticipant> Participants()
        {
            var list = new List<IInteractionParticipant>();
            GetComponents(list);
            var a = Animator;
            if (a != null && a.gameObject != gameObject)
                foreach (var p in a.GetComponents<IInteractionParticipant>()) if (!list.Contains(p)) list.Add(p);
            return list;
        }

        /// <summary>Checks whether any IInteractionVeto component on this actor vetoes taking part.</summary>
        public bool IsVetoed(InteractionActor partner, InteractionAsset asset, string role, out string reason)
        {
            reason = null;
            var list = new List<IInteractionVeto>();
            GetComponents(list);
            var a = Animator;
            if (a != null && a.gameObject != gameObject)
                foreach (var v in a.GetComponents<IInteractionVeto>()) if (!list.Contains(v)) list.Add(v);
            foreach (var v in list)
            {
                if (!v.CanInteract(partner, asset, role, out reason))
                    return true;
            }
            return false;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
