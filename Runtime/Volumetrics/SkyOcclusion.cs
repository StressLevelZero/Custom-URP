using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System;
using UnityEngine;

public static class SkyOcclusion
{
    // Helper method to compute the determinant of a 3x3 matrix
    private static float Determinant3x3(
        float a1, float a2, float a3,
        float b1, float b2, float b3,
        float c1, float c2, float c3)
    {
        return a1 * (b2 * c3 - b3 * c2)
             - a2 * (b1 * c3 - b3 * c1)
             + a3 * (b1 * c2 - b2 * c1);
    }

    public static Vector4 ComputeBarycentricCoordinates(Vector3 p, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        // Compute vectors
        Vector3 vap = p - a;
        Vector3 vab = b - a;
        Vector3 vac = c - a;
        Vector3 vad = d - a;

        // Compute the volume of the tetrahedron (6 times the volume)
        float volume6 = Vector3.Dot(vab, Vector3.Cross(vac, vad));

        if (Mathf.Abs(volume6) > Mathf.Epsilon)
        {
            // Non-degenerate tetrahedron, compute barycentric coordinates in 3D

            Vector3 vbd = b - d;
            Vector3 vcd = c - d;
            Vector3 vad_d = a - d;
            Vector3 p_d = p - d;

            // Compute determinant of the matrix
            float detM = Determinant3x3(
                vad_d.x, vbd.x, vcd.x,
                vad_d.y, vbd.y, vcd.y,
                vad_d.z, vbd.z, vcd.z);

            // Compute weights
            float w_a = Determinant3x3(
                p_d.x, vbd.x, vcd.x,
                p_d.y, vbd.y, vcd.y,
                p_d.z, vbd.z, vcd.z) / detM;

            float w_b = Determinant3x3(
                vad_d.x, p_d.x, vcd.x,
                vad_d.y, p_d.y, vcd.y,
                vad_d.z, p_d.z, vcd.z) / detM;

            float w_c = Determinant3x3(
                vad_d.x, vbd.x, p_d.x,
                vad_d.y, vbd.y, p_d.y,
                vad_d.z, vbd.z, p_d.z) / detM;

            float w_d = 1 - w_a - w_b - w_c;

            return new Vector4(w_a, w_b, w_c, w_d);
        }
        else
        {
            // Degenerate case: points are coplanar or colinear
            // Attempt to compute barycentric coordinates in lower dimensions

            // Check if points form a plane (non-zero area triangle)
            float area2 = Vector3.Cross(vab, vac).sqrMagnitude;

            if (area2 > Mathf.Epsilon * Mathf.Epsilon)
            {
                // Points are coplanar; compute 2D barycentric coordinates

                // Define a plane using points a, b, and c
                Vector3 normal = Vector3.Cross(vab, vac).normalized;

                // Create a local coordinate system on the plane
                Vector3 u = vab.normalized;
                Vector3 v = Vector3.Cross(normal, u);

                // Project points onto the plane
                float bu = Vector3.Dot(vab, u);
                float bv = Vector3.Dot(vab, v);
                float cu = Vector3.Dot(vac, u);
                float cv = Vector3.Dot(vac, v);
                float pu = Vector3.Dot(vap, u);
                float pv = Vector3.Dot(vap, v);

                // Compute barycentric coordinates in 2D
                float denom = (bu * cv - cu * bv);

                if (Mathf.Abs(denom) < Mathf.Epsilon)
                {
                    // Degenerate triangle
                    Debug.LogError("Degenerate triangle with zero area.");
                    return Vector4.zero;
                }

                float w_a = ( ( (bv * cu) - (bu * cv) ) + ( (cv * pu) - (cu * pv) ) + ( (bu * pv) - (bv * pu) ) ) / denom;
                float w_b = ( ( (cv * pu) - (cu * pv) ) + ( (cu * pv) - (cv * pu) ) ) / denom;
                float w_c = 1 - w_a - w_b;
                float w_d = 0f; // Not used in 2D case

                return new Vector4(w_a, w_b, w_c, w_d);
            }
            else
            {
                // Points are colinear or identical; compute 1D barycentric coordinates

                float lengthSquared = vab.sqrMagnitude;

                if (lengthSquared > Mathf.Epsilon)
                {
                    // Points are along a line
                    float t = Vector3.Dot(vap, vab) / lengthSquared;
                    float w_a = 1 - t;
                    float w_b = t;
                    float w_c = 0f;
                    float w_d = 0f;

                    return new Vector4(w_a, w_b, w_c, w_d);
                }
                else
                {
                    // All points are identical
                    Debug.LogWarning("All points are identical or too close together.");
                    return new Vector4(1f, 0f, 0f, 0f);
                }
            }
        }
    }
}

// Monochromatic L2 Spherical Harmonic
[Serializable]
public struct MonoSH : IEquatable<MonoSH>
{
    // L0 term
    public float sh0;

    // L1 terms
    public float sh1;
    public float sh2;
    public float sh3;

    // L2 terms
    public float sh4;
    public float sh5;
    public float sh6;
    public float sh7;
    public float sh8;

    // Constructor
    public MonoSH(float sh0, float sh1, float sh2, float sh3, float sh4, float sh5, float sh6, float sh7, float sh8)
    {
        this.sh0 = sh0;
        this.sh1 = sh1;
        this.sh2 = sh2;
        this.sh3 = sh3;
        this.sh4 = sh4;
        this.sh5 = sh5;
        this.sh6 = sh6;
        this.sh7 = sh7;
        this.sh8 = sh8;
    }

    // Overload the addition operator
    public static MonoSH operator +(MonoSH a, MonoSH b)
    {
        return new MonoSH(
            a.sh0 + b.sh0,
            a.sh1 + b.sh1,
            a.sh2 + b.sh2,
            a.sh3 + b.sh3,
            a.sh4 + b.sh4,
            a.sh5 + b.sh5,
            a.sh6 + b.sh6,
            a.sh7 + b.sh7,
            a.sh8 + b.sh8
        );
    }

    // Overload the multiplication operator for scalar multiplication
    public static MonoSH operator *(MonoSH monoSH, float scalar)
    {
        return new MonoSH(
            monoSH.sh0 * scalar,
            monoSH.sh1 * scalar,
            monoSH.sh2 * scalar,
            monoSH.sh3 * scalar,
            monoSH.sh4 * scalar,
            monoSH.sh5 * scalar,
            monoSH.sh6 * scalar,
            monoSH.sh7 * scalar,
            monoSH.sh8 * scalar
        );
    }

    // Overload the multiplication operator for scalar multiplication (commutative)
    public static MonoSH operator *(float scalar, MonoSH monoSH)
    {
        return monoSH * scalar;
    }

    // Implement IEquatable<MonoSH>
    public bool Equals(MonoSH other)
    {
        return sh0 == other.sh0 &&
               sh1 == other.sh1 &&
               sh2 == other.sh2 &&
               sh3 == other.sh3 &&
               sh4 == other.sh4 &&
               sh5 == other.sh5 &&
               sh6 == other.sh6 &&
               sh7 == other.sh7 &&
               sh8 == other.sh8;
    }

    public override bool Equals(object obj)
    {
        if (obj is MonoSH)
        {
            return Equals((MonoSH)obj);
        }
        return false;
    }

    public override int GetHashCode()
    {
        // Combine hash codes of all fields
        int hash = sh0.GetHashCode();
        hash = (hash * 397) ^ sh1.GetHashCode();
        hash = (hash * 397) ^ sh2.GetHashCode();
        hash = (hash * 397) ^ sh3.GetHashCode();
        hash = (hash * 397) ^ sh4.GetHashCode();
        hash = (hash * 397) ^ sh5.GetHashCode();
        hash = (hash * 397) ^ sh6.GetHashCode();
        hash = (hash * 397) ^ sh7.GetHashCode();
        hash = (hash * 397) ^ sh8.GetHashCode();
        return hash;
    }
    // Method to format the MonoSH data for easy debugging
    public override string ToString()
    {
        return $"MonoSH: L0 = {sh0}, " +
               $"L1 = [{sh1}, {sh2}, {sh3}], " +
               $"L2 = [{sh4}, {sh5}, {sh6}, {sh7}, {sh8}]";
    }

    // Override == and != operators
    public static bool operator ==(MonoSH left, MonoSH right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(MonoSH left, MonoSH right)
    {
        return !(left == right);
    }

    // Static methods
    public static MonoSH White()
    {
        return new MonoSH(1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f);
    }

    public static MonoSH MonochromaticSHFromL2(SphericalHarmonicsL2 sh)
    {
        float[] lw = { 0.299f, 0.587f, 0.114f }; // Luminance weights for R, G, B.

        float[] shMonoCoefficients = new float[9];
        for (int i = 0; i < 9; i++)
        {
            shMonoCoefficients[i] =
                lw[0] * sh[0, i] +
                lw[1] * sh[1, i] +
                lw[2] * sh[2, i];
        }

        return new MonoSH(
            shMonoCoefficients[0],
            shMonoCoefficients[1],
            shMonoCoefficients[2],
            shMonoCoefficients[3],
            shMonoCoefficients[4],
            shMonoCoefficients[5],
            shMonoCoefficients[6],
            shMonoCoefficients[7],
            shMonoCoefficients[8]
        );
    }

    public static void BilinearInterpolateMonoSHL2(
        MonoSH monoSH1,
        MonoSH monoSH2,
        float t,
        ref MonoSH monoSHOut)
    {
        monoSHOut.sh0 = Mathf.Lerp(monoSH1.sh0, monoSH2.sh0, t);
        monoSHOut.sh1 = Mathf.Lerp(monoSH1.sh1, monoSH2.sh1, t);
        monoSHOut.sh2 = Mathf.Lerp(monoSH1.sh2, monoSH2.sh2, t);
        monoSHOut.sh3 = Mathf.Lerp(monoSH1.sh3, monoSH2.sh3, t);
        monoSHOut.sh4 = Mathf.Lerp(monoSH1.sh4, monoSH2.sh4, t);
        monoSHOut.sh5 = Mathf.Lerp(monoSH1.sh5, monoSH2.sh5, t);
        monoSHOut.sh6 = Mathf.Lerp(monoSH1.sh6, monoSH2.sh6, t);
        monoSHOut.sh7 = Mathf.Lerp(monoSH1.sh7, monoSH2.sh7, t);
        monoSHOut.sh8 = Mathf.Lerp(monoSH1.sh8, monoSH2.sh8, t);
    }

    public static MonoSH Interpolate(MonoSH valueA, MonoSH valueB, MonoSH valueC, MonoSH valueD, Vector4 weights, MonoSH result)
    {
        result.sh0 = valueA.sh0 * weights.x + valueB.sh0 * weights.y + valueC.sh0 * weights.z + valueD.sh0 * weights.w;
        result.sh1 = valueA.sh1 * weights.x + valueB.sh1 * weights.y + valueC.sh1 * weights.z + valueD.sh1 * weights.w;
        result.sh2 = valueA.sh2 * weights.x + valueB.sh2 * weights.y + valueC.sh2 * weights.z + valueD.sh2 * weights.w;
        result.sh3 = valueA.sh3 * weights.x + valueB.sh3 * weights.y + valueC.sh3 * weights.z + valueD.sh3 * weights.w;
        result.sh4 = valueA.sh4 * weights.x + valueB.sh4 * weights.y + valueC.sh4 * weights.z + valueD.sh4 * weights.w;
        result.sh5 = valueA.sh5 * weights.x + valueB.sh5 * weights.y + valueC.sh5 * weights.z + valueD.sh5 * weights.w;
        result.sh6 = valueA.sh6 * weights.x + valueB.sh6 * weights.y + valueC.sh6 * weights.z + valueD.sh6 * weights.w;
        result.sh7 = valueA.sh7 * weights.x + valueB.sh7 * weights.y + valueC.sh7 * weights.z + valueD.sh7 * weights.w;
        result.sh8 = valueA.sh8 * weights.x + valueB.sh8 * weights.y + valueC.sh8 * weights.z + valueD.sh8 * weights.w;
        return result;
    }

    public static MonoSH SetCoefficientsFromArray(float[] coefficients)
    {
        if (coefficients == null || coefficients.Length != 9)
        {
            throw new ArgumentException("Coefficients array must have exactly 9 elements.");
        }

        return new MonoSH(
            coefficients[0],
            coefficients[1],
            coefficients[2],
            coefficients[3],
            coefficients[4],
            coefficients[5],
            coefficients[6],
            coefficients[7],
            coefficients[8]
        );
    }

    //  get the coefficients as an array
    public float[] ToArray()
    {
        return new float[] { sh0, sh1, sh2, sh3, sh4, sh5, sh6, sh7, sh8 };
    }
    //No garbage
    public float[] ToArray(float[] targetArray)
    {
        //Making unsafe to remove extra check
        // if (targetArray == null || targetArray.Length != 9)
        // {
        //     throw new ArgumentException("Target array must be of length 9.");
        // }

        targetArray[0] = sh0;
        targetArray[1] = sh1;
        targetArray[2] = sh2;
        targetArray[3] = sh3;
        targetArray[4] = sh4;
        targetArray[5] = sh5;
        targetArray[6] = sh6;
        targetArray[7] = sh7;
        targetArray[8] = sh8;

        return targetArray;
    }
}
