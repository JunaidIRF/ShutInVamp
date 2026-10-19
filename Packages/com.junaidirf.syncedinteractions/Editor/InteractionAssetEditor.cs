using UnityEditor;
using UnityEngine;

namespace JunaidIRF.SyncedInteractions.Editor
{
    [CustomEditor(typeof(InteractionAsset))]
    public class InteractionAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (InteractionAsset)target;
            serializedObject.Update();

            EditorGUILayout.LabelField($"{asset.roles.Count} roles · {asset.Length:0.00} s · {asset.events.Count} events", EditorStyles.miniBoldLabel);

            bool onStage = InteractionStage.OpenAsset == asset;
            bool onDualRig = InteractionDualRig.OpenAsset == asset;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (onStage)
                {
                    if (GUILayout.Button("Close Timeline Stage", GUILayout.Height(28))) { InteractionStage.Close(); GUIUtility.ExitGUI(); }
                }
                else if (onDualRig)
                {
                    if (GUILayout.Button("Apply & Close Dual Rig", GUILayout.Height(28))) { InteractionDualRig.Close(); GUIUtility.ExitGUI(); }
                    if (GUILayout.Button("Apply Now", GUILayout.Height(28), GUILayout.Width(90))) { InteractionDualRig.ApplyNow(); }
                }
                else
                {
                    var missing = InteractionStage.Missing(asset);
                    using (new EditorGUI.DisabledScope(missing.Count > 0))
                    {
                        if (GUILayout.Button(new GUIContent("Open on Stage (Timeline)", missing.Count > 0 ? string.Join("\n", missing) : "Preview both characters and events in Timeline"), GUILayout.Height(28)))
                        { InteractionStage.Open(asset); GUIUtility.ExitGUI(); }

                        if (GUILayout.Button(new GUIContent("Animate Both (Animation Window)", missing.Count > 0 ? string.Join("\n", missing) : "Edit both characters' keyframes together on one dopesheet in the Animation window"), GUILayout.Height(28)))
                        { InteractionDualRig.Open(asset); GUIUtility.ExitGUI(); }
                    }
                }
            }

            if (onStage)
                EditorGUILayout.HelpBox("On stage (Timeline): drag the scrubber to preview in sync. Drag markers to retime events. Move characters in Scene view to move slots.", MessageType.Info);
            else if (onDualRig)
                EditorGUILayout.HelpBox("On Dual Rig (Animation Window): both characters are parented under '[Interaction Rig]' in Unity's Animation window. Keyframe both characters on the same dopesheet. Edits write back to each role's clip automatically.", MessageType.Info);
            else if (InteractionStage.Missing(asset).Count > 0)
                EditorGUILayout.HelpBox("Give every role a clip and a Preview Prefab to open it on the stage or rig.", MessageType.None);

            foreach (var p in asset.Validate()) EditorGUILayout.HelpBox(p, MessageType.Warning);

            EditorGUILayout.Space(4);
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }
}
