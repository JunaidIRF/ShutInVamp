using System;
using UnityEngine;

namespace JunaidIRF.SyncedInteractions
{
    /// <summary>
    /// Something an event does. Subclass it for your game (blood, damage, camera shake...): it shows up in the
    /// event's "Add action" menu by itself. Serialized by reference, so give it fields like any component.
    /// </summary>
    [Serializable]
    public abstract class InteractionAction
    {
        /// <summary>The event's moment has come.</summary>
        public abstract void Fire(InteractionContext context);

        /// <summary>The interaction is over (finished or cancelled): undo anything that shouldn't outlast it. Optional.</summary>
        public virtual void End(InteractionContext context, bool cancelled) { }

        /// <summary>Name in the inspector's menu (defaults to the class name, spaced out).</summary>
        public virtual string MenuName => Spaced(GetType().Name.Replace("Action", ""));

        internal static string Spaced(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1])) sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }

    /// <summary>Everything an action needs: the interaction, the event, and the character and bone it happens on.</summary>
    public readonly struct InteractionContext
    {
        public readonly InteractionHandle handle;
        public readonly InteractionEvent evt;
        /// <summary>The event's role's character (null if it's gone).</summary>
        public readonly InteractionActor actor;
        /// <summary>The event's bone on that character, or its root.</summary>
        public readonly Transform bone;

        public InteractionContext(InteractionHandle handle, InteractionEvent evt, InteractionActor actor, Transform bone)
        {
            this.handle = handle; this.evt = evt; this.actor = actor; this.bone = bone;
        }

        public InteractionAsset Asset => handle.Asset;
        /// <summary>Another role's character.</summary>
        public InteractionActor this[string role] => handle[role];
        /// <summary>Where the event happens in the world.</summary>
        public Vector3 Position => bone != null ? bone.position : actor != null ? actor.transform.position : Vector3.zero;
    }
}
