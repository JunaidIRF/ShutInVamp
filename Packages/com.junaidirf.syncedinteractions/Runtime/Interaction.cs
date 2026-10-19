using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace JunaidIRF.SyncedInteractions
{
    /// <summary>Who plays which role: <c>new InteractionCast { { "Initiator", me }, { "Receiver", them } }</c>.</summary>
    public class InteractionCast : IEnumerable<KeyValuePair<string, InteractionActor>>
    {
        readonly Dictionary<string, InteractionActor> map = new Dictionary<string, InteractionActor>(StringComparer.OrdinalIgnoreCase);

        public void Add(string role, InteractionActor actor) => map[role] = actor;
        public InteractionActor this[string role]
        {
            get => map.TryGetValue(role, out var a) ? a : null;
            set => map[role] = value;
        }
        public int Count => map.Count;
        public IEnumerator<KeyValuePair<string, InteractionActor>> GetEnumerator() => map.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => map.GetEnumerator();

        /// <summary>The actors in the asset's role order (first actor = first role...).</summary>
        public static InteractionCast InOrder(InteractionAsset asset, params InteractionActor[] actors)
        {
            var cast = new InteractionCast();
            for (int i = 0; i < actors.Length && i < asset.roles.Count; i++) cast.Add(asset.roles[i].name, actors[i]);
            return cast;
        }
    }

    /// <summary>How a particular play goes (all optional).</summary>
    public class InteractionOptions
    {
        /// <summary>The anchor, for assets with Anchor: World (where the slots are measured from).</summary>
        public Pose? anchor;
        /// <summary>Playback speed (1 = as authored).</summary>
        public float speed = 1f;
        /// <summary>Ignores Time.timeScale (a pause menu won't stop it).</summary>
        public bool unscaledTime;
        /// <summary>Anything you want back on the handle (<see cref="InteractionHandle.UserData"/>).</summary>
        public object userData;
    }

    /// <summary>
    /// Plays interactions: <c>Interaction.Play(handshake, me, them)</c> lines the characters up, plays every
    /// role's clip on its character in sync, fires the events and gives the characters back at the end.
    /// </summary>
    public static class Interaction
    {
        static readonly List<InteractionHandle> playing = new List<InteractionHandle>();
        /// <summary>Every interaction lining up or playing right now.</summary>
        public static IReadOnlyList<InteractionHandle> Playing => playing;

        /// <summary>Any interaction started lining up / ended (finished or cancelled).</summary>
        public static event Action<InteractionHandle> AnyStarted, AnyEnded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            playing.Clear();
            AnyStarted = null;
            AnyEnded = null;
        }

        /// <summary>Plays <paramref name="asset"/> with the actors in its role order. Null if it can't (the reason is logged).</summary>
        public static InteractionHandle Play(InteractionAsset asset, params InteractionActor[] actors)
            => asset == null ? Fail("no asset") : Play(asset, InteractionCast.InOrder(asset, actors));

        /// <summary>Plays <paramref name="asset"/> with this cast. Null if it can't (the reason is logged).</summary>
        public static InteractionHandle Play(InteractionAsset asset, InteractionCast cast, InteractionOptions options = null)
        {
            if (!CanPlay(asset, cast, options, out string reason)) return Fail(reason, asset);
            var handle = new InteractionHandle(asset, cast, options ?? new InteractionOptions());
            playing.Add(handle);
            InteractionRunner.Ensure();
            handle.Begin();
            AnyStarted?.Invoke(handle);
            return handle;
        }

        /// <summary>Whether it could play with this cast right now, and if not, why.</summary>
        public static bool CanPlay(InteractionAsset asset, InteractionCast cast, InteractionOptions options, out string reason)
        {
            reason = null;
            if (asset == null) { reason = "no asset"; return false; }
            if (!Application.isPlaying) { reason = "interactions play in Play mode (use the asset's Edit / Preview in the editor)"; return false; }
            var problems = asset.Validate();
            if (problems.Count > 0) { reason = problems[0]; return false; }
            if (asset.anchor == AnchorMode.World && (options == null || !options.anchor.HasValue)) { reason = "its anchor is World, so it needs options.anchor"; return false; }
            if (cast == null) { reason = "no cast"; return false; }
            var used = new HashSet<InteractionActor>();
            foreach (var role in asset.roles)
            {
                var actor = cast[role.name];
                if (actor == null) { reason = $"nobody plays '{role.name}'"; return false; }
                if (!actor.isActiveAndEnabled) { reason = $"'{actor.name}' ({role.name}) isn't active"; return false; }
                if (actor.Busy) { reason = $"'{actor.name}' is already in '{actor.Current.Asset.name}'"; return false; }
                if (actor.Animator == null) { reason = $"'{actor.name}' has no Animator"; return false; }
                if (!used.Add(actor)) { reason = $"'{actor.name}' can't play two roles"; return false; }

                InteractionActor other = null;
                foreach (var otherRole in asset.roles)
                {
                    if (otherRole.name != role.name) { other = cast[otherRole.name]; if (other != null) break; }
                }
                if (actor.IsVetoed(other, asset, role.name, out string vetoReason))
                {
                    reason = $"'{actor.name}' vetoed: {vetoReason ?? "not allowed"}";
                    return false;
                }
            }
            return true;
        }

        public static bool CanPlay(InteractionAsset asset, params InteractionActor[] actors)
            => asset != null && CanPlay(asset, InteractionCast.InOrder(asset, actors), null, out _);

        /// <summary>The interaction <paramref name="actor"/> is in (null if none).</summary>
        public static InteractionHandle Of(InteractionActor actor) => actor != null ? actor.Current : null;

        /// <summary>Cancels everything playing (a scene change, a cutscene...). Ignores Cancellable.</summary>
        public static void CancelAll()
        {
            for (int i = playing.Count - 1; i >= 0; i--) playing[i].Stop(true, force: true);
        }

        /// <summary>Advances every interaction (the runner calls it each frame; tests can step it themselves).</summary>
        internal static void Tick(float deltaTime, float unscaledDeltaTime)
        {
            for (int i = playing.Count - 1; i >= 0; i--)
            {
                if (i >= playing.Count) continue;
                var h = playing[i];
                try { h.Tick(deltaTime, unscaledDeltaTime); }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    h.Stop(true, force: true);   // a failing interaction mustn't leave its characters stuck
                }
            }
        }

        internal static void Ended(InteractionHandle h)
        {
            playing.Remove(h);
            AnyEnded?.Invoke(h);
        }

        static InteractionHandle Fail(string reason, InteractionAsset asset = null)
        {
            Debug.LogWarning($"Synced Interactions: can't play {(asset != null ? "'" + asset.name + "'" : "")}: {reason}", asset);
            return null;
        }
    }
}

