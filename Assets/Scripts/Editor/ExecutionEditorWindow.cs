using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > ShutInVamp > Execution Editor — animate an execution's player and victim clips side by side.
///
/// Open Stage puts a Player and an enemy in their own scene (Assets/Scenes/ExecutionStage.unity), placed
/// exactly like PlayerExecution places them in the game. The time slider previews both clips together;
/// while Unity's Animation window previews/records one of them, the other follows its time.
/// The window's own preview uses AnimationMode (reverted when it stops). While following the Animation
/// window, the other character is posed directly and its bones are put back as soon as following ends
/// (or the scene is saved/closed, Play mode starts, scripts reload, the window closes), so poses are
/// never saved; only the .anim clips you record in the Animation window and the ExecutionData asset are.
/// </summary>
public class ExecutionEditorWindow : EditorWindow
{
    const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
    const string EnemyPrefabPath = "Assets/Prefabs/Shooter.prefab";
    const string StagePath = "Assets/Scenes/ExecutionStage.unity";
    const string DataDir = "Assets/Settings/Executions";
    const string StagePlayerName = "Stage Player";
    const string StageEnemyName = "Stage Enemy";
    const string CameraHeadPath = "Bob Target/Camera Head";
    const float FrameStep = 1f / 60f;

    [MenuItem("Tools/ShutInVamp/Execution Editor")]
    static void OpenWindow() => GetWindow<ExecutionEditorWindow>("Execution Editor");

    ExecutionData data;
    SerializedObject so;
    float time;
    bool playing;
    bool loop = true;
    double lastTick;
    double lastSample;
    float lastSampledTime = -1f;
    string newName = "";
    Vector2 scroll;
    AnimationModeDriver driver;

    // follow mode: the character the Animation window isn't editing, and its bones' rest values
    GameObject followTarget;
    readonly List<(Transform t, Vector3 p, Quaternion r, Vector3 s)> followRest = new List<(Transform, Vector3, Quaternion, Vector3)>();

    // ══════════════════════════════════════════════════════════════════
    //  LIFETIME — the preview is stopped whenever anything could keep it stuck on
    // ══════════════════════════════════════════════════════════════════

    void OnEnable()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorSceneManager.sceneClosing += OnSceneClosing;
        EditorSceneManager.sceneSaving += OnSceneSaving;
        AssemblyReloadEvents.beforeAssemblyReload += StopAll;
        if (data == null) data = AssetDatabase.FindAssets("t:ExecutionData", new[] { DataDir })
            .Select(g => AssetDatabase.LoadAssetAtPath<ExecutionData>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
        lastTick = EditorApplication.timeSinceStartup;
    }

    void OnDisable()
    {
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorSceneManager.sceneClosing -= OnSceneClosing;
        EditorSceneManager.sceneSaving -= OnSceneSaving;
        AssemblyReloadEvents.beforeAssemblyReload -= StopAll;
        StopAll();
        if (driver != null) DestroyImmediate(driver);
    }

    void OnPlayModeChanged(PlayModeStateChange _) { playing = false; StopAll(); }
    void OnSceneClosing(Scene _, bool __) { playing = false; StopAll(); }
    void OnSceneSaving(Scene _, string __) => StopAll();   // never save a previewed pose (comes back next tick)

    void StopAll()
    {
        StopPreview();
        EndFollow();
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

    static AnimationClip FindClip(AnimatorController c, string name) => FindState(c, name, out _)?.motion as AnimationClip;

    AnimationClip PlayerClip => data != null ? FindClip(PlayerController(), data.stateName) : null;
    AnimationClip EnemyClip => data != null ? FindClip(EnemyController(), data.stateName) : null;

    static bool StageOpen => SceneManager.GetActiveScene().path == StagePath;

    static GameObject StageRoot(string name) =>
        StageOpen ? SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == name) : null;

    static GameObject StagePlayerHead()
    {
        GameObject p = StageRoot(StagePlayerName);
        Transform head = p != null ? p.transform.Find(CameraHeadPath) : null;
        return head != null ? head.gameObject : null;
    }

    static GameObject StageEnemyModel()
    {
        GameObject e = StageRoot(StageEnemyName);
        Shooter s = e != null ? e.GetComponent<Shooter>() : null;
        return s != null && s.anim != null ? s.anim.gameObject : null;
    }

    static AnimationWindow FindAnimationWindow() => Resources.FindObjectsOfTypeAll<AnimationWindow>().FirstOrDefault();

    /// <summary>The Animation window is previewing one of our two clips (it owns the preview then).</summary>
    bool AnimWindowDriving(out AnimationWindow aw, out bool onPlayerClip)
    {
        aw = FindAnimationWindow();
        onPlayerClip = false;
        if (aw == null || !aw.previewing || aw.animationClip == null) return false;
        AnimationClip pc = PlayerClip, ec = EnemyClip;
        if (aw.animationClip == pc) { onPlayerClip = true; return true; }
        return aw.animationClip == ec;
    }

    static bool AnimWindowBusy()
    {
        var aw = FindAnimationWindow();
        return aw != null && (aw.previewing || aw.recording);
    }

    // ══════════════════════════════════════════════════════════════════
    //  PREVIEW
    // ══════════════════════════════════════════════════════════════════

    void StartPreview()
    {
        if (driver == null)
        {
            driver = CreateInstance<AnimationModeDriver>();
            driver.name = "Execution Editor";
            driver.hideFlags = HideFlags.HideAndDontSave;
        }
        if (!AnimationMode.InAnimationMode(driver)) AnimationMode.StartAnimationMode(driver);
        lastSampledTime = -1f;
    }

    void StopPreview()
    {
        if (driver != null && AnimationMode.InAnimationMode(driver)) AnimationMode.StopAnimationMode(driver);
        lastSampledTime = -1f;
    }

    bool OwnPreview => driver != null && AnimationMode.InAnimationMode(driver);

    /// <summary>Both in ONE sampling pass: each pass reverts whatever it didn't sample.</summary>
    static void SampleBoth(GameObject a, AnimationClip ca, GameObject b, AnimationClip cb, float t)
    {
        if (!AnimationMode.InAnimationMode()) return;
        AnimationMode.BeginSampling();
        if (a != null && ca != null) AnimationMode.SampleAnimationClip(a, ca, Mathf.Clamp(t, 0f, ca.length));
        if (b != null && cb != null) AnimationMode.SampleAnimationClip(b, cb, Mathf.Clamp(t, 0f, cb.length));
        AnimationMode.EndSampling();
    }

    /// <summary>Remember the rest values of every transform <paramref name="clip"/> drives on <paramref name="go"/>.</summary>
    void BeginFollow(GameObject go, AnimationClip clip)
    {
        if (followTarget == go) return;
        EndFollow();
        followTarget = go;
        foreach (string path in AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(Transform)).Select(b => b.path).Distinct())
        {
            Transform t = path.Length == 0 ? go.transform : go.transform.Find(path);
            if (t != null) followRest.Add((t, t.localPosition, t.localRotation, t.localScale));
        }
    }

    /// <summary>Put the followed character back exactly as it was.</summary>
    void EndFollow()
    {
        foreach (var (t, p, r, sc) in followRest)
            if (t != null) { t.localPosition = p; t.localRotation = r; t.localScale = sc; }
        followRest.Clear();
        followTarget = null;
    }

    void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - lastTick);
        lastTick = now;

        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || data == null || !StageOpen)
        {
            StopAll();
            return;
        }

        GameObject head = StagePlayerHead(), model = StageEnemyModel();
        AnimationClip pc = PlayerClip, ec = EnemyClip;
        if (head == null || model == null) { StopAll(); return; }

        if (AnimWindowDriving(out AnimationWindow aw, out bool onPlayerClip))
        {
            // The Animation window samples its own clip (its sampling passes would undo an AnimationMode
            // pose on the other character), so the other one is posed directly and restored in EndFollow
            if (OwnPreview) StopPreview();
            playing = false;
            GameObject other = onPlayerClip ? model : head;
            AnimationClip otherClip = onPlayerClip ? ec : pc;
            if (other == null || otherClip == null) { EndFollow(); return; }
            BeginFollow(other, otherClip);
            if (!Mathf.Approximately(time, aw.time) || now - lastSample > 0.25)
            {
                time = aw.time;
                otherClip.SampleAnimation(other, Mathf.Clamp(time, 0f, otherClip.length));
                lastSample = now;
                Repaint();
                SceneView.RepaintAll();
            }
            return;
        }
        EndFollow();

        // the Animation window previews something else: stay out of its way
        if (AnimWindowBusy()) { if (OwnPreview) StopPreview(); return; }

        if (playing)
        {
            float len = Length();
            time += dt;
            if (time > len) { if (loop) time = 0f; else { time = len; playing = false; } }
            Repaint();
        }

        if (!OwnPreview) StartPreview();
        if (!Mathf.Approximately(time, lastSampledTime) || now - lastSample > 0.25)   // also picks up clip edits
        {
            SampleBoth(head, pc, model, ec, time);
            lastSampledTime = time;
            lastSample = now;
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }

    float Length()
    {
        float len = data != null ? data.duration : 0f;
        AnimationClip pc = PlayerClip, ec = EnemyClip;
        if (pc != null) len = Mathf.Max(len, pc.length);
        if (ec != null) len = Mathf.Max(len, ec.length);
        return Mathf.Max(len, 0.01f);
    }

    void SetTime(float t)
    {
        time = Mathf.Clamp(t, 0f, Length());
        if (AnimWindowDriving(out AnimationWindow aw, out _)) aw.time = time;
    }

    // ══════════════════════════════════════════════════════════════════
    //  STAGE
    // ══════════════════════════════════════════════════════════════════

    void OpenStage()
    {
        StopAll();
        playing = false;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (File.Exists(StagePath)) EditorSceneManager.OpenScene(StagePath, OpenSceneMode.Single);
        else CreateStage();

        if (StageRoot(StagePlayerName) == null || StageRoot(StageEnemyName) == null) AddStageCharacters();
        Place();
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    }

    static void CreateStage()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var light = new GameObject("Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(2f, 1f, 2f);
        Directory.CreateDirectory(Path.GetDirectoryName(StagePath));
        EditorSceneManager.SaveScene(scene, StagePath);
    }

    static void AddStageCharacters()
    {
        var scene = SceneManager.GetActiveScene();
        if (StageRoot(StagePlayerName) == null)
        {
            var p = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath), scene);
            p.name = StagePlayerName;
        }
        if (StageRoot(StageEnemyName) == null)
        {
            var e = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath), scene);
            e.name = StageEnemyName;
        }
    }

    /// <summary>Distance from a character's pivot down to the bottom of its capsule (world units).</summary>
    static float FeetBelowPivot(GameObject go)
    {
        var cc = go.GetComponent<CharacterController>();
        if (cc != null) return (cc.height * 0.5f - cc.center.y) * go.transform.lossyScale.y;
        var cap = go.GetComponent<CapsuleCollider>();
        if (cap != null) return (cap.height * 0.5f - cap.center.y) * go.transform.lossyScale.y;
        return 0f;
    }

    bool CanPlace => StageOpen && data != null && !AnimWindowBusy() && !EditorApplication.isPlayingOrWillChangePlaymode;

    /// <summary>Same placement as PlayerExecution: enemy at the origin facing +Z, player at the offset,
    /// both on the floor, the player looking at the focus bone as it is on the clip's first frame.</summary>
    void Place()
    {
        if (!CanPlace) return;
        GameObject player = StageRoot(StagePlayerName), enemy = StageRoot(StageEnemyName);
        GameObject head = StagePlayerHead(), model = StageEnemyModel();
        if (player == null || enemy == null || head == null || model == null) return;

        bool wasPreviewing = OwnPreview;
        StopPreview();   // read rest poses, not sampled ones

        Undo.RecordObjects(new Object[] { player.transform, enemy.transform, head.transform }, "Place Execution Stage");
        enemy.transform.SetPositionAndRotation(new Vector3(0f, FeetBelowPivot(enemy), 0f), Quaternion.identity);

        Vector3 goal = enemy.transform.TransformPoint(data.playerOffset);
        goal.y = FeetBelowPivot(player);
        player.transform.position = goal;

        Vector3 focus = FocusPoint(model);
        Transform cam = head.GetComponentsInChildren<Camera>(true).Select(c => c.transform).FirstOrDefault(t => t.name == "Player Camera");
        if (cam == null) cam = head.transform;
        for (int i = 0; i < 4; i++)   // the camera sits off the head pivot, so settle a few times
        {
            Vector3 dir = (focus - cam.position).normalized;
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude > 0.0001f) player.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg, -89f, 89f);
            head.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        if (wasPreviewing) StartPreview();
    }

    /// <summary>World position of the focus bone on the enemy clip's first frame, from a hidden throwaway copy.</summary>
    Vector3 FocusPoint(GameObject model)
    {
        AnimationClip ec = EnemyClip;
        var copy = Instantiate(model, model.transform.position, model.transform.rotation);
        copy.hideFlags = HideFlags.HideAndDontSave;
        copy.transform.localScale = model.transform.lossyScale;
        try
        {
            if (ec != null) ec.SampleAnimation(copy, 0f);
            Transform bone = FindDeep(copy.transform, data.focusBone);
            return bone != null ? bone.position : copy.transform.position + Vector3.up * 1.4f;
        }
        finally { DestroyImmediate(copy); }
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            Transform f = FindDeep(c, name);
            if (f != null) return f;
        }
        return null;
    }

    void EditInAnimationWindow(GameObject target, AnimationClip clip)
    {
        if (target == null || clip == null) return;
        StopAll();
        playing = false;
        Selection.activeGameObject = target;
        var aw = GetWindow<AnimationWindow>();
        float t = time;
        // the Animation window picks up the selection on its next update
        EditorApplication.delayCall += () =>
        {
            if (aw == null) return;
            aw.animationClip = clip;
            aw.previewing = true;
            aw.time = t;
        };
    }

    // ══════════════════════════════════════════════════════════════════
    //  NEW EXECUTION
    // ══════════════════════════════════════════════════════════════════

    void CreateExecution(string rawName)
    {
        string name = new string((rawName ?? "").Where(char.IsLetterOrDigit).ToArray());
        if (name.Length == 0) { EditorUtility.DisplayDialog("New Execution", "Use letters/digits for the name.", "OK"); return; }

        AnimationClip srcP = PlayerClip, srcE = EnemyClip;
        AnimatorController pcCtrl = PlayerController(), ecCtrl = EnemyController();
        AnimatorState srcPState = FindState(pcCtrl, data.stateName, out Vector3 pPos);
        AnimatorState srcEState = FindState(ecCtrl, data.stateName, out Vector3 ePos);
        if (srcP == null || srcE == null || srcPState == null || srcEState == null)
        {
            EditorUtility.DisplayDialog("New Execution", "The current execution's states/clips are missing, so there is nothing to copy.", "OK");
            return;
        }

        string state = "Exec" + name;
        string pClip = $"{Path.GetDirectoryName(AssetDatabase.GetAssetPath(srcP)).Replace('\\', '/')}/{state}.anim";
        string eClip = $"{Path.GetDirectoryName(AssetDatabase.GetAssetPath(srcE)).Replace('\\', '/')}/{state}.anim";
        string dataPath = $"{DataDir}/{name}.asset";
        if (File.Exists(pClip) || File.Exists(eClip) || File.Exists(dataPath) || FindState(pcCtrl, state, out _) != null || FindState(ecCtrl, state, out _) != null)
        {
            EditorUtility.DisplayDialog("New Execution", $"'{name}' already exists.", "OK");
            return;
        }

        StopAll();
        AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(srcP), pClip);
        AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(srcE), eClip);
        CopyState(pcCtrl, srcPState, pPos, state, AssetDatabase.LoadAssetAtPath<AnimationClip>(pClip));
        CopyState(ecCtrl, srcEState, ePos, state, AssetDatabase.LoadAssetAtPath<AnimationClip>(eClip));

        var copy = Instantiate(data);
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
        data = copy;
        so = null;
        newName = "";
        time = 0f;
    }

    static void CopyState(AnimatorController ctrl, AnimatorState src, Vector3 srcPos, string name, AnimationClip clip)
    {
        var sm = ctrl.layers[0].stateMachine;
        var st = sm.AddState(name, srcPos + new Vector3(0f, 60f, 0f));
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

    // ══════════════════════════════════════════════════════════════════
    //  GUI
    // ══════════════════════════════════════════════════════════════════

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUI.BeginChangeCheck();
        data = (ExecutionData)EditorGUILayout.ObjectField("Execution", data, typeof(ExecutionData), false);
        if (EditorGUI.EndChangeCheck()) { so = null; time = 0f; Place(); }
        if (data == null)
        {
            EditorGUILayout.HelpBox("Pick an Execution asset (Assets/Settings/Executions).", MessageType.Info);
            EditorGUILayout.EndScrollView();
            return;
        }

        AnimationClip pc = PlayerClip, ec = EnemyClip;
        if (pc == null) EditorGUILayout.HelpBox($"No state '{data.stateName}' with a clip in Camera Head.controller.", MessageType.Error);
        if (ec == null) EditorGUILayout.HelpBox($"No state '{data.stateName}' with a clip in the enemy controller.", MessageType.Error);
        if (pc != null && ec != null && (!Mathf.Approximately(pc.length, ec.length) || !Mathf.Approximately(pc.length, data.duration)))
        {
            EditorGUILayout.HelpBox($"Lengths differ — player clip {pc.length:F2}s, enemy clip {ec.length:F2}s, duration {data.duration:F2}s. Make the clips the same length so they end together.", MessageType.Warning);
            if (GUILayout.Button("Set duration from clips"))
            {
                Undo.RecordObject(data, "Set Execution Duration");
                data.duration = Mathf.Max(pc.length, ec.length);
                EditorUtility.SetDirty(data);
            }
        }

        // ── stage ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Stage", EditorStyles.boldLabel);
        if (!StageOpen)
        {
            EditorGUILayout.HelpBox("Opens Assets/Scenes/ExecutionStage.unity with a Player and an enemy placed like in the game. Your current scene is offered for saving first.", MessageType.None);
            if (GUILayout.Button("Open Stage", GUILayout.Height(28))) OpenStage();
            EditorGUILayout.EndScrollView();
            return;
        }
        using (new EditorGUI.DisabledScope(!CanPlace))
            if (GUILayout.Button(CanPlace ? "Re-place characters" : "Re-place characters (stop Animation window preview first)")) Place();

        // ── timeline ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
        bool animDriving = AnimWindowDriving(out _, out bool onPlayer);
        if (animDriving) EditorGUILayout.HelpBox($"Following the Animation window ({(onPlayer ? "player" : "enemy")} clip) — the other character is kept in sync.", MessageType.None);

        float len = Length();
        EditorGUI.BeginChangeCheck();
        float t = EditorGUILayout.Slider("Time", time, 0f, len);
        Rect sliderRect = GUILayoutUtility.GetLastRect();
        if (EditorGUI.EndChangeCheck()) { playing = false; SetTime(t); }
        DrawTicks(sliderRect, len);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("|<")) { playing = false; SetTime(0f); }
            if (GUILayout.Button("<")) { playing = false; SetTime(time - FrameStep); }
            using (new EditorGUI.DisabledScope(animDriving))
                if (GUILayout.Button(playing ? "Pause" : "Play", GUILayout.Width(70))) { playing = !playing; if (playing && time >= len) time = 0f; }
            if (GUILayout.Button(">")) { playing = false; SetTime(time + FrameStep); }
            if (GUILayout.Button(">|")) { playing = false; SetTime(len); }
            loop = GUILayout.Toggle(loop, "Loop", GUILayout.Width(50));
        }

        // ── animate ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Animate", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Edit Player Clip")) EditInAnimationWindow(StagePlayerHead(), pc);
            if (GUILayout.Button("Edit Enemy Clip")) EditInAnimationWindow(StageEnemyModel(), ec);
        }
        EditorGUILayout.HelpBox("Opens the Animation window on that clip. Press record there and pose RHand / LHand / CamParent (player) or the bones (enemy). The other character follows the Animation window's time. Look through the Game view for the first-person shot.", MessageType.None);

        // ── timing ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Timing & placement", EditorStyles.boldLabel);
        DrawData();

        // ── new ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("New execution (copies this one)", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            newName = EditorGUILayout.TextField("Name", newName);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newName)))
                if (GUILayout.Button("Create", GUILayout.Width(70))) CreateExecution(newName);
        }

        EditorGUILayout.EndScrollView();
    }

    void DrawTicks(Rect sliderRect, float len)
    {
        if (Event.current.type != EventType.Repaint || len <= 0f) return;
        // the slider part of an EditorGUILayout.Slider sits after the label and before the number field
        float x0 = sliderRect.x + EditorGUIUtility.labelWidth + 2f;
        float x1 = sliderRect.xMax - EditorGUIUtility.fieldWidth - 5f;
        if (x1 <= x0) return;
        void Tick(float at, Color c)
        {
            float x = Mathf.Lerp(x0, x1, Mathf.Clamp01(at / len));
            EditorGUI.DrawRect(new Rect(x - 1f, sliderRect.yMax - 4f, 2f, 4f), c);
        }
        if (data.events != null) foreach (var e in data.events) Tick(e.time, new Color(1f, 0.55f, 0.1f));
        Tick(data.killTime, new Color(0.9f, 0.15f, 0.15f));
    }

    void DrawData()
    {
        if (so == null || so.targetObject != data) so = new SerializedObject(data);
        so.Update();

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(so.FindProperty("playerOffset"));
        EditorGUILayout.PropertyField(so.FindProperty("focusBone"));
        bool placementChanged = EditorGUI.EndChangeCheck();

        EditorGUILayout.PropertyField(so.FindProperty("alignTime"));
        EditorGUILayout.PropertyField(so.FindProperty("duration"));
        TimeField(so.FindProperty("killTime"), "Kill time");

        var events = so.FindProperty("events");
        EditorGUILayout.LabelField($"Events ({events.arraySize})");
        for (int i = 0; i < events.arraySize; i++)
        {
            var e = events.GetArrayElementAtIndex(i);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    TimeField(e.FindPropertyRelative("time"), $"#{i + 1} time");
                    if (GUILayout.Button("X", GUILayout.Width(22))) { events.DeleteArrayElementAtIndex(i); break; }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("blood"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("heal"));
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("shake"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("shakeTime"));
                }
            }
        }
        if (GUILayout.Button("Add event at current time"))
        {
            int n = events.arraySize;
            events.InsertArrayElementAtIndex(n);
            var e = events.GetArrayElementAtIndex(n);
            e.FindPropertyRelative("time").floatValue = time;
        }

        so.ApplyModifiedProperties();
        if (placementChanged) Place();
    }

    /// <summary>A time float with Go (preview it) and Now (set it to the current preview time).</summary>
    void TimeField(SerializedProperty p, string label)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(p, new GUIContent(label));
            if (GUILayout.Button("Go", GUILayout.Width(34))) { playing = false; SetTime(p.floatValue); }
            if (GUILayout.Button("Now", GUILayout.Width(40))) p.floatValue = time;
        }
    }
}
