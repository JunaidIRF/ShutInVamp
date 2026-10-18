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
            using (new EditorGUILayout.HorizontalScope())
            {
                if (onStage)
                {
                    if (GUILayout.Button("Close Stage", GUILayout.Height(26))) { InteractionStage.Close(); GUIUtility.ExitGUI(); }
                }
                else
                {
                    var missing = InteractionStage.Missing(asset);
                    using (new EditorGUI.DisabledScope(missing.Count > 0))
                        if (GUILayout.Button(new GUIContent("Open on Stage", missing.Count > 0 ? string.Join("\n", missing) : "Edit and preview it on a Timeline with the preview characters"), GUILayout.Height(26)))
                        { InteractionStage.Open(asset); GUIUtility.ExitGUI(); }
                }
            }
            if (onStage)
                EditorGUILayout.HelpBox("On stage: scrub the Timeline to preview. A role's keys: right-click its clip > Edit in Animation Window. " +
                                        "Events: drag markers to retime, add / delete markers to add / remove events (set what they do here). " +
                                        "Slots: move or turn a character in the Scene view. Everything is saved to this asset as you go.", MessageType.Info);
            else if (InteractionStage.Missing(asset).Count > 0)
                EditorGUILayout.HelpBox("Give every role a clip and a Preview Prefab to open it on the stage.", MessageType.None);

            foreach (var p in asset.Validate()) EditorGUILayout.HelpBox(p, MessageType.Warning);

            EditorGUILayout.Space(4);
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }
}
