using UnityEngine;

// Tracks running min/max values and creates a map to normalized range [0,1]

public class RunningNormalizer
{
    public float min = float.PositiveInfinity;
    public float max = float.NegativeInfinity;

    private const float epsilon = 1e-6f;

    public void Update(float v) 
    {
        if(float.IsNaN(v) || float.IsInfinity(v)) return;
        if(v < min) min = v;
        if(v > max) max = v;
    }

    public float Normalize(float v)
    {
        if (min == float.PositiveInfinity || max == float.NegativeInfinity) {
            // not yet primed; fallback
            return clamp01 ? Mathf.Clamp01(v) : v;
        }
        float denom = Mathf.Max(max - min, EPS);
        float t = (v - min) / denom;
        return clamp01 ? Mathf.Clamp01(t) : t;
    }
}