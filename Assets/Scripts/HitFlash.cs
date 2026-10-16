using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Red overlay flash over every renderer of this object, whatever colours/materials the model uses.
/// The model's own materials are never touched: each MeshRenderer / SkinnedMeshRenderer gets a hidden
/// companion renderer that shares its mesh (and bones, for rigged models) and draws only the overlay,
/// on every sub-mesh. Companions are only enabled while a flash is fading.
/// Call <see cref="Build"/> again if the model's renderers are swapped at runtime.
/// </summary>
public class HitFlash : MonoBehaviour
{
    const string OverlayName = "HitFlash Overlay";

    public Material flashMaterial;          // uses Hidden/ShutInVamp/HitFlash
    public Color flashColor = new Color(1f, 0.1f, 0.1f, 0.75f);  // alpha = strength at the moment of the hit
    public float flashTime = 0.3f;

    struct Overlay
    {
        public Renderer source;
        public Renderer overlay;
        public SkinnedMeshRenderer skinnedSource;   // null for plain meshes
        public SkinnedMeshRenderer skinnedOverlay;
    }

    static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");

    readonly List<Overlay> overlays = new List<Overlay>();
    Material instance;
    float flashStartTime = float.NegativeInfinity;
    bool shown;

    void Awake() => Build();

    void OnDisable()
    {
        flashStartTime = float.NegativeInfinity;
        SetShown(false);
    }

    void OnDestroy()
    {
        if (instance != null) Destroy(instance);
    }

    public void Flash()
    {
        flashStartTime = Time.time;
    }

    void LateUpdate()
    {
        float k = 1f - (Time.time - flashStartTime) / Mathf.Max(flashTime, 0.0001f);
        if (k <= 0f || instance == null)
        {
            if (shown) SetShown(false);
            return;
        }

        Color c = flashColor;
        c.a *= k * k;
        instance.SetColor(FlashColorId, c);
        SetShown(true);

        // follow blend shapes the model's Animator set this frame
        foreach (var o in overlays)
        {
            if (o.overlay == null || o.source == null) continue;

            if (o.skinnedSource != null)
            {
                int count = o.skinnedSource.sharedMesh != null ? o.skinnedSource.sharedMesh.blendShapeCount : 0;
                for (int i = 0; i < count; i++)
                    o.skinnedOverlay.SetBlendShapeWeight(i, o.skinnedSource.GetBlendShapeWeight(i));
            }
        }
    }

    void SetShown(bool show)
    {
        shown = show;
        foreach (var o in overlays)
            if (o.overlay != null) o.overlay.enabled = show && o.source != null && o.source.enabled;
    }

    // ══════════════════════════════════════════════════════════════════
    //  BUILD
    // ══════════════════════════════════════════════════════════════════
    public void Build()
    {
        Clear();
        if (flashMaterial == null) return;

        if (instance == null)
            instance = new Material(flashMaterial) { name = flashMaterial.name + " (HitFlash)" };

        var made = new Dictionary<Renderer, Renderer>();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (IsOverlay(r)) continue;

            Overlay o = r switch
            {
                SkinnedMeshRenderer s => MakeSkinned(s),
                MeshRenderer m => MakeMesh(m),
                _ => default,   // particles, trails, sprites, lines: not flashed
            };
            if (o.overlay == null) continue;

            overlays.Add(o);
            made[r] = o.overlay;
        }

        AddToLodGroups(made);
        SetShown(false);
    }

    void Clear()
    {
        foreach (var o in overlays)
            if (o.overlay != null) Destroy(o.overlay.gameObject);
        overlays.Clear();
    }

    Overlay MakeMesh(MeshRenderer source)
    {
        var filter = source.GetComponent<MeshFilter>();
        // static-batched meshes are merged and can't be redrawn on their own
        if (filter == null || filter.sharedMesh == null || source.isPartOfStaticBatch) return default;

        var go = NewOverlayObject(source);
        go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
        var r = go.AddComponent<MeshRenderer>();
        Setup(r, source, filter.sharedMesh.subMeshCount);
        return new Overlay { source = source, overlay = r };
    }

    Overlay MakeSkinned(SkinnedMeshRenderer source)
    {
        if (source.sharedMesh == null) return default;

        // Shares the source's bones, so it's skinned (and animated) exactly like the model. Where the
        // companion object itself sits doesn't matter for a skinned mesh — the bones place it.
        var go = NewOverlayObject(source);
        var r = go.AddComponent<SkinnedMeshRenderer>();
        r.sharedMesh = source.sharedMesh;
        r.bones = source.bones;
        r.rootBone = source.rootBone;
        r.localBounds = source.localBounds;
        r.quality = source.quality;
        r.updateWhenOffscreen = source.updateWhenOffscreen;
        r.skinnedMotionVectors = false;
        Setup(r, source, source.sharedMesh.subMeshCount);
        return new Overlay { source = source, overlay = r, skinnedSource = source, skinnedOverlay = r };
    }

    GameObject NewOverlayObject(Renderer source)
    {
        var go = new GameObject(OverlayName);
        go.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;   // destroyed with the model as usual
        go.layer = source.gameObject.layer;                // same cameras see it
        go.transform.SetParent(source.transform, false);   // follows the model (moves, bounces, scales)
        return go;
    }

    void Setup(Renderer r, Renderer source, int subMeshCount)
    {
        // one overlay per sub-mesh so every part of a multi-material model flashes
        r.sharedMaterials = Enumerable.Repeat(instance, Mathf.Max(1, subMeshCount)).ToArray();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;   // no motion blur smearing
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.renderingLayerMask = source.renderingLayerMask;
        r.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
        r.enabled = false;
    }

    /// <summary>A LOD group only shows its current level, so put each overlay in the same level as its model.</summary>
    void AddToLodGroups(Dictionary<Renderer, Renderer> made)
    {
        foreach (var group in GetComponentsInChildren<LODGroup>(true))
        {
            LOD[] lods = group.GetLODs();
            for (int i = 0; i < lods.Length; i++)
            {
                var kept = lods[i].renderers.Where(r => r != null && !IsOverlay(r)).ToList();
                var added = kept.Where(made.ContainsKey).Select(r => made[r]).ToList();
                lods[i].renderers = kept.Concat(added).ToArray();
            }
            group.SetLODs(lods);
        }
    }

    static bool IsOverlay(Renderer r) => r.gameObject.name == OverlayName;
}
