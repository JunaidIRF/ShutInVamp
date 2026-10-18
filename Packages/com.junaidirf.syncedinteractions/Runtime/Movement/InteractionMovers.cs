using UnityEngine;
using UnityEngine.AI;

namespace JunaidIRF.SyncedInteractions
{
    /// <summary>
    /// Moves a character for an interaction: takes its motion over at the start (no walking, gravity or physics
    /// fighting the animation), places it while it lines up and plays, and gives it back at the end.
    /// Put a component implementing this on the actor to use your own (a first-person controller that also
    /// aims its camera at <see cref="InteractionPlacement.lookAt"/>); otherwise one is picked from what the
    /// character has: NavMeshAgent, CharacterController, Rigidbody, or just its Transform.
    /// </summary>
    public interface IInteractionMover
    {
        void Begin();
        void Place(InteractionPlacement placement);
        /// <summary>Motion back to normal, where it is now.</summary>
        void End();
    }

    /// <summary>Where a character goes: its position (feet) and facing, and optionally a point to look at.</summary>
    public struct InteractionPlacement
    {
        public Vector3 position;
        public Quaternion rotation;
        /// <summary>A point to aim at (cameras, heads), if the role looks at something.</summary>
        public Vector3? lookAt;

        public InteractionPlacement(Vector3 position, Quaternion rotation, Vector3? lookAt = null)
        {
            this.position = position; this.rotation = rotation; this.lookAt = lookAt;
        }
    }

    public static class InteractionMovers
    {
        /// <summary>The mover for an actor: its own IInteractionMover component if it has one, else a built-in one.</summary>
        public static IInteractionMover For(InteractionActor actor)
        {
            var custom = actor.GetComponent<IInteractionMover>();
            if (custom != null) return custom;
            var agent = actor.GetComponent<NavMeshAgent>();
            if (agent != null) return new NavMeshAgentMover(actor.transform, agent);
            var cc = actor.GetComponent<CharacterController>();
            if (cc != null) return new CharacterControllerMover(actor.transform, cc);
            var rb = actor.GetComponent<Rigidbody>();
            if (rb != null) return new RigidbodyMover(actor.transform, rb);
            return new TransformMover(actor.transform);
        }
    }

    /// <summary>Just sets the transform.</summary>
    public class TransformMover : IInteractionMover
    {
        protected readonly Transform t;
        public TransformMover(Transform t) { this.t = t; }
        public virtual void Begin() { }
        public virtual void Place(InteractionPlacement p) => t.SetPositionAndRotation(p.position, p.rotation);
        public virtual void End() { }
    }

    /// <summary>A CharacterController is switched off while placed (it overrides a moved transform), back on at the end.</summary>
    public class CharacterControllerMover : TransformMover
    {
        readonly CharacterController cc;
        bool wasEnabled;
        public CharacterControllerMover(Transform t, CharacterController cc) : base(t) { this.cc = cc; }
        public override void Begin() { wasEnabled = cc.enabled; cc.enabled = false; }
        public override void End() { if (cc != null) cc.enabled = wasEnabled; }
    }

    /// <summary>The agent stops steering while placed, and is put back on the NavMesh where it stands at the end.</summary>
    public class NavMeshAgentMover : TransformMover
    {
        readonly NavMeshAgent agent;
        bool wasEnabled, wasStopped, updatePosition, updateRotation;
        public NavMeshAgentMover(Transform t, NavMeshAgent agent) : base(t) { this.agent = agent; }

        public override void Begin()
        {
            wasEnabled = agent.enabled;
            updatePosition = agent.updatePosition; updateRotation = agent.updateRotation;
            if (agent.enabled && agent.isOnNavMesh) { wasStopped = agent.isStopped; agent.isStopped = true; agent.ResetPath(); }
            agent.updatePosition = false;
            agent.updateRotation = false;
        }

        public override void End()
        {
            if (agent == null) return;
            agent.updatePosition = updatePosition;
            agent.updateRotation = updateRotation;
            agent.enabled = wasEnabled;
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.Warp(t.position);   // its own idea of where it is caught up with where we left it
                agent.isStopped = wasStopped;
            }
        }
    }

    /// <summary>The body is made kinematic while placed (no physics shoving it), back as it was at the end.</summary>
    public class RigidbodyMover : TransformMover
    {
        readonly Rigidbody rb;
        bool wasKinematic;
        public RigidbodyMover(Transform t, Rigidbody rb) : base(t) { this.rb = rb; }
        public override void Begin() { wasKinematic = rb.isKinematic; rb.isKinematic = true; }
        public override void Place(InteractionPlacement p)
        {
            base.Place(p);
            rb.position = p.position;
            rb.rotation = p.rotation;
        }
        public override void End()
        {
            if (rb == null) return;
            rb.isKinematic = wasKinematic;
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }
    }
}
