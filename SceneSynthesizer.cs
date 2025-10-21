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

        currentState.EvaluateAll();
        
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
        // Create proposal
        var proposal = GenerateProposal(currentState);
        float proposalCost = ComputeCost(proposal);
        float delta = proposalCost - currentCost;
        float alpha = Mathf.Min(1f, Mathf.Exp(-delta / Mathf.Max(temperature, 1e-6f)));

        // Accept or reject
        if (Random.value < alpha)
        {
            // Destroy the previous state to avoid memory leak
            ScriptableObject.Destroy(currentState);
            currentState = proposal;
            currentCost = proposalCost;
        }
        else
        {
            // reject
            ScriptableObject.Destroy(proposal);
        }

        temperature *= coolingRate;
        costHistory.Add(currentCost);
        iteration++;

        // Check convergence
        if (iteration > 50)
        {
            var recent = costHistory.GetRange(costHistory.Count - 50, 50);
            float maxCost = recent.Max();
            float minCost = recent.Min();
            float avgCost = recent.Average();
            float absChange = Mathf.Abs((maxCost - minCost) / (Mathf.Abs(avgCost) + 1e-8f));
            if (absChange < 0.05f)
            {
                optimizationComplete = true;
            }
        }
    }

    SceneState GenerateProposal(SceneState baseState) {
        SceneState copy = ScriptableObject.CreateInstance<SceneState>();
        copy.objects = new List<GameObject>(baseState.objects);
        copy.lights = new List<PlacedLight>(baseState.lights);
        copy.targetObjects = targetObjects;

        float r = Random.value;
        if (r < 0.2f) AddRandomObject(copy);
        else if (r < 0.35f) RemoveRandomObject(copy);
        else if (r < 0.65f) MutateObject(copy);
        else if (r < 0.85f) SwapTwoObjects(copy);
        else MutateLight(copy);
        
        copy.EvaluateAll();
        return copy;
    }

    void AddRandomObject(SceneState s) {
        if (s.objects.Count >= SceneState.maxObjects) return;
        var prefab = objectPrefabs[Random.Range(0, objectPrefabs.Count)];
        Vector3 pos = new Vector3(Random.Range(SceneState.roomBounds.min.x, SceneState.roomBounds.max.x),
                                  0f,
                                  Random.Range(SceneState.roomBounds.min.z, SceneState.roomBounds.max.z));
        Quaternion rot = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
        s.objects.Add(new PlacedObject(prefab, pos, rot));
    }

    void RemoveRandomObject(SceneState s) {
        if (s.objects.Count <= SceneState.minObjects) return;
        int idx = Random.Range(0, s.objects.Count);
        s.objects.RemoveAt(idx);
    }

    void MutateObject(SceneState s)
    {
        if (s.objects.Count == 0) return;
        int idx = Random.Range(0, s.objects.Count);
        var obj = s.objects[idx];
        float r = Random.value;

        if (r < 0.4f)
        {
            Vector3 nudge = new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f));
            Vector3 newPos = obj.position + nudge;
            newPos.x = Mathf.Clamp(newPos.x, SceneState.roomBounds.min.x, SceneState.roomBounds.max.x);
            newPos.z = Mathf.Clamp(newPos.z, SceneState.roomBounds.min.z, SceneState.roomBounds.max.z);
            obj.position = newPos;
        }
        else if (r < 0.65f)
        {
            obj.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
        }
        else
        {
            obj.prefab = objectPrefabs[Random.Range(0, objectPrefabs.Count)];
        }

        s.objects[idx] = obj;
    }

    void SwapTwoObjects(SceneState s) {

    }

    void MutateLight(SceneState s) {

    }

    float ComputeCost(SceneState s)
    {
        // Normalize the raw evaluations
        float H = holesNormalizer.Normalize(s.holesDifficulty);
        float L = lightNormalizer.Normalize(s.lightingDifficulty);
        float O = occNormalizer.Normalize(s.occlusionDifficulty);

        // Normalize target preferences
        float targetH = holesDifficulty / 10f;
        float targetL = lightingDifficulty / 10f;
        float targetO = occlusionDifficulty / 10f;

        // Weighted squared difference
        float C_h = Mathf.Pow(H - targetH, 2);
        float C_l = Mathf.Pow(L - targetL, 2);
        float C_o = Mathf.Pow(O - targetO, 2);
        
        // bias the number of elements to be greater if the target occlusion factor is higher
        float idealN = Mathf.Lerp(SceneState.minObjects, SceneState.maxObjects, targetO);
        float N = s.objects.Count;
        float C_c = Mathf.Pow((N - idealN) / Mathf.Max(1f, SceneState.maxObjects - SceneState.minObjects), 2);

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

}
