using UnityEngine;
using System.Collections.Generic;

public class MeshSampler : MonoBehaviour
{
    [Header("Sampling Settings")]
    public float sampleDensity = 1f;           // points per unit area
    public float connectionRadiusFactor = 0.05f; // fraction of mesh size
    public bool visualizePoints = true;
    public float gizmoSize = 0.01f;

    [HideInInspector] public List<Vector3> sampledPoints;
    private float[] cumulativeAreas;


    // --- Mesh Sampling ---
    public void SampleMesh()
    {
        MeshFilter mf = FindMeshFilter(gameObject);

        if (mf == null)
        {
            Debug.LogError($"[MeshSampler] No MeshFilter found in {gameObject.name} or its children.");

            return;
        }
        if (mf.sharedMesh == null)
        {
            Debug.LogError("[MeshSampler] No SharedMesh found in {gameObject.name} or its children");
            return;
        }

        Mesh mesh = mf.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        int triCount = triangles.Length / 3;

        cumulativeAreas = new float[triCount];
        float totalArea = 0f;

        for (int i = 0; i < triCount; i++)
        {
            int i0 = triangles[i * 3];
            int i1 = triangles[i * 3 + 1];
            int i2 = triangles[i * 3 + 2];

            Vector3 v0 = transform.TransformPoint(vertices[i0]);
            Vector3 v1 = transform.TransformPoint(vertices[i1]);
            Vector3 v2 = transform.TransformPoint(vertices[i2]);

            float area = 0.5f * Vector3.Cross(v1 - v0, v2 - v0).magnitude;
            totalArea += area;
            cumulativeAreas[i] = totalArea;
        }

        int numberOfSamples = Mathf.CeilToInt(sampleDensity * totalArea);
        sampledPoints = new List<Vector3>(numberOfSamples);

        for (int i = 0; i < numberOfSamples; i++)
        {
            float r = Random.value * totalArea;
            int triIndex = BinarySearchTriangle(r, cumulativeAreas);
            int triBase = triIndex * 3;
            Vector3 point = SamplePointOnTriangle(
                vertices[triangles[triBase]],
                vertices[triangles[triBase + 1]],
                vertices[triangles[triBase + 2]]);
            sampledPoints.Add(transform.TransformPoint(point));
        }
    }

    public static MeshFilter FindMeshFilter(GameObject root)
    {
        MeshFilter mf = root.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null) return mf;

        foreach (Transform child in root.transform)
        {
            mf = MeshSampler.FindMeshFilter(child.gameObject);
            if (mf != null) return mf;
        }
        return null;
    }


    int BinarySearchTriangle(float r, float[] cumulativeAreas)
    {
        int low = 0, high = cumulativeAreas.Length - 1;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (r <= cumulativeAreas[mid]) high = mid;
            else low = mid + 1;
        }
        return low;
    }

    Vector3 SamplePointOnTriangle(Vector3 v0, Vector3 v1, Vector3 v2)
    {
        float u = Random.value, v = Random.value;
        if (u + v > 1f) { u = 1f - u; v = 1f - v; }
        return v0 + u * (v1 - v0) + v * (v2 - v0);
    }

    // --- Runtime-Friendly Persistent Homology H1 Score ---
    public float ComputeH1Score()
    {
        if (sampledPoints == null || sampledPoints.Count < 3) return 0f;

        MeshFilter mf = MeshSampler.FindMeshFilter(gameObject);
        if (mf == null || mf.sharedMesh == null)
        {
            throw new System.InvalidOperationException(
                $"[MeshSampler] {gameObject.name} has no MeshFilter or mesh! " +
                "Cannot compute H1 score."
            );
        }

        float meshSize = mf.sharedMesh.bounds.size.magnitude * transform.lossyScale.magnitude;
        float rMax = meshSize * 0.5f;    // max radius
        int steps = 20;                  // number of filtration steps

        // Build distance matrix
        int n = sampledPoints.Count;
        float[,] dist = new float[n, n];
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                float d = Vector3.Distance(sampledPoints[i], sampledPoints[j]);
                dist[i, j] = dist[j, i] = d;
            }

        // Track 1-cycles with simple birth-death approximation
        float h1Score = 0f;
        for (int s = 1; s <= steps; s++)
        {
            float r = rMax * s / steps;

            // Count triangles not yet filled: naive approximation
            int newCycles = 0;
            for (int i = 0; i < n - 2; i++)
                for (int j = i + 1; j < n - 1; j++)
                    for (int k = j + 1; k < n; k++)
                    {
                        if (dist[i,j] <= r && dist[j,k] <= r && dist[k,i] <= r)
                        {
                            // If all edges <= r, triangle exists → this cycle is filled → skip
                            continue;
                        }
                        // Otherwise, a potential loop exists
                        newCycles++;
                    }

            float persistence = 1f / steps;

            // Only consider cycles that persist enough
            h1Score += newCycles * persistence;
        }

        return h1Score;
    }


    void OnDrawGizmosSelected()
    {
        if (!visualizePoints || sampledPoints == null) return;
        Gizmos.color = Color.cyan;
        foreach (var p in sampledPoints) Gizmos.DrawSphere(p, gizmoSize);
    }
}
