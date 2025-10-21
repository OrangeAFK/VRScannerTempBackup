using UnityEngine;
using System.Collections.Generic;

public class MeshSampler : MonoBehaviour
{
    [Header("Sampling Settings")]
    public float sampleDensity = 1f;           // points per unit area
    public float connectionRadiusFactor = 0.05f; // fraction of mesh size
    public int maxSamples = 200;

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

        int numberOfSamples = Mathf.Min(Mathf.CeilToInt(sampleDensity * totalArea), maxSamples);
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

}
