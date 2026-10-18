using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace JunaidIRF.SyncedInteractions
{
    /// <summary>
    /// One interaction playing. <c>yield return handle;</c> in a coroutine waits until it's over.
    /// </summary>
    public sealed class InteractionHandle : CustomYieldInstruction
    {
        public enum Phase { Aligning, Playing, Finished, Cancelled }

        public InteractionAsset Asset { get; }
        public InteractionOptions Options { get; }
        public Phase State { get; private set; }
        /// <summary>Seconds into the animations (0 while lining up).</summary>
        public float Time { get; private set; }
        public float Length { get; }
        public float NormalizedTime => Length > 0f ? Mathf.Clamp01(Time / Length) : 1f;
        public bool IsPlaying => State == Phase.Aligning || State == Phase.Playing;
        public object UserData { get; set; }
        public override bool keepWaiting => IsPlaying;

        /// <summary>The characters are in place and the animations start.</summary>
        public event Action<InteractionHandle> Started;
        /// <summary>An event's moment came (after its actions ran).</summary>
        public event Action<InteractionHandle, InteractionEvent> EventFired;
        /// <summary>Over: finished (false) or cancelled (true).</summary>
        public event Action<InteractionHandle, bool> Ended;

        /// <summary>The character playing <paramref name="role"/> (null if it's gone).</summary>
        public InteractionActor this[string role]
        {
            get
            {
                int i = Asset.RoleIndex(role);
                return i >= 0 ? parts[i].actor : null;
            }
        }

        /// <summary>The role <paramref name="actor"/> plays here (null if it isn't in it).</summary>
        public string RoleOf(InteractionActor actor)
        {
            foreach (var p in parts) if (p.actor == actor) return p.role.name;
            return null;
        }

        class Part
        {
            public InteractionRole role;
            public InteractionActor actor;
            public Animator animator;
            public IInteractionMover mover;
            public bool active, moving;
            public Vector3 fromPos, toPos;
            public Quaternion fromRot, toRot;
            public Vector3 fromLook;
            public bool rootMotionWas;
            public PlayableGraph graph;
            public AnimationClipPlayable clip;
            public AnimationPlayableOutput output;
            public float End(float length) => role.releaseTime > 0f ? Mathf.Min(role.releaseTime, length) : length;
        }

        readonly Part[] parts;
        readonly List<InteractionEvent> events;
        readonly List<(InteractionAction action, InteractionContext context)> fired = new List<(InteractionAction, InteractionContext)>();
        Pose anchor;
        float alignClock;
        int nextEvent;
        bool ending;

        internal InteractionHandle(InteractionAsset asset, InteractionCast cast, InteractionOptions options)
        {
            Asset = asset;
            Options = options;
            UserData = options.userData;
            Length = asset.Length;
            parts = new Part[asset.roles.Count];
            for (int i = 0; i < parts.Length; i++)
            {
                var actor = cast[asset.roles[i].name];
                parts[i] = new Part { role = asset.roles[i], actor = actor, animator = actor.Animator, mover = actor.Mover };
            }
            events = new List<InteractionEvent>();
            foreach (var e in asset.events) if (e != null) events.Add(e);
            events.Sort((a, b) => a.time.CompareTo(b.time));
        }

        // ── start: claim the characters, work out the slots ──
        internal void Begin()
        {
            State = Phase.Aligning;
            foreach (var p in parts)
            {
                p.actor.Current = this;
                p.actor.CurrentRole = p.role.name;
                p.active = true;
            }
            foreach (var p in parts) p.actor.NotifyStart(this, p.role.name);   // AI off, input off... before we move them
            foreach (var p in parts) p.mover.Begin();

            anchor = Options.anchor ?? AnchorPose();
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                Transform t = p.actor.transform;
                p.fromPos = t.position;
                p.fromRot = t.rotation;
                bool isAnchor = Asset.anchor == AnchorMode.Role && Asset.RoleIndex(Asset.anchorRole) == i;
                p.moving = Asset.align != AlignMode.None && p.role.moveToSlot && !isAnchor;
                p.toPos = p.fromPos;
                p.toRot = p.fromRot;
                if (!p.moving) continue;
                Pose slot = p.role.Slot;
                p.toPos = anchor.position + anchor.rotation * slot.position;
                p.toRot = anchor.rotation * slot.rotation;
                if (Asset.snapToGround) p.toPos.y = GroundY(p.toPos, p) + p.actor.FeetBelowPivot;
                Vector3? look = LookPoint(p);
                if (look.HasValue)
                {
                    Vector3 flat = look.Value - p.toPos; flat.y = 0f;
                    if (flat.sqrMagnitude > 0.0001f) p.toRot = Quaternion.LookRotation(flat, Vector3.up);
                    // looking straight ahead to begin with, as far away as the target
                    p.fromLook = p.fromPos + Vector3.up * 1.6f + p.fromRot * Vector3.forward * Mathf.Max(0.5f, Vector3.Distance(p.fromPos, look.Value));
                }
            }

            if (Asset.align != AlignMode.Glide || Asset.alignTime <= 0f) { Place(1f); StartPlayback(); }
        }

        Pose AnchorPose()
        {
            int i = Asset.RoleIndex(Asset.anchorRole);
            Transform t = parts[Mathf.Max(0, i)].actor.transform;
            Vector3 fwd = t.forward; fwd.y = 0f;
            return new Pose(t.position, fwd.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(fwd, Vector3.up) : Quaternion.identity);
        }

        /// <summary>The ground under <paramref name="at"/> (not the characters themselves); the slot's own height if there's none.</summary>
        float GroundY(Vector3 at, Part self)
        {
            float top = Mathf.Max(at.y, anchor.position.y) + 1.5f;
            var hits = Physics.RaycastAll(new Vector3(at.x, top, at.z), Vector3.down, 4f + 1.5f, Asset.groundMask, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
            {
                bool character = false;
                foreach (var p in parts) if (h.transform.IsChildOf(p.actor.transform) || (p.animator != null && h.transform.IsChildOf(p.animator.transform))) { character = true; break; }
                if (!character && h.point.y > best) best = h.point.y;
            }
            return float.IsNegativeInfinity(best) ? at.y - self.actor.FeetBelowPivot : best;
        }

        Vector3? LookPoint(Part p)
        {
            if (string.IsNullOrEmpty(p.role.lookAtRole)) return null;
            int i = Asset.RoleIndex(p.role.lookAtRole);
            if (i < 0 || parts[i].actor == null) return null;
            var target = parts[i].actor;
            if (string.IsNullOrEmpty(p.role.lookAtBone)) return target.transform.position + Vector3.up * 1.6f;
            var bone = target.Bone(p.role.lookAtBone);
            return bone != null ? bone.position : target.transform.position + Vector3.up * 1.6f;
        }

        void Place(float k)
        {
            foreach (var p in parts)
            {
                if (!p.moving || !p.active || p.actor == null) continue;
                Vector3? look = LookPoint(p);
                Vector3? lookNow = look.HasValue ? Vector3.Lerp(p.fromLook, look.Value, k) : (Vector3?)null;
                p.mover.Place(new InteractionPlacement(Vector3.Lerp(p.fromPos, p.toPos, k), Quaternion.Slerp(p.fromRot, p.toRot, k), lookNow));
            }
        }

        // ── the animations: one graph per character, its clip laid over the character's own animation ──
        void StartPlayback()
        {
            State = Phase.Playing;
            Time = 0f;
            foreach (var p in parts)
            {
                if (!p.active || p.animator == null) continue;
                p.rootMotionWas = p.animator.applyRootMotion;
                p.animator.applyRootMotion = p.role.applyRootMotion;
                p.graph = PlayableGraph.Create($"{Asset.name} ({p.role.name})");
                p.graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                p.clip = AnimationClipPlayable.Create(p.graph, p.role.clip);
                p.clip.SetSpeed(0);                       // time comes from the shared clock only (SetTime below)
                p.clip.SetApplyFootIK(p.animator.isHuman);
                p.output = AnimationPlayableOutput.Create(p.graph, "Synced Interaction", p.animator);
                p.output.SetSourcePlayable(p.clip);
                p.graph.Play();
            }
            Apply();
            Started?.Invoke(this);
            FireEvents();   // anything at 0
        }

        internal void Tick(float deltaTime, float unscaledDeltaTime)
        {
            if (!IsPlaying) return;
            float dt = (Options.unscaledTime ? unscaledDeltaTime : deltaTime) * Mathf.Max(0f, Options.speed);
            foreach (var p in parts) if (p.active && p.actor == null) { ActorLost(null); return; }

            if (State == Phase.Aligning)
            {
                alignClock += dt;
                float k = Mathf.Clamp01(alignClock / Asset.alignTime);
                Place(Mathf.SmoothStep(0f, 1f, k));
                if (k >= 1f) StartPlayback();
                return;
            }

            Time = Mathf.Min(Time + dt, Length);
            Apply();
            FireEvents();
            if (!IsPlaying) return;   // an action stopped it
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].active && parts[i].role.releaseTime > 0f && Time >= parts[i].role.releaseTime) EndPart(parts[i], false);
            if (Time >= Length) Stop(false, force: true);
        }

        /// <summary>Every character's clip at the shared time, each weighted by its blend in / out.</summary>
        void Apply()
        {
            foreach (var p in parts)
            {
                if (!p.active || !p.graph.IsValid()) continue;
                float end = p.End(Length);
                float w = p.role.blendIn > 0f ? Mathf.Clamp01(Time / p.role.blendIn) : 1f;
                if (p.role.blendOut > 0f) w *= Mathf.Clamp01((end - Time) / p.role.blendOut);
                p.clip.SetTime(Mathf.Min(Time, p.role.clip.length));
                p.output.SetWeight(w);
            }
        }

        void FireEvents()
        {
            while (nextEvent < events.Count && Time >= events[nextEvent].time)
            {
                var e = events[nextEvent++];
                int ri = string.IsNullOrEmpty(e.role) ? 0 : Asset.RoleIndex(e.role);
                InteractionActor actor = ri >= 0 ? parts[ri].actor : null;
                Transform bone = actor != null ? actor.Bone(e.bone) : null;
                if (actor != null && bone == null) bone = actor.transform;
                var context = new InteractionContext(this, e, actor, bone);
                if (e.actions != null)
                    foreach (var a in e.actions)
                    {
                        if (a == null) continue;
                        fired.Add((a, context));
                        try { a.Fire(context); }
                        catch (Exception ex) { Debug.LogException(ex, Asset); }
                        if (!IsPlaying) return;
                    }
                EventFired?.Invoke(this, e);
                if (!IsPlaying) return;
            }
        }

        // ── the end ──

        /// <summary>Stops it now (if the asset allows it). The characters blend straight back to their own animation.</summary>
        public void Cancel()
        {
            if (!Asset.cancellable) { Debug.LogWarning($"Synced Interactions: '{Asset.name}' can't be cancelled (Cancellable is off).", Asset); return; }
            Stop(true, force: true);
        }

        /// <summary>Lets one role go now (the rest carry on): it gets its character's own animation and motion back.</summary>
        public void Release(string role)
        {
            int i = Asset.RoleIndex(role);
            if (i >= 0 && parts[i].active) EndPart(parts[i], false);
        }

        internal void ActorLost(InteractionActor actor)
        {
            if (!IsPlaying) return;
            // destroyed ones: nothing left to give back. A disabled one is still there and is ended normally below
            foreach (var p in parts)
                if (p.active && p.actor == null) { DestroyGraph(p); p.active = false; }
            Stop(true, force: true);
        }

        internal void Stop(bool cancelled, bool force = true)
        {
            if (ending || !IsPlaying) return;
            ending = true;
            State = cancelled ? Phase.Cancelled : Phase.Finished;
            foreach (var p in parts) if (p.active) EndPart(p, cancelled);
            foreach (var (action, context) in fired)
            {
                try { action.End(context, cancelled); }
                catch (Exception ex) { Debug.LogException(ex, Asset); }
            }
            fired.Clear();
            Interaction.Ended(this);
            Ended?.Invoke(this, cancelled);
        }

        void EndPart(Part p, bool cancelled)
        {
            if (!p.active) return;
            p.active = false;
            DestroyGraph(p);
            if (p.actor == null) return;
            if (p.animator != null) p.animator.applyRootMotion = p.rootMotionWas;
            try { p.mover.End(); } catch (Exception ex) { Debug.LogException(ex, p.actor); }
            if (p.actor.Current == this) { p.actor.Current = null; p.actor.CurrentRole = null; }
            p.actor.NotifyEnd(this, p.role.name, cancelled);
        }

        static void DestroyGraph(Part p)
        {
            if (p.graph.IsValid()) p.graph.Destroy();
        }

        public override string ToString() => $"{(Asset != null ? Asset.name : "?")} {State} {Time:0.00}/{Length:0.00}s";
    }
}
