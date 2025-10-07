using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "SceneState", menuName = "SceneSynth/SceneState")]
public class SceneState : ScriptableObject
{
    public List<PlacedObject> objects = new List<PlacedObject>();
    public List<PlacedLight> lights = new List<PlacedLight>();
    public float holesDifficulty;
    public float lightingDifficulty;
    public float occlusionDifficulty;

    // Runtime Initialization
    public static SceneState CreateRandom(List<GameObject> pool)
    {
        SceneState s = ScriptableObject.CreateInstance<SceneState>();
        s.objects = new List<PlacedObject>();
        s.lights = new List<PlacedLight>();

        for (int i = 0; i < 5; i++)
        {
            var prefab = pool[Random.Range(0, pool.Count)];
            var pos = new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f));
            s.objects.Add(new PlacedObject(prefab, pos, Quaternion.Euler(0, Random.Range(0, 360f), 0)));
        }

        for (int i = 0; i < 2; i++)
        {
            var pos = new Vector3(Random.Range(-3f, 3f), Random.Range(2f, 4f), Random.Range(-3f, 3f));
            s.lights.Add(new PlacedLight(pos, Random.Range(0.5f, 2f)));
        }

        s.EvaluateAll();
        return s;
    }

    public SceneState GetProposal(List<GameObject> pool)
    {
        SceneState copy = ScriptableObject.CreateInstance<SceneState>();
        copy.objects = new List<PlacedObject>(this.objects);
        copy.lights = new List<PlacedLight>(this.lights);
        copy.holesDifficulty = this.holesDifficulty;
        copy.lightingDifficulty = this.lightingDifficulty;
        copy.occlusionDifficulty = this.occlusionDifficulty;

        if (Random.value < 0.5f) copy.MutateObject(pool);
        else copy.MutateLight();

        copy.EvaluateAll();
        return copy;
    }

    public void EvaluateAll()
    {
        holesDifficulty = EvaluateHoles();
        lightingDifficulty = EvaluateLighting();
        occlusionDifficulty = EvaluateOcclusion();
    }

    float EvaluateHoles()
    {
        float totalH1 = 0f;

        foreach (var o in objects)
        {
            if (o.prefab == null) continue;

            GameObject instance = GameObject.Instantiate(o.prefab, o.position, o.rotation);
            MeshSampler sampler = instance.GetComponent<MeshSampler>();
            if (sampler == null)
                sampler = instance.AddComponent<MeshSampler>();

            sampler.SampleMesh(); // always sample fresh
            totalH1 += sampler.ComputeH1Score();

            // Clean up the temporary instance
            GameObject.DestroyImmediate(instance);
        }

        return totalH1;
    }

    float EvaluateLighting()
    {
        float totalScore = 0f;

        foreach (var o in objects)
        {
            if (o.prefab == null) continue;

            // Instantiate temporary object
            GameObject instance = GameObject.Instantiate(o.prefab, o.position, o.rotation);

            MeshFilter mf = MeshSampler.FindMeshFilter(instance); //instance.GetComponent<MeshFilter>() ?? instance.GetComponentInChildren<MeshFilter>(true);
            if (mf == null || mf.sharedMesh == null) {
                string objName = instance.name;
                GameObject.DestroyImmediate(instance);
                throw new System.InvalidOperationException($"{objName} has no MeshFilter or mesh!");
            }


            var vertices = mf.sharedMesh.vertices;
            var normals = mf.sharedMesh.normals;

            float objectScore = 0f;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldPos = instance.transform.rotation * vertices[i] + instance.transform.position;
                Vector3 worldNormal = instance.transform.rotation * normals[i];

                float vertexScore = 0f;
                foreach (var l in lights)
                {
                    Vector3 toLight = (l.position - worldPos).normalized;
                    float dist = Vector3.Distance(l.position, worldPos);
                    float NdotL = Vector3.Dot(worldNormal, toLight);
                    if (NdotL <= 0f) continue;
                    vertexScore += NdotL * (l.intensity / (dist * dist));
                }
                objectScore += vertexScore;
            }

            objectScore /= Mathf.Max(1, vertices.Length);
            totalScore += objectScore;

            GameObject.DestroyImmediate(instance);
        }

        return totalScore;
    }

    float EvaluateOcclusion()
    {
        float totalOcclusion = 0f;

        foreach (var o in objects)
        {
            if (o.prefab == null) continue;

            // Instantiate temporary object
            GameObject instance = GameObject.Instantiate(o.prefab, o.position, o.rotation);

            MeshSampler sampler = instance.GetComponent<MeshSampler>();
            if (sampler == null)
                sampler = instance.AddComponent<MeshSampler>();

            sampler.SampleMesh();
            var points = sampler.sampledPoints;
            if (points == null || points.Count == 0)
            {
                GameObject.DestroyImmediate(instance);
                continue;
            }

            float objectOcclusion = 0f;

            foreach (var localPoint in points)
            {
                Vector3 worldPoint = instance.transform.rotation * localPoint + instance.transform.position;
                int occludedRays = 0;
                int raySamples = 12;

                for (int i = 0; i < raySamples; i++)
                {
                    Vector3 dir = Random.onUnitSphere;
                    Ray ray = new Ray(worldPoint + dir * 0.01f, dir);
                    if (Physics.Raycast(ray, out RaycastHit hit, 10f))
                    {
                        if (hit.collider != null && !IsPartOfObject(hit.collider.gameObject, instance))
                            occludedRays++;
                    }
                }
                objectOcclusion += (float)occludedRays / raySamples;
            }

            objectOcclusion /= points.Count;
            totalOcclusion += objectOcclusion;

            GameObject.DestroyImmediate(instance);
        }

        return totalOcclusion;
    }


    bool IsPartOfObject(GameObject hitObj, GameObject originalPrefab)
    {
        return hitObj == originalPrefab || hitObj.transform.IsChildOf(originalPrefab.transform);
    }

    public void Apply(Transform parent)
    {
        foreach (Transform c in parent) GameObject.Destroy(c.gameObject);
        foreach (var o in objects)
            GameObject.Instantiate(o.prefab, o.position, o.rotation, parent);

        foreach (var l in lights)
        {
            var go = new GameObject("DynLight");
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.intensity = l.intensity;
            go.transform.SetParent(parent);
            go.transform.localPosition = l.position;
        }
    }

    void MutateObject(List<GameObject> pool)
    {
        if (objects.Count == 0) return;
        int index = Random.Range(0, objects.Count);
        var obj = objects[index];

        if (Random.value < 0.5f)
            obj.position += new Vector3(Random.Range(-1f,1f), 0, Random.Range(-1f,1f));
        else
            obj.prefab = pool[Random.Range(0, pool.Count)];

        objects[index] = obj;
    }

    void MutateLight()
    {
        if (lights.Count == 0) return;
        int index = Random.Range(0, lights.Count);
        var light = lights[index];
        light.position += new Vector3(Random.Range(-1f,1f), Random.Range(-0.5f,0.5f), Random.Range(-1f,1f));
        light.intensity = Mathf.Clamp(light.intensity + Random.Range(-0.2f, 0.2f), 0.1f, 5f);
        lights[index] = light;
    }
}

[System.Serializable]
public struct PlacedObject
{
    public GameObject prefab;
    public Vector3 position;
    public Quaternion rotation;
    public PlacedObject(GameObject p, Vector3 pos, Quaternion rot)
    {
        prefab = p; position = pos; rotation = rot;
    }
}

[System.Serializable]
public struct PlacedLight
{
    public Vector3 position;
    public float intensity;
    public PlacedLight(Vector3 pos, float i) { position = pos; intensity = i; }
}
