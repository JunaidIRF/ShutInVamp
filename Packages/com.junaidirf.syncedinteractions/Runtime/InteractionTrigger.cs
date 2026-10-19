using System;
using UnityEngine;
using UnityEngine.Events;

namespace JunaidIRF.SyncedInteractions
{
    /// <summary>
    /// Component that automatically finds interaction partners and lets you trigger them
    /// via a key (e.g. [E]) or by code. Shows an optional HUD prompt and fires events.
    /// </summary>
    [AddComponentMenu("Synced Interactions/Interaction Trigger")]
    [DisallowMultipleComponent]
    public class InteractionTrigger : MonoBehaviour
    {
        [Header("Actor & Asset")]
        [Tooltip("The initiator actor (empty = found on this object or below)")]
        public InteractionActor initiator;

        [Tooltip("The interaction asset to play (or primary asset)")]
        public InteractionAsset asset;

        [Tooltip("Optional pool of assets: if assigned, one is picked (randomly or by CanPlay check)")]
        public InteractionAsset[] assetPool;

        [Header("Discovery Query")]
        public InteractionQuery query = new InteractionQuery();

        [Header("Input Trigger")]
        public bool triggerOnKey = true;
        public KeyCode key = KeyCode.E;

        [Header("Prompt UI")]
        [Tooltip("GameObject to show when a valid target is in range (e.g. '[E] Interact')")]
        public GameObject prompt;

        [Tooltip("Move the prompt object to target's position or bone")]
        public bool movePromptToTarget;
        public Vector3 promptWorldOffset = new Vector3(0f, 1.5f, 0f);

        [Header("Events")]
        public UnityEvent<InteractionActor> onCandidateFound = new UnityEvent<InteractionActor>();
        public UnityEvent onCandidateLost = new UnityEvent();
        public UnityEvent<InteractionHandle> onTriggered = new UnityEvent<InteractionHandle>();

        public InteractionActor CurrentCandidate { get; private set; }

        void Awake()
        {
            if (initiator == null) initiator = GetComponentInChildren<InteractionActor>();
        }

        void Start()
        {
            SetPrompt(false);
        }

        void OnDisable()
        {
            ClearCandidate();
        }

        void Update()
        {
            if (initiator == null || !initiator.isActiveAndEnabled || initiator.Busy)
            {
                ClearCandidate();
                return;
            }

            var testAsset = PickAsset();
            var best = query.FindBest(initiator, testAsset);

            if (best != CurrentCandidate)
            {
                CurrentCandidate = best;
                if (CurrentCandidate != null)
                {
                    SetPrompt(true);
                    UpdatePromptPosition();
                    onCandidateFound?.Invoke(CurrentCandidate);
                }
                else
                {
                    SetPrompt(false);
                    onCandidateLost?.Invoke();
                }
            }
            else if (CurrentCandidate != null)
            {
                UpdatePromptPosition();
            }

            if (triggerOnKey && Input.GetKeyDown(key) && CurrentCandidate != null)
            {
                Trigger();
            }
        }

        /// <summary>Starts the interaction with the current candidate (or searches for one if needed).</summary>
        public InteractionHandle Trigger()
        {
            if (initiator == null || initiator.Busy) return null;
            var chosen = CurrentCandidate != null ? CurrentCandidate : query.FindBest(initiator, PickAsset());
            if (chosen == null) return null;

            var chosenAsset = PickAsset(chosen);
            if (chosenAsset == null) return null;

            ClearCandidate();
            var handle = Interaction.Play(chosenAsset, initiator, chosen);
            if (handle != null)
            {
                onTriggered?.Invoke(handle);
            }
            return handle;
        }

        /// <summary>Picks a playable asset from the asset pool or fallback asset.</summary>
        public InteractionAsset PickAsset(InteractionActor candidate = null)
        {
            if (assetPool != null && assetPool.Length > 0)
            {
                int start = UnityEngine.Random.Range(0, assetPool.Length);
                for (int i = 0; i < assetPool.Length; i++)
                {
                    var a = assetPool[(start + i) % assetPool.Length];
                    if (a != null && (candidate == null || Interaction.CanPlay(a, initiator, candidate))) return a;
                }
            }
            return asset;
        }

        void SetPrompt(bool visible)
        {
            if (prompt != null && prompt.activeSelf != visible) prompt.SetActive(visible);
        }

        void UpdatePromptPosition()
        {
            if (!movePromptToTarget || prompt == null || CurrentCandidate == null) return;
            Transform t = !string.IsNullOrEmpty(query.targetBone) ? CurrentCandidate.Bone(query.targetBone) : CurrentCandidate.transform;
            Vector3 pos = (t != null ? t.position : CurrentCandidate.transform.position) + promptWorldOffset;
            prompt.transform.position = pos;
        }

        void ClearCandidate()
        {
            if (CurrentCandidate != null)
            {
                CurrentCandidate = null;
                SetPrompt(false);
                onCandidateLost?.Invoke();
            }
        }

        void OnDrawGizmosSelected()
        {
            var origin = initiator != null ? initiator.transform : transform;
            if (!string.IsNullOrEmpty(query?.originBone) && initiator != null)
            {
                var b = initiator.Bone(query.originBone);
                if (b != null) origin = b;
            }

            Gizmos.color = new Color(0f, 0.8f, 1f, 0.25f);
            Gizmos.DrawWireSphere(origin.position, query != null ? query.maxDistance : 2.5f);

            if (query != null && query.maxAngle < 180f)
            {
                Gizmos.color = new Color(0f, 0.8f, 1f, 0.6f);
                Vector3 fwd = origin.forward;
                if (query.flatDistance) fwd.y = 0f;
                fwd.Normalize();
                Quaternion left = Quaternion.AngleAxis(-query.maxAngle, Vector3.up);
                Quaternion right = Quaternion.AngleAxis(query.maxAngle, Vector3.up);
                Gizmos.DrawLine(origin.position, origin.position + left * fwd * query.maxDistance);
                Gizmos.DrawLine(origin.position, origin.position + right * fwd * query.maxDistance);
            }
        }
    }
}
