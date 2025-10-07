using UnityEngine;
using System.Collections.Generic;

public class SceneSynthesizer : MonoBehaviour
{
    public List<GameObject> objectPrefabs;

    [Header("User Pain Points (1-10)")]
    [Range(1, 10)] public int holesDifficulty, lightingDifficulty, occlusionDifficulty;

    [Header("Annealing Settings")]
    public float temperature = 1.0f;
    public float coolingRate = 0.99f;
    public int maxIterations = 500;

    [Header("Cost Term Weights")]
    public float lambdaHoles = 1f;
    public float lambdaLights = 1f;
    public float lambdaOcclusion = 1f;

    private SceneState currentState;
    private float currentCost;
    private List<float> costHistory = new List<float>();
    private int iteration = 0;
    private bool optimizationComplete = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        currentState = SceneState.CreateRandom(objectPrefabs);
        currentCost = ComputeCost(currentState);
        currentState.Apply(this.transform);
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

    public void OptimizeStep()
    {
        // Create proposal
        var proposal = currentState.GetProposal(objectPrefabs);
        float proposalCost = ComputeCost(proposal);
        float delta = proposalCost - currentCost;
        float alpha = Mathf.Min(1f, Mathf.Exp(-delta / temperature));

        // Accept or reject
        if (Random.value < alpha)
        {
            // Destroy the previous state to avoid memory leak
            ScriptableObject.Destroy(currentState);

            currentState = proposal;
            currentCost = proposalCost;
            // currentState.Apply(this.transform); // enable for intermediate state visualization (laggy)
        }
        else
        {
            // If rejected, destroy the unused proposal
            ScriptableObject.Destroy(proposal);
        }

        temperature *= coolingRate;
        costHistory.Add(currentCost);
        iteration++;

        // Check convergence
        if (iteration > 50)
        {
            var recent = costHistory.GetRange(costHistory.Count - 50, 50);
            float maxCost = Mathf.Max(recent.ToArray());
            float minCost = Mathf.Min(recent.ToArray());
            float avgCost = 0;
            foreach (float c in recent) avgCost += c;
            avgCost /= recent.Count;
            float absChange = Mathf.Abs((maxCost - minCost) / (Mathf.Abs(avgCost) + 1e-8f));
            if (absChange < 0.05f)
            {
                optimizationComplete = true;
            }
        }
    }


    float ComputeCost(SceneState s)
    {
        // Normalize evaluations to [0, 1]
        float holesNorm = Mathf.Clamp01(s.holesDifficulty / 10f);
        float lightingNorm = Mathf.Clamp01(s.lightingDifficulty / 10f);
        float occlusionNorm = Mathf.Clamp01(s.occlusionDifficulty / 10f);

        // Normalize target preferences
        float targetH = holesDifficulty / 10f;
        float targetL = lightingDifficulty / 10f;
        float targetO = occlusionDifficulty / 10f;

        // Weighted squared difference
        float cost = 0f;
        cost += lambdaHoles * Mathf.Pow(holesNorm - targetH, 2);
        cost += lambdaLights * Mathf.Pow(lightingNorm - targetL, 2);
        cost += lambdaOcclusion * Mathf.Pow(occlusionNorm - targetO, 2);

        return cost;
    }

}
