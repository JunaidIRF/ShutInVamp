using UnityEngine;

namespace JunaidIRF.SyncedInteractions
{
    /// <summary>Ticks every interaction once a frame, after the game's own Update (and before animation is evaluated).</summary>
    [DefaultExecutionOrder(1000)]
    [AddComponentMenu("")]
    internal class InteractionRunner : MonoBehaviour
    {
        static InteractionRunner instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        internal static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("[Synced Interactions]") { hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<InteractionRunner>();
        }

        void Update() => Interaction.Tick(Time.deltaTime, Time.unscaledDeltaTime);

        void OnDestroy()
        {
            if (instance == this) instance = null;
            Interaction.CancelAll();
        }
    }
}
