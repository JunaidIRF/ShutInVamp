using System;
using System.Collections.Generic;
using UnityEngine;
using JunaidIRF.SyncedInteractions;

namespace ShutInVamp.Interactions
{
    /// <summary>Blood spray on the victim.</summary>
    [Serializable]
    public class ExecutionBloodAction : InteractionAction
    {
        public Sfx sound;

        public override void Fire(InteractionContext c)
        {
            if (c.actor == null) return;
            var shooter = c.actor.GetComponent<Shooter>();
            if (shooter == null) return;

            var cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : c.Position - Vector3.forward;
            Vector3 dir = (c.Position - camPos).normalized;

            shooter.Bleed(c.Position, dir);
            sound?.PlayAt(c.Position);
        }

        public override string MenuName => "ShutInVamp/Blood";
    }

    /// <summary>Sever a body part from the victim.</summary>
    [Serializable]
    public class ExecutionSeverAction : InteractionAction, IInteractionPreviewAction
    {
        public Dismemberment.BodyPart part = Dismemberment.BodyPart.Head;

        public override void Fire(InteractionContext c)
        {
            if (c.actor == null) return;
            var shooter = c.actor.GetComponent<Shooter>();
            if (shooter == null) return;

            Transform cut = shooter.PartBone(part);
            var cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : c.Position - Vector3.forward;
            Vector3 dir = cut != null ? (cut.position - camPos).normalized : c.actor.transform.forward;

            shooter.Sever(part, dir);
        }

        public void Preview(GameObject character, Transform bone, bool reached)
        {
            if (character == null) return;
            var d = character.GetComponentInChildren<Dismemberment>();
            if (d == null) return;
            Transform cutBone = d.BoneOf(part);
            if (cutBone != null) cutBone.localScale = reached ? Vector3.zero : Vector3.one;
        }

        public void EndPreview(GameObject character)
        {
            if (character == null) return;
            var d = character.GetComponentInChildren<Dismemberment>();
            if (d == null) return;
            Transform cutBone = d.BoneOf(part);
            if (cutBone != null) cutBone.localScale = Vector3.one;
        }

        public override string MenuName => "ShutInVamp/Sever Part";
    }

    /// <summary>Heals the player during an execution.</summary>
    [Serializable]
    public class ExecutionHealAction : InteractionAction
    {
        [Min(0f)] public float amount = 10f;

        public override void Fire(InteractionContext c)
        {
            var player = UnityEngine.Object.FindAnyObjectByType<Player>();
            if (player != null) player.Heal(amount);
        }

        public override string MenuName => "ShutInVamp/Heal Player";
    }

    /// <summary>Triggers camera shake through GuardJuice.</summary>
    [Serializable]
    public class ExecutionCameraShakeAction : InteractionAction
    {
        [Min(0f)] public float degrees = 1.5f;
        [Min(0f)] public float time = 0.2f;

        public override void Fire(InteractionContext c)
        {
            var juice = UnityEngine.Object.FindAnyObjectByType<GuardJuice>();
            if (juice != null) juice.Shake(degrees, time);
        }

        public override string MenuName => "ShutInVamp/Camera Shake";
    }

    /// <summary>Kills the victim and knocks the ragdoll back.</summary>
    [Serializable]
    public class ExecutionKillAction : InteractionAction
    {
        public float killBodySpeed = 1.05f;
        public float killHitSpeed = 1.8f;
        public float killLift = 0.36f;
        public bool decapitateOnKill;

        public override void Fire(InteractionContext c)
        {
            if (c.actor == null) return;
            var shooter = c.actor.GetComponent<Shooter>();
            if (shooter == null) return;

            var cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : c.Position - Vector3.forward;
            Vector3 dir = (c.Position - camPos).normalized;

            shooter.FinishExecution(c.Position, dir, killBodySpeed, killHitSpeed, killLift, decapitateOnKill);
        }

        public override string MenuName => "ShutInVamp/Kill Victim";
    }

    /// <summary>Switches the hands layer so the victim's body can cover them.</summary>
    [Serializable]
    public class ExecutionHandsLayerAction : InteractionAction, IInteractionPreviewAction
    {
        public ExecutionData.HandsLayer layer = ExecutionData.HandsLayer.World;
        public int worldLayer = 0; // Default layer

        public override void Fire(InteractionContext c)
        {
            var playerExec = UnityEngine.Object.FindAnyObjectByType<PlayerExecution>();
            if (playerExec != null)
            {
                if (layer == ExecutionData.HandsLayer.World) playerExec.SetHandsWorld();
                else if (layer == ExecutionData.HandsLayer.Overlay) playerExec.RestoreHands();
            }
        }

        public override void End(InteractionContext c, bool cancelled)
        {
            var playerExec = UnityEngine.Object.FindAnyObjectByType<PlayerExecution>();
            if (playerExec != null) playerExec.RestoreHands();
        }

        public void Preview(GameObject character, Transform bone, bool reached) { }
        public void EndPreview(GameObject character) { }

        public override string MenuName => "ShutInVamp/Hands Layer";
    }
}
