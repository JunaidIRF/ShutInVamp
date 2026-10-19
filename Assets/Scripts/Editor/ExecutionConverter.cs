using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using JunaidIRF.SyncedInteractions;
using ShutInVamp.Interactions;

public static class ExecutionConverter
{
    const string ExecutionsFolder = "Assets/Settings/Executions";
    const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
    const string EnemyPrefabPath = "Assets/Prefabs/Shooter.prefab";

    [MenuItem("Tools/ShutInVamp/Convert Executions to Interactions")]
    public static void ConvertAll()
    {
        var guids = AssetDatabase.FindAssets("t:ExecutionData", new[] { ExecutionsFolder });
        int count = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<ExecutionData>(path);
            if (data == null) continue;

            Convert(data, path);
            count++;
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Execution Converter: successfully converted {count} execution(s) to InteractionAssets in '{ExecutionsFolder}'.");
    }

    public static InteractionAsset Convert(ExecutionData data, string sourcePath)
    {
        string outPath = sourcePath.Replace(".asset", "_Interaction.asset");
        var asset = AssetDatabase.LoadAssetAtPath<InteractionAsset>(outPath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<InteractionAsset>();
            AssetDatabase.CreateAsset(asset, outPath);
        }

        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        var playerClip = ExecutionAnimEditing.PlayerClip(data);
        var enemyClip = ExecutionAnimEditing.EnemyClip(data);

        asset.roles.Clear();

        // Role 0: Victim (stays at origin, anchor)
        var victimRole = new InteractionRole("Victim")
        {
            clip = enemyClip,
            previewPrefab = enemyPrefab,
            offset = Vector3.zero,
            yaw = 0f,
            moveToSlot = false,
            releaseTime = data.killTime
        };

        // Role 1: Player (moves to slot, faces focus bone)
        var playerRole = new InteractionRole("Player")
        {
            clip = playerClip,
            previewPrefab = playerPrefab,
            offset = data.playerOffset,
            yaw = 180f,
            moveToSlot = true,
            lookAtRole = "Victim",
            lookAtBone = data.focusBone
        };

        asset.roles.Add(victimRole);
        asset.roles.Add(playerRole);

        asset.anchor = AnchorMode.Role;
        asset.anchorRole = "Victim";
        asset.align = AlignMode.Glide;
        asset.alignTime = data.alignTime;
        asset.duration = data.duration;
        asset.snapToGround = true;

        asset.events.Clear();

        // If worldHandsAtStart is set, add hands action at t=0
        if (data.worldHandsAtStart)
        {
            var evtHands = new InteractionEvent
            {
                name = "Hands World",
                time = 0f,
                role = "Player",
                bone = ""
            };
            evtHands.actions.Add(new ExecutionHandsLayerAction { layer = ExecutionData.HandsLayer.World });
            asset.events.Add(evtHands);
        }

        // Convert events
        if (data.events != null)
        {
            for (int i = 0; i < data.events.Length; i++)
            {
                var src = data.events[i];
                var evt = new InteractionEvent
                {
                    name = $"Event {i + 1}",
                    time = src.time,
                    role = "Victim",
                    bone = string.IsNullOrEmpty(src.bone) ? data.focusBone : src.bone
                };

                if (src.blood)
                    evt.actions.Add(new ExecutionBloodAction());

                if (src.sever != Dismemberment.BodyPart.None)
                    evt.actions.Add(new ExecutionSeverAction { part = src.sever });

                if (src.heal > 0f)
                    evt.actions.Add(new ExecutionHealAction { amount = src.heal });

                if (src.shake > 0f)
                    evt.actions.Add(new ExecutionCameraShakeAction { degrees = src.shake, time = src.shakeTime });

                if (src.hands != ExecutionData.HandsLayer.Keep)
                    evt.actions.Add(new ExecutionHandsLayerAction { layer = src.hands });

                asset.events.Add(evt);
            }
        }

        // Kill Event at killTime
        if (data.killTime > 0f)
        {
            var killEvt = new InteractionEvent
            {
                name = "Kill",
                time = data.killTime,
                role = "Victim",
                bone = data.focusBone
            };
            killEvt.actions.Add(new ExecutionKillAction
            {
                killBodySpeed = data.killBodySpeed,
                killHitSpeed = data.killHitSpeed,
                killLift = data.killLift,
                decapitateOnKill = data.decapitateOnKill
            });
            asset.events.Add(killEvt);
        }

        EditorUtility.SetDirty(asset);
        return asset;
    }
}
