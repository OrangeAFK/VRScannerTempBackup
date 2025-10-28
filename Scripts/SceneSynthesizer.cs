using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;

// TODO: create random scene (ideally starting with biased number of objects) and bias the running normalizers
// create mutation states

public class SceneSynthesizer : MonoBehaviour
{
    public List<GameObject> objectPrefabs;

    [Header("User Pain Points (1-10)")]
    [Range(1, 10)] public int holesDifficulty, lightingDifficulty, occlusionDifficulty;

    [Header("JSON Settings")]
    public string sceneJsonPath = "Assets/Resources/Data/KitchenScene.json";
    public string outputPath = "Assets/Resources/Data/optimized.json";
    public string resourcesFolder = "Objects/Kitchen";
    


    [Header("Annealing Settings")]
    public float temperature = 1.0f;
    public float coolingRate = 0.99f;
    public int maxIterations = 4000; 

    [Header("Cost Term Weights")]
    public float lambdaHoles = 1f;
    public float lambdaLighting = 1f;
    public float lambdaOcclusion = 1f;
    public float lambdaCount = 0.2f;
    public float lambdaIntersection = 1f;
    public float lambdaBounds = 0.5f;

    [Header("Proposal Settings")]
    public int maxObjects = 25;
    public int minObjects = 4;
    public int numLights = 5;

    public enum OptimizeMode { Auto, Manual }
    [Header("Optimizer Mode")]
    public OptimizeMode mode = OptimizeMode.Auto;


    private SceneState currentState;
    private float currentCost;
    private List<float> costHistory = new List<float>();
    private int iteration = 0;
    private bool optimizationComplete = false;

    private Dictionary<string, GameObject> prefabLookup = new();


    void Start()
    {
        objectPrefabs = Resources.LoadAll<GameObject>(resourcesFolder).ToList();
        Debug.Log($"Loaded {objectPrefabs.Count} prefabs from Resources/{resourcesFolder}");

        prefabLookup.Clear();
        foreach (var p in objectPrefabs) {
            prefabLookup[p.name.ToLower()] = p;
        }

        LoadSceneJSON();
        

        if (mode == OptimizeMode.Auto)
            StartCoroutine(OptimizerCoroutine());
    }

    void LoadSceneJSON() {
        if(!File.Exists(sceneJsonPath)) { Debug.LogError("Scene JSON not found."); return; }

        string json = File.ReadAllText(sceneJsonPath);
        SceneData sceneData = JsonUtility.FromJson<SceneData>(json);
        
        currentState = ScriptableObject.CreateInstance<SceneState>();
        currentState.objects = new List<GameObject>();
        currentState.lights = new List<GameObject>();

        // objects
        foreach (var o in sceneData.objects)
        {
            string key = o.name.ToLower();
            if (!prefabLookup.TryGetValue(key, out GameObject prefab))
            {
                Debug.LogWarning($"Prefab not found for: {o.name}");
                continue;
            }

            GameObject obj = Instantiate(prefab);
            obj.name = o.name;
            obj.transform.position = o.position;
            obj.transform.eulerAngles = o.rotation;
            obj.transform.localScale = o.scale;
            

            currentState.objects.Add(obj);
        }

        // lights
        foreach (var l in sceneData.lights)
        {
            GameObject lightObj = new GameObject("Light");
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 10f;
            light.intensity = l.intensity;
            lightObj.transform.position = l.position;
            currentState.lights.Add(lightObj);
        }

        currentCost = ComputeCost(currentState);
        Debug.Log($"Scene loaded. Objects: {currentState.objects.Count}, Lights: {currentState.lights.Count}");
    }

    void SaveJSON() {
        SceneData data = new SceneData();
        data.sceneName = "Optimized Scene";
        data.objects = new List<ObjectData>();
        data.lights = new List<LightData>();

        // save objects
        foreach (var obj in currentState.objects)
        {
            if (obj == null) continue;
            
            ObjectData od = new ObjectData();
            od.name = obj.name;  // must match prefab name for re-loading
            od.position = obj.transform.position;
            od.rotation = obj.transform.eulerAngles;
            od.scale = obj.transform.localScale;

            data.objects.Add(od);
        }

        foreach (var l in currentState.lights)
        {
            if (l == null) continue;
            
            Light light = l.GetComponent<Light>();
            if (light == null) continue;

            LightData ld = new LightData();
            ld.position = l.transform.position;
            ld.intensity = light.intensity;

            data.lights.Add(ld);
        }

        string json = JsonUtility.ToJson(data, true); // pretty-print
        File.WriteAllText(outputPath, json);

        Debug.Log($"✅ Scene saved to JSON: {outputPath}");

    }

    // archived random scenestart
    private void RandomInit() {
        int targetObjects = Mathf.RoundToInt(Mathf.Lerp(minObjects, maxObjects, occlusionDifficulty / 10f));
        Debug.Log($"t: {targetObjects}");

        // create random in-bounds initialization state
        currentState = ScriptableObject.CreateInstance<SceneState>();
        currentState.targetObjects = targetObjects;
        // initialize objects
        currentState.objects = new List<GameObject>();
        for (int i = 0; i < targetObjects; i++)
        {
            var prefab = objectPrefabs[Random.Range(0, objectPrefabs.Count)];
            var obj = Instantiate(prefab);
            obj.transform.position = new Vector3(
                Random.Range(SceneState.roomBounds.min.x, SceneState.roomBounds.max.x),
                0f,
                Random.Range(SceneState.roomBounds.min.z, SceneState.roomBounds.max.z)
            );
            obj.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            currentState.objects.Add(obj);
        }
        // initialize lights
        currentState.lights = new List<GameObject>();
        for (int i = 0; i < numLights; i++)
        {
            GameObject lightObj = new GameObject("Light" + i);
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 10f;
            light.intensity = Random.Range(0.5f, 2f);

            lightObj.transform.position = new Vector3(
                Random.Range(SceneState.roomBounds.min.x, SceneState.roomBounds.max.x),
                Random.Range(SceneState.roomBounds.min.y + 0.5f, SceneState.roomBounds.max.y - 0.5f),
                Random.Range(SceneState.roomBounds.min.z, SceneState.roomBounds.max.z)
            );

            currentState.lights.Add(lightObj);
        }

        
        currentCost = ComputeCost(currentState);
    }

    System.Collections.IEnumerator OptimizerCoroutine()
    {
        while (!optimizationComplete && iteration < maxIterations)
        {
            OptimizeStep();
            yield return null;
        }
        
        if (optimizationComplete)
        {
            Debug.Log("Optimization completed due to cost convergence at iteration: " + iteration);
        }
        else
        {
            Debug.Log("Reached max iterations.");
        }

        SaveJSON();
    }

    public void OptimizeStep()
    {
        // --- Create proposal ---
        var mutation = new Mutation(currentState, objectPrefabs, maxObjects, minObjects);
        var initObj = FindObjectsOfType<GameObject>().Length;
        mutation.ApplyRandom();
        var nowObj = FindObjectsOfType<GameObject>().Length;
        bool added = (nowObj > initObj);

        // --- Evaluate proposal ---
        float proposalCost = ComputeCost(currentState);
        float delta = proposalCost - currentCost;
        float alpha = Mathf.Exp(-delta / Mathf.Max(temperature, 1e-6f));

        if (Random.value < alpha)
        {
            // accept
            currentCost = proposalCost;

            // destroy the removed object (if any)
            if (mutation.removedObject != null)
            {
                Destroy(mutation.removedObject);
                mutation.removedObject = null;
            }
        }
        else
        {
            // reject
            mutation.Revert();
        }

        // --- Update temperature ---
        temperature *= coolingRate;

        // --- Store cost history ---
        costHistory.Add(currentCost);
        iteration++;

        const int window = 100; // rolling window for stability check
        if (iteration >= window)
        {
            float meanDelta = 0f;
            for (int i = costHistory.Count - window + 1; i < costHistory.Count; i++)
            {
                meanDelta += Mathf.Abs(costHistory[i] - costHistory[i - 1]);
            }
            meanDelta /= (window - 1);

            // Converged if average change is below threshold AND temperature is low
            if (meanDelta < 1e-4f && temperature < 1e-2f)
            {
                optimizationComplete = true;
                Debug.Log($"Optimization converged at iteration {iteration} (mean delta {meanDelta:F6})");
            }
        }
    }


    float ComputeCost(SceneState s)
    {
        s.EvaluateAll();

        // aliases for simplicity
        float H = s.holesDifficulty;
        float L = s.lightingDifficulty;
        float O = s.occlusionDifficulty;

        // Normalize target preferences
        float targetH = holesDifficulty / 10f;
        float targetL = lightingDifficulty / 10f;
        float targetO = occlusionDifficulty / 10f;

        // Weighted squared difference
        float C_h = Mathf.Pow(H - targetH, 2);
        float C_l = Mathf.Pow(L - targetL, 2);
        float C_o = Mathf.Pow(O - targetO, 2);
        
        // bias the number of elements to be greater if the target occlusion factor is higher
        float idealN = Mathf.Lerp(minObjects, maxObjects, targetO);
        float N = s.objects.Count;
        float C_c = Mathf.Pow((N - idealN) / Mathf.Max(1f, maxObjects - minObjects), 2);

        // TODO: semantic realism terms
        // ----------------------------


        float cost = lambdaHoles * C_h
                   + lambdaLighting * C_l
                   + lambdaOcclusion * C_o
                   + lambdaCount * C_c
                   + lambdaIntersection * s.intersectPenalty
                   + lambdaBounds * s.boundsPenalty;
        return cost;
    }

    public class Mutation
    {
        SceneState state;
        List<GameObject> objectPrefabs;

        public struct ObjectBackup { public GameObject obj; public Vector3 pos; public Vector3 scale; public Quaternion rot; public float intensity; }
        public List<ObjectBackup> gameObjMutations = new();
        public GameObject addedObject = null;
        int removedIndex = -1;
        public GameObject removedObject = null;
        int swapIndexA = -1, swapIndexB = -1;

        int maxObjects, minObjects;

        public Mutation(SceneState s, List<GameObject> prefabs, int maxObjs, int minObjs)
        {
            state = s;
            objectPrefabs = prefabs;
            maxObjects = maxObjs;
            minObjects = minObjs;
        }

        public void ApplyRandom()
        {
            float r = Random.value;

            if (r < 0.2f) // Add object
            {
                if (state.objects.Count >= maxObjects) return;
                var prefab = objectPrefabs[Random.Range(0, objectPrefabs.Count)];
                addedObject = Object.Instantiate(prefab);
                addedObject.transform.position = new Vector3(
                    Random.Range(SceneState.roomBounds.min.x, SceneState.roomBounds.max.x),
                    0f,
                    Random.Range(SceneState.roomBounds.min.z, SceneState.roomBounds.max.z)
                );
                addedObject.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                state.objects.Add(addedObject);
            }
            else if (r < 0.35f) // Remove object
            {
                if (state.objects.Count <= minObjects) return;
                removedIndex = Random.Range(0, state.objects.Count);
                removedObject = state.objects[removedIndex];
                state.objects.RemoveAt(removedIndex);
            }
            else if (r < 0.65f) // Mutate object
            {
                if (state.objects.Count == 0) return;
                int idx = Random.Range(0, state.objects.Count);
                var obj = state.objects[idx];
                gameObjMutations.Add(new ObjectBackup { obj = obj, pos = obj.transform.position, rot = obj.transform.rotation, scale=obj.transform.localScale });

                float choice = Random.value;
                if (choice < 0.5f) // position nudge
                {
                    Vector3 nudge = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
                    obj.transform.position += nudge;
                    obj.transform.position = new Vector3(
                        Mathf.Clamp(obj.transform.position.x, SceneState.roomBounds.min.x, SceneState.roomBounds.max.x),
                        obj.transform.position.y,
                        Mathf.Clamp(obj.transform.position.z, SceneState.roomBounds.min.z, SceneState.roomBounds.max.z)
                    );
                }
                else // rotation change
                {
                    obj.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                }
            }
            else if (r < 0.85f) // Swap two objects
            {
                if (state.objects.Count < 2) return;
                swapIndexA = Random.Range(0, state.objects.Count);
                swapIndexB = Random.Range(0, state.objects.Count);
                if (swapIndexA == swapIndexB) swapIndexB = (swapIndexB + 1) % state.objects.Count;
                var tmp = state.objects[swapIndexA];
                state.objects[swapIndexA] = state.objects[swapIndexB];
                state.objects[swapIndexB] = tmp;
            }
            else // Mutate light
            {
                if (state.lights.Count == 0) return;
                int idx = Random.Range(0, state.lights.Count);
                var lightObj = state.lights[idx];
                var light = lightObj.GetComponent<Light>();
                gameObjMutations.Add(new ObjectBackup { obj = lightObj, pos = lightObj.transform.position, intensity = light.intensity });

                Vector3 nudge = new Vector3(Random.Range(-1f, 1f), Random.Range(-0.5f, 0.5f), Random.Range(-1f, 1f));
                lightObj.transform.position += nudge;
                lightObj.transform.position = new Vector3(
                    Mathf.Clamp(lightObj.transform.position.x, SceneState.roomBounds.min.x, SceneState.roomBounds.max.x),
                    Mathf.Clamp(lightObj.transform.position.y, SceneState.roomBounds.min.y + 0.5f, SceneState.roomBounds.max.y - 0.5f),
                    Mathf.Clamp(lightObj.transform.position.z, SceneState.roomBounds.min.z, SceneState.roomBounds.max.z)
                );
                light.intensity = Mathf.Clamp(light.intensity + Random.Range(-0.2f, 0.2f), 0.05f, 5f);
            }
        }

        public void Revert()
        {
            // Revert mutated objects and lights
            foreach (var backup in gameObjMutations)
            {
                if (backup.obj != null)
                {
                    backup.obj.transform.position = backup.pos;
                    if(backup.rot != null) { backup.obj.transform.rotation = backup.rot; } // obj
                    else { backup.obj.GetComponent<Light>().intensity = backup.intensity; } // light
                }
            }
            gameObjMutations.Clear();

            // Revert added object
            if (addedObject != null)
            {
                Debug.Log("reverted!");
                Object.Destroy(addedObject);
                state.objects.Remove(addedObject);
                addedObject = null;
            }

            // Revert removed object
            if (removedObject != null && removedIndex >= 0)
            {
                state.objects.Insert(removedIndex, removedObject);
                removedObject = null;
                removedIndex = -1;
            }

            // Revert swap
            if (swapIndexA >= 0 && swapIndexB >= 0)
            {
                var tmp = state.objects[swapIndexA];
                state.objects[swapIndexA] = state.objects[swapIndexB];
                state.objects[swapIndexB] = tmp;
                swapIndexA = swapIndexB = -1;
            }
        }
    }

    void Update()
    {
        if (mode == OptimizeMode.Manual)
        {
            if (Input.GetKeyDown(KeyCode.F))
            {
                OptimizeStep();
                Debug.Log($"Manual Step {iteration}, Cost = {currentCost:F4}");
            }
        }
    }
}

[System.Serializable]
public class SceneData
{
    public string sceneName;
    public List<ObjectData> objects;
    public List<LightData> lights;
}

[System.Serializable]
public class ObjectData
{
    public string name;
    public Vector3 position;
    public Vector3 rotation;
    public Vector3 scale;
}

[System.Serializable]
public class LightData
{
    public Vector3 position;
    public float intensity;
}
