using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JunaidIRF.SyncedInteractions.Editor
{
    /// <summary>
    /// Every [SerializeReference] InteractionAction: a header with its type (click to change it, or to pick one for
    /// an empty slot) and its fields below. New action classes in any assembly show up in the menu by themselves.
    /// </summary>
    [CustomPropertyDrawer(typeof(InteractionAction), true)]
    public class InteractionActionDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float h = EditorGUIUtility.singleLineHeight + 2f;
            if (property.managedReferenceValue == null || !property.isExpanded) return h;
            var end = property.GetEndProperty();
            var it = property.Copy();
            if (it.NextVisible(true))
                while (!SerializedProperty.EqualContents(it, end))
                {
                    h += EditorGUI.GetPropertyHeight(it, true) + EditorGUIUtility.standardVerticalSpacing;
                    if (!it.NextVisible(false)) break;
                }
            return h;
        }

        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            var line = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);
            object value = property.managedReferenceValue;
            string typeName = value == null ? "(choose an action)" : ((InteractionAction)value).MenuName;

            var foldRect = new Rect(line.x, line.y, 14f, line.height);
            if (value != null) property.isExpanded = EditorGUI.Foldout(foldRect, property.isExpanded, GUIContent.none, true);
            var buttonRect = new Rect(line.x + 14f, line.y, line.width - 14f, line.height);
            if (EditorGUI.DropdownButton(buttonRect, new GUIContent(typeName), FocusType.Keyboard, value == null ? EditorStyles.miniPullDown : EditorStyles.boldLabel))
                TypeMenu(property);

            if (value == null || !property.isExpanded) return;
            EditorGUI.indentLevel++;
            float y = line.yMax + 2f;
            var end = property.GetEndProperty();
            var it = property.Copy();
            if (it.NextVisible(true))
                while (!SerializedProperty.EqualContents(it, end))
                {
                    float h = EditorGUI.GetPropertyHeight(it, true);
                    EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, h), it, true);
                    y += h + EditorGUIUtility.standardVerticalSpacing;
                    if (!it.NextVisible(false)) break;
                }
            EditorGUI.indentLevel--;
        }

        static void TypeMenu(SerializedProperty property)
        {
            var menu = new GenericMenu();
            var types = TypeCache.GetTypesDerivedFrom<InteractionAction>().Where(t => !t.IsAbstract && !t.IsGenericType && t.GetConstructor(Type.EmptyTypes) != null)
                                 .OrderBy(t => t.Assembly == typeof(InteractionAction).Assembly ? 0 : 1).ThenBy(t => t.Name);
            var target = property.serializedObject;
            string path = property.propertyPath;
            foreach (var t in types)
            {
                var sample = (InteractionAction)Activator.CreateInstance(t);
                string group = t.Assembly == typeof(InteractionAction).Assembly ? "Built-in/" : "";
                menu.AddItem(new GUIContent(group + sample.MenuName), property.managedReferenceValue != null && property.managedReferenceValue.GetType() == t, () =>
                {
                    target.Update();
                    var p = target.FindProperty(path);
                    p.managedReferenceValue = Activator.CreateInstance(t);
                    p.isExpanded = true;
                    target.ApplyModifiedProperties();
                });
            }
            menu.ShowAsContext();
        }
    }

    /// <summary>A bone name: a text field, plus a menu of the humanoid bones and the preview character's own bones.</summary>
    [CustomPropertyDrawer(typeof(InteractionBoneAttribute))]
    public class InteractionBoneDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            var field = new Rect(rect.x, rect.y, rect.width - 22f, rect.height);
            EditorGUI.PropertyField(field, property, label);
            if (!GUI.Button(new Rect(field.xMax + 2f, rect.y, 20f, rect.height), "▾", EditorStyles.miniButton)) return;

            var menu = new GenericMenu();
            var target = property.serializedObject;
            string path = property.propertyPath;
            void Pick(string v) { target.Update(); target.FindProperty(path).stringValue = v; target.ApplyModifiedProperties(); }
            menu.AddItem(new GUIContent("(root)"), string.IsNullOrEmpty(property.stringValue), () => Pick(""));

            var preview = PreviewFor(property);
            if (preview != null)
            {
                var animator = preview.GetComponentInChildren<Animator>(true);
                Transform root = animator != null ? animator.transform : preview.transform;
                foreach (var t in root.GetComponentsInChildren<Transform>(true).Skip(1).Take(400))
                    if (t.GetComponent<Renderer>() == null) menu.AddItem(new GUIContent("Preview bones/" + t.name.Replace("/", "_")), property.stringValue == t.name, () => Pick(t.name));
            }
            foreach (HumanBodyBones b in Enum.GetValues(typeof(HumanBodyBones)))
                if (b != HumanBodyBones.LastBone) menu.AddItem(new GUIContent("Humanoid/" + b), property.stringValue == b.ToString(), () => Pick(b.ToString()));
            menu.ShowAsContext();
        }

        /// <summary>The preview prefab of the role this bone belongs to (an event's role, a role's look-at role).</summary>
        static GameObject PreviewFor(SerializedProperty property)
        {
            if (!(property.serializedObject.targetObject is InteractionAsset asset)) return null;
            string path = property.propertyPath;
            string role = null;
            int dot = path.LastIndexOf('.');
            var owner = dot > 0 ? property.serializedObject.FindProperty(path.Substring(0, dot)) : null;
            if (owner != null)
            {
                if (path.StartsWith("events")) role = owner.FindPropertyRelative("role")?.stringValue;
                else if (path.StartsWith("roles")) role = owner.FindPropertyRelative("lookAtRole")?.stringValue;
            }
            var r = string.IsNullOrEmpty(role) ? (asset.roles.Count > 0 ? asset.roles[0] : null) : asset.Role(role);
            return r != null ? r.previewPrefab : null;
        }
    }

    /// <summary>Role names: a text field plus a menu of the asset's roles.</summary>
    [CustomPropertyDrawer(typeof(InteractionRoleNameAttribute))]
    public class InteractionRoleNameDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            var asset = property.serializedObject.targetObject as InteractionAsset;
            if (asset == null) { EditorGUI.PropertyField(rect, property, label); return; }
            var field = new Rect(rect.x, rect.y, rect.width - 22f, rect.height);
            EditorGUI.PropertyField(field, property, label);
            if (!GUI.Button(new Rect(field.xMax + 2f, rect.y, 20f, rect.height), "▾", EditorStyles.miniButton)) return;
            var menu = new GenericMenu();
            var target = property.serializedObject;
            string path = property.propertyPath;
            foreach (var r in asset.roles)
            {
                if (r == null) continue;
                string n = r.name;
                menu.AddItem(new GUIContent(n), property.stringValue == n, () => { target.Update(); target.FindProperty(path).stringValue = n; target.ApplyModifiedProperties(); });
            }
            menu.ShowAsContext();
        }
    }
}
