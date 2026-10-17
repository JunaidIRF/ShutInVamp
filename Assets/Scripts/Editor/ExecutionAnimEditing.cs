using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Animate an execution as ONE clip in Unity's normal Animation window.
///
/// "Animate" (on an ExecutionData asset) opens Assets/Scenes/ExecutionStage.unity with an "Execution Rig":
///   Execution Rig (Animator)
///     Player  — a copy of the player's Camera Head (CamParent / RHand / LHand), placed like in the game
///     Enemy   — a copy of the enemy model, placed like in the game
/// and an editing clip that holds both characters' curves ("Player/..." and "Enemy/..."), so both are
/// keyed, scrubbed and snapped on the same timeline. Event timing is Animation Event markers on that
/// timeline: "Kill" = kill time, every other marker = one execution event (in time order).
///
/// Every change to the editing clip is copied straight back into the game's two clips and the
/// ExecutionData times. The game clips are the source of truth: the editing clip is rebuilt from them
/// each time you press Animate. The stage is a scratch scene; nothing in it is used by the game.
/// </summary>
[InitializeOnLoad]
public static class ExecutionAnimEditing
{
    const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
    const string EnemyPrefabPath = "Assets/Prefabs/Shooter.prefab";
    const string StagePath = "Assets/Scenes/ExecutionStage.unity";
    const string EditDir = "Assets/Animations/Executions";
    const string RigControllerPath = EditDir + "/ExecutionRig.controller";
    const string CameraHeadPath = "Bob Target/Camera Head";
    const string PlayerPrefix = "Player";
    const string EnemyPrefix = "Enemy";
    const string KillMarker = "Kill";
    const string EventMarker = "Event";
    // the only player parts the runtime clips may animate (Camera Head itself is driven by mouse look)
    static readonly string[] PlayerParts = { "CamParent", "RHand", "LHand" };

    const string SessionClip = "ExecAnimEditing.clip";
    const string SessionData = "ExecAnimEditing.data";

    static int lastDirty = -1;
    static double nextPoll;

    static ExecutionAnimEditing()
    {
        EditorApplication.update += Poll;
    }

    // ══════════════════════════════════════════════════════════════════
    //  LOOKUPS
    // ══════════════════════════════════════════════════════════════════

    static AnimatorController PlayerController()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Transform head = prefab != null ? prefab.transform.Find(CameraHeadPath) : null;
        Animator a = head != null ? head.GetComponent<Animator>() : null;
        return a != null ? a.runtimeAnimatorController as AnimatorController : null;
    }

    static AnimatorController EnemyController()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        Shooter s = prefab != null ? prefab.GetComponent<Shooter>() : null;
        return s != null && s.anim != null ? s.anim.runtimeAnimatorController as AnimatorController : null;
    }

    static AnimatorState FindState(AnimatorController c, string name, out Vector3 position)
    {
        position = Vector3.zero;
        if (c == null || string.IsNullOrEmpty(name)) return null;
        foreach (var child in c.layers[0].stateMachine.states)
            if (child.state.name == name) { position = child.position; return child.state; }
        return null;
    }

    public static AnimationClip PlayerClip(ExecutionData d) => d != null ? FindState(PlayerController(), d.stateName, out _)?.motion as AnimationClip : null;
    public static AnimationClip EnemyClip(ExecutionData d) => d != null ? FindState(EnemyController(), d.stateName, out _)?.motion as AnimationClip : null;

    public static ExecutionData EditingData => AssetDatabase.LoadAssetAtPath<ExecutionData>(SessionState.GetString(SessionData, ""));
    static AnimationClip EditingClip => AssetDatabase.LoadAssetAtPath<AnimationClip>(SessionState.GetString(SessionClip, ""));

    // ══════════════════════════════════════════════════════════════════
    //  OPEN
    // ══════════════════════════════════════════════════════════════════

    public static void Open(ExecutionData data)
    {
        AnimationClip pc = PlayerClip(data), ec = EnemyClip(data);
        if (pc == null || ec == null)
        {
            EditorUtility.DisplayDialog("Animate Execution", $"State '{data.stateName}' needs a clip in both Camera Head.controller and the enemy controller.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        ApplyNow();   // don't lose edits from a previous session

        AnimationClip edit = BuildEditingClip(data, pc, ec);
        var ctrl = BuildRigController(edit);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var light = new GameObject("Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(2f, 1f, 2f);
        GameObject rig = BuildRig(data, ec, ctrl);
        Directory.CreateDirectory(Path.GetDirectoryName(StagePath));
        EditorSceneManager.SaveScene(scene, StagePath);

        SessionState.SetString(SessionClip, AssetDatabase.GetAssetPath(edit));
        SessionState.SetString(SessionData, AssetDatabase.GetAssetPath(data));
        lastDirty = EditorUtility.GetDirtyCount(edit);

        Selection.activeGameObject = rig;
        var aw = EditorWindow.GetWindow<AnimationWindow>();
        EditorApplication.delayCall += () =>
        {
            if (aw == null) return;
            aw.animationClip = edit;
            aw.previewing = true;
            aw.time = 0f;
        };
    }

    /// <summary>One clip with both characters' curves under "Player/" and "Enemy/", plus timing markers.</summary>
    static AnimationClip BuildEditingClip(ExecutionData data, AnimationClip pc, AnimationClip ec)
    {
        if (!AssetDatabase.IsValidFolder(EditDir)) AssetDatabase.CreateFolder("Assets/Animations", "Executions");
        string path = $"{EditDir}/{data.stateName} (edit both).anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.frameRate = Mathf.Max(pc.frameRate, ec.frameRate);

        CopyCurves(pc, clip, b => Prefixed(PlayerPrefix, b.path));
        CopyCurves(ec, clip, b => Prefixed(EnemyPrefix, b.path));

        var markers = new System.Collections.Generic.List<AnimationEvent> { new AnimationEvent { time = data.killTime, functionName = KillMarker } };
        if (data.events != null) markers.AddRange(data.events.Select(e => new AnimationEvent { time = e.time, functionName = EventMarker }));
        AnimationUtility.SetAnimationEvents(clip, markers.OrderBy(m => m.time).ToArray());

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssetIfDirty(clip);
        return clip;
    }

    static AnimatorController BuildRigController(AnimationClip edit)
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(RigControllerPath);
        if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(RigControllerPath);
        var sm = ctrl.layers[0].stateMachine;
        foreach (var s in sm.states.ToArray()) sm.RemoveState(s.state);
        var st = sm.AddState("Edit");
        st.motion = edit;
        st.speed = 0f;   // the Animation window previews it; in Play mode it just holds frame 0 (no event spam)
        sm.defaultState = st;
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssetIfDirty(ctrl);
        return ctrl;
    }

    /// <summary>Copies of the player's Camera Head and the enemy model, placed exactly like PlayerExecution does.</summary>
    static GameObject BuildRig(ExecutionData data, AnimationClip ec, AnimatorController ctrl)
    {
        var rig = new GameObject("Execution Rig");
        rig.AddComponent<Animator>().runtimeAnimatorController = ctrl;

        // plain copies of the prefabs (not prefab instances), only used to place the two parts
        var enemyRoot = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath));
        var playerRoot = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath));
        try
        {
            enemyRoot.transform.SetPositionAndRotation(new Vector3(0f, FeetBelowPivot(enemyRoot), 0f), Quaternion.identity);
            GameObject model = enemyRoot.GetComponent<Shooter>().anim.gameObject;
            ec.SampleAnimation(model, 0f);   // first frame, where the player lines up
            Transform bone = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == data.focusBone);
            Vector3 focus = bone != null ? bone.position : model.transform.position + Vector3.up * 1.4f;

            Vector3 goal = enemyRoot.transform.TransformPoint(data.playerOffset);
            goal.y = FeetBelowPivot(playerRoot);
            playerRoot.transform.position = goal;
            Transform head = playerRoot.transform.Find(CameraHeadPath);
            Transform cam = head.GetComponentsInChildren<Camera>(true).Select(c => c.transform).FirstOrDefault(t => t.name == "Player Camera") ?? head;
            for (int i = 0; i < 4; i++)   // the camera sits off the head pivot, so settle a few times
            {
                Vector3 dir = (focus - cam.position).normalized;
                Vector3 flat = new Vector3(dir.x, 0f, dir.z);
                if (flat.sqrMagnitude > 0.0001f) playerRoot.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
                head.localRotation = Quaternion.Euler(Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg, -89f, 89f), 0f, 0f);
            }

            // move the two animated parts under the rig; their own Animators would steal the Animation window
            head.SetParent(rig.transform, true);
            head.name = PlayerPrefix;
            model.transform.SetParent(rig.transform, true);
            model.name = EnemyPrefix;
            foreach (var a in head.GetComponentsInChildren<Animator>(true).Concat(model.GetComponentsInChildren<Animator>(true)))
                Object.DestroyImmediate(a);
        }
        finally
        {
            Object.DestroyImmediate(enemyRoot);
            Object.DestroyImmediate(playerRoot);
        }
        return rig;
    }

    static float FeetBelowPivot(GameObject go)
    {
        var cc = go.GetComponent<CharacterController>();
        if (cc != null) return (cc.height * 0.5f - cc.center.y) * go.transform.lossyScale.y;
        var cap = go.GetComponent<CapsuleCollider>();
        if (cap != null) return (cap.height * 0.5f - cap.center.y) * go.transform.lossyScale.y;
        return 0f;
    }

    // ══════════════════════════════════════════════════════════════════
    //  WRITE BACK — editing clip -> the game's two clips + ExecutionData times
    // ══════════════════════════════════════════════════════════════════

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 0.3;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        AnimationClip edit = EditingClip;
        if (edit == null) return;
        int dirty = EditorUtility.GetDirtyCount(edit);
        if (lastDirty == -1) { lastDirty = dirty; return; }   // after a script reload
        if (dirty == lastDirty) return;
        lastDirty = dirty;
        WriteBack(edit, EditingData);
    }

    /// <summary>Copy the current editing session into the game assets right now.</summary>
    public static void ApplyNow()
    {
        AnimationClip edit = EditingClip;
        ExecutionData data = EditingData;
        if (edit != null && data != null) WriteBack(edit, data);
    }

    static void WriteBack(AnimationClip edit, ExecutionData data)
    {
        AnimationClip pc = PlayerClip(data), ec = EnemyClip(data);
        if (data == null || pc == null || ec == null || edit.name != $"{data.stateName} (edit both)") return;

        var dropped = new System.Collections.Generic.List<string>();
        var playerCurves = new System.Collections.Generic.List<(EditorCurveBinding, AnimationCurve)>();
        var enemyCurves = new System.Collections.Generic.List<(EditorCurveBinding, AnimationCurve)>();
        foreach (var b in AnimationUtility.GetCurveBindings(edit))
        {
            string sub;
            if (TryStrip(b.path, PlayerPrefix, out sub) && PlayerParts.Any(p => sub == p || sub.StartsWith(p + "/")))
                playerCurves.Add((Rebased(b, sub), AnimationUtility.GetEditorCurve(edit, b)));
            else if (TryStrip(b.path, EnemyPrefix, out sub) && sub.Length > 0)
                enemyCurves.Add((Rebased(b, sub), AnimationUtility.GetEditorCurve(edit, b)));
            else
                dropped.Add($"{b.path}.{b.propertyName}");
        }

        Fill(pc, playerCurves, edit.frameRate);
        Fill(ec, enemyCurves, edit.frameRate);

        var markers = AnimationUtility.GetAnimationEvents(edit).OrderBy(m => m.time).ToArray();
        var kill = markers.FirstOrDefault(m => m.functionName == KillMarker);
        var times = markers.Where(m => m != kill).Select(m => m.time).ToArray();
        Undo.RecordObject(data, "Execution timing");
        if (kill != null) data.killTime = kill.time;
        var old = data.events ?? new ExecutionData.Event[0];
        data.events = times.Select((t, i) =>
        {
            var e = i < old.Length ? old[i] : new ExecutionData.Event { blood = true, shake = 0.8f, shakeTime = 0.15f };
            e.time = t;
            return e;
        }).ToArray();
        data.duration = edit.length;
        EditorUtility.SetDirty(data);

        AssetDatabase.SaveAssetIfDirty(pc);
        AssetDatabase.SaveAssetIfDirty(ec);
        AssetDatabase.SaveAssetIfDirty(data);
        if (dropped.Count > 0)
            Debug.LogWarning($"Execution '{data.stateName}': not copied to the game clips (only Player/{string.Join(", Player/", PlayerParts)} and the enemy's bones are used): {string.Join(", ", dropped.Distinct())}");
    }

    static void Fill(AnimationClip clip, System.Collections.Generic.List<(EditorCurveBinding b, AnimationCurve c)> curves, float frameRate)
    {
        clip.ClearCurves();
        clip.frameRate = frameRate;
        foreach (var (b, c) in curves) AnimationUtility.SetEditorCurve(clip, b, c);
        clip.EnsureQuaternionContinuity();
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
    }

    static void CopyCurves(AnimationClip from, AnimationClip to, System.Func<EditorCurveBinding, string> newPath)
    {
        foreach (var b in AnimationUtility.GetCurveBindings(from))
            AnimationUtility.SetEditorCurve(to, Rebased(b, newPath(b)), AnimationUtility.GetEditorCurve(from, b));
    }

    static EditorCurveBinding Rebased(EditorCurveBinding b, string path) { b.path = path; return b; }
    static string Prefixed(string prefix, string path) => path.Length == 0 ? prefix : prefix + "/" + path;

    static bool TryStrip(string path, string prefix, out string rest)
    {
        rest = null;
        if (path == prefix) { rest = ""; return true; }
        if (!path.StartsWith(prefix + "/")) return false;
        rest = path.Substring(prefix.Length + 1);
        return true;
    }

    /// <summary>Saving the project (Ctrl+S) also applies the current session.</summary>
    class SaveHook : AssetModificationProcessor
    {
        static string[] OnWillSaveAssets(string[] paths)
        {
            string edit = SessionState.GetString(SessionClip, "");
            if (edit.Length > 0 && paths.Contains(edit)) EditorApplication.delayCall += ApplyNow;
            return paths;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  NEW EXECUTION — copies the clips, both controller states and the data asset
    // ══════════════════════════════════════════════════════════════════

    public static ExecutionData Duplicate(ExecutionData data, string rawName)
    {
        string name = new string((rawName ?? "").Where(char.IsLetterOrDigit).ToArray());
        if (name.Length == 0) { EditorUtility.DisplayDialog("New Execution", "Use letters/digits for the name.", "OK"); return null; }

        AnimationClip srcP = PlayerClip(data), srcE = EnemyClip(data);
        AnimatorController pcCtrl = PlayerController(), ecCtrl = EnemyController();
        AnimatorState srcPState = FindState(pcCtrl, data.stateName, out Vector3 pPos);
        AnimatorState srcEState = FindState(ecCtrl, data.stateName, out Vector3 ePos);
        if (srcP == null || srcE == null || srcPState == null || srcEState == null)
        {
            EditorUtility.DisplayDialog("New Execution", "This execution's states/clips are missing, so there is nothing to copy.", "OK");
            return null;
        }

        string state = "Exec" + name;
        string pClip = $"{Path.GetDirectoryName(AssetDatabase.GetAssetPath(srcP)).Replace('\\', '/')}/{state}.anim";
        string eClip = $"{Path.GetDirectoryName(AssetDatabase.GetAssetPath(srcE)).Replace('\\', '/')}/{state}.anim";
        string dataPath = $"{Path.GetDirectoryName(AssetDatabase.GetAssetPath(data)).Replace('\\', '/')}/{name}.asset";
        if (File.Exists(pClip) || File.Exists(eClip) || File.Exists(dataPath) || FindState(pcCtrl, state, out _) != null || FindState(ecCtrl, state, out _) != null)
        {
            EditorUtility.DisplayDialog("New Execution", $"'{name}' already exists.", "OK");
            return null;
        }

        AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(srcP), pClip);
        AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(srcE), eClip);
        CopyState(pcCtrl, srcPState, pPos, state, AssetDatabase.LoadAssetAtPath<AnimationClip>(pClip));
        CopyState(ecCtrl, srcEState, ePos, state, AssetDatabase.LoadAssetAtPath<AnimationClip>(eClip));

        var copy = Object.Instantiate(data);
        copy.stateName = state;
        AssetDatabase.CreateAsset(copy, dataPath);

        var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            var ex = root.GetComponent<PlayerExecution>();
            if (ex != null)
            {
                ex.executions = (ex.executions ?? new ExecutionData[0]).Concat(new[] { copy }).ToArray();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        AssetDatabase.SaveAssets();
        return copy;
    }

    static void CopyState(AnimatorController ctrl, AnimatorState src, Vector3 srcPos, string name, AnimationClip clip)
    {
        var st = ctrl.layers[0].stateMachine.AddState(name, srcPos + new Vector3(0f, 60f, 0f));
        st.motion = clip;
        st.tag = src.tag;
        st.speed = src.speed;
        st.writeDefaultValues = src.writeDefaultValues;
        foreach (var t in src.transitions)
        {
            if (t.destinationState == null) continue;
            var n = st.AddTransition(t.destinationState);
            n.hasExitTime = t.hasExitTime;
            n.exitTime = t.exitTime;
            n.hasFixedDuration = t.hasFixedDuration;
            n.duration = t.duration;
            n.offset = t.offset;
            foreach (var c in t.conditions) n.AddCondition(c.mode, c.threshold, c.parameter);
        }
        EditorUtility.SetDirty(ctrl);
    }
}

/// <summary>Buttons on an ExecutionData asset: Animate, Apply now, New execution.</summary>
[CustomEditor(typeof(ExecutionData))]
public class ExecutionDataEditor : Editor
{
    string newName = "";

    public override void OnInspectorGUI()
    {
        var data = (ExecutionData)target;
        bool editing = ExecutionAnimEditing.EditingData == data;

        if (GUILayout.Button("Animate (both characters, one clip)", GUILayout.Height(30)))
            ExecutionAnimEditing.Open(data);
        if (editing)
        {
            EditorGUILayout.HelpBox("Editing in ExecutionStage: select 'Execution Rig' in the Animation window. Changes copy to the game clips automatically. Markers: 'Kill' = kill time, others = events (in order).", MessageType.Info);
            if (GUILayout.Button("Apply to game clips now")) ExecutionAnimEditing.ApplyNow();
        }

        EditorGUILayout.Space();
        DrawDefaultInspector();

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            newName = EditorGUILayout.TextField("New from this", newName);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newName)))
                if (GUILayout.Button("Create", GUILayout.Width(60)))
                {
                    var made = ExecutionAnimEditing.Duplicate(data, newName);
                    if (made != null) { newName = ""; Selection.activeObject = made; }
                }
        }
    }
}
