using UnityEngine;

namespace JunaidIRF.SyncedInteractions
{
    /// <summary>Marks the authoring stage's objects (editor only; it's never in a game scene).</summary>
    [AddComponentMenu("")]
    public class InteractionStageInfo : MonoBehaviour
    {
        public InteractionAsset asset;
        public GameObject[] roleRoots = new GameObject[0];
    }
}
