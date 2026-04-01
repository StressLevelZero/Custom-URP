using UnityEngine;
using UnityEngine.Rendering;

public static class SHDering
{
    // Generates quasi-uniform directions on the sphere (good enough for min/max checks).
    static Vector3[] FibonacciSphere(int n)
    {
        var dirs = new Vector3[n];
        float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int i = 0; i < n; i++)
        {
            float t = (i + 0.5f) / n;
            float z = 1f - 2f * t;
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            float phi = goldenAngle * i;
            dirs[i] = new Vector3(r * Mathf.Cos(phi), z, r * Mathf.Sin(phi)); // normalized already
        }
        return dirs;
    }

    static SphericalHarmonicsL2 ScaleBands(SphericalHarmonicsL2 sh, float l1Scale, float l2Scale)
    {
        // Unity order: [0]=L00, [1..3]=L1, [4..8]=L2  
        for (int c = 0; c < 3; c++)
        {
            for (int i = 1; i <= 3; i++) sh[c, i] *= l1Scale;
            for (int i = 4; i <= 8; i++) sh[c, i] *= l2Scale;
        }
        return sh;
    }

    static float MinValueOverSphere(SphericalHarmonicsL2 sh, Vector3[] dirs, Color[] tmp)
    {
        sh.Evaluate(dirs, tmp);
        float m = float.PositiveInfinity;
        for (int i = 0; i < tmp.Length; i++)
        {
            // Use the most restrictive channel to avoid hue shifts.
            m = Mathf.Min(m, tmp[i].r, tmp[i].g, tmp[i].b);
        }
        return m;
    }

    /// <param name="minAllowed">
    /// Allow a small negative (e.g. -0.01) to keep contrast; 0 means strict non-negative.
    /// </param>
    public static SphericalHarmonicsL2 DeringScaleL2Only(
        SphericalHarmonicsL2 sh,
        int samples = 128,
        float minAllowed = -0.01f,
        int binarySearchIters = 12,
        bool reduceL1IfStillNegative = true)
    {
        var dirs = FibonacciSphere(samples);
        var tmp = new Color[samples];

        float min0 = MinValueOverSphere(sh, dirs, tmp);
        if (min0 >= minAllowed)
            return sh;

        // Binary search L2 scale in [0,1] while keeping L1 = 1.
        float lo = 0f, hi = 1f;
        for (int it = 0; it < binarySearchIters; it++)
        {
            float mid = 0.5f * (lo + hi);
            var test = ScaleBands(sh, 1f, mid);
            float mn = MinValueOverSphere(test, dirs, tmp);
            if (mn >= minAllowed) lo = mid; else hi = mid;
        }

        var outSh = ScaleBands(sh, 1f, lo);
        float minAfter = MinValueOverSphere(outSh, dirs, tmp);
        if (minAfter >= minAllowed || !reduceL1IfStillNegative)
            return outSh;

        // Rare case: even nuking L2 doesn't fix it -> reduce L1 too (and set L2=0).
        lo = 0f; hi = 1f;
        for (int it = 0; it < binarySearchIters; it++)
        {
            float mid = 0.5f * (lo + hi);
            var test = ScaleBands(sh, mid, 0f);
            float mn = MinValueOverSphere(test, dirs, tmp);
            if (mn >= minAllowed) lo = mid; else hi = mid;
        }

        return ScaleBands(sh, lo, 0f);
    }
}
