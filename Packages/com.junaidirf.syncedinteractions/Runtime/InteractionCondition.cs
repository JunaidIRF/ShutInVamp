using System;
using UnityEngine;

namespace JunaidIRF.SyncedInteractions
{
    /// <summary>
    /// A condition that must be met between an initiator and a target for an interaction to happen.
    /// Serialized by reference: subclass it for game-specific conditions (e.g. stunned, behind target, health threshold).
    /// </summary>
    [Serializable]
    public abstract class InteractionCondition
    {
        /// <summary>Returns true if the condition passes; false if it fails.</summary>
        public abstract bool Evaluate(InteractionActor initiator, InteractionActor target, InteractionAsset asset);

        /// <summary>Human-readable reason when the condition fails.</summary>
        public virtual string FailureReason => Spaced(GetType().Name.Replace("Condition", "")) + " condition failed";

        /// <summary>Name shown in the inspector's type menu.</summary>
        public virtual string MenuName => Spaced(GetType().Name.Replace("Condition", ""));

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
}
