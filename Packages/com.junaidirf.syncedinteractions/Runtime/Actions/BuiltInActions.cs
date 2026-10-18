using System;
using System.Collections.Generic;
using UnityEngine;

namespace JunaidIRF.SyncedInteractions
{
    /// <summary>Fires one of the target character's Named Events (on its Interaction Actor).</summary>
    [Serializable]
    public class InvokeActorEventAction : InteractionAction
    {
        public string eventName = "";
        public override void Fire(InteractionContext c) { if (c.actor != null) c.actor.FireNamed(eventName); }
        public override string MenuName => "Invoke Actor Event";
    }

    /// <summary>Plays a sound at the event's bone (a random one of the clips, a random pitch).</summary>
    [Serializable]
    public class PlaySoundAction : InteractionAction
    {
        public AudioClip[] clips = new AudioClip[0];
        [Range(0f, 1f)] public float volume = 1f;
        public Vector2 pitch = new Vector2(0.95f, 1.05f);
        [Tooltip("0 = 2D (heard the same everywhere), 1 = 3D at the bone")]
        [Range(0f, 1f)] public float spatialBlend = 1f;
        public bool followBone = true;

        public override void Fire(InteractionContext c)
        {
            if (clips == null || clips.Length == 0) return;
            var clip = clips[UnityEngine.Random.Range(0, clips.Length)];
            if (clip == null) return;
            var go = new GameObject("Interaction Sound: " + clip.name);
            go.transform.position = c.Position;
            if (followBone && c.bone != null) go.transform.SetParent(c.bone, true);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.volume = volume;
            s.pitch = UnityEngine.Random.Range(pitch.x, pitch.y);
            s.spatialBlend = spatialBlend;
            s.Play();
            UnityEngine.Object.Destroy(go, clip.length / Mathf.Max(0.05f, Mathf.Abs(s.pitch)) + 0.1f);
        }
    }

    /// <summary>Spawns a prefab (an effect, a prop) at the event's bone.</summary>
    [Serializable]
    public class SpawnPrefabAction : InteractionAction
    {
        public GameObject prefab;
        [Tooltip("Stays on the bone and moves with it")]
        public bool attach;
        public Vector3 positionOffset;
        public Vector3 rotationOffset;
        [Tooltip("Destroyed after this long (0 = left alone)")]
        [Min(0f)] public float lifetime = 5f;
        [Tooltip("Destroyed when the interaction ends (finished or cancelled)")]
        public bool destroyAtEnd;

        [NonSerialized] readonly List<GameObject> spawned = new List<GameObject>();

        public override void Fire(InteractionContext c)
        {
            if (prefab == null) return;
            Transform at = c.bone;
            Quaternion rot = (at != null ? at.rotation : Quaternion.identity) * Quaternion.Euler(rotationOffset);
            Vector3 pos = at != null ? at.TransformPoint(positionOffset) : c.Position + positionOffset;
            var go = UnityEngine.Object.Instantiate(prefab, pos, rot, attach ? at : null);
            if (lifetime > 0f) UnityEngine.Object.Destroy(go, lifetime);
            if (destroyAtEnd) spawned.Add(go);
        }

        public override void End(InteractionContext c, bool cancelled)
        {
            foreach (var go in spawned) if (go != null) UnityEngine.Object.Destroy(go);
            spawned.Clear();
        }
    }

    /// <summary>Shows or hides a child of the target character (a weapon, a prop in the hand). Optionally put back at the end.</summary>
    [Serializable]
    public class SetActiveAction : InteractionAction
    {
        [Tooltip("The child's name or path under the character (empty = the event's bone)")]
        public string child = "";
        public bool active = true;
        public bool restoreAtEnd = true;

        [NonSerialized] GameObject target;
        [NonSerialized] bool was;

        public override void Fire(InteractionContext c)
        {
            target = Find(c, child);
            if (target == null) return;
            was = target.activeSelf;
            target.SetActive(active);
        }

        public override void End(InteractionContext c, bool cancelled)
        {
            if (restoreAtEnd && target != null) target.SetActive(was);
            target = null;
        }

        internal static GameObject Find(InteractionContext c, string child)
        {
            if (string.IsNullOrEmpty(child)) return c.bone != null ? c.bone.gameObject : null;
            return c.actor != null ? c.actor.Bone(child)?.gameObject ?? FindIn(c.actor.transform, child) : null;
        }

        static GameObject FindIn(Transform root, string name)
        {
            var t = root.Find(name);
            if (t != null) return t.gameObject;
            foreach (Transform ch in root.GetComponentsInChildren<Transform>(true)) if (ch.name == name) return ch.gameObject;
            return null;
        }
    }

    /// <summary>Puts a child of the target character (and everything under it) on another layer; back at the end.</summary>
    [Serializable]
    public class SetLayerAction : InteractionAction
    {
        [Tooltip("The child's name or path under the character (empty = the whole character)")]
        public string child = "";
        public int layer;
        public bool restoreAtEnd = true;

        [NonSerialized] readonly List<(GameObject go, int layer)> before = new List<(GameObject, int)>();

        public override void Fire(InteractionContext c)
        {
            GameObject root = string.IsNullOrEmpty(child) ? (c.actor != null ? c.actor.gameObject : null) : SetActiveAction.Find(c, child);
            if (root == null) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (restoreAtEnd && !before.Exists(b => b.go == t.gameObject)) before.Add((t.gameObject, t.gameObject.layer));
                t.gameObject.layer = layer;
            }
        }

        public override void End(InteractionContext c, bool cancelled)
        {
            foreach (var (go, l) in before) if (go != null) go.layer = l;
            before.Clear();
        }
    }

    /// <summary>Lets a role go now (e.g. hands a victim to its ragdoll); the others carry on.</summary>
    [Serializable]
    public class ReleaseRoleAction : InteractionAction
    {
        [InteractionRoleName, Tooltip("Empty = the event's role")]
        public string role = "";
        public override void Fire(InteractionContext c) => c.handle.Release(string.IsNullOrEmpty(role) ? c.handle.RoleOf(c.actor) : role);
        public override string MenuName => "Release Role";
    }

    /// <summary>Stops the whole interaction here (as finished, not cancelled).</summary>
    [Serializable]
    public class EndInteractionAction : InteractionAction
    {
        public override void Fire(InteractionContext c) => c.handle.Stop(false);
        public override string MenuName => "End Interaction";
    }

    /// <summary>Sends a message to the target character (calls a method by name on all its scripts).</summary>
    [Serializable]
    public class SendMessageAction : InteractionAction
    {
        public string method = "";
        public bool includeChildren;
        public override void Fire(InteractionContext c)
        {
            if (c.actor == null || string.IsNullOrEmpty(method)) return;
            if (includeChildren) c.actor.BroadcastMessage(method, c, SendMessageOptions.DontRequireReceiver);
            else c.actor.SendMessage(method, c, SendMessageOptions.DontRequireReceiver);
        }
    }
}
