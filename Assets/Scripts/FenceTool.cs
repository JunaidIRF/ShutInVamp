using UnityEngine;
using System.Collections.Generic;

public class FenceTool : MonoBehaviour
{
    public enum PivotType { PivotAtPost, PivotInCenter }

    [Header("Fence Settings")]
    public GameObject fencePrefab;

    // We hide this from the default inspector so we can draw it custom with a button in the Editor script
    [HideInInspector]
    public float spacing = 1.65f;

    [Tooltip("Where is the pivot on your model?")]
    public PivotType pivotMode = PivotType.PivotInCenter;

    [Header("Path Settings")]
    public bool closeLoop = false;
    public bool useSmoothCurves = true;
    [Range(1, 50)] public int curveResolution = 20;
    public List<Transform> points = new List<Transform>();

    [Header("Snapping")]
    public bool snapToTerrain = true;
    public LayerMask terrainLayer = ~0;
    public float rayHeight = 500f;

    [Header("Adjustments")]
    public float heightOffset = 0.0f;
    public Vector3 rotationOffset;
    public bool alignToSlope = true;

    // --- AUTO SPACING LOGIC ---
    public void AutoCalculateSpacing()
    {
        if (fencePrefab == null)
        {
            Debug.LogError("Assign a Fence Prefab first!");
            return;
        }

        // Try to find a mesh to measure
        MeshFilter meshFilter = fencePrefab.GetComponentInChildren<MeshFilter>();
        if (meshFilter == null)
        {
            // Try SkinnedMeshRenderer if it's an animated object
            SkinnedMeshRenderer skinned = fencePrefab.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skinned != null)
            {
                CalculateFromBounds(skinned.sharedMesh.bounds);
                return;
            }
            Debug.LogError("Could not find a MeshFilter on the prefab to measure.");
            return;
        }

        CalculateFromBounds(meshFilter.sharedMesh.bounds);
    }

    void CalculateFromBounds(Bounds bounds)
    {
        // Fences are usually longest along X or Z. We ignore Y (height).
        // We pick the larger of X or Z to be safe.
        float length = Mathf.Max(bounds.size.x, bounds.size.z);

        // Apply prefab scale if it's not 1
        float scale = Mathf.Max(fencePrefab.transform.localScale.x, fencePrefab.transform.localScale.z);

        spacing = length * scale;

        // Slight buffer? usually raw mesh size is best, but sometimes -0.01 helps overlap
        Debug.Log($"Auto-Spacing Calculated: {spacing} (Mesh Size: {length} * Scale: {scale})");
    }

    // --- GENERATION ---
    public void GenerateFences()
    {
        if (fencePrefab == null || points.Count < 2) return;

        // Cleanup
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (!points.Contains(child)) DestroyImmediate(child.gameObject);
        }

        List<Vector3> highResPath = GenerateHighResPath();
        if (highResPath.Count < 2) return;

        Vector3 walkerPos = highResPath[0];
        List<Vector3> nodes = new List<Vector3>();
        nodes.Add(walkerPos);

        int lastIndex = 0;
        while (true)
        {
            bool foundNext = false;
            for (int i = lastIndex; i < highResPath.Count; i++)
            {
                if (Vector3.Distance(walkerPos, highResPath[i]) >= spacing)
                {
                    walkerPos = highResPath[i];
                    nodes.Add(walkerPos);
                    lastIndex = i;
                    foundNext = true;
                    break;
                }
            }

            if (!foundNext)
            {
                if (closeLoop && nodes.Count > 2)
                {
                    if (Vector3.Distance(walkerPos, nodes[0]) <= spacing * 1.5f) { }
                }
                break;
            }
        }

        for (int i = 0; i < nodes.Count - 1; i++)
        {
            Vector3 nodeA = nodes[i];
            Vector3 nodeB = nodes[i + 1];
            if (i == nodes.Count - 1 && closeLoop) nodeB = nodes[0];

            Vector3 direction = (nodeB - nodeA).normalized;
            Vector3 spawnPos = nodeA;

            if (pivotMode == PivotType.PivotInCenter) spawnPos = Vector3.Lerp(nodeA, nodeB, 0.5f);

            PlaceFence(spawnPos, direction);
        }
    }

    void PlaceFence(Vector3 position, Vector3 direction)
    {
        Vector3 finalPos = position;
        Vector3 normal = Vector3.up;

        if (snapToTerrain)
        {
            Vector3 rayOrigin = new Vector3(position.x, rayHeight, position.z);
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 2000f, terrainLayer))
            {
                finalPos = hit.point;
                normal = hit.normal;
            }
        }
        finalPos += Vector3.up * heightOffset;

        GameObject newFence = Instantiate(fencePrefab, transform);
        newFence.transform.position = finalPos;

        Quaternion baseRot;
        if (alignToSlope && snapToTerrain)
        {
            Vector3 slopeForward = Vector3.ProjectOnPlane(direction, normal).normalized;
            if (slopeForward == Vector3.zero) slopeForward = direction;
            baseRot = Quaternion.LookRotation(slopeForward);
        }
        else
        {
            baseRot = Quaternion.LookRotation(direction);
        }

        newFence.transform.rotation = baseRot * Quaternion.Euler(rotationOffset);
    }

    List<Vector3> GenerateHighResPath()
    {
        List<Vector3> path = new List<Vector3>();

        // --- FIXED SECTION START ---
        // Even when NOT using smooth curves, we must subdivide the line linearly.
        // If we don't, the fence spacer logic (GenerateFences) has no points to "walk" along
        // and will just jump from one corner to the next immediately.
        if (!useSmoothCurves)
        {
            int count = points.Count;
            if (count < 2) return path;

            int segments = closeLoop ? count : count - 1;
            int linearResolution = 50; // Use high resolution for straight lines too

            for (int i = 0; i < segments; i++)
            {
                Vector3 pStart = GetPoint(i);
                Vector3 pEnd = GetPoint(i + 1);

                for (int j = 0; j < linearResolution; j++)
                {
                    float t = (float)j / linearResolution;
                    path.Add(Vector3.Lerp(pStart, pEnd, t));
                }
            }

            if (closeLoop) path.Add(GetPoint(0));
            else path.Add(GetPoint(count - 1));

            return path;
        }
        // --- FIXED SECTION END ---

        int cCount = points.Count;
        int cSegments = closeLoop ? cCount : cCount - 1;
        int internalResolution = 50;

        for (int i = 0; i < cSegments; i++)
        {
            Vector3 p0 = GetPoint(i - 1);
            Vector3 p1 = GetPoint(i);
            Vector3 p2 = GetPoint(i + 1);
            Vector3 p3 = GetPoint(i + 2);
            for (int j = 0; j < internalResolution; j++)
            {
                float t = (float)j / internalResolution;
                path.Add(GetCatmullRomPosition(t, p0, p1, p2, p3));
            }
        }
        if (closeLoop) path.Add(GetPoint(0));
        else path.Add(GetPoint(cCount - 1));
        return path;
    }

    Vector3 GetPoint(int i)
    {
        if (points.Count == 0) return Vector3.zero;
        if (closeLoop)
        {
            int index = (i % points.Count + points.Count) % points.Count;
            return points[index].position;
        }
        else
        {
            int index = Mathf.Clamp(i, 0, points.Count - 1);
            return points[index].position;
        }
    }

    Vector3 GetCatmullRomPosition(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        Vector3 a = 2f * p1;
        Vector3 b = p2 - p0;
        Vector3 c = 2f * p0 - 5f * p1 + 4f * p2 - p3;
        Vector3 d = -p0 + 3f * p1 - 3f * p2 + p3;
        return 0.5f * (a + (b * t) + (c * t * t) + (d * t * t * t));
    }

    public void AddPoint(Vector3 position)
    {
        GameObject p = new GameObject($"Point_{points.Count}");
        p.transform.parent = transform;
        p.transform.position = position;
        points.Add(p.transform);
    }

    public void FinalizeAndCleanup()
    {
        foreach (var p in points) if (p != null) DestroyImmediate(p.gameObject);
        points.Clear();
        DestroyImmediate(this);
    }

    public void CenterPivotToFences()
    {
        if (transform.childCount == 0) return;
        Vector3 centerSum = Vector3.zero;
        int c = 0;
        List<Transform> children = new List<Transform>();
        foreach (Transform child in transform) { if (!points.Contains(child)) { children.Add(child); centerSum += child.position; c++; } }
        if (c == 0) return;
        Vector3 centerPos = centerSum / c;
        transform.DetachChildren();
        transform.position = centerPos;
        foreach (Transform child in children) child.parent = transform;
        foreach (Transform p in points) if (p != null) p.parent = transform;
    }

    private void OnDrawGizmos()
    {
        if (points == null || points.Count == 0) return;
        Gizmos.color = Color.green;
        foreach (var p in points) if (p != null) Gizmos.DrawCube(p.position, Vector3.one * 0.5f);
        if (points.Count >= 2)
        {
            Gizmos.color = useSmoothCurves ? Color.cyan : Color.red;
            List<Vector3> preview = new List<Vector3>();
            if (useSmoothCurves)
            {
                int count = points.Count;
                int segments = closeLoop ? count : count - 1;
                for (int i = 0; i < segments; i++)
                {
                    for (int j = 0; j < 10; j++) preview.Add(GetCatmullRomPosition(j / 10f, GetPoint(i - 1), GetPoint(i), GetPoint(i + 1), GetPoint(i + 2)));
                }
            }
            else { foreach (var p in points) if (p != null) preview.Add(p.position); }
            for (int i = 0; i < preview.Count - 1; i++) Gizmos.DrawLine(preview[i], preview[i + 1]);
        }
    }
}