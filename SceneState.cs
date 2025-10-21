using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "SceneState", menuName = "SceneSynth/SceneState")]
public class SceneState : ScriptableObject
{
    public List<PlacedObject> objects = new List<PlacedObject>();
    public List<PlacedLight> lights = new List<PlacedLight>();

    // raw scores pre-normalization
    public float holesDifficulty;
    public float lightingDifficulty;
    public float occlusionDifficulty;

    public static readonly Bounds roomBounds = new Bounds(new Vector3(0f,2f,0f), new Vector3(10f, 4f, 10f));

    public const int minObjects = 4;
    public const int maxObjects = 25;
    public const int initObjects = 6;

    public const int minLights = 1;
    public const int maxLights = 4;
    public const int initLights = 2;
    
    // Runtime Initialization
    public static SceneState CreateRandom(List<GameObject> pool)
    {
        SceneState s = ScriptableObject.CreateInstance<SceneState>();
        s.objects = new List<PlacedObject>();
        s.lights = new List<PlacedLight>();

        // initialize objects
        for (int i = 0; i < SceneState.initObjects; i++)
        {
            var prefab = pool[Random.Range(0, pool.Count)];
            var pos = new Vector3(Random.Range(SceneState.roomBounds.min.x, SceneState.roomBounds.max.x), 0f, Random.Range(SceneState.roomBounds.min.z, SceneState.roomBounds.max.z));
            s.objects.Add(new PlacedObject(prefab, pos, Quaternion.Euler(0, Random.Range(0, 360f), 0)));
        }

        // initialize lights
        for (int i = 0; i < SceneState.initLights; i++)
        {
            var pos = new Vector3(Random.Range(SceneState.roomBounds.min.x, SceneState.roomBounds.max.x),
                                  Random.Range(SceneState.roomBounds.min.y + 0.5f, SceneState.roomBounds.max.y - 0.5f),
                                  Random.Range(SceneState.roomBounds.min.z, SceneState.roomBounds.max.z));

            s.lights.Add(new PlacedLight(pos, Random.Range(0.5f, 2f)));
        }

        s.EvaluateAll();
        return s;
    }

    public SceneState GetProposal(List<GameObject> pool)
    {
        Debug.Log("1");
        // deep copy of strucsts
        SceneState copy = ScriptableObject.CreateInstance<SceneState>();
        copy.objects = new List<PlacedObject>(this.objects);
        copy.lights = new List<PlacedLight>(this.lights);
        copy.holesDifficulty = this.holesDifficulty;
        copy.lightingDifficulty = this.lightingDifficulty;
        copy.occlusionDifficulty = this.occlusionDifficulty;

        Debug.Log("2");
        float r = Random.value;
        if (r < 0.2f) {
            copy.AddRandomObject(pool);
        }
        else if (r < 0.35f) copy.RemoveRandomObject();
        else if (r < 0.65f) copy.MutateObject(pool);
        else if (r < 0.85f) copy.SwapTwoObjects();
        else copy.MutateLight();

        Debug.Log("3");
        if(HardRejectProposal(copy)) {
            Debug.Log("HR inside GetProposal");
            Destroy(copy);
            return null; // null condition indicates reject
        }
        Debug.Log("4");
        copy.EvaluateAll();
        Debug.Log("5");
        Debug.Log("Returning proposal from GetProposal: " + copy);
        return copy;
    }

    // structural mutation
    public void AddRandomObject(List<GameObject> pool)
    {
        if (objects.Count >= SceneState.maxObjects) return;
        var prefab = pool[Random.Range(0, pool.Count)];
        Vector3 pos = new Vector3(Random.Range(SceneState.roomBounds.min.x, SceneState.roomBounds.max.x),
                                  0f,
                                  Random.Range(SceneState.roomBounds.min.z, SceneState.roomBounds.max.z));
        Quaternion rot = Quaternion.Euler(0, Random.Range(0f, 360f), 0f);
        objects.Add(new PlacedObject(prefab, pos, rot));
    }

    public void RemoveRandomObject() {
        if(objects.Count <= SceneState.minObjects) return;
        int idx = Random.Range(0, objects.Count);
        objects.RemoveAt(idx);
    }

    public void SwapTwoObjects() {
        if(objects.Count < 2) return;
        int a = Random.Range(0, objects.Count);
        int b = Random.Range(0, objects.Count - 1);
        if(b >= a) b++;
        var tmp = objects[a];
        objects[a] = objects[b];
        objects[b] = tmp;
    }

    public void MutateObject(List<GameObject> pool) {
        if(objects.Count==0) return;
        int idx = Random.Range(0, objects.Count);
        var obj = objects[idx];

        float r = Random.value;
        if(r < 0.4f) {
            // nudge position inside room
            Vector3 nudge = new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f));
            Vector3 newPos = obj.position + nudge;
            // clamp to room
            newPos.x = Mathf.Clamp(newPos.x, SceneState.roomBounds.min.x, SceneState.roomBounds.max.x);
            newPos.z = Mathf.Clamp(newPos.z, SceneState.roomBounds.min.z, SceneState.roomBounds.max.z);
            obj.position = newPos;
        }
        else if(r < 0.65f) {
            obj.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
        }
        else {
            obj.prefab = pool[Random.Range(0, pool.Count)];
        }
        objects[idx] = obj;
    }

    public void MutateLight()
    {
        if(lights.Count==0) return;
        int idx = Random.Range(0, lights.Count);
        var light = lights[idx];
        // nudge position and clamp
        Vector3 nudge = new Vector3(Random.Range(-1f, 1f), Random.Range(-0.5f, 0.5f), Random.Range(-1f, 1f));
        Vector3 newPos = light.position + nudge;
        newPos.x = Mathf.Clamp(newPos.x, SceneState.roomBounds.min.x, SceneState.roomBounds.max.x);
        newPos.y = Mathf.Clamp(newPos.y, SceneState.roomBounds.min.y + 0.5f, SceneState.roomBounds.max.y - 0.5f);
        newPos.z = Mathf.Clamp(newPos.z, SceneState.roomBounds.min.z, SceneState.roomBounds.max.z);
        
        light.position = newPos;
        light.intensity = Mathf.Clamp(light.intensity + Random.Range(-0.2f, 0.2f), 0.05f, 5f);
        lights[idx] = light;
    }

    // fast hard reject test
    private bool HardRejectProposal(SceneState s)
    {
        // object and light positions should be clamped, but we'll still check for it here in case 
        // that it has to be modified down the line.

        // object count or object position out of bounds
        // TODO: change so that it object position AABB must be fully inside the room bounds, once object placement position is set with respect to prefab dimensions
        if(s.objects.Count < SceneState.minObjects || s.objects.Count > SceneState.maxObjects) {
            Debug.Log($"[HardReject] Object count out of range: {s.objects.Count}");
            return true;
        }
        foreach(var o in s.objects) {
            Bounds b = GetWorldBounds(o);
            if(!SceneState.roomBounds.Intersects(b)) {
                Debug.Log($"HardReject: Object {o.prefab?.name ?? "?"} out of room bounds {b.center}");
                return true;
            }
        }

        // pairwise intersection
        for (int i = 0; i < s.objects.Count; i++)
        {
            var bi = GetWorldBounds(s.objects[i]);
            for (int j = i + 1; j < s.objects.Count; j++)
            {
                var bj = GetWorldBounds(s.objects[j]);
                if (bi.Intersects(bj))
                {
                    // TODO: allow slight intersection if small tolerance? here reject
                    Debug.Log(
                        $"HardReject: Collision between {s.objects[i].prefab?.name ?? "?"} and {s.objects[j].prefab?.name ?? "?"}"
                    );
                    return true;
                }
            }
        }

        // lights out of bounds
        foreach(var light in s.lights)
        {
            if(!SceneState.roomBounds.Contains(light.position)) {
                Debug.Log($"HardReject: Light out of bounds at {light.position}");
                return true;
            }
        }

        return false;
    }

    // get AABB 
    private Bounds GetWorldBounds(PlacedObject o) {
        // TODO: raise error here
        if(o.prefab==null) return new Bounds(o.position, Vector3.one*0.5f);

        // will use meshfilter instead of renderer to generate AABB since obj 
        // isn't instantiated until the final scene is drawn
        var mf = o.prefab.GetComponentInChildren<MeshFilter>();
        if(mf==null || mf.sharedMesh==null) {
            // raise error if no meshfilter
            throw new System.InvalidOperationException($"PlacedObject '{o.prefab.name}' has no MeshFilter or sharedMesh!");
        }

        Bounds meshB = mf.sharedMesh.bounds;
        Vector3[] corners = new Vector3[8];
        Vector3 ext = meshB.extents;
        Vector3 c = meshB.center;
        int cornerIdx = 0;
        for (int xi = -1; xi <= 1; xi += 2)
        for (int yi = -1; yi <= 1; yi += 2)
        for (int zi = -1; zi <= 1; zi += 2)
        {
            Vector3 localCorner = c + Vector3.Scale(ext, new Vector3(xi, yi, zi));
            Vector3 worldCorner = o.rotation * localCorner + o.position;
            corners[cornerIdx++] = worldCorner;
        }

        Bounds worldAABB = new Bounds(corners[0], Vector3.zero);
        for (int i = 1; i < corners.Length; i++) worldAABB.Encapsulate(corners[i]);
        return worldAABB;
    }

    public void EvaluateAll()
    {
        holesDifficulty = EvaluateHoles();
        lightingDifficulty = EvaluateLighting();
        occlusionDifficulty = EvaluateOcclusion();
    }

    // TODO: make sure the evaluation functions are well-formed

    float EvaluateHoles()
    {
        float totalH1 = 0f;
        int count = 0;

        foreach (var o in objects)
        {
            if (o.prefab == null) continue;

            GameObject instance = GameObject.Instantiate(o.prefab, o.position, o.rotation);
            MeshSampler sampler = instance.GetComponent<MeshSampler>();
            if (sampler == null)
                sampler = instance.AddComponent<MeshSampler>();

            sampler.SampleMesh(); // always sample fresh
            totalH1 += sampler.ComputeH1Score();
            count++;
            // Clean up the temporary instance
            Destroy(instance);
        }
        return (count==0) ? 0f : totalH1/count; // mean H1 score 
    }

    float EvaluateLighting()
    {
        float totalScore = 0f;
        int count = 0;

        foreach (var o in objects)
        {
            if (o.prefab == null) continue;

            // Instantiate temporary object
            GameObject instance = GameObject.Instantiate(o.prefab, o.position, o.rotation);

            MeshFilter mf = MeshSampler.FindMeshFilter(instance); //instance.GetComponent<MeshFilter>() ?? instance.GetComponentInChildren<MeshFilter>(true);
            if (mf == null || mf.sharedMesh == null) {
                string objName = instance.name;
                Destroy(instance);
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
            count++;
            Destroy(instance);
        }

        return (count==0) ? 0f : totalScore/count;
    }

    float EvaluateOcclusion()
    {
        float totalOcclusion = 0f;
        int count = 0;

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
                Destroy(instance);
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
            count++;
            Destroy(instance);
        }

        return (count==0) ? 0f : totalOcclusion/count;
    }


    bool IsPartOfObject(GameObject hitObj, GameObject originalPrefab)
    {
        return hitObj == originalPrefab || hitObj.transform.IsChildOf(originalPrefab.transform);
    }

    public void Apply(Transform parent)
    {
        foreach (Transform c in parent) Destroy(c.gameObject);
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
