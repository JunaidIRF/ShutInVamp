using System;
using System.Collections.Generic;
using UnityEngine;

namespace JunaidIRF.SyncedInteractions
{
    public enum QuerySortMode
    {
        Closest,
        BestFacingAngle,
    }

    /// <summary>
    /// Finds the best partner for an interaction around an initiator, filtering by distance, angle,
    /// line of sight, veto hooks and custom conditions.
    /// </summary>
    [Serializable]
    public class InteractionQuery
    {
        [Header("Range & Direction")]
        [Tooltip("Maximum distance to candidate partner")]
        [Min(0f)] public float maxDistance = 2.5f;

        [Tooltip("Flat horizontal distance only (ignores height difference up to maxHeightDifference)")]
        public bool flatDistance = true;

        [Tooltip("Maximum allowed height difference between characters when flatDistance is on")]
        [Min(0f)] public float maxHeightDifference = 2f;

        [Tooltip("Maximum angle off initiator's forward look direction (degrees; 180 = full circle around)")]
        [Range(0f, 180f)] public float maxAngle = 60f;

        [Header("Bones / Points of Interest")]
        [InteractionBone, Tooltip("Origin bone on initiator for direction and line-of-sight (empty = initiator transform)")]
        public string originBone;

        [InteractionBone, Tooltip("Target bone on candidate for distance and line-of-sight (empty = candidate transform)")]
        public string targetBone;

        [Header("Line of Sight")]
        [Tooltip("Requires an unobstructed raycast between origin and target")]
        public bool requireLineOfSight = true;

        [Tooltip("Physics layers that block line of sight")]
        public LayerMask obstacleMask = ~0;

        [Header("Filter & Ranking")]
        [Tooltip("Role the candidate should play (empty = any other role)")]
        public string targetRole = "Receiver";

        [Tooltip("How candidates are sorted when multiple qualify")]
        public QuerySortMode sortMode = QuerySortMode.Closest;

        [Header("Conditions")]
        [SerializeReference] public List<InteractionCondition> conditions = new List<InteractionCondition>();

        /// <summary>Checks whether a candidate qualifies for an interaction with the initiator.</summary>
        public bool Evaluate(InteractionActor initiator, InteractionActor candidate, InteractionAsset asset, out string failureReason)
        {
            failureReason = null;
            if (initiator == null) { failureReason = "initiator is null"; return false; }
            if (candidate == null) { failureReason = "candidate is null"; return false; }
            if (candidate == initiator) { failureReason = "cannot interact with self"; return false; }
            if (!candidate.isActiveAndEnabled) { failureReason = "candidate is disabled"; return false; }
            if (!candidate.available) { failureReason = "candidate is not available for interactions"; return false; }
            if (candidate.Busy) { failureReason = "candidate is already in an interaction"; return false; }
            if (candidate.Animator == null) { failureReason = "candidate has no Animator"; return false; }

            string initRole = asset != null && asset.roles.Count > 0 ? asset.roles[0].name : "Initiator";
            string candRole = !string.IsNullOrEmpty(targetRole) ? targetRole : (asset != null && asset.roles.Count > 1 ? asset.roles[1].name : "Receiver");

            if (initiator.IsVetoed(candidate, asset, initRole, out string veto1))
            {
                failureReason = veto1 ?? "initiator vetoed";
                return false;
            }
            if (candidate.IsVetoed(initiator, asset, candRole, out string veto2))
            {
                failureReason = veto2 ?? "candidate vetoed";
                return false;
            }

            Transform oT = !string.IsNullOrEmpty(originBone) ? initiator.Bone(originBone) : initiator.transform;
            if (oT == null) oT = initiator.transform;
            Vector3 oPos = oT.position;
            Vector3 oForward = oT.forward;

            Transform tT = !string.IsNullOrEmpty(targetBone) ? candidate.Bone(targetBone) : candidate.transform;
            if (tT == null) tT = candidate.transform;
            Vector3 tPos = tT.position;

            if (flatDistance)
            {
                if (Mathf.Abs(tPos.y - oPos.y) > maxHeightDifference) { failureReason = "height difference too large"; return false; }
                Vector3 flatDiff = new Vector3(tPos.x - oPos.x, 0f, tPos.z - oPos.z);
                if (flatDiff.sqrMagnitude > maxDistance * maxDistance) { failureReason = "out of range"; return false; }
            }
            else
            {
                if (Vector3.SqrMagnitude(tPos - oPos) > maxDistance * maxDistance) { failureReason = "out of range"; return false; }
            }

            if (maxAngle < 180f)
            {
                Vector3 toTarget = tPos - oPos;
                if (flatDistance)
                {
                    toTarget.y = 0f;
                    oForward.y = 0f;
                }
                if (toTarget.sqrMagnitude > 1e-4f && oForward.sqrMagnitude > 1e-4f)
                {
                    float angle = Vector3.Angle(oForward, toTarget);
                    if (angle > maxAngle) { failureReason = $"angle ({angle:0.#}°) exceeds max ({maxAngle:0.#}°)"; return false; }
                }
            }

            if (requireLineOfSight && !CheckLineOfSight(oPos, tPos, initiator, candidate))
            {
                failureReason = "line of sight blocked";
                return false;
            }

            if (conditions != null)
            {
                for (int i = 0; i < conditions.Count; i++)
                {
                    var c = conditions[i];
                    if (c != null && !c.Evaluate(initiator, candidate, asset))
                    {
                        failureReason = c.FailureReason;
                        return false;
                    }
                }
            }

            return true;
        }

        bool CheckLineOfSight(Vector3 from, Vector3 to, InteractionActor initiator, InteractionActor candidate)
        {
            Vector3 diff = to - from;
            float dist = diff.magnitude;
            if (dist < 1e-3f) return true;
            Ray ray = new Ray(from, diff / dist);
            var hits = Physics.RaycastAll(ray, dist, obstacleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                var col = hits[i].collider;
                if (col == null) continue;
                if (col.transform.IsChildOf(initiator.transform) || (initiator.Animator != null && col.transform.IsChildOf(initiator.Animator.transform))) continue;
                if (col.transform.IsChildOf(candidate.transform) || (candidate.Animator != null && col.transform.IsChildOf(candidate.Animator.transform))) continue;
                return false;
            }
            return true;
        }

        /// <summary>Finds the highest-scoring candidate among all active actors.</summary>
        public InteractionActor FindBest(InteractionActor initiator, InteractionAsset asset = null)
        {
            if (initiator == null) return null;
            var all = InteractionActor.All;
            InteractionActor best = null;
            float bestScore = float.MaxValue;

            Transform oT = !string.IsNullOrEmpty(originBone) ? initiator.Bone(originBone) : initiator.transform;
            if (oT == null) oT = initiator.transform;
            Vector3 oPos = oT.position;
            Vector3 oFwd = oT.forward;

            for (int i = 0; i < all.Count; i++)
            {
                var candidate = all[i];
                if (!Evaluate(initiator, candidate, asset, out _)) continue;

                Transform tT = !string.IsNullOrEmpty(targetBone) ? candidate.Bone(targetBone) : candidate.transform;
                if (tT == null) tT = candidate.transform;
                Vector3 tPos = tT.position;

                float score;
                if (sortMode == QuerySortMode.BestFacingAngle)
                {
                    Vector3 toT = tPos - oPos;
                    if (flatDistance) { toT.y = 0f; oFwd.y = 0f; }
                    score = Vector3.Angle(oFwd, toT);
                }
                else
                {
                    score = flatDistance ? Vector2.Distance(new Vector2(oPos.x, oPos.z), new Vector2(tPos.x, tPos.z)) : Vector3.Distance(oPos, tPos);
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            return best;
        }

        /// <summary>Finds all qualifying candidates sorted by rank.</summary>
        public List<InteractionActor> FindAll(InteractionActor initiator, InteractionAsset asset = null)
        {
            var list = new List<InteractionActor>();
            if (initiator == null) return list;
            var all = InteractionActor.All;
            for (int i = 0; i < all.Count; i++)
            {
                var candidate = all[i];
                if (Evaluate(initiator, candidate, asset, out _)) list.Add(candidate);
            }
            if (list.Count > 1)
            {
                Transform oT = !string.IsNullOrEmpty(originBone) ? initiator.Bone(originBone) : initiator.transform;
                if (oT == null) oT = initiator.transform;
                Vector3 oPos = oT.position;
                Vector3 oFwd = oT.forward;

                list.Sort((a, b) =>
                {
                    Transform tA = !string.IsNullOrEmpty(targetBone) ? a.Bone(targetBone) : a.transform;
                    Transform tB = !string.IsNullOrEmpty(targetBone) ? b.Bone(targetBone) : b.transform;
                    Vector3 posA = tA != null ? tA.position : a.transform.position;
                    Vector3 posB = tB != null ? tB.position : b.transform.position;

                    float sA, sB;
                    if (sortMode == QuerySortMode.BestFacingAngle)
                    {
                        Vector3 toA = posA - oPos; if (flatDistance) toA.y = 0f;
                        Vector3 toB = posB - oPos; if (flatDistance) toB.y = 0f;
                        Vector3 fwd = oFwd; if (flatDistance) fwd.y = 0f;
                        sA = Vector3.Angle(fwd, toA);
                        sB = Vector3.Angle(fwd, toB);
                    }
                    else
                    {
                        sA = flatDistance ? Vector2.Distance(new Vector2(oPos.x, oPos.z), new Vector2(posA.x, posA.z)) : Vector3.Distance(oPos, posA);
                        sB = flatDistance ? Vector2.Distance(new Vector2(oPos.x, oPos.z), new Vector2(posB.x, posB.z)) : Vector3.Distance(oPos, posB);
                    }
                    return sA.CompareTo(sB);
                });
            }
            return list;
        }

        /// <summary>Quick one-call search with standard defaults.</summary>
        public static InteractionActor FindBest(InteractionActor initiator, InteractionAsset asset, float maxDistance = 2.5f, float maxAngle = 60f)
        {
            var q = new InteractionQuery { maxDistance = maxDistance, maxAngle = maxAngle };
            return q.FindBest(initiator, asset);
        }
    }
}
