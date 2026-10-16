using UnityEditor;
using UnityEngine;

public static class SharedFieldCopierContextMenu
{
    [MenuItem("CONTEXT/MonoBehaviour/Copy Shared Fields")]
    private static void CopySharedFields(MenuCommand command)
    {
        var component = command.context as Component;
        if (component != null)
        {
            SharedFieldCopier.CopyFields(component);
        }
    }

    [MenuItem("CONTEXT/MonoBehaviour/Paste Shared Fields", true)]
    private static bool ValidatePasteSharedFields(MenuCommand command)
    {
        return SharedFieldCopier.HasCopiedFields();
    }

    [MenuItem("CONTEXT/MonoBehaviour/Paste Shared Fields")]
    private static void PasteSharedFields(MenuCommand command)
    {
        var component = command.context as Component;
        if (component != null)
        {
            SharedFieldCopier.PasteFields(component);
        }
    }
}
