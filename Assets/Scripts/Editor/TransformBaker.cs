using UnityEngine;
using UnityEditor;
using System.IO;

public class TransformBaker : EditorWindow
{
    public enum BakeSpace { Local, World }

    private BakeSpace bakeSpace = BakeSpace.Local;
    private bool bakePosition = true;
    private bool bakeRotation = true;
    private bool bakeScale = true;
    private bool overwriteMesh = false;

    [MenuItem("Tools/Advanced Bake Transform...", false, 30)]
    public static void ShowWindow() { GetWindow<TransformBaker>("Bake Transform"); }

    void OnEnable() { Selection.selectionChanged += Repaint; }
    void OnDisable() { Selection.selectionChanged -= Repaint; }

    void OnGUI()
    {
        GUILayout.Label("Advanced Transform Baker", EditorStyles.boldLabel);
        GameObject selectedObject = Selection.activeGameObject;

        if (selectedObject == null)
        {
            EditorGUILayout.HelpBox("Select a GameObject in the Hierarchy.", MessageType.Info);
            return; // Exit here, no more code in OnGUI will run
        }

        MeshFilter meshFilter = selectedObject.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            EditorGUILayout.HelpBox("Selected GameObject must have a MeshFilter with a valid Mesh attached.", MessageType.Warning);
            return;
        }

        bool isImportedAsset = AssetDatabase.IsForeignAsset(meshFilter.sharedMesh);

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Bake Space", EditorStyles.boldLabel);
        bakeSpace = (BakeSpace)EditorGUILayout.EnumPopup(new GUIContent("Bake Space", "Local: Bakes transform relative to parent.\nWorld: Bakes absolute world transform and un-parents the object."), bakeSpace);

        if (bakeSpace == BakeSpace.Local)
            EditorGUILayout.HelpBox("Bakes the object's transform relative to its parent. Ideal for cleaning up hierarchies.", MessageType.Info);
        else
            EditorGUILayout.HelpBox("Bakes the object's absolute position in the world. The object will be un-parented to ensure it ends up at the true world origin (0,0,0).", MessageType.Warning);

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Properties to Bake", EditorStyles.boldLabel);
        bakePosition = EditorGUILayout.Toggle("Bake Position", bakePosition);
        bakeRotation = EditorGUILayout.Toggle("Bake Rotation", bakeRotation);
        bakeScale = EditorGUILayout.Toggle("Bake Scale", bakeScale);

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Output Mesh", EditorStyles.boldLabel);
        EditorGUI.BeginDisabledGroup(isImportedAsset);
        overwriteMesh = EditorGUILayout.Toggle(new GUIContent("Overwrite Original Mesh", "This is disabled for imported models (e.g., FBX)."), overwriteMesh);
        EditorGUI.EndDisabledGroup();

        if (isImportedAsset)
            EditorGUILayout.HelpBox("The selected mesh is from an imported model (FBX, etc.). A new Unity mesh asset (.asset) will be created in the same folder.", MessageType.Info);

        EditorGUILayout.Space();

        if (GUILayout.Button("Bake Selected Transform"))
        {
            bool doBake = true;
            if (overwriteMesh && !isImportedAsset)
            {
                doBake = EditorUtility.DisplayDialog("Confirm Destructive Bake", "You are about to OVERWRITE the mesh asset. This cannot be undone. Are you sure?", "Yes, Overwrite", "Cancel");
            }

            if (doBake)
            {
                Bake(selectedObject, overwriteMesh && !isImportedAsset);
            }
        }
    }

    private void Bake(GameObject target, bool overwrite)
    {
        MeshFilter meshFilter = target.GetComponent<MeshFilter>();
        Mesh originalMesh = meshFilter.sharedMesh;
        string originalPath = AssetDatabase.GetAssetPath(originalMesh);

        Mesh newMesh = new Mesh { name = originalMesh.name };
        newMesh.vertices = originalMesh.vertices;
        newMesh.normals = originalMesh.normals;
        newMesh.tangents = originalMesh.tangents;
        newMesh.uv = originalMesh.uv;
        newMesh.uv2 = originalMesh.uv2;
        newMesh.colors = originalMesh.colors;
        newMesh.subMeshCount = originalMesh.subMeshCount;
        for (int i = 0; i < originalMesh.subMeshCount; i++) { newMesh.SetTriangles(originalMesh.GetTriangles(i), i); }

        Matrix4x4 bakeMatrix;
        if (bakeSpace == BakeSpace.World)
        {
            bakeMatrix = target.transform.localToWorldMatrix;
        }
        else
        {
            Vector3 pos = bakePosition ? target.transform.localPosition : Vector3.zero;
            Quaternion rot = bakeRotation ? target.transform.localRotation : Quaternion.identity;
            Vector3 scale = bakeScale ? target.transform.localScale : Vector3.one;
            bakeMatrix = Matrix4x4.TRS(pos, rot, scale);
        }

        Vector3[] vertices = newMesh.vertices;
        Vector3[] normals = newMesh.normals;
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = bakeMatrix.MultiplyPoint3x4(vertices[i]);
            normals[i] = bakeMatrix.inverse.transpose.MultiplyVector(normals[i]).normalized;
        }
        newMesh.vertices = vertices;
        newMesh.normals = normals;
        newMesh.RecalculateBounds();
        newMesh.Optimize();

        Mesh finalMesh = SaveMesh(newMesh, target.name, originalPath, overwrite);
        if (finalMesh == null) return;

        Undo.RecordObject(meshFilter, "Assign Baked Mesh");
        meshFilter.sharedMesh = finalMesh;
        MeshCollider meshCollider = target.GetComponent<MeshCollider>();
        if (meshCollider != null) { Undo.RecordObject(meshCollider, "Assign Baked Mesh to Collider"); meshCollider.sharedMesh = finalMesh; }

        Undo.RecordObject(target.transform, "Reset Baked Transform");
        if (bakeSpace == BakeSpace.World)
        {
            target.transform.SetParent(null, true);
            if (bakePosition) target.transform.position = Vector3.zero;
            if (bakeRotation) target.transform.rotation = Quaternion.identity;
            if (bakeScale) target.transform.localScale = Vector3.one;
        }
        else
        {
            if (bakePosition) target.transform.localPosition = Vector3.zero;
            if (bakeRotation) target.transform.localRotation = Quaternion.identity;
            if (bakeScale) target.transform.localScale = Vector3.one;
        }

        EditorUtility.FocusProjectWindow();
        EditorGUIUtility.PingObject(finalMesh);
    }

    private Mesh SaveMesh(Mesh mesh, string gameObjectName, string originalPath, bool overwrite)
    {
        if (overwrite)
        {
            Mesh originalAsset = AssetDatabase.LoadAssetAtPath<Mesh>(originalPath);
            originalAsset.Clear();
            originalAsset.vertices = mesh.vertices;
            originalAsset.normals = mesh.normals;
            originalAsset.tangents = mesh.tangents;
            originalAsset.uv = mesh.uv;
            originalAsset.uv2 = mesh.uv2;
            originalAsset.colors = mesh.colors;
            originalAsset.subMeshCount = mesh.subMeshCount;
            for (int i = 0; i < mesh.subMeshCount; i++) { originalAsset.SetTriangles(mesh.GetTriangles(i), i); }
            originalAsset.RecalculateBounds();
            EditorUtility.SetDirty(originalAsset);
            Debug.Log("<b>Transform Baker:</b> Successfully overwrote mesh: " + originalAsset.name, originalAsset);
            return originalAsset;
        }
        else
        {
            mesh.name = gameObjectName + "_Baked";
            string directory = string.IsNullOrEmpty(originalPath) ? "Assets" : Path.GetDirectoryName(originalPath);
            string path = Path.Combine(directory, mesh.name + ".asset");
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(path);
            AssetDatabase.CreateAsset(mesh, uniquePath);
            Debug.Log("<b>Transform Baker:</b> New mesh created at " + uniquePath, AssetDatabase.LoadAssetAtPath<Mesh>(uniquePath));
            return mesh;
        }
    }
}