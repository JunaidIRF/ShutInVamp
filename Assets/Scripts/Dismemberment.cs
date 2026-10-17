using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

/// <summary>
/// Cuts parts (head, arms, legs) off a skinned enemy model. The skinned mesh is split by bone weights
/// (everything weighted mostly to the part's bone or its children), so it works for any rig: the body keeps
/// skinning without the part, the part becomes a physics object, both ends get a stump and the wound spurts
/// blood. Needs the model's mesh to be Read/Write enabled. Call after the ragdoll is on.
/// </summary>
public class Dismemberment : MonoBehaviour
{
    [Serializable]
    public class Part
    {
        public string name = "Head";
        [BonePicker] public string bone = "mixamorig:Head";   // the part is this bone and everything under it
        public float stumpSize = 0.1f;                        // m across
        public float mass = 4f;
    }

    public List<Part> parts = new List<Part>
    {
        new Part { name = "Head", bone = "mixamorig:Head", stumpSize = 0.1f, mass = 4f },
        new Part { name = "Left Arm", bone = "mixamorig:LeftArm", stumpSize = 0.09f, mass = 3f },
        new Part { name = "Right Arm", bone = "mixamorig:RightArm", stumpSize = 0.09f, mass = 3f },
        new Part { name = "Left Leg", bone = "mixamorig:LeftUpLeg", stumpSize = 0.15f, mass = 6f },
        new Part { name = "Right Leg", bone = "mixamorig:RightUpLeg", stumpSize = 0.15f, mass = 6f },
    };
    [Tooltip("A blow whose line passes further than this from every bone of a part doesn't cut anything")]
    public float maxHitDistance = 0.35f;

    [Header("Severed part")]
    [FormerlySerializedAs("headSpeed")] public float pieceSpeed = 4f;        // m/s along the killing blow
    [FormerlySerializedAs("headLift")] public float pieceLift = 3f;          // m/s upward
    [FormerlySerializedAs("headSpin")] public float pieceSpin = 8f;          // rad/s, random axis
    [FormerlySerializedAs("headLifetime")] public float pieceLifetime = 20f;

    [Header("Blood")]
    public Color stumpColor = new Color(0.35f, 0.02f, 0.03f);
    public GameObject bloodFx;              // one splash at the cut; falls back to the Shooter's blood effect
    [FormerlySerializedAs("neckBloodFx")] public GameObject jetFx;   // the jet out of the wound, replayed for every pulse
    public int spurts = 5;
    public float spurtInterval = 0.32f;
    public float spurtFalloff = 0.82f;      // each pulse this much weaker than the last
    [FormerlySerializedAs("headSpurts")] public int pieceSpurts = 2;   // a couple of weaker jets out of the flying part
    [FormerlySerializedAs("headSpurtStrength")] public float pieceSpurtStrength = 0.6f;
    public float bloodLifetime = 9f;

    readonly HashSet<Part> severed = new HashSet<Part>();

    public bool IsSevered(string partName) { var p = Find(partName); return p != null && severed.Contains(p); }

    void OnEnable() => severed.Clear();

    public Part Find(string partName) => parts.Find(p => p.name == partName);

    /// <summary>
    /// The part a blow along <paramref name="direction"/> through <paramref name="point"/> lands on: the one
    /// with the bone segment nearest that line. Null for the torso (or a miss).
    /// </summary>
    public Part PartAt(Vector3 point, Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) return null;
        direction.Normalize();
        var bones = new Dictionary<Part, Transform>();
        foreach (var p in parts) { var t = FindDeep(transform, p.bone); if (t != null) bones[p] = t; }

        var smr = GetComponentInChildren<SkinnedMeshRenderer>();
        Transform root = smr != null && smr.rootBone != null ? smr.rootBone : transform;
        Part best = null;
        float bestDistance = maxHitDistance;
        foreach (var t in root.GetComponentsInChildren<Transform>())
        {
            if (t == root || t.GetComponent<Renderer>() != null) continue;   // bones only (no stumps / effects)
            // the segment from the parent joint to this one belongs to the part this bone is in
            float d = LineToSegment(point, direction, t.parent.position, t.position);
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = null;
            foreach (var kv in bones)
                if (t == kv.Value || t.IsChildOf(kv.Value)) { best = kv.Key; break; }
        }
        return best;
    }

    /// <summary>Cuts off whatever a blow through <paramref name="point"/> along <paramref name="direction"/> lands on.</summary>
    public bool SeverAt(Vector3 point, Vector3 direction)
    {
        Part p = PartAt(point, direction);
        return p != null && Sever(p, direction);
    }

    public bool Sever(string partName, Vector3 direction)
    {
        Part p = Find(partName);
        return p != null && Sever(p, direction);
    }

    public bool Sever(Part part, Vector3 direction)
    {
        if (part == null || severed.Contains(part)) return false;
        Transform bone = FindDeep(transform, part.bone);
        if (bone == null || bone.parent == null) return false;
        if (bloodFx == null) { var s = GetComponentInParent<Shooter>(); if (s != null) bloodFx = s.bloodFx; }

        // where it comes off: at the joint, the cut facing along the part (out of the body)
        Transform stumpParent = bone.parent;
        Vector3 cutPoint = Vector3.Lerp(stumpParent.position, bone.position, 0.85f);
        Transform next = null;
        foreach (Transform c in bone) if (c.GetComponent<Renderer>() == null) { next = c; break; }
        Vector3 along = next != null ? next.position - bone.position : bone.position - stumpParent.position;
        along = along.sqrMagnitude > 0.000001f ? along.normalized : Vector3.up;

        var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var done = new Dictionary<Mesh, Mesh>();   // original -> body (HitFlash overlays share the mesh)
        Transform piece = null;
        bool any = false;

        foreach (var smr in renderers)
        {
            Mesh original = smr.sharedMesh;
            if (original == null) continue;
            if (done.TryGetValue(original, out Mesh body)) { smr.sharedMesh = body; continue; }
            if (!original.isReadable) { Debug.LogWarning($"Dismemberment: '{original.name}' isn't Read/Write enabled"); continue; }
            if (!smr.enabled || smr.name.Contains("HitFlash")) continue;   // overlays get the body mesh above / below

            // how much of each vertex follows the part (its bone + everything under it)
            var partBones = new HashSet<int>();
            for (int b = 0; b < smr.bones.Length; b++)
                if (smr.bones[b] != null && (smr.bones[b] == bone || smr.bones[b].IsChildOf(bone))) partBones.Add(b);
            if (partBones.Count == 0) continue;
            var weights = original.boneWeights;
            var share = new float[weights.Length];
            for (int v = 0; v < weights.Length; v++)
            {
                var w = weights[v];
                float sum = 0f;
                if (partBones.Contains(w.boneIndex0)) sum += w.weight0;
                if (partBones.Contains(w.boneIndex1)) sum += w.weight1;
                if (partBones.Contains(w.boneIndex2)) sum += w.weight2;
                if (partBones.Contains(w.boneIndex3)) sum += w.weight3;
                share[v] = sum;
            }

            // pose right now, for the severed part. Baked without the renderer's scale, the vertices are already
            // world-sized in the renderer's position/rotation (the piece gets scale 1)
            var baked = new Mesh();
            smr.BakeMesh(baked, false);
            Vector3[] bakedV = baked.vertices, bakedN = baked.normals;
            Vector2[] uv = original.uv;
            Destroy(baked);

            body = Instantiate(original);
            body.name = original.name + " (no " + part.name + ")";
            // the piece keeps only the vertices its triangles use (so its bounds / collider fit it)
            var remap = new Dictionary<int, int>();
            var pv = new List<Vector3>(); var pn = new List<Vector3>(); var puv = new List<Vector2>();
            var pieceSubs = new List<List<int>>();
            int pieceTris = 0;
            for (int sm = 0; sm < original.subMeshCount; sm++)
            {
                var tris = original.GetTriangles(sm);
                var keep = new List<int>(tris.Length);
                var cut = new List<int>();
                for (int i = 0; i < tris.Length; i += 3)
                {
                    float avg = (share[tris[i]] + share[tris[i + 1]] + share[tris[i + 2]]) / 3f;
                    if (avg <= 0.5f) { keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]); continue; }
                    for (int k = 0; k < 3; k++)
                    {
                        int v = tris[i + k];
                        if (!remap.TryGetValue(v, out int nv))
                        {
                            nv = pv.Count;
                            remap[v] = nv;
                            pv.Add(bakedV[v]);
                            pn.Add(bakedN.Length > v ? bakedN[v] : Vector3.up);
                            puv.Add(uv.Length > v ? uv[v] : Vector2.zero);
                        }
                        cut.Add(nv);
                    }
                }
                body.SetTriangles(keep, sm);
                pieceSubs.Add(cut);
                pieceTris += cut.Count / 3;
            }
            if (pieceTris == 0) { Destroy(body); continue; }
            var pieceMesh = new Mesh { name = original.name + " (" + part.name + ")" };
            pieceMesh.SetVertices(pv);
            pieceMesh.SetNormals(pn);
            pieceMesh.SetUVs(0, puv);
            pieceMesh.subMeshCount = pieceSubs.Count;
            for (int sm = 0; sm < pieceSubs.Count; sm++) pieceMesh.SetTriangles(pieceSubs[sm], sm);
            pieceMesh.RecalculateBounds();
            done[original] = body;
            smr.sharedMesh = body;
            any = true;

            piece = SpawnPiece(part, pieceMesh, smr, direction, cutPoint, along);
        }
        if (!any) return false;
        // hidden overlay copies that were skipped above
        foreach (var smr in renderers)
            if (smr.sharedMesh != null && done.TryGetValue(smr.sharedMesh, out Mesh b)) smr.sharedMesh = b;

        severed.Add(part);

        // the body's ragdoll lets go of the part (an invisible limb would still swing, bump and drag),
        // then the bones collapse so nothing is left to see
        var ragdoll = GetComponent<Ragdoll>();
        if (ragdoll != null) ragdoll.Detach(bone);
        else
        {
            foreach (var c in bone.GetComponentsInChildren<Collider>()) c.enabled = false;
            foreach (var r in bone.GetComponentsInChildren<Rigidbody>()) r.detectCollisions = false;
        }
        bone.localScale = Vector3.zero;

        // stump, a splash at the cut, then the wound pumps blood out in pulses
        Stump(stumpParent, cutPoint, along, part.stumpSize);
        if (bloodFx != null) Destroy(Instantiate(bloodFx, cutPoint, Quaternion.LookRotation(along)), bloodLifetime);
        StartCoroutine(Spurt(stumpParent, cutPoint, along, spurts, 1f));
        if (piece != null) StartCoroutine(Spurt(piece, cutPoint, -along, pieceSpurts, pieceSpurtStrength));
        return true;
    }

    Transform SpawnPiece(Part part, Mesh mesh, SkinnedMeshRenderer source, Vector3 direction, Vector3 cutPoint, Vector3 along)
    {
        // centre the mesh on its pivot, so the rigidbody spins around the piece itself
        Vector3 centre = mesh.bounds.center;
        var verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++) verts[i] -= centre;
        mesh.vertices = verts;
        mesh.RecalculateBounds();

        var go = new GameObject("Severed " + part.name);
        go.layer = source.gameObject.layer;
        // baked vertices are world-sized in the renderer's position / rotation
        go.transform.SetPositionAndRotation(source.transform.position + source.transform.rotation * centre, source.transform.rotation);
        go.transform.localScale = Vector3.one;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = source.sharedMaterials;

        // a box tumbles and settles (a sphere would roll on forever)
        var col = go.AddComponent<BoxCollider>();
        col.size = mesh.bounds.size * 0.9f;

        var rb = go.AddComponent<Rigidbody>();
        // don't get stuck in (or launched by) the body it came off. After the rigidbody: adding one rebuilds
        // the collider, which forgets ignored pairs
        foreach (var c in GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(col, c);
        rb.mass = part.mass;
        rb.linearDamping = 0.2f;
        rb.angularDamping = 1.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        rb.linearVelocity = dir * pieceSpeed + Vector3.up * pieceLift;
        rb.angularVelocity = Random.onUnitSphere * pieceSpin;

        // a red cap where it came off
        Stump(go.transform, cutPoint, -along, part.stumpSize);

        Destroy(go, pieceLifetime);
        return go.transform;
    }

    /// <summary>A flattened dark-red cap at <paramref name="worldPos"/>, its flat side facing <paramref name="normal"/>.</summary>
    void Stump(Transform parent, Vector3 worldPos, Vector3 normal, float size)
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s.name = "Stump";
        DestroyImmediate(s.GetComponent<Collider>());   // a deferred destroy would join the piece's rigidbody for a step
        s.layer = parent.gameObject.layer;
        s.transform.SetParent(parent, false);
        s.transform.position = worldPos;
        s.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
        Vector3 ls = parent.lossyScale;
        float k = size / Mathf.Max(Mathf.Abs(ls.x), 0.0001f);
        s.transform.localScale = new Vector3(k, k * 0.45f, k);
        var r = s.GetComponent<MeshRenderer>();
        var src = GetComponentInChildren<SkinnedMeshRenderer>();
        var mat = new Material(src != null && src.sharedMaterial != null ? src.sharedMaterial : r.sharedMaterial);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", stumpColor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", stumpColor);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.6f);
        r.sharedMaterial = mat;
    }

    /// <summary>
    /// The jet rides on <paramref name="parent"/> (the ragdoll bone at the wound, or the flying part), so it
    /// keeps pointing out of the wound as that tumbles; its drops fly free in world space.
    /// </summary>
    IEnumerator Spurt(Transform parent, Vector3 at, Vector3 dir, int pulses, float strength)
    {
        if (jetFx == null || pulses <= 0) yield break;
        var fx = Instantiate(jetFx, at, Quaternion.LookRotation(dir));
        fx.transform.SetParent(parent, true);
        var ps = fx.GetComponent<ParticleSystem>();
        var main = ps.main;
        float speed = main.startSpeedMultiplier;
        var emission = ps.emission;
        float rate = emission.rateOverTimeMultiplier;
        for (int i = 0; i < pulses && fx != null; i++)
        {
            float k = strength * Mathf.Pow(spurtFalloff, i);
            main.startSpeedMultiplier = speed * k;
            emission.rateOverTimeMultiplier = rate * Mathf.Lerp(0.5f, 1f, k);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);   // keeps the drops in the air, restarts the jet
            ps.Play(true);
            yield return new WaitForSeconds(spurtInterval);
        }
        if (fx != null) Destroy(fx, bloodLifetime);
    }

    /// <summary>Closest distance between the line through <paramref name="p"/> along unit <paramref name="d"/> and segment ab.</summary>
    static float LineToSegment(Vector3 p, Vector3 d, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a, w = a - p;
        float abab = Vector3.Dot(ab, ab), abd = Vector3.Dot(ab, d);
        float denom = abab - abd * abd;   // |ab|^2 * sin^2 of the angle between them
        float s = denom > 1e-8f ? Mathf.Clamp01((abd * Vector3.Dot(w, d) - Vector3.Dot(w, ab)) / denom) : 0f;
        Vector3 q = a + ab * s;           // nearest point on the segment...
        Vector3 off = q - p;
        return (off - d * Vector3.Dot(off, d)).magnitude;   // ...and its distance from the line
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
}
