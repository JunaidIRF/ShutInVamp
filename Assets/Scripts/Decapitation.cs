using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Takes the head off a skinned enemy model. The skinned mesh is split by bone weights (everything
/// weighted mostly to the head bone or its children), so it works for any rig with a head bone: the body
/// keeps skinning without the head, the head becomes a physics object, the neck gets a stump and
/// spurts blood. Needs the model's mesh to be Read/Write enabled. Call after the ragdoll is on.
/// </summary>
public class Decapitation : MonoBehaviour
{
    [Header("Bones")]
    [BonePicker] public string headBone = "mixamorig:Head";
    [BonePicker] public string neckBone = "mixamorig:Neck";

    [Header("Head")]
    public float headSpeed = 4f;            // m/s along the killing blow
    public float headLift = 3f;             // m/s upward
    public float headSpin = 8f;             // rad/s, random axis
    public float headMass = 4f;
    public float headLifetime = 20f;

    [Header("Neck")]
    public Color stumpColor = new Color(0.35f, 0.02f, 0.03f);
    public GameObject bloodFx;              // one splash at the cut; falls back to the Shooter's blood effect
    public GameObject neckBloodFx;          // the jet out of the neck, replayed for every pulse
    public int spurts = 5;
    public float spurtInterval = 0.32f;
    public float spurtFalloff = 0.82f;      // each pulse this much weaker than the last
    public int headSpurts = 2;              // a couple of weaker jets out of the flying head
    public float headSpurtStrength = 0.6f;
    public float bloodLifetime = 9f;

    public bool IsDecapitated { get; private set; }

    void OnEnable() => IsDecapitated = false;

    public void Decapitate(Vector3 direction)
    {
        if (IsDecapitated) return;
        Transform head = FindDeep(transform, headBone), neck = FindDeep(transform, neckBone);
        if (head == null) return;
        if (bloodFx == null) { var s = GetComponentInParent<Shooter>(); if (s != null) bloodFx = s.bloodFx; }

        // where it comes off: just below the head, facing along the neck
        Transform stumpParent = neck != null ? neck : head.parent;
        Vector3 cutPoint = Vector3.Lerp(stumpParent.position, head.position, 0.85f);
        Vector3 neckDir = head.position - stumpParent.position;
        neckDir = neckDir.sqrMagnitude > 0.000001f ? neckDir.normalized : Vector3.up;
        float stumpSize = Mathf.Max(0.04f, Vector3.Distance(stumpParent.position, head.position) * 0.9f);

        var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var done = new Dictionary<Mesh, Mesh>();   // original -> body (HitFlash overlays share the mesh)
        Transform severed = null;
        bool any = false;

        foreach (var smr in renderers)
        {
            Mesh original = smr.sharedMesh;
            if (original == null) continue;
            if (done.TryGetValue(original, out Mesh body)) { smr.sharedMesh = body; continue; }
            if (!original.isReadable) { Debug.LogWarning($"Decapitation: '{original.name}' isn't Read/Write enabled"); continue; }
            if (!smr.enabled || smr.name.Contains("HitFlash")) continue;   // overlays get the body mesh above / below

            // how much of each vertex follows the head (head bone + everything under it)
            var headBones = new HashSet<int>();
            for (int b = 0; b < smr.bones.Length; b++)
                if (smr.bones[b] != null && (smr.bones[b] == head || smr.bones[b].IsChildOf(head))) headBones.Add(b);
            if (headBones.Count == 0) continue;
            var weights = original.boneWeights;
            var share = new float[weights.Length];
            for (int v = 0; v < weights.Length; v++)
            {
                var w = weights[v];
                float sum = 0f;
                if (headBones.Contains(w.boneIndex0)) sum += w.weight0;
                if (headBones.Contains(w.boneIndex1)) sum += w.weight1;
                if (headBones.Contains(w.boneIndex2)) sum += w.weight2;
                if (headBones.Contains(w.boneIndex3)) sum += w.weight3;
                share[v] = sum;
            }

            // pose right now, for the severed head. Baked without the renderer's scale, the vertices are already
            // world-sized in the renderer's position/rotation (the head object gets scale 1)
            var baked = new Mesh();
            smr.BakeMesh(baked, false);
            Vector3[] bakedV = baked.vertices, bakedN = baked.normals;
            Vector2[] uv = original.uv;
            Destroy(baked);

            body = Instantiate(original);
            body.name = original.name + " (no head)";
            // the head keeps only the vertices its triangles use (so its bounds / collider fit the head)
            var remap = new Dictionary<int, int>();
            var hv = new List<Vector3>(); var hn = new List<Vector3>(); var huv = new List<Vector2>();
            var headSubs = new List<List<int>>();
            int headTris = 0;
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
                            nv = hv.Count;
                            remap[v] = nv;
                            hv.Add(bakedV[v]);
                            hn.Add(bakedN.Length > v ? bakedN[v] : Vector3.up);
                            huv.Add(uv.Length > v ? uv[v] : Vector2.zero);
                        }
                        cut.Add(nv);
                    }
                }
                body.SetTriangles(keep, sm);
                headSubs.Add(cut);
                headTris += cut.Count / 3;
            }
            if (headTris == 0) { Destroy(body); continue; }
            var headMesh = new Mesh { name = original.name + " (head)" };
            headMesh.SetVertices(hv);
            headMesh.SetNormals(hn);
            headMesh.SetUVs(0, huv);
            headMesh.subMeshCount = headSubs.Count;
            for (int sm = 0; sm < headSubs.Count; sm++) headMesh.SetTriangles(headSubs[sm], sm);
            headMesh.RecalculateBounds();
            done[original] = body;
            smr.sharedMesh = body;
            any = true;

            severed = SpawnHead(headMesh, smr, direction, cutPoint, neckDir, stumpSize);
        }
        if (!any) return;
        // hidden overlay copies that were skipped above
        foreach (var smr in renderers)
            if (smr.sharedMesh != null && done.TryGetValue(smr.sharedMesh, out Mesh b)) smr.sharedMesh = b;

        IsDecapitated = true;

        // the body's head bone: no collider bumping where nothing is drawn, nothing left to see
        foreach (var c in head.GetComponents<Collider>()) c.enabled = false;
        // its rigidbody stays dynamic: it's jointed to the neck, so a kinematic one would hang the body in the air
        var rb = head.GetComponent<Rigidbody>();
        if (rb != null) { rb.detectCollisions = false; rb.mass = Mathf.Min(rb.mass, 0.5f); }
        head.localScale = Vector3.zero;

        // neck stump, a splash at the cut, then the neck pumps blood out in pulses
        Stump(stumpParent, cutPoint, neckDir, stumpSize);
        if (bloodFx != null) Destroy(Instantiate(bloodFx, cutPoint, Quaternion.LookRotation(neckDir)), bloodLifetime);
        StartCoroutine(Spurt(stumpParent, cutPoint, neckDir, spurts, 1f));
        if (severed != null) StartCoroutine(Spurt(severed, cutPoint, -neckDir, headSpurts, headSpurtStrength));
    }

    Transform SpawnHead(Mesh mesh, SkinnedMeshRenderer source, Vector3 direction, Vector3 cutPoint, Vector3 neckDir, float stumpSize)
    {
        // centre the mesh on its pivot, so the rigidbody spins around the head itself
        Vector3 centre = mesh.bounds.center;
        var verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++) verts[i] -= centre;
        mesh.vertices = verts;
        mesh.RecalculateBounds();

        var go = new GameObject("Severed Head");
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
        // don't get stuck in (or launched by) the body it came off
        foreach (var c in GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(col, c);

        var rb = go.AddComponent<Rigidbody>();
        rb.mass = headMass;
        rb.linearDamping = 0.2f;
        rb.angularDamping = 1.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        rb.linearVelocity = dir * headSpeed + Vector3.up * headLift;
        rb.angularVelocity = Random.onUnitSphere * headSpin;

        // a red cap where it came off
        Stump(go.transform, cutPoint, -neckDir, stumpSize);

        Destroy(go, headLifetime);
        return go.transform;
    }

    /// <summary>A flattened dark-red cap at <paramref name="worldPos"/>, its flat side facing <paramref name="normal"/>.</summary>
    void Stump(Transform parent, Vector3 worldPos, Vector3 normal, float size)
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s.name = "Neck Stump";
        DestroyImmediate(s.GetComponent<Collider>());   // a deferred destroy would join the head's rigidbody for a step
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
    /// The jet rides on <paramref name="parent"/> (the ragdoll's neck, or the flying head), so it keeps
    /// pointing out of the wound as that tumbles; its drops fly free in world space.
    /// </summary>
    IEnumerator Spurt(Transform parent, Vector3 at, Vector3 dir, int pulses, float strength)
    {
        if (neckBloodFx == null || pulses <= 0) yield break;
        var fx = Instantiate(neckBloodFx, at, Quaternion.LookRotation(dir));
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
