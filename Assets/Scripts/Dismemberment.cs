using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

/// <summary>
/// Cuts parts off a skinned enemy model: any bone, with everything under it. The skinned mesh is split by
/// bone weights (triangles weighted mostly to the bone or its children go), so it works for any rig: the
/// body keeps skinning without the part, the part becomes a physics object, and both openings are closed
/// with a flesh cap built from the hole's own edge (part of the mesh, so it stays flush as the body moves).
/// The wound spurts blood. Works on a living (animated) model too, e.g. from an execution event. Needs the
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

    /// <summary>A body part on this rig: the bone it comes off at (with everything under it) and how much it weighs.</summary>
    [Serializable]
    public class Part
    {
        public BodyPart part = BodyPart.Head;
        [BonePicker] public string bone = "mixamorig:Head";   // the part is this bone and everything under it
        [Tooltip("Weight of the cut-off piece in kg. 0 = from its size")]
        public float mass = 4f;
        [Tooltip("A killing claw blow landing on this part cuts it off")]
        public bool claw = true;
    }

    public List<Part> parts = DefaultParts();

    /// <summary>The Mixamo rig's bones for every body part; claw blows take the head and whole limbs.</summary>
    public static List<Part> DefaultParts() => new List<Part>
    {
        new Part { part = BodyPart.Head, bone = "mixamorig:Head", mass = 4f },
        new Part { part = BodyPart.LeftArm, bone = "mixamorig:LeftArm", mass = 3f },
        new Part { part = BodyPart.LeftForearm, bone = "mixamorig:LeftForeArm", mass = 0f, claw = false },
        new Part { part = BodyPart.LeftHand, bone = "mixamorig:LeftHand", mass = 0f, claw = false },
        new Part { part = BodyPart.RightArm, bone = "mixamorig:RightArm", mass = 3f },
        new Part { part = BodyPart.RightForearm, bone = "mixamorig:RightForeArm", mass = 0f, claw = false },
        new Part { part = BodyPart.RightHand, bone = "mixamorig:RightHand", mass = 0f, claw = false },
        new Part { part = BodyPart.LeftLeg, bone = "mixamorig:LeftUpLeg", mass = 6f },
        new Part { part = BodyPart.LeftLowerLeg, bone = "mixamorig:LeftLeg", mass = 0f, claw = false },
        new Part { part = BodyPart.LeftFoot, bone = "mixamorig:LeftFoot", mass = 0f, claw = false },
        new Part { part = BodyPart.RightLeg, bone = "mixamorig:RightUpLeg", mass = 6f },
        new Part { part = BodyPart.RightLowerLeg, bone = "mixamorig:RightLeg", mass = 0f, claw = false },
        new Part { part = BodyPart.RightFoot, bone = "mixamorig:RightFoot", mass = 0f, claw = false },
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

    [Header("Wound")]
    [FormerlySerializedAs("stumpColor")] public Color woundColor = new Color(0.3f, 0.02f, 0.025f);
    [Tooltip("How far the middle of the wound sinks in, as a share of the hole's radius")]
    [Range(0f, 1f)] public float woundDepth = 0.3f;

    [Header("Blood")]
    public GameObject bloodFx;              // one splash at the cut; falls back to the Shooter's blood effect
    [FormerlySerializedAs("neckBloodFx")] public GameObject jetFx;   // the jet out of the wound, replayed for every pulse
    public int spurts = 5;
    public float spurtInterval = 0.32f;
    public float spurtFalloff = 0.82f;      // each pulse this much weaker than the last
    [FormerlySerializedAs("headSpurts")] public int pieceSpurts = 2;   // a couple of weaker jets out of the flying part
    [FormerlySerializedAs("headSpurtStrength")] public float pieceSpurtStrength = 0.6f;
    public float bloodLifetime = 9f;

    readonly List<Transform> severed = new List<Transform>();                  // bones cut off so far
    readonly List<(Transform bone, GameObject go)> jets = new List<(Transform, GameObject)>();   // blood jets on the body
    readonly List<Collider> pieces = new List<Collider>();                     // severed parts still around
    Material woundMaterial;

    void OnEnable()
    {
        severed.Clear();
        jets.Clear();
        pieces.Clear();
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
            if (t == root || t.GetComponent<Renderer>() != null || IsCut(t)) continue;   // bones only (no effects)
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
        if (woundMaterial == null) woundMaterial = MakeWoundMaterial(BodyMaterial(), woundColor);

        Part tuned = parts.Find(p => p.bone == bone.name);
        string partName = tuned != null ? Label(tuned.part) : BoneLabel(bone.name);
        CutPose(bone, out Vector3 cutPoint, out Vector3 along);   // replaced by the measured wound below

        var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var done = new Dictionary<Mesh, Mesh>();   // original -> body (HitFlash overlays share the mesh)
        Transform piece = null;
        bool any = false;

        foreach (var smr in renderers)
        {
            Mesh original = smr.sharedMesh;
            if (original == null || done.ContainsKey(original)) continue;
            if (!original.isReadable) { Debug.LogWarning($"Dismemberment: '{original.name}' isn't Read/Write enabled"); continue; }
            if (!smr.enabled || smr.name.Contains("HitFlash")) continue;   // overlays get the body mesh below

            // the pose right now, for the flying piece. Baked without the renderer's scale, the vertices are
            // world-sized in the renderer's position / rotation (the piece gets scale 1)
            var baked = new Mesh();
            smr.BakeMesh(baked, false);
            Vector3[] posedV = baked.vertices, posedN = baked.normals;
            Destroy(baked);

            CutResult cut = CutMesh(smr, original, smr.sharedMaterials, woundMaterial, bone, posedV, posedN, woundDepth);
            if (cut == null) continue;
            cut.body.name = original.name + " (no " + partName + ")";
            done[original] = cut.body;
            smr.sharedMesh = cut.body;
            smr.sharedMaterials = cut.bodyMaterials;
            any = true;

            if (cut.hasWound)
            {
                cutPoint = smr.transform.position + smr.transform.rotation * cut.posedCentre;
                along = smr.transform.rotation * cut.posedNormal;
            }
            float mass = tuned != null && tuned.mass > 0f ? tuned.mass : Mathf.Clamp(cut.piece.bounds.size.x * cut.piece.bounds.size.y * cut.piece.bounds.size.z * 500f, 0.5f, 25f);
            cut.piece.name = original.name + " (" + partName + ")";
            piece = SpawnPiece(partName, mass, cut.piece, cut.bodyMaterials, smr, direction);
        }
        if (!any) return false;
        // overlay copies that were skipped above
        foreach (var smr in renderers)
            if (smr.sharedMesh != null && done.TryGetValue(smr.sharedMesh, out Mesh b)) smr.sharedMesh = b;

        severed.Add(bone);

        // jets from an earlier cut further down this part go with it
        for (int i = jets.Count - 1; i >= 0; i--)
        {
            var (at, go) = jets[i];
            if (go == null || at == null || at == bone || at.IsChildOf(bone)) { if (go != null) Destroy(go); jets.RemoveAt(i); }
        }

        // the body's ragdoll lets go of the part (an invisible limb would still swing, bump and drag)
        var ragdoll = GetComponent<Ragdoll>();
        if (ragdoll != null) ragdoll.Detach(bone);
        else
        {
            foreach (var c in bone.GetComponentsInChildren<Collider>()) c.enabled = false;
            foreach (var r in bone.GetComponentsInChildren<Rigidbody>()) r.detectCollisions = false;
        }

        // a splash at the cut, then the wound pumps blood out in pulses
        Transform jetParent = bone.parent;
        if (bloodFx != null) Destroy(Instantiate(bloodFx, cutPoint, Quaternion.LookRotation(along)), bloodLifetime);
        StartCoroutine(Spurt(jetParent, cutPoint, along, spurts, 1f, true));
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

    Transform SpawnPiece(string partName, float mass, Mesh mesh, Material[] materials, SkinnedMeshRenderer source, Vector3 direction)
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
        mr.sharedMaterials = materials;

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
        if (onBody) jets.Add((parent, fx));
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
    //  THE CUT — shared with the editor preview, so the Animate stage shows exactly what the game does
    // ══════════════════════════════════════════════════════════════════

    /// <summary>A bone that can be cut: one of this model's bones below the root bone.</summary>
    public bool CanCut(Transform bone)
    {
        if (bone == null || bone.parent == null || !bone.IsChildOf(transform)) return false;
        Transform root = RootBone();
        return bone != root && bone.IsChildOf(root);
    }

    /// <summary>A rough cut from the bones alone (if the mesh has no clean opening): by the joint, facing into the part.</summary>
    public static void CutPose(Transform bone, out Vector3 point, out Vector3 along)
    {
        Transform parent = bone.parent;
        point = Vector3.Lerp(parent.position, bone.position, 0.85f);
        along = bone.position - parent.position;
        along = along.sqrMagnitude > 0.000001f ? along.normalized : Vector3.up;
    }

    /// <summary>The flat dark flesh the wound caps are drawn with.</summary>
    public static Material MakeWoundMaterial(Material body, Color color)
    {
        Material mat = body != null ? new Material(body) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.name = "Wound";
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", null);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.35f);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
        return mat;
    }

    public class CutResult
    {
        public Mesh body;                  // still skinned: no part triangles, no weight on the part's bones, hole capped
        public Material[] bodyMaterials;   // the renderer's materials, plus the wound material if it wasn't there
        public int capCentre = -1;         // the body vertex in the middle of the new cap (-1 if no clean opening)
        public bool hasWound;              // an opening was found and capped
        public Mesh piece;                 // the part in its pose (renderer space, no scale), its end capped; null if not asked
        public Vector3 posedCentre;        // the wound's middle and facing (out of the body, into the part), in the
        public Vector3 posedNormal;        //   piece's renderer space; only with a posed mesh
    }

    /// <summary>
    /// Cuts <paramref name="mesh"/> at <paramref name="bone"/> (it and everything under it) as a clean slice:
    /// along the line where the skin follows the body's bones and the part's equally, splitting the triangles
    /// it runs through, so the opening's edge is a smooth ring around the joint. The body keeps every vertex
    /// (indices stay valid for later cuts) but loses the part's triangles and all weight on the part's bones,
    /// so its edge stays put. Both openings (found by vertex position: UV seams split vertices) are closed
    /// with a slightly sunken cap in the wound submesh. With <paramref name="posed"/> (baked, renderer space
    /// without scale) it also builds the cut-off piece. Null if nothing goes with the bone.
    /// </summary>
    public static CutResult CutMesh(SkinnedMeshRenderer smr, Mesh mesh, Material[] materials, Material wound, Transform bone,
                                    Vector3[] posed, Vector3[] posedNormals, float depth)
    {
        float[] share = PartShare(smr, mesh, new[] { bone }, out HashSet<int> partBones);
        if (share == null) return null;

        // every vertex attribute as a list: the slice adds vertices on the cut line
        var V = new List<Vector3>(mesh.vertices);
        int n0 = V.Count;
        var N = new List<Vector3>(mesh.normals);
        while (N.Count < n0) N.Add(Vector3.up);
        Vector4[] tangents = mesh.tangents;
        var T = tangents.Length == n0 ? new List<Vector4>(tangents) : null;
        Vector2[] uvs = mesh.uv;
        var UV = new List<Vector2>(uvs.Length == n0 ? uvs : new Vector2[n0]);
        bool hasUV = uvs.Length == n0;
        var W = new List<BoneWeight>(mesh.boneWeights);
        var P = posed != null ? new List<Vector3>(posed) : null;
        var PN = posed != null ? new List<Vector3>(posedNormals != null && posedNormals.Length == n0 ? posedNormals : new Vector3[n0]) : null;

        int subCount = mesh.subMeshCount;
        int woundSub = Array.IndexOf(materials, wound);
        if (woundSub >= subCount) woundSub = -1;
        int capSub = woundSub >= 0 ? woundSub : subCount;
        var tris = new int[subCount][];
        for (int sm = 0; sm < subCount; sm++) tris[sm] = mesh.GetTriangles(sm);

        // vertices at the same spot count as one (UV seams duplicate them), so an opening is one closed edge
        float q = Mathf.Max(mesh.bounds.size.magnitude, 1e-6f) * 1e-5f;
        var firstAt = new Dictionary<Vector3Int, int>();
        var weld = new List<int>(n0);
        int SpotOf(int v)
        {
            Vector3 p = V[v];
            var key = new Vector3Int(Mathf.RoundToInt(p.x / q), Mathf.RoundToInt(p.y / q), Mathf.RoundToInt(p.z / q));
            if (!firstAt.TryGetValue(key, out int w)) { w = v; firstAt[key] = v; }
            return w;
        }
        for (int v = 0; v < n0; v++) weld.Add(SpotOf(v));

        // 1. sort the triangles by which side of the line they're on where the skin is exactly half body, half
        //    part, splitting the ones it runs through: the cut follows the weights (which ring each joint) and
        //    its edge is a smooth line through the triangles rather than their zigzag edges
        var keep = new List<int>[subCount];
        var cut = new List<int>[subCount];
        var onPlane = new Dictionary<long, int>();   // edge -> its new vertex on the line, shared by both triangles
        const float eps = 1e-4f;
        int Split(int x, int y, float dx, float dy)
        {
            long key = x < y ? ((long)x << 32) | (uint)y : ((long)y << 32) | (uint)x;
            if (onPlane.TryGetValue(key, out int made)) return made;
            float s = dx / (dx - dy);
            int v = V.Count;
            V.Add(Vector3.Lerp(V[x], V[y], s));
            N.Add(Vector3.Lerp(N[x], N[y], s).normalized);
            T?.Add(Vector4.Lerp(T[x], T[y], s));
            UV.Add(Vector2.Lerp(UV[x], UV[y], s));
            W.Add(Mix(W[x], W[y], s));
            P?.Add(Vector3.Lerp(P[x], P[y], s));
            PN?.Add(Vector3.Lerp(PN[x], PN[y], s).normalized);
            weld.Add(SpotOf(v));
            onPlane[key] = v;
            return v;
        }
        for (int sm = 0; sm < subCount; sm++)
        {
            keep[sm] = new List<int>();
            cut[sm] = new List<int>();
            var t = tris[sm];
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                int a = t[i], b = t[i + 1], c = t[i + 2];
                var tv = new[] { a, b, c };
                var td = new float[3];
                for (int k = 0; k < 3; k++)
                {
                    float dk = (tv[k] < share.Length ? share[tv[k]] : 0f) - 0.5f;
                    td[k] = Mathf.Abs(dk) < eps ? eps : dk;   // exactly half counts as the part's side
                }
                int onPart = (td[0] > 0f ? 1 : 0) + (td[1] > 0f ? 1 : 0) + (td[2] > 0f ? 1 : 0);
                if (onPart == 3) { cut[sm].AddRange(tv); continue; }
                if (onPart == 0) { keep[sm].AddRange(tv); continue; }

                // one corner alone on its side: rotate it first (keeps the winding), split its two edges
                bool loneIsPart = onPart == 1;
                int l = 0;
                while ((td[l] > 0f) != loneIsPart) l++;
                int A = tv[l], B = tv[(l + 1) % 3], C = tv[(l + 2) % 3];
                float dA = td[l], dB = td[(l + 1) % 3], dC = td[(l + 2) % 3];
                int ab = Split(A, B, dA, dB), ac = Split(A, C, dA, dC);
                var lone = loneIsPart ? cut[sm] : keep[sm];
                var rest = loneIsPart ? keep[sm] : cut[sm];
                lone.AddRange(new[] { A, ab, ac });
                rest.AddRange(new[] { ab, B, C });
                rest.AddRange(new[] { ab, C, ac });
            }
        }

        if (Count(cut) == 0) return null;

        // 2. the openings: body edges the part shared, and the same from the part's side
        var keepEdges = EdgeSet(keep, weld);
        var cutEdges = EdgeSet(cut, weld);
        var bodyRim = Rim(keep, cutEdges, weld);
        var partRim = Rim(cut, keepEdges, weld);
        var result = new CutResult();

        // ── body: weights off the part's bones, the part's triangles out, the opening capped ──
        var bw = new List<BoneWeight>(W.Count);
        foreach (var w in W) bw.Add(WithoutBones(w, partBones));
        var capTris = new List<int>();
        var copiedFrom = new List<int>();

        // The mesh's own space can be squashed (this rig's is), so the cap is shaped where the skin really draws
        // it: each vertex through its skin matrix in the current pose. New positions and normals then go back
        // through the inverse, so they come out right once skinned.
        Matrix4x4[] boneSkin = BoneSkin(smr, mesh);
        var world = new Vector3[V.Count];
        var partWorld = Vector3.zero; int partCount = 0;
        for (int v = 0; v < V.Count; v++) world[v] = Skin(boneSkin, bw[v]).MultiplyPoint3x4(V[v]);
        foreach (var list in cut) foreach (int v in list) { partWorld += Skin(boneSkin, W[v]).MultiplyPoint3x4(V[v]); partCount++; }
        if (partCount > 0) partWorld /= partCount;
        if (Shape(bodyRim, world, weld, partWorld, depth, out var bodyEdges, out Vector3 middleW, out Vector3 normalW))
        {
            var rimWeights = bw.ToArray();
            BoneWeight middleWeight = Blend(bodyEdges, rimWeights);
            Matrix4x4 toMesh = Skin(boneSkin, middleWeight).inverse;
            Fan(bodyEdges, world, middleW, normalW,
                v => V[v], (v, n) => Skin(boneSkin, rimWeights[v]).inverse.MultiplyVector(n).normalized,
                toMesh.MultiplyPoint3x4(middleW), toMesh.MultiplyVector(normalW).normalized,
                V, N, T, UV, capTris, copiedFrom, out int centreIndex);
            // the cap moves exactly like the rim it closes: each copy takes its vertex's weights, the middle their blend
            foreach (int v in copiedFrom) bw.Add(rimWeights[v]);
            bw.Add(middleWeight);
            result.capCentre = centreIndex;
            result.hasWound = true;
        }

        var body = new Mesh();
        if (V.Count > 65535) body.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        body.SetVertices(V);
        body.SetNormals(N);
        if (T != null) body.SetTangents(T);
        if (hasUV) body.SetUVs(0, UV);
        body.boneWeights = bw.ToArray();
        body.bindposes = mesh.bindposes;
        body.subMeshCount = Mathf.Max(subCount, capSub + 1);
        for (int sm = 0; sm < body.subMeshCount; sm++)
        {
            var list = sm < subCount ? keep[sm] : new List<int>();
            if (sm == capSub) list.AddRange(capTris);
            body.SetTriangles(list, sm);
        }
        body.RecalculateBounds();
        result.body = body;
        if (woundSub >= 0) result.bodyMaterials = materials;
        else
        {
            result.bodyMaterials = new Material[subCount + 1];
            for (int i = 0; i < subCount; i++) result.bodyMaterials[i] = i < materials.Length ? materials[i] : null;
            result.bodyMaterials[subCount] = wound;
        }

        if (P == null) return result;

        // ── the piece, in its pose: only the vertices its triangles use, its end capped too ──
        var remap = new Dictionary<int, int>();
        var pv = new List<Vector3>(); var pn = new List<Vector3>(); var puv = new List<Vector2>();
        var pieceSubs = new List<int>[body.subMeshCount];
        for (int sm = 0; sm < pieceSubs.Length; sm++)
        {
            pieceSubs[sm] = new List<int>();
            if (sm >= subCount) continue;
            foreach (int v in cut[sm])
            {
                if (!remap.TryGetValue(v, out int nv))
                {
                    nv = pv.Count;
                    remap[v] = nv;
                    pv.Add(P[v]);
                    pn.Add(PN[v].sqrMagnitude > 0f ? PN[v] : Vector3.up);
                    puv.Add(UV[v]);
                }
                pieceSubs[sm].Add(nv);
            }
        }
        var pieceRim = new List<(int a, int b)>();
        foreach (var (a, b) in partRim) if (remap.TryGetValue(a, out int ra) && remap.TryGetValue(b, out int rb)) pieceRim.Add((ra, rb));
        // one piece index per spot (the weld above, in the piece's indices)
        var firstAtSpot = new Dictionary<int, int>();
        foreach (var kv in remap)
            if (!firstAtSpot.TryGetValue(weld[kv.Key], out int f) || kv.Value < f) firstAtSpot[weld[kv.Key]] = kv.Value;
        var pieceWeld = new int[pv.Count];
        foreach (var kv in remap) pieceWeld[kv.Value] = firstAtSpot[weld[kv.Key]];
        // the piece is in its pose already (real space): shaped and built right there
        var pieceCap = new List<int>();
        var posedArray = P.ToArray();
        var piecePos = pv.ToArray();
        if (Shape(pieceRim, piecePos, pieceWeld, Middle(posedArray, keep), depth, out var pieceEdges, out Vector3 pm, out Vector3 pnrm))
            Fan(pieceEdges, piecePos, pm, pnrm, v => piecePos[v], (v, n) => n, pm, pnrm, pv, pn, null, puv, pieceCap, new List<int>(), out _);
        pieceSubs[capSub].AddRange(pieceCap);

        var piece = new Mesh();
        if (pv.Count > 65535) piece.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        piece.SetVertices(pv);
        piece.SetNormals(pn);
        piece.SetUVs(0, puv);
        piece.subMeshCount = pieceSubs.Length;
        for (int sm = 0; sm < pieceSubs.Length; sm++) piece.SetTriangles(pieceSubs[sm], sm);
        piece.RecalculateBounds();
        result.piece = piece;

        // where the wound is in the pose, for the blood: the body's opening, facing into the part
        if (PlaneOf(bodyRim, posedArray, weld, Middle(posedArray, cut), out Vector3 wc, out Vector3 wn))
        {
            result.posedCentre = wc;
            result.posedNormal = wn;
        }
        return result;
    }

    static int Count(List<int>[] lists) { int c = 0; foreach (var l in lists) c += l.Count; return c; }

    static HashSet<long> EdgeSet(List<int>[] tris, IList<int> weld)
    {
        var set = new HashSet<long>();
        foreach (var list in tris)
            for (int i = 0; i + 2 < list.Count; i += 3)
            {
                set.Add(EdgeKey(weld[list[i]], weld[list[i + 1]]));
                set.Add(EdgeKey(weld[list[i + 1]], weld[list[i + 2]]));
                set.Add(EdgeKey(weld[list[i + 2]], weld[list[i]]));
            }
        return set;
    }

    /// <summary>
    /// The plane an opening lies in: through its middle (each spot once), facing (area vector, which holds
    /// for slanted / uneven rims) towards <paramref name="towards"/>. False if there's no usable opening.
    /// </summary>
    static bool PlaneOf(List<(int a, int b)> rim, IList<Vector3> pos, IList<int> weld, Vector3 towards, out Vector3 centre, out Vector3 normal)
    {
        centre = Vector3.zero; normal = Vector3.up;
        if (rim.Count < 3) return false;
        var seen = new HashSet<int>();
        int count = 0;
        foreach (var (a, b) in rim)
        {
            if (seen.Add(weld[a])) { centre += pos[a]; count++; }
            if (seen.Add(weld[b])) { centre += pos[b]; count++; }
        }
        centre /= count;
        Vector3 area = Vector3.zero;
        foreach (var (a, b) in rim) area += Vector3.Cross(pos[a] - centre, pos[b] - centre);
        if (area.sqrMagnitude < 1e-24f) return false;
        normal = area.normalized;
        if (Vector3.Dot(normal, towards - centre) < 0f) normal = -normal;
        return true;
    }

    /// <summary>A vertex between two others: their weights blended by <paramref name="s"/> (4 strongest bones).</summary>
    static BoneWeight Mix(BoneWeight x, BoneWeight y, float s)
    {
        var sum = new Dictionary<int, float>();
        void Add(int bone, float w) { if (w > 0f) sum[bone] = (sum.TryGetValue(bone, out float o) ? o : 0f) + w; }
        Add(x.boneIndex0, x.weight0 * (1f - s)); Add(x.boneIndex1, x.weight1 * (1f - s)); Add(x.boneIndex2, x.weight2 * (1f - s)); Add(x.boneIndex3, x.weight3 * (1f - s));
        Add(y.boneIndex0, y.weight0 * s); Add(y.boneIndex1, y.weight1 * s); Add(y.boneIndex2, y.weight2 * s); Add(y.boneIndex3, y.weight3 * s);
        return Strongest(sum, x);
    }

    static BoneWeight Strongest(Dictionary<int, float> sum, BoneWeight fallback)
    {
        var top = new List<KeyValuePair<int, float>>(sum);
        top.Sort((p, r) => r.Value.CompareTo(p.Value));
        float total = 0f;
        for (int i = 0; i < top.Count && i < 4; i++) total += top[i].Value;
        if (total <= 0f) return fallback;
        var w = new BoneWeight();
        if (top.Count > 0) { w.boneIndex0 = top[0].Key; w.weight0 = top[0].Value / total; }
        if (top.Count > 1) { w.boneIndex1 = top[1].Key; w.weight1 = top[1].Value / total; }
        if (top.Count > 2) { w.boneIndex2 = top[2].Key; w.weight2 = top[2].Value / total; }
        if (top.Count > 3) { w.boneIndex3 = top[3].Key; w.weight3 = top[3].Value / total; }
        return w;
    }

    /// <summary>
    /// An opening's shape, in the space of <paramref name="geo"/>: its rim edges (stray ones far from the opening,
    /// a second unrelated seam, dropped), the sunken middle of the cap and the way it faces (the side that was
    /// taken away, towards <paramref name="towards"/>). False if there's no usable opening.
    /// </summary>
    static bool Shape(List<(int a, int b)> rim, IList<Vector3> geo, IList<int> weld, Vector3 towards, float depth,
                      out List<(int a, int b)> edges, out Vector3 middle, out Vector3 normal)
    {
        edges = null; middle = Vector3.zero;
        if (!PlaneOf(rim, geo, weld, towards, out Vector3 centre, out normal)) return false;
        var dist = new List<float>(rim.Count);
        foreach (var (a, b) in rim) dist.Add(((geo[a] + geo[b]) * 0.5f - centre).magnitude);
        var sorted = new List<float>(dist); sorted.Sort();
        float median = sorted[sorted.Count / 2];
        edges = new List<(int a, int b)>();
        for (int i = 0; i < rim.Count; i++) if (dist[i] <= median * 3f + 1e-9f) edges.Add(rim[i]);
        if (edges.Count < 3) return false;
        middle = centre - normal * depth * median;   // sunk into the side that stays
        return true;
    }

    /// <summary>
    /// Builds a cap: copies of the rim vertices (own normals and material; <paramref name="copiedFrom"/> lists which
    /// vertex each copy is, in order), then the middle vertex (<paramref name="centreIndex"/>), and a fan between
    /// them wound to face <paramref name="normal"/>. Shaped in the space of <paramref name="geo"/>; positions and
    /// normals go into the output through <paramref name="posOf"/> / <paramref name="normalOf"/> and the given
    /// middle (the body's mesh space differs from the space it's shaped in).
    /// </summary>
    static void Fan(List<(int a, int b)> edges, IList<Vector3> geo, Vector3 middle, Vector3 normal,
                    Func<int, Vector3> posOf, Func<int, Vector3, Vector3> normalOf, Vector3 middleOut, Vector3 middleNormalOut,
                    List<Vector3> outV, List<Vector3> outN, List<Vector4> outT, List<Vector2> outUV, List<int> outTris,
                    List<int> copiedFrom, out int centreIndex)
    {
        var copy = new Dictionary<int, int>();
        int Copy(int v)
        {
            if (copy.TryGetValue(v, out int c)) return c;
            c = outV.Count;
            outV.Add(posOf(v));
            Vector3 inward = middle - geo[v];
            inward -= normal * Vector3.Dot(inward, normal);
            outN.Add(normalOf(v, (normal + inward.normalized * 0.2f).normalized));   // tilted in a little: a shallow bowl
            outT?.Add(new Vector4(1f, 0f, 0f, 1f));
            outUV?.Add(Vector2.zero);
            copiedFrom.Add(v);
            copy[v] = c;
            return c;
        }

        // wind the fan so its front faces the removed side (checked where it's shaped, not assumed)
        Vector3 facing = Vector3.zero;
        foreach (var (a, b) in edges) facing += Vector3.Cross(geo[a] - geo[b], middle - geo[b]);
        bool reverse = Vector3.Dot(facing, normal) < 0f;

        var fan = new List<int>(edges.Count * 2);
        foreach (var (a, b) in edges) { int ca = Copy(a), cb = Copy(b); fan.Add(cb); fan.Add(ca); }
        centreIndex = outV.Count;
        outV.Add(middleOut);
        outN.Add(middleNormalOut);
        outT?.Add(new Vector4(1f, 0f, 0f, 1f));
        outUV?.Add(Vector2.zero);
        for (int i = 0; i < fan.Count; i += 2)
        {
            if (reverse) { outTris.Add(fan[i + 1]); outTris.Add(fan[i]); }
            else { outTris.Add(fan[i]); outTris.Add(fan[i + 1]); }
            outTris.Add(centreIndex);
        }
    }

    /// <summary>Per bone: mesh space to world in the current pose (bone matrix times bind pose).</summary>
    static Matrix4x4[] BoneSkin(SkinnedMeshRenderer smr, Mesh mesh)
    {
        Transform[] bones = smr.bones;
        Matrix4x4[] bind = mesh.bindposes;
        var m = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
            m[i] = bones[i] != null && i < bind.Length ? bones[i].localToWorldMatrix * bind[i] : smr.transform.localToWorldMatrix;
        return m;
    }

    /// <summary>A vertex's skin matrix, as the skinning blends it: the weighted sum of its bones' matrices.</summary>
    static Matrix4x4 Skin(Matrix4x4[] boneSkin, BoneWeight w)
    {
        var m = new Matrix4x4();
        void Add(int bone, float weight)
        {
            if (weight <= 0f || bone < 0 || bone >= boneSkin.Length) return;
            Matrix4x4 b = boneSkin[bone];
            for (int i = 0; i < 16; i++) m[i] += b[i] * weight;
        }
        Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
        return m;
    }

    /// <summary>Edges of <paramref name="tris"/> (in their winding) that the other side also has: the opening's rim.</summary>
    static List<(int a, int b)> Rim(List<int>[] tris, HashSet<long> otherSide, IList<int> weld)
    {
        var rim = new List<(int, int)>();
        foreach (var list in tris)
            for (int i = 0; i + 2 < list.Count; i += 3)
            {
                int a = list[i], b = list[i + 1], c = list[i + 2];
                if (otherSide.Contains(EdgeKey(weld[a], weld[b]))) rim.Add((a, b));
                if (otherSide.Contains(EdgeKey(weld[b], weld[c]))) rim.Add((b, c));
                if (otherSide.Contains(EdgeKey(weld[c], weld[a]))) rim.Add((c, a));
            }
        return rim;
    }

    static Vector3 Middle(IList<Vector3> pos, List<int>[] tris)
    {
        Vector3 sum = Vector3.zero; int count = 0;
        foreach (var list in tris) foreach (int v in list) { sum += pos[v]; count++; }
        return count > 0 ? sum / count : Vector3.zero;
    }

    /// <summary>The cap's middle vertex follows what the rim follows: the rim's weights, averaged (4 strongest bones).</summary>
    static BoneWeight Blend(List<(int a, int b)> rim, BoneWeight[] weights)
    {
        var sum = new Dictionary<int, float>();
        void Add(BoneWeight w)
        {
            if (w.weight0 > 0f) sum[w.boneIndex0] = (sum.TryGetValue(w.boneIndex0, out float s0) ? s0 : 0f) + w.weight0;
            if (w.weight1 > 0f) sum[w.boneIndex1] = (sum.TryGetValue(w.boneIndex1, out float s1) ? s1 : 0f) + w.weight1;
            if (w.weight2 > 0f) sum[w.boneIndex2] = (sum.TryGetValue(w.boneIndex2, out float s2) ? s2 : 0f) + w.weight2;
            if (w.weight3 > 0f) sum[w.boneIndex3] = (sum.TryGetValue(w.boneIndex3, out float s3) ? s3 : 0f) + w.weight3;
        }
        foreach (var (a, b) in rim) { Add(weights[a]); Add(weights[b]); }
        return Strongest(sum, weights[rim[0].a]);
    }

    /// <summary><paramref name="w"/> without the bones in <paramref name="drop"/>, the rest scaled back up to 1 (unchanged if nothing would be left).</summary>
    static BoneWeight WithoutBones(BoneWeight w, HashSet<int> drop)
    {
        float w0 = drop.Contains(w.boneIndex0) ? 0f : w.weight0, w1 = drop.Contains(w.boneIndex1) ? 0f : w.weight1;
        float w2 = drop.Contains(w.boneIndex2) ? 0f : w.weight2, w3 = drop.Contains(w.boneIndex3) ? 0f : w.weight3;
        float total = w0 + w1 + w2 + w3;
        if (total <= 0f) return w;
        w.weight0 = w0 / total; w.weight1 = w1 / total; w.weight2 = w2 / total; w.weight3 = w3 / total;
        return w;
    }

    static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    /// <summary>How much of each vertex follows <paramref name="bones"/> (each with everything under it). Null if none do.</summary>
    public static float[] PartShare(SkinnedMeshRenderer smr, Mesh mesh, IList<Transform> bones, out HashSet<int> partBones)
    {
        partBones = new HashSet<int>();
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
