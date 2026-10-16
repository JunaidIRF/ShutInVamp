using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System;
using UnityEditor;

public static class SharedFieldCopier
{
    private static Dictionary<string, object> copiedFields = new Dictionary<string, object>();

    public static void CopyFields(Component component)
    {
        copiedFields.Clear();
        var fields = component.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        foreach (var field in fields)
        {
            if (field.IsPublic || field.IsDefined(typeof(SerializeField), true))
            {
                object value = field.GetValue(component);

                if (value is IList list)
                {
                    if (value.GetType().IsArray)
                    {
                        var elementType = value.GetType().GetElementType();
                        var arrayCopy = System.Array.CreateInstance(elementType, list.Count);
                        for (int i = 0; i < list.Count; i++)
                        {
                            arrayCopy.SetValue(list[i], i);
                        }
                        copiedFields[field.Name] = arrayCopy;
                    }
                    else
                    {
                        var listCopy = (IList)Activator.CreateInstance(value.GetType());
                        foreach (var item in list)
                        {
                            listCopy.Add(item);
                        }
                        copiedFields[field.Name] = listCopy;
                    }
                }
                else
                {
                    copiedFields[field.Name] = value;
                }

            }
        }

        Debug.Log($"Copied fields from {component.GetType().Name}");
    }

    public static void PasteFields(Component component)
    {
        var fields = component.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        Undo.RecordObject(component, "Paste Shared Fields");

        foreach (var field in fields)
        {
            if (field.IsPublic || field.IsDefined(typeof(SerializeField), true))
            {
                if (copiedFields.ContainsKey(field.Name))
                {
                    object copiedValue = copiedFields[field.Name];

                    if (copiedValue != null && AreTypesCompatible(field.FieldType, copiedValue.GetType()))
                    {
                        field.SetValue(component, copiedValue);
                    }
                }
            }
        }

        EditorUtility.SetDirty(component);
        Debug.Log($"Pasted fields into {component.GetType().Name}");
    }

    public static bool HasCopiedFields()
    {
        return copiedFields.Count > 0;
    }

    private static bool AreTypesCompatible(System.Type fieldType, System.Type copiedType)
    {
        if (fieldType == copiedType)
            return true;

        if (typeof(IList).IsAssignableFrom(fieldType) && typeof(IList).IsAssignableFrom(copiedType))
            return true;

        return false;
    }
}
