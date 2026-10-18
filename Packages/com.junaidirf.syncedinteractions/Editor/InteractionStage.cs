using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace JunaidIRF.SyncedInteractions.Editor
{
    /// <summary>A Timeline marker for one of the interaction's events (made and read by the stage).</summary>
    [Serializable, System.ComponentModel.DisplayName("Interaction Event")]
    public class InteractionEventMarker : Marker
    {
        [HideInInspector] public string eventId;
    }

    /// <summary>
    /// The authoring stage: a scratch scene with each role's preview character standing on its slot, and a
    /// Timeline with one animation track per role (its clip, bound to its character) and a marker per event.
    /// Scrub to see them in sync; edit a role's keys with "Edit in Animation Window" on its clip; drag markers
    /// to retime events (add / delete markers to add / remove events); move a character to move its slot.
    /// Everything is written back to the asset as you go and when the stage closes.
    /// </summary>
    [InitializeOnLoad]
    public static class InteractionStage
    {
        const string SessionAsset = "SyncedInteractions.Stage.Asset";
        const string SessionReturn = "SyncedInteractions.Stage.Return";
        const string Folder = "Assets/SyncedInteractionsStage";
        static double nextPoll;
        static double lastPreviewTime = -1;

        static InteractionStage()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.ExitingEditMode && IsOpen) WriteBack(); };
        }

        static InteractionStageInfo Info => Object.FindAnyObjectByType<InteractionStageInfo>();
        public static bool IsOpen => Info != null;
        public static InteractionAsset OpenAsset => Info != null ? Info.asset : null;

        /// <summary>Preview characters missing (a stage can't be built without them).</summary>
        public static List<string> Missing(InteractionAsset asset)
        {
            var list = new List<string>();
            foreach (var r in asset.roles)
            {
                if (r == null) continue;
                if (r.previewPrefab == null) list.Add($"'{r.name}' has no Preview Prefab");
                if (r.clip == null) list.Add($"'{r.name}' has no clip");
            }
            return list;
        }

        public static void Open(InteractionAsset asset)
        {
            var missing = Missing(asset);
            if (missing.Count > 0) { EditorUtility.DisplayDialog("Synced Interactions", "Can't open the stage:\n" + string.Join("\n", missing), "OK"); return; }
            if (IsOpen) Close();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            // where to go back to afterwards
            var open = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s.isLoaded && !string.IsNullOrEmpty(s.path)) open.Add(s.path); }
            string active = SceneManager.GetActiveScene().path;
            open.Remove(active);
            if (!string.IsNullOrEmpty(active)) open.Insert(0, active);
            SessionState.SetString(SessionReturn, string.Join("|", open));
            SessionState.SetString(SessionAsset, AssetDatabase.GetAssetPath(asset));

            string assetPath = AssetDatabase.GetAssetPath(asset);
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            asset = AssetDatabase.LoadAssetAtPath<InteractionAsset>(assetPath);   // switching scenes unloads it from memory
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = Vector3.one * 2f;

            var stage = new GameObject("Interaction Stage: " + asset.name);
            var info = stage.AddComponent<InteractionStageInfo>();
            info.asset = asset;
            var director = stage.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.extrapolationMode = DirectorWrapMode.Hold;

            // the characters, each on its slot (the anchor role on the origin)
            int anchor = asset.anchor == AnchorMode.Role ? Mathf.Max(0, asset.RoleIndex(asset.anchorRole)) : -1;
            info.roleRoots = new GameObject[asset.roles.Count];
            var animators = new Animator[asset.roles.Count];
            for (int i = 0; i < asset.roles.Count; i++)
            {
                var role = asset.roles[i];
                var go = (GameObject)PrefabUtility.InstantiatePrefab(role.previewPrefab);
                go.name = $"{role.name} ({role.previewPrefab.name})";
                Pose slot = i == anchor ? new Pose(Vector3.zero, Quaternion.identity) : role.Slot;
                go.transform.SetPositionAndRotation(new Vector3(slot.position.x, FeetBelowPivot(go), slot.position.z), slot.rotation);
                info.roleRoots[i] = go;
                var actor = go.GetComponent<InteractionActor>();
                animators[i] = actor != null && actor.animator != null ? actor.animator : go.GetComponentInChildren<Animator>(true);
            }

            // the timeline
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "SyncedInteractionsStage");
            string path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{asset.name} (stage).playable");
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, path);
            for (int i = 0; i < asset.roles.Count; i++)
            {
                var role = asset.roles[i];
                var track = timeline.CreateTrack<AnimationTrack>(null, role.name);
                track.trackOffset = TrackOffset.ApplySceneOffsets;
                var clip = track.CreateClip(role.clip);
                clip.displayName = role.clip.name;
                director.SetGenericBinding(track, animators[i]);
            }
            timeline.CreateMarkerTrack();
            foreach (var e in asset.events)
            {
                if (e == null) continue;
                if (string.IsNullOrEmpty(e.id)) e.id = NewId();
                var m = timeline.markerTrack.CreateMarker<InteractionEventMarker>(e.time);
                m.eventId = e.id;
                m.name = e.Label;
            }
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            director.playableAsset = timeline;
            director.time = 0;
            director.Evaluate();

            Selection.activeGameObject = stage;
            EditorApplication.ExecuteMenuItem("Window/Sequencing/Timeline");
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) { sv.pivot = new Vector3(0f, 1f, 0f); sv.rotation = Quaternion.Euler(15f, 140f, 0f); sv.size = 3f; sv.Repaint(); }
            lastPreviewTime = -1;
        }

        public static void Close()
        {
            var info = Info;
            if (info != null)
            {
                WriteBack();
                EndPreviews(info);
                var director = info.GetComponent<PlayableDirector>();
                string tl = director != null && director.playableAsset != null ? AssetDatabase.GetAssetPath(director.playableAsset) : null;
                if (!string.IsNullOrEmpty(tl)) AssetDatabase.DeleteAsset(tl);
                if (AssetDatabase.IsValidFolder(Folder) && AssetDatabase.FindAssets("", new[] { Folder }).Length == 0) AssetDatabase.DeleteAsset(Folder);
            }
            var back = SessionState.GetString(SessionReturn, "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            SessionState.EraseString(SessionReturn);
            SessionState.EraseString(SessionAsset);
            if (back.Length == 0) { EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single); return; }
            EditorSceneManager.OpenScene(back[0], OpenSceneMode.Single);
            for (int i = 1; i < back.Length; i++) EditorSceneManager.OpenScene(back[i], OpenSceneMode.Additive);
        }

        static void Poll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 0.25;
            var info = Info;
            if (info == null || info.asset == null) return;
            WriteBack();
            var director = info.GetComponent<PlayableDirector>();
            if (director != null && Math.Abs(director.time - lastPreviewTime) > 1e-5)
            {
                lastPreviewTime = director.time;
                Previews(info, (float)director.time);
            }
        }

        /// <summary>The stage's markers and character positions into the asset (only what changed, with Undo).</summary>
        public static void WriteBack()
        {
            var info = Info;
            if (info == null || info.asset == null) return;
            var asset = info.asset;
            var director = info.GetComponent<PlayableDirector>();
            var timeline = director != null ? director.playableAsset as TimelineAsset : null;
            if (timeline == null) return;

            // events from the markers
            var markers = new List<InteractionEventMarker>();
            if (timeline.markerTrack != null) markers.AddRange(timeline.markerTrack.GetMarkers().OfType<InteractionEventMarker>());
            foreach (var t in timeline.GetOutputTracks())   // (markers dropped on other tracks count too; the marker track is one of these)
                foreach (var m in t.GetMarkers().OfType<InteractionEventMarker>()) if (!markers.Contains(m)) markers.Add(m);
            var events = new List<InteractionEvent>();
            bool changed = false;
            foreach (var m in markers)
            {
                var e = string.IsNullOrEmpty(m.eventId) ? null : asset.events.Find(x => x != null && x.id == m.eventId);
                if (e == null)
                {
                    // a marker added on the timeline: a new event
                    e = new InteractionEvent { id = NewId(), name = $"Event {asset.events.Count + 1}", time = (float)m.time, role = asset.roles.Count > 0 ? asset.roles[0].name : "" };
                    m.eventId = e.id; m.name = e.name;
                    EditorUtility.SetDirty(timeline);
                    changed = true;
                }
                else if (Mathf.Abs(e.time - (float)m.time) > 1e-4f) changed = true;
                if (m.name != e.Label) { m.name = e.Label; EditorUtility.SetDirty(timeline); }
                events.Add(e);
            }
            if (events.Count != asset.events.Count(x => x != null)) changed = true;   // a marker deleted

            // slots from where the characters stand
            int anchor = asset.anchor == AnchorMode.Role ? Mathf.Max(0, asset.RoleIndex(asset.anchorRole)) : -1;
            Pose a = anchor >= 0 && anchor < info.roleRoots.Length && info.roleRoots[anchor] != null
                ? new Pose(info.roleRoots[anchor].transform.position - Vector3.up * FeetBelowPivot(info.roleRoots[anchor]), FlatRotation(info.roleRoots[anchor].transform))
                : new Pose(Vector3.zero, Quaternion.identity);
            var slots = new (Vector3 offset, float yaw)[asset.roles.Count];
            for (int i = 0; i < asset.roles.Count && i < info.roleRoots.Length; i++)
            {
                slots[i] = (asset.roles[i].offset, asset.roles[i].yaw);
                var go = info.roleRoots[i];
                if (go == null || i == anchor) continue;
                Vector3 feet = go.transform.position - Vector3.up * FeetBelowPivot(go);
                Vector3 offset = Quaternion.Inverse(a.rotation) * (feet - a.position);
                float yaw = Mathf.DeltaAngle(0f, (Quaternion.Inverse(a.rotation) * FlatRotation(go.transform)).eulerAngles.y);
                offset = Round(offset);
                yaw = Mathf.Round(yaw * 10f) / 10f;
                if ((offset - asset.roles[i].offset).sqrMagnitude > 1e-6f || Mathf.Abs(Mathf.DeltaAngle(yaw, asset.roles[i].yaw)) > 0.05f) { slots[i] = (offset, yaw); changed = true; }
            }

            if (!changed) return;
            Undo.RecordObject(asset, "Edit interaction on stage");
            for (int i = 0; i < events.Count; i++) events[i].time = Mathf.Max(0f, (float)markers[i].time);
            events.Sort((x, y) => x.time.CompareTo(y.time));
            asset.events = events;
            for (int i = 0; i < slots.Length; i++) { asset.roles[i].offset = slots[i].offset; asset.roles[i].yaw = slots[i].yaw; }
            EditorUtility.SetDirty(asset);
        }

        // ── action previews: actions that can show themselves do, from their event's time on ──
        static void Previews(InteractionStageInfo info, float time)
        {
            var asset = info.asset;
            foreach (var e in asset.events)
            {
                if (e?.actions == null) continue;
                int ri = string.IsNullOrEmpty(e.role) ? 0 : asset.RoleIndex(e.role);
                if (ri < 0 || ri >= info.roleRoots.Length || info.roleRoots[ri] == null) continue;
                var character = info.roleRoots[ri];
                foreach (var act in e.actions)
                    if (act is IInteractionPreviewAction p)
                    {
                        try { p.Preview(character, Bone(character, e.bone), time >= e.time - 1e-4f); }
                        catch (Exception ex) { Debug.LogException(ex); }
                    }
            }
            SceneView.RepaintAll();
        }

        static void EndPreviews(InteractionStageInfo info)
        {
            var asset = info.asset;
            if (asset == null) return;
            foreach (var e in asset.events)
            {
                if (e?.actions == null) continue;
                int ri = string.IsNullOrEmpty(e.role) ? 0 : asset.RoleIndex(e.role);
                if (ri < 0 || ri >= info.roleRoots.Length || info.roleRoots[ri] == null) continue;
                foreach (var act in e.actions)
                    if (act is IInteractionPreviewAction p)
                    {
                        try { p.EndPreview(info.roleRoots[ri]); } catch (Exception ex) { Debug.LogException(ex); }
                    }
            }
        }

        /// <summary>A bone on a stage character, found the way the game finds it.</summary>
        public static Transform Bone(GameObject character, string name)
        {
            var actor = character.GetComponent<InteractionActor>();
            if (actor != null) return actor.Bone(name) ?? character.transform;
            var animator = character.GetComponentInChildren<Animator>(true);
            if (string.IsNullOrEmpty(name)) return animator != null ? animator.transform : character.transform;
            if (animator != null && animator.isHuman && Enum.TryParse(name, true, out HumanBodyBones hb) && hb != HumanBodyBones.LastBone)
            {
                var b = animator.GetBoneTransform(hb);
                if (b != null) return b;
            }
            return character.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name) ?? character.transform;
        }

        static float FeetBelowPivot(GameObject go)
        {
            var cc = go.GetComponent<CharacterController>();
            if (cc != null) return (cc.height * 0.5f - cc.center.y) * go.transform.lossyScale.y;
            var cap = go.GetComponent<CapsuleCollider>();
            if (cap != null) return (cap.height * 0.5f - cap.center.y) * go.transform.lossyScale.y;
            return 0f;
        }

        static Quaternion FlatRotation(Transform t)
        {
            Vector3 f = t.forward; f.y = 0f;
            return f.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(f, Vector3.up) : Quaternion.identity;
        }

        static Vector3 Round(Vector3 v) => new Vector3(Mathf.Round(v.x * 1000f) / 1000f, Mathf.Round(v.y * 1000f) / 1000f, Mathf.Round(v.z * 1000f) / 1000f);
        static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);
    }
}
