using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class SceneState : ScriptableObject
{
    public List<GameObject> objects;
    public List<PlacedLight> lights;

    public float holesDifficulty;
    public float occlusionDifficulty;
    public float lightingDifficulty;

    public float intersectPenalty;
    public float boundsPenalty;
    public float countPenalty;

    public static readonly Bounds roomBounds = new Bounds(new Vector3(0f,2f,0f), new Vector3(10f, 4f, 10f));

    public int targetObjects = 10; // set arbitrarily, synthesizer will set the real value

    public const int maxSamples = 200;

    public float saturationThreshold = 1.2f;
    public float darknessThreshold = 0.1f;

    public void EvaluateAll()
    {
        holesDifficulty = EvaluateHoles();
        lightingDifficulty = EvaluateLighting();
        occlusionDifficulty = EvaluateOcclusion();
        intersectPenalty = EvaluateIntersection();
        boundsPenalty = EvaluateBounds();
        countPenalty = EvaluateCount();
    }

    // holes score evaluates mesh watertightness based on boundary edge ratio
    float EvaluateHoles()
    {
        float totalBoundaryLength = 0f;
        float totalSurfaceArea = 0f;

        foreach(var obj in objects) {
            var meshFilter = obj.GetComponent<MeshFilter>();
            if(meshFilter == null) continue;
            Mesh mesh = meshFilter.sharedMesh;
            if(mesh == null) continue;

            totalSurfaceArea += ComputeMeshSurfaceArea(mesh, obj.transform);
            totalBoundaryLength += ComputeMeshBoundaryLength(mesh, obj.transform);
        }

        if (totalSurfaceArea < 1e-5f) return 0f;

        float ratio = totalBoundaryLength / totalSurfaceArea;
        return Mathf.SmoothStep(0, 1, Mathf.Clamp01(ratio * 10f)); // 10f is a heuristic scalar
    }

    private float ComputeMeshSurfaceArea(Mesh mesh, Transform t) {
        var verts = mesh.vertices;
        var tris = mesh.triangles;
        float area = 0f;
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 a = t.TransformPoint(verts[tris[i]]);
            Vector3 b = t.TransformPoint(verts[tris[i + 1]]);
            Vector3 c = t.TransformPoint(verts[tris[i + 2]]);
            area += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
        }
        return area;
    }

    private float ComputeMeshBoundaryLength(Mesh mesh, Transform t)
    {
        var edgeCounts = new Dictionary<(int, int), int>();
        var tris = mesh.triangles;

        for (int i = 0; i < tris.Length; i += 3)
        {
            int a = tris[i]; int b = tris[i + 1]; int c = tris[i + 2];
            AddEdge(a, b); AddEdge(b, c); AddEdge(c, a);
        }

        void AddEdge(int i1, int i2)
        {
            var key = i1 < i2 ? (i1, i2) : (i2, i1);
            if (edgeCounts.ContainsKey(key)) edgeCounts[key]++;
            else edgeCounts[key] = 1;
        }

        float boundaryLength = 0f;
        foreach (var kv in edgeCounts)
        {
            if (kv.Value == 1)
            {
                Vector3 p1 = t.TransformPoint(mesh.vertices[kv.Key.Item1]);
                Vector3 p2 = t.TransformPoint(mesh.vertices[kv.Key.Item2]);
                boundaryLength += Vector3.Distance(p1, p2);
            }
        }
        return boundaryLength;
    }

    float EvaluateLighting()
    {
        if (lights == null || lights.Count == 0) return 0f;

        List<float> penalties = new();

        foreach (var obj in objects)
        {
            var meshFilter = obj.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null) continue;
            Mesh mesh = meshFilter.sharedMesh;

            var verts = mesh.vertices;
            var normals = mesh.normals;

            float objPenalty = 0f;
            int sampleCount = Mathf.Min(verts.Length, maxSamples);

            for (int i = 0; i < sampleCount; i++)
            {
                int idx = Random.Range(0, verts.Length);
                Vector3 pos = obj.transform.TransformPoint(verts[idx]);
                Vector3 normal = obj.transform.TransformDirection(normals[idx]);

                float totalLight = 0f;
                foreach (var light in lights)
                {
                    Vector3 toLight = light.position - pos;
                    float atten = 1f / (1f + toLight.sqrMagnitude);
                    float intensity = light.intensity * Mathf.Max(0, Vector3.Dot(normal, toLight.normalized)) * atten;
                    totalLight += intensity;
                }

                // Penalize overexposed or underexposed samples
                float diff = Mathf.Max(0, totalLight - saturationThreshold) + Mathf.Max(0, darknessThreshold - totalLight);
                objPenalty += diff * diff;

            }
            penalties.Add(objPenalty / Mathf.Max(1, sampleCount));
        }

        if (penalties.Count == 0) return 0f;
        return Mathf.Clamp01(penalties.Average());
    }

    float EvaluateOcclusion()
    {
        int rayCount = 8;
        float alpha = 2f;
        float totalVisibility = 0f;
        int totalSamples = 0;

        foreach (var obj in objects)
        {
            if (obj == null) continue;

            MeshFilter mf = obj.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            Mesh mesh = mf.sharedMesh;
            var verts = mesh.vertices;
            var normals = mesh.normals;

            int sampleCount = Mathf.Min(verts.Length, maxSamples);
            for (int i = 0; i < sampleCount; i++)
            {
                int idx = Random.Range(0, verts.Length);
                Vector3 worldPos = obj.transform.TransformPoint(verts[idx]);
                Vector3 worldNormal = obj.transform.TransformDirection(normals[idx]);

                float vis = 0f;
                for (int j = 0; j < rayCount; j++)
                {
                    Vector3 dir = RandomHemisphereDirection(worldNormal);
                    if (Physics.Raycast(worldPos + dir * 0.001f, dir, out RaycastHit hit, 1f))
                        vis += Mathf.Exp(-alpha * hit.distance);
                    else
                        vis += 1f;
                }

                totalVisibility += vis / rayCount;
                totalSamples++;
            }
        }

        if (totalSamples == 0) return 0f;
        float meanVis = totalVisibility / totalSamples;
        return 1f - meanVis;
    }


    Vector3 RandomHemisphereDirection(Vector3 normal)
    {
        Vector3 dir = Random.onUnitSphere;
        return Vector3.Dot(dir, normal) < 0 ? -dir : dir;
    }


    float EvaluateIntersection() {
        float penalty = 0f;
        for(int i = 0; i < objects.Count; i++) {
            Collider a = objects[i].GetComponent<Collider>();
            if(a == null) continue;
            for(int j = i + 1; j < objects.Count; j++) {
                Collider b = objects[j].GetComponent<Collider>();
                if(b == null) continue;
                if(a.bounds.Intersects(b.bounds)) {
                    float overlap = (a.bounds.size.magnitude + b.bounds.size.magnitude) - Vector3.Distance(a.bounds.center, b.bounds.center);
                    penalty += Mathf.Max(0, overlap * overlap);
                }
            }
        }

        return penalty / Mathf.Max(1, objects.Count);
    }

    float EvaluateBounds() {
        float penalty = 0f;
        foreach (var o in objects) {
            if(!roomBounds.Contains(o.transform.position)) {
                Vector3 closest = roomBounds.ClosestPoint(o.transform.position);
                float dist = Vector3.Distance(o.transform.position, closest);
                penalty += dist * dist;
            }
        }
        return penalty / Mathf.Max(1, objects.Count);
    }

    float EvaluateCount() {
        int diff = objects.Count - targetObjects;
        return diff * diff;
    }
}

[System.Serializable]
public struct PlacedLight
{
    public Vector3 position;
    public float intensity;
    public PlacedLight(Vector3 pos, float i) { position = pos; intensity = i; }
}
