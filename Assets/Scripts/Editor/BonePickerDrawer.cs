using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws a [BonePicker] string as a Transform field: drag a bone in (from the Animate stage's
/// Execution Rig/Enemy, or the enemy prefab) and its name is stored. None clears it.
/// The field shows the matching bone on the enemy prefab; a name it can't find is flagged.
/// </summary>
[CustomPropertyDrawer(typeof(BonePickerAttribute))]
public class BonePickerDrawer : PropertyDrawer
{
    const string EnemyPrefabPath = "Assets/Prefabs/Shooter.prefab";

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        string name = property.stringValue;
        Transform current = FindEnemyBone(name);
        var shown = new GUIContent(label);
        if (!string.IsNullOrEmpty(name) && current == null) shown.text += $" (missing '{name}')";

        EditorGUI.BeginProperty(position, label, property);
        EditorGUI.BeginChangeCheck();
        var picked = (Transform)EditorGUI.ObjectField(position, shown, current, typeof(Transform), true);
        if (EditorGUI.EndChangeCheck()) property.stringValue = picked != null ? picked.name : "";
        EditorGUI.EndProperty();
    }

    static Transform FindEnemyBone(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        Shooter s = prefab != null ? prefab.GetComponent<Shooter>() : null;
        return s != null && s.anim != null ? FindDeep(s.anim.transform, name) : null;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            Transform f = FindDeep(c, name);
            if (f != null) return f;
        }
        return null;
    }
}
