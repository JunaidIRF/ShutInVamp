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
    public GameObject bloodFx;              // falls back to the Shooter's blood effect
    public int spurts = 4;
    public float spurtInterval = 0.35f;
    public float bloodLifetime = 9f;

    public bool IsDecapitated { get; private set; }

    void OnEnable() => IsDecapitated = false;

    public void Decapitate(Vector3 direction)
    {
        if (IsDecapitated) return;
        Transform head = FindDeep(transform, headBone), neck = FindDeep(transform, neckBone);
        if (head == null) return;
        if (bloodFx == null) { var s = GetComponentInParent<Shooter>(); if (s != null) bloodFx = s.bloodFx; }

        var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var done = new Dictionary<Mesh, Mesh>();   // original -> body (HitFlash overlays share the mesh)
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

            SpawnHead(headMesh, smr, direction);
        }
        if (!any) return;
        // hidden overlay copies that were skipped above
        foreach (var smr in renderers)
            if (smr.sharedMesh != null && done.TryGetValue(smr.sharedMesh, out Mesh b)) smr.sharedMesh = b;

        IsDecapitated = true;

        // the body's head bone: no collider bumping where nothing is drawn, nothing left to see
        foreach (var c in head.GetComponents<Collider>()) c.enabled = false;
        var rb = head.GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; rb.detectCollisions = false; }
        head.localScale = Vector3.zero;

        // neck stump + spurting blood
        Transform stumpParent = neck != null ? neck : head.parent;
        Vector3 cutPoint = Vector3.Lerp(stumpParent.position, head.position, 0.85f);
        Stump(stumpParent, cutPoint, Mathf.Max(0.04f, Vector3.Distance(stumpParent.position, head.position) * 0.9f));
        StartCoroutine(Spurt(stumpParent, head, cutPoint));
    }

    void SpawnHead(Mesh mesh, SkinnedMeshRenderer source, Vector3 direction)
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
        go.transform.SetPositionAndRotation(source.transform.TransformPoint(centre), source.transform.rotation);
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

        // a red cap where it came off (bottom of the head mesh)
        Vector3 bottom = new Vector3(mesh.bounds.center.x, mesh.bounds.min.y, mesh.bounds.center.z);
        Stump(go.transform, go.transform.TransformPoint(bottom), mesh.bounds.size.x * go.transform.lossyScale.x * 0.5f);

        Destroy(go, headLifetime);
    }

    void Stump(Transform parent, Vector3 worldPos, float size)
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s.name = "Neck Stump";
        DestroyImmediate(s.GetComponent<Collider>());   // a deferred destroy would join the head's rigidbody for a step
        s.layer = parent.gameObject.layer;
        s.transform.SetParent(parent, false);
        s.transform.position = worldPos;
        s.transform.localRotation = Quaternion.identity;
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

    IEnumerator Spurt(Transform neck, Transform head, Vector3 firstPoint)
    {
        if (bloodFx == null) yield break;
        for (int i = 0; i <= spurts; i++)
        {
            // out of the neck, following the body as it falls
            Vector3 up = (head.position - neck.position);
            Vector3 dir = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
            Vector3 at = i == 0 ? firstPoint : Vector3.Lerp(neck.position, head.position, 0.85f);
            var fx = Instantiate(bloodFx, at, Quaternion.LookRotation(dir));
            if (i == 0) fx.transform.localScale *= 1.3f;   // the big first burst
            Destroy(fx, bloodLifetime);
            yield return new WaitForSeconds(spurtInterval);
        }
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
