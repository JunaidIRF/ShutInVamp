#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;

namespace JunaidIRF.SyncedInteractions.EditorCapture
{
    /// <summary>
    /// Lightweight floating utility window that renders an asset's CustomEditor in isolation.
    /// Provides pixel-perfect, borderless documentation screenshots without dock clutter.
    /// </summary>
    public class EditorStudioWindow : EditorWindow
    {
        public UnityEngine.Object targetObject;
        public UnityEditor.Editor customEditor;
        public Vector2 scrollPosition;
        public bool hasPaintedAtLeastOnce;
        public Action onRepainted;

        public static EditorStudioWindow Open(UnityEngine.Object target, int width, int height)
        {
            var window = CreateInstance<EditorStudioWindow>();
            window.targetObject = target;
            if (target != null)
            {
                window.customEditor = UnityEditor.Editor.CreateEditor(target);
            }

            window.titleContent = new GUIContent("Studio: " + (target != null ? target.name : "Inspector"));
            window.minSize = new Vector2(width, height);
            window.maxSize = new Vector2(width, height);
            window.position = new Rect(200, 150, width, height);
            window.ShowUtility();
            window.Focus();
            return window;
        }

        private void OnGUI()
        {
            // Dark theme container styling
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), new Color(0.18f, 0.18f, 0.18f, 1f));

            GUILayout.Space(8);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                using (new GUILayout.VerticalScope())
                {
                    if (targetObject == null)
                    {
                        EditorGUILayout.HelpBox("No target object assigned to Studio.", MessageType.Info);
                    }
                    else
                    {
                        if (customEditor == null || customEditor.target != targetObject)
                        {
                            customEditor = UnityEditor.Editor.CreateEditor(targetObject);
                        }

                        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                        customEditor.OnInspectorGUI();
                        EditorGUILayout.EndScrollView();
                    }
                }
                GUILayout.Space(10);
            }

            if (Event.current.type == EventType.Repaint)
            {
                hasPaintedAtLeastOnce = true;
                onRepainted?.Invoke();
            }
        }

        private void OnDestroy()
        {
            if (customEditor != null)
            {
                DestroyImmediate(customEditor);
                customEditor = null;
            }
        }
    }
}
#endif
