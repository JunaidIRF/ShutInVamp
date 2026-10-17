using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

/// <summary>
/// Cuts parts off a skinned enemy model: any bone, with everything under it. The skinned mesh is split by
/// bone weights (triangles weighted mostly to the bone or its children go), so it works for any rig: the
/// body keeps skinning without the part, the part becomes a physics object, both ends get a stump and the
/// wound spurts blood. Works on a living (animated) model too, e.g. from an execution event. Needs the
/// model's mesh to be Read/Write enabled.
/// </summary>
public class Dismemberment : MonoBehaviour
{
    /// <summary>What can be cut off (picked by execution events). Each one is mapped to this rig's bone in <see cref="parts"/>.</summary>
    public enum BodyPart
    {
        None,
        Head,
        [InspectorName("Left Arm (at the shoulder)")] LeftArm,
        [InspectorName("Left Forearm (at the elbow)")] LeftForearm,
        [InspectorName("Left Hand (at the wrist)")] LeftHand,
        [InspectorName("Right Arm (at the shoulder)")] RightArm,
        [InspectorName("Right Forearm (at the elbow)")] RightForearm,
        [InspectorName("Right Hand (at the wrist)")] RightHand,
        [InspectorName("Left Leg (at the hip)")] LeftLeg,
        [InspectorName("Left Lower Leg (at the knee)")] LeftLowerLeg,
        [InspectorName("Left Foot (at the ankle)")] LeftFoot,
        [InspectorName("Right Leg (at the hip)")] RightLeg,
        [InspectorName("Right Lower Leg (at the knee)")] RightLowerLeg,
        [InspectorName("Right Foot (at the ankle)")] RightFoot,
    }

    /// <summary>A body part on this rig: the bone it comes off at (with everything under it) and how it looks / weighs.</summary>
    [Serializable]
    public class Part
    {
        public BodyPart part = BodyPart.Head;
        [BonePicker] public string bone = "mixamorig:Head";   // the part is this bone and everything under it
        [Tooltip("Stump width in m. 0 = the wound's own width, measured on the mesh")]
        public float stumpSize = 0f;
        [Tooltip("Weight of the cut-off piece in kg. 0 = from its size")]
        public float mass = 4f;
        [Tooltip("A killing claw blow landing on this part cuts it off")]
        public bool claw = true;
    }

    public List<Part> parts = DefaultParts();

    /// <summary>The Mixamo rig's bones for every body part; claw blows take the head and whole limbs.</summary>
    public static List<Part> DefaultParts() => new List<Part>
    {
        new Part { part = BodyPart.Head, bone = "mixamorig:Head", stumpSize = 0f, mass = 4f },
        new Part { part = BodyPart.LeftArm, bone = "mixamorig:LeftArm", stumpSize = 0f, mass = 3f },
        new Part { part = BodyPart.LeftForearm, bone = "mixamorig:LeftForeArm", stumpSize = 0f, mass = 0f, claw = false },
        new Part { part = BodyPart.LeftHand, bone = "mixamorig:LeftHand", stumpSize = 0f, mass = 0f, claw = false },
        new Part { part = BodyPart.RightArm, bone = "mixamorig:RightArm", stumpSize = 0f, mass = 3f },
        new Part { part = BodyPart.RightForearm, bone = "mixamorig:RightForeArm", stumpSize = 0f, mass = 0f, claw = false },
        new Part { part = BodyPart.RightHand, bone = "mixamorig:RightHand", stumpSize = 0f, mass = 0f, claw = false },
        new Part { part = BodyPart.LeftLeg, bone = "mixamorig:LeftUpLeg", stumpSize = 0f, mass = 6f },
        new Part { part = BodyPart.LeftLowerLeg, bone = "mixamorig:LeftLeg", stumpSize = 0f, mass = 0f, claw = false },
        new Part { part = BodyPart.LeftFoot, bone = "mixamorig:LeftFoot", stumpSize = 0f, mass = 0f, claw = false },
        new Part { part = BodyPart.RightLeg, bone = "mixamorig:RightUpLeg", stumpSize = 0f, mass = 6f },
        new Part { part = BodyPart.RightLowerLeg, bone = "mixamorig:RightLeg", stumpSize = 0f, mass = 0f, claw = false },
        new Part { part = BodyPart.RightFoot, bone = "mixamorig:RightFoot", stumpSize = 0f, mass = 0f, claw = false },
    };

    /// <summary>"LeftLowerLeg" -> "Left Lower Leg".</summary>
    public static string Label(BodyPart part)
    {
        string s = part.ToString();
        var sb = new System.Text.StringBuilder(s.Length + 4);
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i])) sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
    [Tooltip("A blow whose line passes further than this from every bone of a part doesn't cut anything")]
    public float maxHitDistance = 0.35f;

    [Header("Severed part")]
    [FormerlySerializedAs("headSpeed")] public float pieceSpeed = 4f;        // m/s along the blow
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

    readonly List<Transform> severed = new List<Transform>();                  // bones cut off so far
    readonly List<(Transform bone, GameObject go)> wounds = new List<(Transform, GameObject)>();   // stumps / jets on the body
    readonly List<Collider> pieces = new List<Collider>();                     // severed parts still around

    void OnEnable()
    {
        severed.Clear();
        wounds.Clear();
        pieces.Clear();
    }

    // a living model's Animator would bring a cut bone's scale back
    void LateUpdate()
    {
        foreach (var b in severed)
            if (b != null) b.localScale = Vector3.zero;
    }

    /// <summary>True if <paramref name="bone"/> (or a bone above it) has been cut off.</summary>
    public bool IsCut(Transform bone)
    {
        foreach (var b in severed)
            if (b != null && bone != null && (bone == b || bone.IsChildOf(b))) return true;
        return false;
    }

    public Part Find(BodyPart part) => part == BodyPart.None ? null : parts.Find(p => p.part == part);

    /// <summary>This rig's bone for <paramref name="part"/> (null if it isn't mapped).</summary>
    public Transform BoneOf(BodyPart part)
    {
        Part p = Find(part);
        return p != null ? FindDeep(transform, p.bone) : null;
    }

    /// <summary>
    /// The part a blow along <paramref name="direction"/> through <paramref name="point"/> lands on: the one
    /// with the bone segment nearest that line. Null for the torso (or a miss).
    /// </summary>
    public Part PartAt(Vector3 point, Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) return null;
        direction.Normalize();
        var bones = new Dictionary<Part, Transform>();
        foreach (var p in parts)
        {
            if (!p.claw) continue;
            var t = FindDeep(transform, p.bone);
            if (t != null && !IsCut(t)) bones[p] = t;
        }

        Transform root = RootBone();
        Part best = null;
        float bestDistance = maxHitDistance;
        foreach (var t in root.GetComponentsInChildren<Transform>())
        {
            if (t == root || t.GetComponent<Renderer>() != null || IsCut(t)) continue;   // bones only (no stumps / effects)
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

    /// <summary>Cuts off <paramref name="part"/> (at the bone it's mapped to in <see cref="parts"/>).</summary>
    public bool Sever(BodyPart part, Vector3 direction) => Sever(Find(part), direction);

    public bool Sever(Part part, Vector3 direction) => part != null && Sever(FindDeep(transform, part.bone), direction);

    /// <summary>Cuts the body off at <paramref name="bone"/>: it and everything under it fly off.</summary>
    public bool Sever(Transform bone, Vector3 direction)
    {
        if (!CanCut(bone) || IsCut(bone)) return false;
        if (bloodFx == null) { var s = GetComponentInParent<Shooter>(); if (s != null) bloodFx = s.bloodFx; }

        CutPose(bone, out Vector3 cutPoint, out Vector3 along);
        Part tuned = parts.Find(p => p.bone == bone.name);
        string partName = tuned != null ? Label(tuned.part) : BoneLabel(bone.name);
        float stumpSize = tuned != null ? tuned.stumpSize : 0f;   // 0 = measured below

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

            float[] share = PartShare(smr, original, new[] { bone });
            if (share == null) continue;

            // pose right now, for the severed part. Baked without the renderer's scale, the vertices are already
            // world-sized in the renderer's position/rotation (the piece gets scale 1)
            var baked = new Mesh();
            smr.BakeMesh(baked, false);
            Vector3[] bakedV = baked.vertices, bakedN = baked.normals;
            Vector2[] uv = original.uv;
            Destroy(baked);

            body = Instantiate(original);
            body.name = original.name + " (no " + partName + ")";
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
                    if (!IsPartTriangle(share, tris[i], tris[i + 1], tris[i + 2])) { keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]); continue; }
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
            var pieceMesh = new Mesh { name = original.name + " (" + partName + ")" };
            pieceMesh.SetVertices(pv);
            pieceMesh.SetNormals(pn);
            pieceMesh.SetUVs(0, puv);
            pieceMesh.subMeshCount = pieceSubs.Count;
            for (int sm = 0; sm < pieceSubs.Count; sm++) pieceMesh.SetTriangles(pieceSubs[sm], sm);
            pieceMesh.RecalculateBounds();
            done[original] = body;
            smr.sharedMesh = body;
            any = true;

            // the wound on the posed mesh: where the stumps sit and how wide (they and the jets face along the bone)
            var world = new Vector3[bakedV.Length];
            for (int v = 0; v < world.Length; v++) world[v] = smr.transform.position + smr.transform.rotation * bakedV[v];
            if (MeasureCut(original, share, world, along, out Vector3 ringCentre, out float ringWidth))
            {
                cutPoint = ringCentre;
                if (stumpSize <= 0f) stumpSize = ringWidth;
            }
            if (stumpSize <= 0f) stumpSize = 0.08f;
            float mass = tuned != null && tuned.mass > 0f ? tuned.mass : Mathf.Clamp(pieceMesh.bounds.size.x * pieceMesh.bounds.size.y * pieceMesh.bounds.size.z * 500f, 0.5f, 25f);
            piece = SpawnPiece(partName, mass, stumpSize, pieceMesh, smr, direction, cutPoint, along);
        }
        if (!any) return false;
        // hidden overlay copies that were skipped above
        foreach (var smr in renderers)
            if (smr.sharedMesh != null && done.TryGetValue(smr.sharedMesh, out Mesh b)) smr.sharedMesh = b;

        severed.Add(bone);

        // stumps / jets from an earlier cut further down this part go with it
        for (int i = wounds.Count - 1; i >= 0; i--)
        {
            var (at, go) = wounds[i];
            if (go == null || at == null || at == bone || at.IsChildOf(bone)) { if (go != null) Destroy(go); wounds.RemoveAt(i); }
        }

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
        Transform stumpParent = bone.parent;
        wounds.Add((stumpParent, MakeStump(stumpParent, cutPoint, along, stumpSize, BodyMaterial(), stumpColor)));
        if (bloodFx != null) Destroy(Instantiate(bloodFx, cutPoint, Quaternion.LookRotation(along)), bloodLifetime);
        StartCoroutine(Spurt(stumpParent, cutPoint, along, spurts, 1f, true));
        if (piece != null) StartCoroutine(Spurt(piece, cutPoint, -along, pieceSpurts, pieceSpurtStrength, false));
        return true;
    }

    /// <summary>
    /// Keeps the severed parts from colliding with the body they came off. Call again when the body's
    /// colliders change (the ragdoll switching on): re-enabled colliders forget ignored pairs.
    /// </summary>
    public void IgnoreBody()
    {
        pieces.RemoveAll(p => p == null);
        if (pieces.Count == 0) return;
        Shooter owner = GetComponentInParent<Shooter>();
        Transform root = owner != null ? owner.transform : transform;
        foreach (var c in root.GetComponentsInChildren<Collider>())
            if (c.enabled)   // ignoring a disabled collider is an error
                foreach (var p in pieces) Physics.IgnoreCollision(p, c);
    }

    Transform SpawnPiece(string partName, float mass, float stumpSize, Mesh mesh, SkinnedMeshRenderer source, Vector3 direction, Vector3 cutPoint, Vector3 along)
    {
        // centre the mesh on its pivot, so the rigidbody spins around the piece itself
        Vector3 centre = mesh.bounds.center;
        var verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++) verts[i] -= centre;
        mesh.vertices = verts;
        mesh.RecalculateBounds();

        var go = new GameObject("Severed " + partName);
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
        rb.mass = mass;
        rb.linearDamping = 0.2f;
        rb.angularDamping = 1.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        rb.linearVelocity = dir * pieceSpeed + Vector3.up * pieceLift;
        rb.angularVelocity = Random.onUnitSphere * pieceSpin;

        // don't get stuck in (or launched by) the body it came off. After the rigidbody: adding one rebuilds
        // the collider, which forgets ignored pairs
        pieces.Add(col);
        IgnoreBody();

        // a red cap where it came off
        MakeStump(go.transform, cutPoint, -along, stumpSize, BodyMaterial(), stumpColor);

        Destroy(go, pieceLifetime);
        return go.transform;
    }

    Material BodyMaterial()
    {
        var src = GetComponentInChildren<SkinnedMeshRenderer>();
        return src != null ? src.sharedMaterial : null;
    }

    /// <summary>
    /// The jet rides on <paramref name="parent"/> (the bone at the wound, or the flying part), so it keeps
    /// pointing out of the wound as that moves; its drops fly free in world space.
    /// </summary>
    IEnumerator Spurt(Transform parent, Vector3 at, Vector3 dir, int pulses, float strength, bool onBody)
    {
        if (jetFx == null || pulses <= 0) yield break;
        var fx = Instantiate(jetFx, at, Quaternion.LookRotation(dir));
        fx.transform.SetParent(parent, true);
        if (onBody) wounds.Add((parent, fx));
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

    Transform RootBone()
    {
        var smr = GetComponentInChildren<SkinnedMeshRenderer>();
        return smr != null && smr.rootBone != null ? smr.rootBone : transform;
    }

    // ══════════════════════════════════════════════════════════════════
    //  SHARED WITH THE EDITOR PREVIEW — the Animate stage shows cuts with exactly these rules
    // ══════════════════════════════════════════════════════════════════

    /// <summary>A bone that can be cut: one of this model's bones below the root bone.</summary>
    public bool CanCut(Transform bone)
    {
        if (bone == null || bone.parent == null || !bone.IsChildOf(transform)) return false;
        Transform root = RootBone();
        return bone != root && bone.IsChildOf(root);
    }

    /// <summary>
    /// A rough cut from the bones alone (when the mesh can't be measured): just before the joint, facing
    /// along the parent bone into the part.
    /// </summary>
    public static void CutPose(Transform bone, out Vector3 point, out Vector3 along)
    {
        Transform parent = bone.parent;
        point = Vector3.Lerp(parent.position, bone.position, 0.85f);
        along = bone.position - parent.position;
        along = along.sqrMagnitude > 0.000001f ? along.normalized : Vector3.up;
    }

    /// <summary>How much of each vertex follows <paramref name="bones"/> (each with everything under it). Null if none do.</summary>
    public static float[] PartShare(SkinnedMeshRenderer smr, Mesh mesh, IList<Transform> bones)
    {
        var partBones = new HashSet<int>();
        Transform[] smrBones = smr.bones;
        for (int b = 0; b < smrBones.Length; b++)
        {
            if (smrBones[b] == null) continue;
            foreach (var bone in bones)
                if (bone != null && (smrBones[b] == bone || smrBones[b].IsChildOf(bone))) { partBones.Add(b); break; }
        }
        if (partBones.Count == 0) return null;
        var weights = mesh.boneWeights;
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
        return share;
    }

    /// <summary>A triangle goes with the part when its corners follow the part more than half on average.</summary>
    public static bool IsPartTriangle(float[] share, int a, int b, int c) => (share[a] + share[b] + share[c]) / 3f > 0.5f;

    /// <summary>A copy of <paramref name="mesh"/> without the triangles that go with <paramref name="bones"/> (same rule as a cut).</summary>
    public static Mesh WithoutParts(SkinnedMeshRenderer smr, Mesh mesh, IList<Transform> bones)
    {
        float[] share = PartShare(smr, mesh, bones);
        var body = Instantiate(mesh);
        if (share == null) return body;
        for (int sm = 0; sm < mesh.subMeshCount; sm++)
        {
            var tris = mesh.GetTriangles(sm);
            var keep = new List<int>(tris.Length);
            for (int i = 0; i < tris.Length; i += 3)
                if (!IsPartTriangle(share, tris[i], tris[i + 1], tris[i + 2])) { keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]); }
            body.SetTriangles(keep, sm);
        }
        return body;
    }

    /// <summary>
    /// The cut at <paramref name="bone"/> in the model's current pose: the wound's centre, the way it faces
    /// (out of the body, into the part) and its width. What the stumps and the blood jets use.
    /// </summary>
    public void CutShape(Transform bone, out Vector3 point, out Vector3 normal, out float size)
    {
        CutPose(bone, out point, out normal);
        size = 0.08f;
        var smr = GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr != null && smr.sharedMesh != null && smr.sharedMesh.isReadable)
        {
            float[] share = PartShare(smr, smr.sharedMesh, new[] { bone });
            if (share != null && MeasureCut(smr.sharedMesh, share, PosedVertices(smr), normal, out Vector3 c, out float w))
            {
                point = c; size = w;
            }
        }
        Part tuned = parts.Find(p => p.bone == bone.name);
        if (tuned != null && tuned.stumpSize > 0f) size = tuned.stumpSize;
    }

    /// <summary>The skinned mesh's vertices in world space, in its current pose.</summary>
    static Vector3[] PosedVertices(SkinnedMeshRenderer smr)
    {
        var baked = new Mesh();
        smr.BakeMesh(baked, false);   // world-sized, in the renderer's position / rotation
        Vector3[] v = baked.vertices;
        DestroyImmediate(baked);
        for (int i = 0; i < v.Length; i++) v[i] = smr.transform.position + smr.transform.rotation * v[i];
        return v;
    }

    /// <summary>
    /// Measures the wound on the posed mesh: the ring of vertices the part's triangles share with the body's
    /// (the edge of the hole). Centre = the ring's middle, width = its diameter across
    /// <paramref name="facing"/>. The facing itself comes from the bones (<see cref="CutPose"/>): the mesh's
    /// rings are too uneven (UV seams, slanted wrists / ankles) to give a reliable one. False if there's no ring.
    /// </summary>
    public static bool MeasureCut(Mesh mesh, float[] share, Vector3[] world, Vector3 facing, out Vector3 centre, out float width)
    {
        centre = Vector3.zero; width = 0f;
        int n = Mathf.Min(world.Length, share.Length);
        var inPart = new bool[n];
        var inBody = new bool[n];
        for (int sm = 0; sm < mesh.subMeshCount; sm++)
        {
            var tris = mesh.GetTriangles(sm);
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                if (a >= n || b >= n || c >= n) continue;
                var side = IsPartTriangle(share, a, b, c) ? inPart : inBody;
                side[a] = side[b] = side[c] = true;
            }
        }
        var ring = new List<Vector3>();
        for (int v = 0; v < n; v++)
            if (inPart[v] && inBody[v]) ring.Add(world[v]);
        if (ring.Count < 3) return false;

        foreach (var p in ring) centre += p;
        centre /= ring.Count;
        var radii = new List<float>(ring.Count);
        foreach (var p in ring) { Vector3 d = p - centre; radii.Add((d - facing * Vector3.Dot(d, facing)).magnitude); }
        radii.Sort();
        width = Mathf.Clamp(radii[Mathf.Min(radii.Count - 1, (int)(radii.Count * 0.9f))] * 2f, 0.02f, 0.5f);
        return true;
    }

    /// <summary>A flattened dark-red cap at <paramref name="worldPos"/>, its flat side facing <paramref name="normal"/>.</summary>
    public static GameObject MakeStump(Transform parent, Vector3 worldPos, Vector3 normal, float size, Material bodyMaterial, Color color)
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
        var mat = new Material(bodyMaterial != null ? bodyMaterial : r.sharedMaterial);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.6f);
        r.sharedMaterial = mat;
        return s;
    }

    /// <summary>"mixamorig:LeftForeArm" -> "LeftForeArm".</summary>
    public static string BoneLabel(string boneName)
    {
        if (string.IsNullOrEmpty(boneName)) return "";
        int colon = boneName.LastIndexOf(':');
        return colon >= 0 ? boneName.Substring(colon + 1) : boneName;
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

    public static Transform FindDeep(Transform root, string name)
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
