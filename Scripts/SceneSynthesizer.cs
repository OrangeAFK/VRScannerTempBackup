using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

// TODO: create random scene (ideally starting with biased number of objects) and bias the running normalizers
// create mutation states

public class SceneSynthesizer : MonoBehaviour
{
    public List<GameObject> objectPrefabs;

    [Header("User Pain Points (1-10)")]
    [Range(1, 10)] public int holesDifficulty, lightingDifficulty, occlusionDifficulty;

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

    private SceneState currentState;
    private float currentCost;
    private List<float> costHistory = new List<float>();
    private int iteration = 0;
    private bool optimizationComplete = false;

    void Start()
    {
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
        currentState.lights = new List<PlacedLight>();
        for (int i = 0; i < numLights; i++)
        {
            PlacedLight light = new PlacedLight(
                new Vector3(
                    Random.Range(SceneState.roomBounds.min.x, SceneState.roomBounds.max.x),
                    Random.Range(SceneState.roomBounds.min.y + 0.5f, SceneState.roomBounds.max.y - 0.5f),
                    Random.Range(SceneState.roomBounds.min.z, SceneState.roomBounds.max.z)
                ),
                Random.Range(0.5f, 2.0f) // reasonable intensity
            );
            currentState.lights.Add(light);
        }
        
        currentCost = ComputeCost(currentState);
        StartCoroutine(OptimizerCoroutine());
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
    }

    public void OptimizeStep()
    {
        // --- Create proposal ---
        var mutation = new Mutation(currentState, objectPrefabs, maxObjects, minObjects);
        mutation.ApplyRandom();

        // --- Evaluate proposal ---
        float proposalCost = ComputeCost(currentState);
        float delta = proposalCost - currentCost;
        float alpha = Mathf.Exp(-delta / Mathf.Max(temperature, 1e-6f));

        if (Random.value < alpha)
        {
            // accept
            currentCost = proposalCost;
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

        struct ObjectBackup { public GameObject obj; public Vector3 pos; public Quaternion rot; }
        List<ObjectBackup> mutatedObjects = new();
        GameObject addedObject = null;
        int removedIndex = -1;
        GameObject removedObject = null;
        int swapIndexA = -1, swapIndexB = -1;

        int maxObjects, minObjects;

        // Light mutation backup
        int mutatedLightIndex = -1;
        PlacedLight lightBackup;

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
                mutatedObjects.Add(new ObjectBackup { obj = obj, pos = obj.transform.position, rot = obj.transform.rotation });

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
                mutatedLightIndex = idx;
                lightBackup = state.lights[idx];

                var light = state.lights[idx];
                Vector3 nudge = new Vector3(Random.Range(-1f, 1f), Random.Range(-0.5f, 0.5f), Random.Range(-1f, 1f));
                light.position += nudge;
                light.position = new Vector3(
                    Mathf.Clamp(light.position.x, SceneState.roomBounds.min.x, SceneState.roomBounds.max.x),
                    Mathf.Clamp(light.position.y, SceneState.roomBounds.min.y + 0.5f, SceneState.roomBounds.max.y - 0.5f),
                    Mathf.Clamp(light.position.z, SceneState.roomBounds.min.z, SceneState.roomBounds.max.z)
                );
                light.intensity = Mathf.Clamp(light.intensity + Random.Range(-0.2f, 0.2f), 0.05f, 5f);
                state.lights[idx] = light;
            }
        }

        public void Revert()
        {
            // Revert mutated objects
            foreach (var backup in mutatedObjects)
            {
                if (backup.obj != null)
                {
                    backup.obj.transform.position = backup.pos;
                    backup.obj.transform.rotation = backup.rot;
                }
            }
            mutatedObjects.Clear();

            // Revert added object
            if (addedObject != null)
            {
                state.objects.Remove(addedObject);
                Object.Destroy(addedObject);
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

            // Revert mutated light
            if (mutatedLightIndex >= 0)
            {
                state.lights[mutatedLightIndex] = lightBackup;
                mutatedLightIndex = -1;
            }
        }
    }
}