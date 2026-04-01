// Assets/Editor/SHDeringBakeHook.cs
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
internal static class SHDeringBakeHook
{
    static readonly Dictionary<int, bool> s_prevDeringByInstanceId = new();

    static bool s_armedForThisBake;
    static bool s_anyDeringWasEnabled;
    static bool s_bakeCancelled;
    static bool s_wasRunning;

    // Post-bake deferred apply gate
    static bool s_pendingRun;
    static int  s_stableFrames;

    static LightProbes s_lastLP;
    static int s_lastCountSelf;
    static int s_lastBakedLen;
    static int s_lastPosLen;

    const int RequiredStableFrames = 6; // bump if still flaky

    static SHDeringBakeHook()
    {
        // NOTE: bakeStarted / bakeCompleted only fire when LightingSettings.autoGenerate is false. :contentReference[oaicite:2]{index=2}
        Lightmapping.bakeStarted += OnBakeStarted;
       // Lightmapping.bakeCancelled += OnBakeCancelled;
        Lightmapping.bakeCompleted += OnBakeCompleted;

        EditorApplication.update += Update;
    }

    static void OnBakeCancelled()
    {
        s_bakeCancelled = true;
        s_pendingRun = false;
    }

    static void OnBakeStarted()
    {
        if (s_armedForThisBake)
            return;

        s_bakeCancelled = false;
        s_prevDeringByInstanceId.Clear();
        s_anyDeringWasEnabled = false;

        foreach (var lpg in FindAllSceneLightProbeGroups())
        {
            if (lpg == null) continue;
            if (!lpg.dering) continue;

            s_anyDeringWasEnabled = true;
            s_prevDeringByInstanceId[lpg.GetInstanceID()] = true;

            // Bypass Unity’s default deringing:
            lpg.dering = false;
        }

        s_armedForThisBake = s_anyDeringWasEnabled;
    }

    static void OnBakeCompleted()
    {
        // We still gate the run in Update() because "bake completed" can happen
        // before LightingData/Artifacts import fully settles.
        // (Also: only fires when autoGenerate is false.) :contentReference[oaicite:3]{index=3}
    }

    static void Update()
    {
        bool running = Lightmapping.isRunning;

        // Detect bake end by transition (works even if bakeCompleted doesn’t fire in some flows)
        if (s_wasRunning && !running)
        {
            if (s_armedForThisBake)
            {
                RestoreDeringFlags();

                if (!s_bakeCancelled && s_anyDeringWasEnabled)
                {
                    ArmDeferredRun();
                }

                s_armedForThisBake = false;
                s_anyDeringWasEnabled = false;
                s_bakeCancelled = false;
            }
        }

        // Run only when editor + lighting data are stable
        if (s_pendingRun)
        {
            if (IsLightingDataStableThisFrame())
            {
                s_stableFrames++;
                if (s_stableFrames >= RequiredStableFrames)
                {
                    s_pendingRun = false;
                    s_stableFrames = 0;

                    // DelayCall so we don’t run inside the Update callback itself
                    EditorApplication.delayCall += RunCustomDeringMenuPass;
                }
            }
            else
            {
                s_stableFrames = 0;
            }
        }

        s_wasRunning = running;
    }

    static void ArmDeferredRun()
    {
        s_pendingRun = true;
        s_stableFrames = 0;

        // reset stability tracking
        s_lastLP = null;
        s_lastCountSelf = s_lastBakedLen = s_lastPosLen = -1;
    }

    static bool IsLightingDataStableThisFrame()
    {
        // Avoid running during asset refresh/import/compilation
        if (EditorApplication.isCompiling) return false;
        if (EditorApplication.isUpdating) return false;
        if (Lightmapping.isRunning) return false;

        var lp = LightmapSettings.lightProbes;
        if (lp == null) return false;

        var baked = lp.bakedProbes; // :contentReference[oaicite:4]{index=4}
        if (baked == null || baked.Length == 0) return false;

       // int countSelf = lp.countSelf; // :contentReference[oaicite:5]{index=5}
        int countSelf = lp.count; 
        if (countSelf <= 0) return false;

        // Make sure the data is self-consistent before we touch it
        int bakedLen = baked.Length;
        int posLen;
        try
        {
            //posLen = lp.GetPositionsSelf().Length; // :contentReference[oaicite:6]{index=6}
            posLen = lp.positions.Length; // :contentReference[oaicite:6]{index=6}
        }
        catch
        {
            return false;
        }

        if (bakedLen != countSelf) return false;
        if (posLen != countSelf) return false;

        // Require stability across frames (same object + same sizes)
        bool stableNow =
            ReferenceEquals(lp, s_lastLP) &&
            countSelf == s_lastCountSelf &&
            bakedLen == s_lastBakedLen &&
            posLen == s_lastPosLen;

        s_lastLP = lp;
        s_lastCountSelf = countSelf;
        s_lastBakedLen = bakedLen;
        s_lastPosLen = posLen;

        return stableNow;
    }

    static void RunCustomDeringMenuPass()
    {
        // Same pathway you already use
        SHDeringMenu.RunCustomDering_OnlyTickedLPGs_ActiveScene();
    }

    static void RestoreDeringFlags()
    {
        if (s_prevDeringByInstanceId.Count == 0)
            return;

        foreach (var lpg in FindAllSceneLightProbeGroups())
        {
            if (lpg == null) continue;

            int id = lpg.GetInstanceID();
            if (s_prevDeringByInstanceId.TryGetValue(id, out bool wasOn))
                lpg.dering = wasOn;
        }

        s_prevDeringByInstanceId.Clear();
    }

    static IEnumerable<LightProbeGroup> FindAllSceneLightProbeGroups()
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindObjectsByType<LightProbeGroup>(FindObjectsSortMode.None);
#else
        return Object.FindObjectsOfType<LightProbeGroup>();
#endif
    }
}
