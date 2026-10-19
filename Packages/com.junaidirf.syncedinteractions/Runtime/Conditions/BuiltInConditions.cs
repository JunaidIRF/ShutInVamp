using System;
using UnityEngine;

namespace JunaidIRF.SyncedInteractions
{
    /// <summary>Checks the facing direction of the initiator and target (face-to-face, from behind, etc.).</summary>
    [Serializable]
    public class FacingCondition : InteractionCondition
    {
        public enum Mode
        {
            /// <summary>Both characters face towards each other (e.g. handshakes, duels).</summary>
            FaceToFace,
            /// <summary>Initiator is behind the target, facing its back (e.g. stealth takedowns, backstabs).</summary>
            BehindTarget,
            /// <summary>Initiator looks towards the target within the angle.</summary>
            InitiatorFacesTarget,
            /// <summary>Target looks towards the initiator within the angle.</summary>
            TargetFacesInitiator,
        }

        public Mode mode = Mode.FaceToFace;
        [Tooltip("Allowed angle tolerance in degrees")]
        [Range(0f, 180f)] public float maxAngle = 60f;
        [Tooltip("Compare horizontal (ground-plane) facing only")]
        public bool horizontalOnly = true;

        public override bool Evaluate(InteractionActor initiator, InteractionActor target, InteractionAsset asset)
        {
            if (initiator == null || target == null) return false;

            Vector3 iPos = initiator.transform.position;
            Vector3 tPos = target.transform.position;
            Vector3 iFwd = initiator.transform.forward;
            Vector3 tFwd = target.transform.forward;

            if (horizontalOnly)
            {
                iFwd.y = 0f; tFwd.y = 0f;
                iPos.y = 0f; tPos.y = 0f;
            }
            iFwd.Normalize();
            tFwd.Normalize();

            Vector3 toTarget = (tPos - iPos).normalized;
            Vector3 toInitiator = (iPos - tPos).normalized;

            switch (mode)
            {
                case Mode.FaceToFace:
                    return Vector3.Angle(iFwd, toTarget) <= maxAngle && Vector3.Angle(tFwd, toInitiator) <= maxAngle;
                case Mode.BehindTarget:
                    // Initiator looks towards target, target looks away (in same direction as toTarget)
                    return Vector3.Angle(iFwd, toTarget) <= maxAngle && Vector3.Angle(tFwd, toTarget) <= maxAngle;
                case Mode.InitiatorFacesTarget:
                    return Vector3.Angle(iFwd, toTarget) <= maxAngle;
                case Mode.TargetFacesInitiator:
                    return Vector3.Angle(tFwd, toInitiator) <= maxAngle;
                default:
                    return true;
            }
        }

        public override string FailureReason => mode switch
        {
            Mode.FaceToFace => "characters are not facing each other",
            Mode.BehindTarget => "initiator is not behind target",
            Mode.InitiatorFacesTarget => "initiator is not facing target",
            Mode.TargetFacesInitiator => "target is not facing initiator",
            _ => base.FailureReason
        };

        public override string MenuName => "Facing";
    }

    /// <summary>Ensures an unobstructed line of sight between two characters' bones or roots.</summary>
    [Serializable]
    public class LineOfSightCondition : InteractionCondition
    {
        [InteractionBone, Tooltip("Origin bone on initiator (empty = character root or camera head)")]
        public string originBone;
        [InteractionBone, Tooltip("Target bone on candidate (empty = character root or chest)")]
        public string targetBone;
        [Tooltip("Physics layers that block line of sight")]
        public LayerMask obstacleMask = ~0;

        public override bool Evaluate(InteractionActor initiator, InteractionActor target, InteractionAsset asset)
        {
            if (initiator == null || target == null) return false;

            Transform oT = !string.IsNullOrEmpty(originBone) ? initiator.Bone(originBone) : initiator.transform;
            Transform tT = !string.IsNullOrEmpty(targetBone) ? target.Bone(targetBone) : target.transform;
            Vector3 from = oT != null ? oT.position : initiator.transform.position;
            Vector3 to = tT != null ? tT.position : target.transform.position;

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
                if (col.transform.IsChildOf(target.transform) || (target.Animator != null && col.transform.IsChildOf(target.Animator.transform))) continue;
                return false;
            }
            return true;
        }

        public override string FailureReason => "line of sight blocked by obstacle";
        public override string MenuName => "Line Of Sight";
    }

    /// <summary>Checks that the distance between initiator and target falls within a specified min/max range.</summary>
    [Serializable]
    public class DistanceRangeCondition : InteractionCondition
    {
        [Min(0f)] public float minDistance = 0f;
        [Min(0f)] public float maxDistance = 3f;
        public bool horizontalOnly = true;

        public override bool Evaluate(InteractionActor initiator, InteractionActor target, InteractionAsset asset)
        {
            if (initiator == null || target == null) return false;
            Vector3 a = initiator.transform.position;
            Vector3 b = target.transform.position;
            float d = horizontalOnly ? Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z)) : Vector3.Distance(a, b);
            return d >= minDistance && d <= maxDistance;
        }

        public override string FailureReason => "target is outside required distance range";
        public override string MenuName => "Distance Range";
    }

    /// <summary>Requires the target GameObject to have a specific tag.</summary>
    [Serializable]
    public class TargetTagCondition : InteractionCondition
    {
        public string requiredTag = "Untagged";

        public override bool Evaluate(InteractionActor initiator, InteractionActor target, InteractionAsset asset)
        {
            return target != null && (target.CompareTag(requiredTag) || (target.Animator != null && target.Animator.CompareTag(requiredTag)));
        }

        public override string FailureReason => $"target does not have tag '{requiredTag}'";
        public override string MenuName => "Target Tag";
    }

    /// <summary>Requires the target GameObject to be on one of the specified layers.</summary>
    [Serializable]
    public class TargetLayerCondition : InteractionCondition
    {
        public LayerMask requiredLayers = ~0;

        public override bool Evaluate(InteractionActor initiator, InteractionActor target, InteractionAsset asset)
        {
            if (target == null) return false;
            int mask = 1 << target.gameObject.layer;
            return (requiredLayers.value & mask) != 0;
        }

        public override string FailureReason => "target is not on a permitted layer";
        public override string MenuName => "Target Layer";
    }
}
