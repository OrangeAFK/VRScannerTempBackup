using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

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
    public int lights = 5;

    private SceneState currentState;
    private float currentCost;
    private List<float> costHistory = new List<float>();
    private int iteration = 0;
    private bool optimizationComplete = false;

    private RunningNormalizer holesNormalizer = new RunningNormalizer();
    private RunningNormalizer lightNormalizer = new RunningNormalizer();
    private RunningNormalizer occNormalizer = new RunningNormalizer();

    void Start()
    {
        currentState = CreateRandomState();
        currentCost = ComputeCost(currentState);
        // prime the normalizers with some random samples
        for (int i = 0; i < 5; i++)
        {
            var tmp = CreateRandomState(objectPrefabs);
            tmp.EvaluateAll();
            holesNormalizer.Update(tmp.holesDifficulty);
            lightNormalizer.Update(tmp.lightingDifficulty);
            occNormalizer.Update(tmp.occlusionDifficulty);
            Destroy(tmp);
        }

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
        currentState.Apply(this.transform);
    }

    SceneState CreateRandomState() {
        
    }

    public void OptimizeStep()
    {
        // Create proposal
        var proposal = currentState.GetProposal(objectPrefabs);
        if(proposal == null)
        {
            // hard reject. don't increment iterations or change temperature for invalid states
            ++hardRejectCount;
            Debug.Log("hard reject");
            // TODO: remove once debugged.
            if(hardRejectCount > 200 && iteration == 0) {
                Debug.Log("0 Iterations After Rejections. Quitting...");
                EditorApplication.isPlaying = false;
            }
            if(hardRejectCount > 1000) {
                Debug.Log("Iteration: " + iteration + ", Hard rejects: " + hardRejectCount);
                EditorApplication.isPlaying = false;
            }
            return;
        }

        float proposalCost = ComputeCost(proposal);
        float delta = proposalCost - currentCost;
        float alpha = Mathf.Min(1f, Mathf.Exp(-delta / Mathf.Max(temperature, 1e-6f)));

        // Accept or reject
        if (Random.value < alpha)
        {
            // Destroy the previous state to avoid memory leak
            ScriptableObject.DestroyImmediate(currentState);
            currentState = proposal;
            currentCost = proposalCost;
        }
        else
        {
            // reject
            ScriptableObject.DestroyImmediate(proposal);
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


        float cost = lambdaHoles*C_h
             + lambdaLights*C_l
             + lambdaOcclusion*C_o
             + lambdaCount*C_c;
        return cost;
    }

}
