using UnityEngine;

namespace JunaidIRF.SyncedInteractions
{
    /// <summary>
    /// An action that can show itself on the editor's authoring stage (a cut, a prop appearing...), so scrubbing
    /// the timeline past its event shows what the game will do. Implement it on your own actions; optional.
    /// </summary>
    public interface IInteractionPreviewAction
    {
        /// <summary>The stage's time is at or past the event (<paramref name="reached"/>) or before it. Called whenever the time changes.</summary>
        void Preview(GameObject character, Transform bone, bool reached);
        /// <summary>The stage is closing: put the character back as it was.</summary>
        void EndPreview(GameObject character);
    }

    /// <summary>Marks the authoring stage's objects (editor only; it's never in a game scene).</summary>
    [AddComponentMenu("")]
    public class InteractionStageInfo : MonoBehaviour
    {
        public InteractionAsset asset;
        public GameObject[] roleRoots = new GameObject[0];
    }
}
