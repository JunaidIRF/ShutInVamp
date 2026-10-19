using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace JunaidIRF.SyncedInteractions.Editor
{
    /// <summary>
    /// Dual-Rig animator: combines all roles' curves into ONE clip on a single root rig, so both characters'
    /// keyframes appear together on the exact same dopesheet in Unity's standard Animation window.
    /// Changes to the combined clip automatically write back to each role's individual animation clip.
    /// Modular for ANY InteractionAsset and any rigged characters.
    /// </summary>
    [InitializeOnLoad]
    public static class InteractionDualRig
    {
        const string SessionAsset = "SyncedInteractions.DualRig.Asset";
        const string SessionClip = "SyncedInteractions.DualRig.Clip";
        const string SessionReturn = "SyncedInteractions.DualRig.Return";
        const string Folder = "Assets/SyncedInteractionsStage";
        const string StageScenePath = Folder + "/RigStage.unity";

        static int lastDirty = -1;
        static double nextPoll;

        static InteractionDualRig()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.ExitingEditMode && IsOpen) ApplyNow(); };
        }

        public static bool IsOpen => !string.IsNullOrEmpty(SessionState.GetString(SessionAsset, "")) && SceneManager.GetActiveScene().path == StageScenePath;
        public static InteractionAsset OpenAsset => AssetDatabase.LoadAssetAtPath<InteractionAsset>(SessionState.GetString(SessionAsset, ""));
        static AnimationClip EditingClip => AssetDatabase.LoadAssetAtPath<AnimationClip>(SessionState.GetString(SessionClip, ""));

        public static void Open(InteractionAsset asset)
        {
            if (asset == null) return;
            var missing = InteractionStage.Missing(asset);
            if (missing.Count > 0)
            {
                EditorUtility.DisplayDialog("Dual Rig Animator", "Can't open the rig:\n" + string.Join("\n", missing), "OK");
                return;
            }

            if (IsOpen) Close();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            // Remember return scene(s)
            var open = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && !string.IsNullOrEmpty(s.path)) open.Add(s.path);
            }
            string active = SceneManager.GetActiveScene().path;
            open.Remove(active);
            if (!string.IsNullOrEmpty(active)) open.Insert(0, active);
            SessionState.SetString(SessionReturn, string.Join("|", open));
            SessionState.SetString(SessionAsset, AssetDatabase.GetAssetPath(asset));

            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "SyncedInteractionsStage");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, StageScenePath);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = Vector3.one * 2f;

            string assetPath = AssetDatabase.GetAssetPath(asset);
            asset = AssetDatabase.LoadAssetAtPath<InteractionAsset>(assetPath);

            AnimationClip editClip = BuildEditingClip(asset);
            var ctrl = BuildRigController(editClip, $"{Folder}/{asset.name}_Rig.controller");
            GameObject rig = BuildRig(asset, ctrl);

            SessionState.SetString(SessionClip, AssetDatabase.GetAssetPath(editClip));
            lastDirty = EditorUtility.GetDirtyCount(editClip);

            Selection.activeGameObject = rig;
            EditorApplication.delayCall += () =>
            {
                var aw = EditorWindow.GetWindow<AnimationWindow>();
                if (aw != null)
                {
                    aw.animationClip = editClip;
                    aw.previewing = true;
                    aw.time = 0f;
                    aw.Repaint();
                }
            };

            var sv = SceneView.lastActiveSceneView;
            if (sv != null)
            {
                sv.pivot = new Vector3(0f, 1f, 0f);
                sv.rotation = Quaternion.Euler(15f, 140f, 0f);
                sv.size = 3f;
                sv.Repaint();
            }
        }

        public static void Close()
        {
            if (IsOpen) ApplyNow();

            var back = SessionState.GetString(SessionReturn, "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            SessionState.EraseString(SessionReturn);
            SessionState.EraseString(SessionAsset);
            SessionState.EraseString(SessionClip);

            if (back.Length == 0) EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            else
            {
                EditorSceneManager.OpenScene(back[0], OpenSceneMode.Single);
                for (int i = 1; i < back.Length; i++) EditorSceneManager.OpenScene(back[i], OpenSceneMode.Additive);
            }

            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }

        static AnimationClip BuildEditingClip(InteractionAsset asset)
        {
            string path = $"{Folder}/{asset.name} (edit both).anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();

            float frameRate = 30f;
            foreach (var r in asset.roles)
                if (r?.clip != null) frameRate = Mathf.Max(frameRate, r.clip.frameRate);
            clip.frameRate = frameRate;

            for (int i = 0; i < asset.roles.Count; i++)
            {
                var role = asset.roles[i];
                if (role?.clip == null) continue;
                string prefix = role.name;
                foreach (var b in AnimationUtility.GetCurveBindings(role.clip))
                {
                    var curve = AnimationUtility.GetEditorCurve(role.clip, b);
                    string newPath = string.IsNullOrEmpty(b.path) ? prefix : $"{prefix}/{b.path}";
                    var rebased = new EditorCurveBinding { path = newPath, propertyName = b.propertyName, type = b.type };
                    AnimationUtility.SetEditorCurve(clip, rebased, curve);
                }
            }

            // Events as animation event markers on the dopesheet
            var markers = new List<AnimationEvent>();
            foreach (var e in asset.events)
            {
                if (e == null) continue;
                markers.Add(new AnimationEvent { time = e.time, functionName = string.IsNullOrEmpty(e.name) ? "Event" : e.name, stringParameter = e.id });
            }
            AnimationUtility.SetAnimationEvents(clip, markers.OrderBy(m => m.time).ToArray());

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            return clip;
        }

        static AnimatorController BuildRigController(AnimationClip edit, string path)
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);

            var sm = ctrl.layers[0].stateMachine;
            foreach (var s in sm.states.ToArray()) sm.RemoveState(s.state);

            var st = sm.AddState("EditDualRig");
            st.motion = edit;
            st.speed = 0f;
            sm.defaultState = st;

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssetIfDirty(ctrl);
            return ctrl;
        }

        static GameObject BuildRig(InteractionAsset asset, AnimatorController ctrl)
        {
            var rig = new GameObject($"[Interaction Rig: {asset.name}]");
            rig.AddComponent<Animator>().runtimeAnimatorController = ctrl;

            int anchor = asset.anchor == AnchorMode.Role ? Mathf.Max(0, asset.RoleIndex(asset.anchorRole)) : -1;
            for (int i = 0; i < asset.roles.Count; i++)
            {
                var role = asset.roles[i];
                var go = (GameObject)Object.Instantiate(role.previewPrefab);
                go.name = role.name;
                Pose slot = i == anchor ? new Pose(Vector3.zero, Quaternion.identity) : role.Slot;
                go.transform.SetPositionAndRotation(new Vector3(slot.position.x, FeetBelowPivot(go), slot.position.z), slot.rotation);
                go.transform.SetParent(rig.transform, true);

                // Disable child animators so only root Animator drives the dual hierarchy
                foreach (var anim in go.GetComponentsInChildren<Animator>(true))
                {
                    anim.enabled = false;
                }
            }
            return rig;
        }

        static void Poll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 0.3;

            if (!IsOpen) return;
            AnimationClip edit = EditingClip;
            if (edit == null) return;

            int dirty = EditorUtility.GetDirtyCount(edit);
            if (lastDirty == -1) { lastDirty = dirty; return; }
            if (dirty == lastDirty) return;
            lastDirty = dirty;

            ApplyNow();
        }

        public static void ApplyNow()
        {
            AnimationClip edit = EditingClip;
            InteractionAsset asset = OpenAsset;
            if (edit == null || asset == null) return;

            var bindings = AnimationUtility.GetCurveBindings(edit);
            for (int i = 0; i < asset.roles.Count; i++)
            {
                var role = asset.roles[i];
                if (role?.clip == null) continue;
                string prefix = role.name;

                var roleCurves = new List<(EditorCurveBinding binding, AnimationCurve curve)>();
                foreach (var b in bindings)
                {
                    if (b.path == prefix)
                    {
                        var rebased = new EditorCurveBinding { path = "", propertyName = b.propertyName, type = b.type };
                        roleCurves.Add((rebased, AnimationUtility.GetEditorCurve(edit, b)));
                    }
                    else if (b.path.StartsWith(prefix + "/"))
                    {
                        string sub = b.path.Substring(prefix.Length + 1);
                        var rebased = new EditorCurveBinding { path = sub, propertyName = b.propertyName, type = b.type };
                        roleCurves.Add((rebased, AnimationUtility.GetEditorCurve(edit, b)));
                    }
                }

                if (roleCurves.Count > 0)
                {
                    role.clip.ClearCurves();
                    role.clip.frameRate = edit.frameRate;
                    foreach (var (b, c) in roleCurves) AnimationUtility.SetEditorCurve(role.clip, b, c);
                    role.clip.EnsureQuaternionContinuity();
                    EditorUtility.SetDirty(role.clip);
                    AssetDatabase.SaveAssetIfDirty(role.clip);
                }
            }

            // Sync retimed markers back to events
            var markers = AnimationUtility.GetAnimationEvents(edit).OrderBy(m => m.time).ToArray();
            for (int j = 0; j < markers.Length && j < asset.events.Count; j++)
            {
                asset.events[j].time = markers[j].time;
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        static float FeetBelowPivot(GameObject go)
        {
            var cc = go.GetComponent<CharacterController>();
            if (cc != null) return (cc.height * 0.5f - cc.center.y) * go.transform.lossyScale.y;
            var cap = go.GetComponent<CapsuleCollider>();
            if (cap != null) return (cap.height * 0.5f - cap.center.y) * go.transform.lossyScale.y;
            return 0f;
        }

        class SaveHook : AssetModificationProcessor
        {
            static string[] OnWillSaveAssets(string[] paths)
            {
                string edit = SessionState.GetString(SessionClip, "");
                if (edit.Length > 0 && paths.Contains(edit)) EditorApplication.delayCall += ApplyNow;
                return paths;
            }
        }
    }
}
