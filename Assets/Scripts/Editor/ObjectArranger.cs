using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ObjectArranger : EditorWindow
{
    // ----- Source -----
    [SerializeField] bool usePrefab = false;
    [SerializeField] GameObject prefabSource;            // asset prefab OR (if usePrefab=false) ignored
    [SerializeField] int copiesPerSource = 5;

    // ----- Modes -----
    enum Mode { Line, Grid, Circle, Scatter }
    [SerializeField] Mode mode = Mode.Line;

    // Line
    enum Axis { X, Y, Z }
    [SerializeField] Axis lineAxis = Axis.Z;
    [SerializeField] float spacing = 2f;

    // Grid
    [SerializeField] float gridSpacing = 2f;

    // Circle
    [SerializeField] float radius = 5f;

    // Scatter
    [SerializeField] Vector3 scatterArea = new Vector3(10f, 0f, 10f);

    // Rotation / scale
    [SerializeField] Vector3 rotationStep = Vector3.zero;
    [SerializeField] bool randomRotation = false;
    [SerializeField] bool randomYOnly = true;
    [SerializeField] bool randomScale = false;
    [SerializeField] Vector3 minScale = Vector3.one;
    [SerializeField] Vector3 maxScale = Vector3.one * 1.2f;

    // Origin & Per-object
    [SerializeField] bool perObjectMode = false;

    // Surface
    [SerializeField] bool alignToSurface = false;
    [SerializeField] LayerMask surfaceMask = ~0;

    // Brush
    [SerializeField] bool brushMode = false;
    [SerializeField] float brushRadius = 2f;
    [SerializeField] int brushDensity = 3;
    [SerializeField] bool useTotalBoundsForBrush = true; // <--- NEW: Checkbox variable for the new feature

    // Other
    [SerializeField] bool replaceSelection = false;

    [MenuItem("Tools/Object Arranger")]
    static void Open() => GetWindow<ObjectArranger>("Object Arranger");

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }



    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    void OnGUI()
    {
        GUILayout.Label("Source", EditorStyles.boldLabel);
        usePrefab = EditorGUILayout.Toggle("Use Prefab (asset)", usePrefab);
        if (usePrefab)
            prefabSource = (GameObject)EditorGUILayout.ObjectField("Prefab", prefabSource, typeof(GameObject), false);
        else
            EditorGUILayout.HelpBox("Using selected object(s) in Hierarchy as source(s).", MessageType.Info);

        copiesPerSource = Mathf.Max(1, EditorGUILayout.IntField("Copies Per Source", copiesPerSource));

        EditorGUILayout.Space();
        GUILayout.Label("Arrangement", EditorStyles.boldLabel);
        mode = (Mode)EditorGUILayout.EnumPopup("Mode", mode);

        if (mode == Mode.Line)
        {
            lineAxis = (Axis)EditorGUILayout.EnumPopup("Axis", lineAxis);
            spacing = EditorGUILayout.FloatField("Spacing", spacing);
        }
        else if (mode == Mode.Grid)
        {
            gridSpacing = EditorGUILayout.FloatField("Cell Size", gridSpacing);
        }
        else if (mode == Mode.Circle)
        {
            radius = EditorGUILayout.FloatField("Radius", radius);
        }
        else if (mode == Mode.Scatter)
        {
            scatterArea = EditorGUILayout.Vector3Field("Area (X,Y,Z)", scatterArea);
        }

        EditorGUILayout.Space();
        // --- FIX 1: Using GUIContent to shorten labels and add tooltips ---
        perObjectMode = EditorGUILayout.Toggle(new GUIContent("Per Object Mode", "Each selected object seeds its own pattern instead of one large group arrangement."), perObjectMode); // <--- MODIFIED

        EditorGUILayout.Space();
        GUILayout.Label("Rotation / Scale", EditorStyles.boldLabel);
        rotationStep = EditorGUILayout.Vector3Field("Rotation Step (per instance)", rotationStep);
        randomRotation = EditorGUILayout.Toggle("Random Rotation", randomRotation);
        if (randomRotation) randomYOnly = EditorGUILayout.Toggle("Random Y Only", randomYOnly);
        randomScale = EditorGUILayout.Toggle("Random Scale", randomScale);
        if (randomScale)
        {
            minScale = EditorGUILayout.Vector3Field("Min Scale", minScale);
            maxScale = EditorGUILayout.Vector3Field("Max Scale", maxScale);
        }

        EditorGUILayout.Space();
        GUILayout.Label("Surface", EditorStyles.boldLabel);
        // --- FIX 1: Using GUIContent ---
        alignToSurface = EditorGUILayout.Toggle(new GUIContent("Align To Surface", "Raycasts down from the spawn position to place the object on the first surface it hits."), alignToSurface); // <--- MODIFIED
        if (alignToSurface) surfaceMask = LayerMaskField("Surface Mask", surfaceMask);

        EditorGUILayout.Space();
        // --- FIX 1: Using GUIContent ---
        replaceSelection = EditorGUILayout.Toggle(new GUIContent("Replace Selection", "Doesn't create new objects, just applies random rotation/scale to the current selection."), replaceSelection); // <--- MODIFIED

        EditorGUILayout.Space();
        GUILayout.Label("Brush (Scene View)", EditorStyles.boldLabel);
        brushMode = EditorGUILayout.Toggle("Enable Brush Mode", brushMode);
        if (brushMode)
        {
            brushRadius = EditorGUILayout.FloatField("Brush Radius", brushRadius);
            brushDensity = Mathf.Max(1, EditorGUILayout.IntField("Brush Density (per click)", brushDensity));
            // --- FIX 2: Added the checkbox UI ---
            useTotalBoundsForBrush = EditorGUILayout.Toggle(new GUIContent("Use Total Bounds", "When using Brush in Center pivot mode, calculates the bounds of all child objects to prevent spawning below the ground."), useTotalBoundsForBrush); // <--- NEW
            EditorGUILayout.HelpBox("Brush uses the prefab if 'Use Prefab' is checked, otherwise uses the active selected object in Hierarchy.", MessageType.Info);
        }

        EditorGUILayout.Space(10);
        if (GUILayout.Button("Generate / Apply"))
        {
            if (!usePrefab && Selection.gameObjects.Length == 0 && !replaceSelection)
            {
                Debug.LogError("Select at least one object in the hierarchy (or enable prefab).");
                return;
            }
            if (usePrefab && prefabSource == null && !replaceSelection)
            {
                Debug.LogError("Assign a prefab asset if 'Use Prefab' is enabled.");
                return;
            }

            ArrangeObjects();
        }
    }

    void ArrangeObjects()
    {
        // Replace selection: just randomize selected transforms and exit
        if (replaceSelection && Selection.gameObjects.Length > 0)
        {
            foreach (var go in Selection.gameObjects)
                ApplyRandomization(go.transform, 0);
            return;
        }

        // Prepare source list
        List<GameObject> sources = new List<GameObject>();
        if (usePrefab)
        {
            // When using prefab mode we will treat prefabSource as the single source repeated copiesPerSource times
            for (int i = 0; i < copiesPerSource; i++) sources.Add(prefabSource);
        }
        else
        {
            var sel = Selection.gameObjects;
            if (sel.Length == 0) { Debug.LogError("No selection found."); return; }
            foreach (var s in sel)
                for (int i = 0; i < copiesPerSource; i++)
                    sources.Add(s);
        }

        Transform parent = new GameObject("Arranged_" + mode.ToString()).transform;
        Undo.RegisterCreatedObjectUndo(parent.gameObject, "Create Arranged Parent");

        if (perObjectMode && !usePrefab)
        {
            // Each selection object should seed its own pattern
            var originalSelections = Selection.gameObjects;
            foreach (var seed in originalSelections)
            {
                // For each seed create copiesPerSource instances arranged from seed's position
                for (int i = 0; i < copiesPerSource; i++)
                {
                    Vector3 pos = GetPositionForIndex(mode, i, seed.transform.position);
                    if (alignToSurface)
                    {
                        if (Physics.Raycast(pos + Vector3.up * 1000f, Vector3.down, out var hit, Mathf.Infinity, surfaceMask)) pos = hit.point;
                    }

                    GameObject inst = CreateInstanceFromSource(seed, parent);
                    if (inst == null) continue;
                    inst.transform.position = pos;
                    inst.transform.rotation = seed.transform.rotation * Quaternion.Euler(rotationStep * i);
                    ApplyRandomization(inst.transform, i);
                }
            }
            return;
        }

        // Grouped arrangement (all sources treated in order)
        for (int idx = 0; idx < sources.Count; idx++)
        {
            GameObject src = sources[idx];
            Vector3 posBase = GetPositionForIndex(mode, idx, Vector3.zero); // for grouped, offsets from origin (0,0,0)
            // If using prefab, we place around origin. If using scene objects, we keep relative positions (use their transforms)
            if (!usePrefab)
            {
                // If we used perObjectMode we'd have returned earlier. Here we just line them up from world origin.
                posBase = GetPositionForIndex(mode, idx, Vector3.zero);
            }

            // compute world position
            Vector3 worldPos = posBase;
            if (alignToSurface)
            {
                if (Physics.Raycast(worldPos + Vector3.up * 1000f, Vector3.down, out var hit, Mathf.Infinity, surfaceMask)) worldPos = hit.point;
            }

            GameObject inst = CreateInstanceFromSource(src, parent);
            if (inst == null) continue;

            // set transform
            // If src is a scene object and we want to copy its rotation: use its rotation, otherwise identity
            Quaternion baseRot = (usePrefab) ? Quaternion.identity : src.transform.rotation;
            inst.transform.position = worldPos;
            inst.transform.rotation = baseRot * Quaternion.Euler(rotationStep * idx);
            ApplyRandomization(inst.transform, idx);
        }
    }

    Vector3 GetPositionForIndex(Mode m, int index, Vector3 seedPosition)
    {
        switch (m)
        {
            case Mode.Line:
                Vector3 v = (lineAxis == Axis.X) ? Vector3.right : (lineAxis == Axis.Y) ? Vector3.up : Vector3.forward;
                return seedPosition + v * spacing * index;

            case Mode.Grid:
                int cols = Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, index + 1)));
                int x = index % cols;
                int z = index / cols;
                return seedPosition + new Vector3(x * gridSpacing, 0f, z * gridSpacing);

            case Mode.Circle:
                if (index == 0) return seedPosition;
                float a = (index / (float)Mathf.Max(1, copiesPerSource)) * Mathf.PI * 2f;
                return seedPosition + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;

            case Mode.Scatter:
                return seedPosition + new Vector3(
                    Random.Range(-scatterArea.x * 0.5f, scatterArea.x * 0.5f),
                    Random.Range(-scatterArea.y * 0.5f, scatterArea.y * 0.5f),
                    Random.Range(-scatterArea.z * 0.5f, scatterArea.z * 0.5f)
                );
        }
        return seedPosition;
    }

    GameObject CreateInstanceFromSource(GameObject src, Transform parent)
    {
        GameObject inst = null;
        if (usePrefab)
        {
            if (prefabSource == null) return null;
            // Instantiate prefab asset, parented
#if UNITY_2018_3_OR_NEWER
            inst = PrefabUtility.InstantiatePrefab(prefabSource, parent) as GameObject;
#else
            inst = Instantiate(prefabSource) as GameObject;
            inst.transform.SetParent(parent, true);
#endif
        }
        else
        {
            if (src == null) return null;
            // Clone the scene object
            inst = Object.Instantiate(src);
            // make sure the instance is not linked as a prefab instance, give it unique name
            inst.name = src.name + "_Copy";
            // register undo then parent
            Undo.RegisterCreatedObjectUndo(inst, "Create Instance");
            Undo.SetTransformParent(inst.transform, parent, "Parent Instance");
            return inst;
        }

        // For prefab instantiation path we must still register Undo (PrefabUtility.InstantiatePrefab might not auto register in all versions)
        if (inst != null)
        {
            Undo.RegisterCreatedObjectUndo(inst, "Create Prefab Instance");
            // Ensure parent is set (if PrefabUtility.InstantiatePrefab didn't parent them)
            if (inst.transform.parent == null && parent != null)
                Undo.SetTransformParent(inst.transform, parent, "Parent Prefab Instance");
        }
        return inst;
    }

    void ApplyRandomization(Transform t, int index)
    {
        if (t == null) return;

        // Random rotation
        if (randomRotation)
        {
            if (randomYOnly) t.rotation *= Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            else t.rotation = Random.rotation;
        }

        // Random scale
        if (randomScale)
        {
            t.localScale = new Vector3(
                Random.Range(minScale.x, maxScale.x),
                Random.Range(minScale.y, maxScale.y),
                Random.Range(minScale.z, maxScale.z)
            );
        }
    }

    // ----- Scene GUI (brush + visualizer) -----
    void OnSceneGUI(SceneView sv)
    {
        if (!brushMode) return;

        Event e = Event.current;
        // draw brush indicator under mouse
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
        {
            // draw wire + filled disc
            Handles.color = new Color(1f, 0.5f, 0f, 0.1f);
            Handles.DrawSolidDisc(hit.point, hit.normal, brushRadius * 0.5f);
            Handles.color = new Color(1f, 0.8f, 0.2f, 1f);
            Handles.DrawWireDisc(hit.point, hit.normal, brushRadius * 0.5f);
            SceneView.RepaintAll();

            // on left-click paint
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && !e.shift && !e.control)
            {
                // source object for brush: prefab if enabled else Selection.activeGameObject
                GameObject brushSource = usePrefab ? prefabSource : Selection.activeGameObject;
                if (brushSource == null)
                {
                    Debug.LogWarning("No brush source: assign prefab or select an object in hierarchy.");
                    return;
                }

                Transform brushParent = new GameObject("Brush_Painted").transform;
                Undo.RegisterCreatedObjectUndo(brushParent.gameObject, "Create Brush Parent");

                for (int i = 0; i < brushDensity; i++)
                {
                    // Only scatter on X/Z, not Y
                    Vector2 circle = Random.insideUnitCircle * brushRadius * 0.5f;
                    Vector3 pos = hit.point + new Vector3(circle.x, 0f, circle.y);

                    // project onto surface if aligning
                    if (alignToSurface && Physics.Raycast(pos + Vector3.up * 1000f, Vector3.down, out var h2, Mathf.Infinity, surfaceMask))
                        pos = h2.point;

                    GameObject inst = null;
                    if (usePrefab)
                    {
                        inst = PrefabUtility.InstantiatePrefab(prefabSource, brushParent) as GameObject;
                        if (inst != null)
                        {
                            Undo.RegisterCreatedObjectUndo(inst, "Paint Prefab");
                            if (inst.transform.parent == null) Undo.SetTransformParent(inst.transform, brushParent, "Parent Painted Prefab");
                        }
                    }
                    else
                    {
                        inst = Object.Instantiate(brushSource);
                        inst.name = brushSource.name + "_Painted";
                        if (inst != null)
                        {
                            Undo.RegisterCreatedObjectUndo(inst, "Paint Instance");
                            Undo.SetTransformParent(inst.transform, brushParent, "Parent Painted Instance");
                        }
                    }

                    if (inst == null) continue;

                    // --- FIX 2: Replaced the old "Center Mode" logic with a more robust one ---
                    if (Tools.pivotMode == PivotMode.Pivot)
                    {
                        inst.transform.position = pos;
                    }
                    else // Center mode
                    {
                        if (useTotalBoundsForBrush) // Use new, accurate method
                        {
                            Renderer[] allRenderers = inst.GetComponentsInChildren<Renderer>();
                            if (allRenderers.Length > 0)
                            {
                                Bounds totalBounds = allRenderers[0].bounds;
                                for (int j = 1; j < allRenderers.Length; j++)
                                {
                                    totalBounds.Encapsulate(allRenderers[j].bounds);
                                }
                                float bottomOffset = inst.transform.position.y - totalBounds.min.y;
                                inst.transform.position = pos + Vector3.up * bottomOffset;
                            }
                            else
                            {
                                inst.transform.position = pos; // Fallback if no renderers
                            }
                        }
                        else // Use old method (kept for legacy/comparison)
                        {
                            Renderer rend = inst.GetComponentInChildren<Renderer>();
                            if (rend != null)
                            {
                                Bounds bounds = rend.bounds;
                                float bottomOffset = inst.transform.position.y - bounds.min.y;
                                inst.transform.position = pos + Vector3.up * bottomOffset;
                            }
                            else
                            {
                                inst.transform.position = pos;
                            }
                        }
                    }
                    // --- END OF FIX 2 ---

                    // Align to surface
                    if (alignToSurface) inst.transform.up = hit.normal;

                    // optional randomization
                    if (randomRotation)
                    {
                        if (randomYOnly) inst.transform.rotation *= Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                        else inst.transform.rotation = Random.rotation;
                    }
                    if (randomScale)
                    {
                        inst.transform.localScale = new Vector3(
                            Random.Range(minScale.x, maxScale.x),
                            Random.Range(minScale.y, maxScale.y),
                            Random.Range(minScale.z, maxScale.z)
                        );
                    }
                }

                e.Use();
            }
        }
    }

    // ----- Helpers -----
    public static LayerMask LayerMaskField(string label, LayerMask layerMask)
    {
        var layers = UnityEditorInternal.InternalEditorUtility.layers;
        var layerNumbers = new int[layers.Length];
        for (int i = 0; i < layers.Length; i++) layerNumbers[i] = LayerMask.NameToLayer(layers[i]);

        int maskWithoutEmpty = 0;
        for (int i = 0; i < layerNumbers.Length; i++)
            if (((1 << layerNumbers[i]) & layerMask.value) != 0) maskWithoutEmpty |= (1 << i);

        maskWithoutEmpty = EditorGUILayout.MaskField(label, maskWithoutEmpty, layers);

        int mask = 0;
        for (int i = 0; i < layerNumbers.Length; i++)
            if ((maskWithoutEmpty & (1 << i)) != 0) mask |= (1 << layerNumbers[i]);

        layerMask.value = mask;
        return layerMask;
    }
}