using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(FenceTool))]
public class FenceToolEditor : Editor
{
    private bool isPickingPoints = false;

    public override void OnInspectorGUI()
    {
        FenceTool script = (FenceTool)target;

        // Draw standard inspector BUT exclude 'spacing' because we want to draw it manually
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script", "spacing");
        serializedObject.ApplyModifiedProperties();

        // --- CUSTOM SPACING ROW ---
        GUILayout.Space(5);
        EditorGUILayout.BeginHorizontal();
        {
            // Draw the float field
            script.spacing = EditorGUILayout.FloatField("Spacing", script.spacing);

            // Draw the Auto Button
            if (GUILayout.Button("Auto", GUILayout.Width(50)))
            {
                script.AutoCalculateSpacing();
            }
        }
        EditorGUILayout.EndHorizontal();
        // --------------------------

        GUILayout.Space(15);

        // POINTS SECTION
        GUI.backgroundColor = Color.cyan;
        if (isPickingPoints)
        {
            GUILayout.Box("CLICK SCENE TO ADD POINTS", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("STOP PICKING (Done)")) isPickingPoints = false;
        }
        else
        {
            if (GUILayout.Button("ADD POINTS (Click in Scene)", GUILayout.Height(30)))
            {
                isPickingPoints = true;
                SceneView.RepaintAll();
            }
        }

        GUI.backgroundColor = Color.red;
        if (GUILayout.Button("Clear All Points"))
        {
            if (EditorUtility.DisplayDialog("Clear?", "Delete all points?", "Yes", "Cancel"))
            {
                for (int i = script.points.Count - 1; i >= 0; i--)
                {
                    if (script.points[i] != null) DestroyImmediate(script.points[i].gameObject);
                }
                script.points.Clear();
            }
        }

        GUILayout.Space(10);

        // GENERATE SECTION
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("GENERATE FENCES", GUILayout.Height(40)))
        {
            script.GenerateFences();
        }

        GUILayout.Space(20);
        EditorGUILayout.LabelField("Cleanup Tools", EditorStyles.boldLabel);

        // CLEANUP SECTION
        GUI.backgroundColor = Color.yellow;
        if (GUILayout.Button("CENTER PIVOT (Fix 0,0,0)", GUILayout.Height(30)))
        {
            script.CenterPivotToFences();
        }

        GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
        if (GUILayout.Button("FINALIZE (Remove Script & Points)", GUILayout.Height(30)))
        {
            if (EditorUtility.DisplayDialog("Finalize?",
                "This removes the tool and points, leaving only the fences. Continue?", "Yes", "Cancel"))
            {
                script.FinalizeAndCleanup();
            }
        }
        GUI.backgroundColor = Color.white;
    }

    protected void OnSceneGUI()
    {
        if (!isPickingPoints) return;

        FenceTool script = (FenceTool)target;
        Event e = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 10000f))
            {
                Undo.RecordObject(script.gameObject, "Add Point");
                script.AddPoint(hit.point);
                e.Use();
            }
        }
    }

    [MenuItem("Tools/Fence Builder/Create New Fence Line")]
    public static void CreateFenceTool()
    {
        GameObject go = new GameObject("Fence Builder");
        FenceTool tool = go.AddComponent<FenceTool>();
        Selection.activeGameObject = go;
        SceneView.lastActiveSceneView.FrameSelected();
    }
}