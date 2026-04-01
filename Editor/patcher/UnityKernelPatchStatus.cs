using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public static class UnityKernelDirectLightingPatchStatus
{
    
    private const string SessionKey = "SLZ.KernelPatched";

    // A string that exists ONLY in the patched file.
    private const string PatchSentinel = "const float falloff = 1 / (distance * distance)";

    public static readonly bool IsPatched;

    static UnityKernelDirectLightingPatchStatus()
    {
        // -1 = not yet set this session
        int cached = SessionState.GetInt(SessionKey, -1);
        if (cached != -1)
        {
            IsPatched = cached == 1;
            return;
        }

        bool result = CheckSentinel(); // or CheckViaGit()
        //Debug.Log("Sentinel: " + result);
        SessionState.SetInt(SessionKey, result ? 1 : 0);
        IsPatched = result;
    }

    private static bool CheckSentinel()
    {
        try
        {
            string kernelPath = Path.Combine(
                EditorApplication.applicationContentsPath,
                "Resources", "OpenCL", "kernels", "directLighting.h");

            if (!File.Exists(kernelPath))
                return false;

            // ReadAllText is fine — kernel files are small.
            return File.ReadAllText(kernelPath).Contains(PatchSentinel);
        }
        catch
        {
            return false;
        }
    }
    
    // Call this if you want to force a recheck (e.g. after applying the patch)
    public static void Invalidate() => SessionState.EraseInt(SessionKey);
}