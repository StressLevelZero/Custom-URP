// Assets/Editor/SHDeringBakeHook.cs
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class SHDeringBakeHook
{
    // ---- Persistent keys (survive domain reload within a session) ----
    const string K_Armed           = "SHDeringBakeHook.Armed";
    const string K_AnyDeringWasOn  = "SHDeringBakeHook.AnyDeringWasOn";
    const string K_PendingRun      = "SHDeringBakeHook.PendingRun";
    const string K_PreBakeHash     = "SHDeringBakeHook.PreBakeHash";

    // Instance-id dictionary of dering flags we flipped. Rebuilt fresh each bake.
    // If lost to a domain reload, RestoreDeringFlags silently no-ops — the on-disk
    // scene state already reflects the flip, so there's nothing to undo without
    // the snapshot anyway.
    static readonly Dictionary<int, bool> s_prevDeringByInstanceId = new();

    // Poll state — only alive between bake end and stability.
    static int  s_stableFrames;
    static bool s_pollSubscribed;

    static LightProbes s_lastLP;
    static int s_lastCountSelf;
    static int s_lastBakedLen;
    static int s_lastPosLen;

    const int RequiredStableFrames = 6;

    // ---- Explicit post-dering chain (replaces bakeCompleted for dependents) ----
    public static event Action PostDeringCallback;

    // ---- SessionState-backed flags ----
    static bool Armed          { get => SessionState.GetBool(K_Armed, false);         set => SessionState.SetBool(K_Armed, value); }
    static bool AnyDeringWasOn { get => SessionState.GetBool(K_AnyDeringWasOn, false); set => SessionState.SetBool(K_AnyDeringWasOn, value); }
    static bool PendingRun     { get => SessionState.GetBool(K_PendingRun, false);     set => SessionState.SetBool(K_PendingRun, value); }
    static int  PreBakeHash    { get => SessionState.GetInt(K_PreBakeHash, 0);         set => SessionState.SetInt(K_PreBakeHash, value); }

    static SHDeringBakeHook()
    {
        Lightmapping.bakeStarted   += OnBakeStarted;
        //Lightmapping.bakeCancelled += OnBakeCancelled;
        Lightmapping.bakeCompleted += OnBakeCompleted;

        // If a domain reload happened mid-pending-run, resume the poll.
        if (PendingRun) SubscribePoll();
    }

    static void OnBakeStarted()
    {
        if (Armed) return; // re-entry guard

        s_prevDeringByInstanceId.Clear();

        bool any = false;
        foreach (var lpg in FindAllSceneLightProbeGroups())
        {
            if (lpg == null) continue;
           // if (!lpg.dering) continue; //Todo: Add some type of global override toggle to dering even without the checkbox 

            any = true;
            s_prevDeringByInstanceId[lpg.GetInstanceID()] = true;
            lpg.dering = false; // bypass Unity's default dering
        }

        AnyDeringWasOn = any;
        Armed = any;
        PreBakeHash = any ? ComputeBakedProbesHash() : 0;
    }

    static void OnBakeCancelled()
    {
        if (!Armed) return;

        RestoreDeringFlags();
        Armed = false;
        AnyDeringWasOn = false;
        PendingRun = false;
        UnsubscribePoll();
    }

    static void OnBakeCompleted()
    {
        if (!Armed) return;

        RestoreDeringFlags();

        bool probesActuallyChanged =
            AnyDeringWasOn &&
            ComputeBakedProbesHash() != PreBakeHash;

        Armed = false;
        AnyDeringWasOn = false;

        if (probesActuallyChanged)
            ArmDeferredRun();
    }

    static void ArmDeferredRun()
    {
        PendingRun = true;
        s_stableFrames = 0;

        s_lastLP = null;
        s_lastCountSelf = s_lastBakedLen = s_lastPosLen = -1;

        SubscribePoll();
    }

    static void SubscribePoll()
    {
        if (s_pollSubscribed) return;
        EditorApplication.update += PollForStability;
        s_pollSubscribed = true;
    }

    static void UnsubscribePoll()
    {
        if (!s_pollSubscribed) return;
        EditorApplication.update -= PollForStability;
        s_pollSubscribed = false;
    }

    static void PollForStability()
    {
        if (!PendingRun)
        {
            UnsubscribePoll();
            return;
        }

        if (IsLightingDataStableThisFrame())
        {
            s_stableFrames++;
            if (s_stableFrames >= RequiredStableFrames)
            {
                PendingRun = false;
                s_stableFrames = 0;
                UnsubscribePoll();
                EditorApplication.delayCall += RunCustomDeringAndNotify;
            }
        }
        else
        {
            s_stableFrames = 0;
        }
    }

    static bool IsLightingDataStableThisFrame()
    {
        if (EditorApplication.isCompiling) return false;
        if (EditorApplication.isUpdating)  return false;
        if (Lightmapping.isRunning)        return false;

        var lp = LightmapSettings.lightProbes;
        if (lp == null) return false;

        var baked = lp.bakedProbes;
        if (baked == null || baked.Length == 0) return false;

        int countSelf = lp.count;
        if (countSelf <= 0) return false;

        int bakedLen = baked.Length;
        int posLen;
        try { posLen = lp.positions.Length; }
        catch { return false; }

        if (bakedLen != countSelf) return false;
        if (posLen   != countSelf) return false;

        bool stableNow =
            ReferenceEquals(lp, s_lastLP) &&
            countSelf == s_lastCountSelf &&
            bakedLen  == s_lastBakedLen  &&
            posLen    == s_lastPosLen;

        s_lastLP        = lp;
        s_lastCountSelf = countSelf;
        s_lastBakedLen  = bakedLen;
        s_lastPosLen    = posLen;

        return stableNow;
    }

    static void RunCustomDeringAndNotify()
    {
        try
        {
            SHDeringMenu.RunCustomDering_OnlyTickedLPGs_ActiveScene();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            var cb = PostDeringCallback;
            if (cb != null)
            {
                foreach (var d in cb.GetInvocationList())
                {
                    try { ((Action)d)(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
        }
    }

    static void RestoreDeringFlags()
    {
        if (s_prevDeringByInstanceId.Count == 0) return;

        foreach (var lpg in FindAllSceneLightProbeGroups())
        {
            if (lpg == null) continue;

            int id = lpg.GetInstanceID();
            if (s_prevDeringByInstanceId.TryGetValue(id, out bool wasOn))
                lpg.dering = wasOn;
        }

        s_prevDeringByInstanceId.Clear();
    }

    static int ComputeBakedProbesHash()
    {
        var lp = LightmapSettings.lightProbes;
        if (lp == null) return 0;

        var baked = lp.bakedProbes;
        if (baked == null) return 0;

        unchecked
        {
            int h = 17;
            h = h * 31 + baked.Length;

            int step = Mathf.Max(1, baked.Length / 32);
            for (int i = 0; i < baked.Length; i += step)
            {
                var sh = baked[i];
                h = h * 31 + sh[0, 0].GetHashCode();
                h = h * 31 + sh[1, 2].GetHashCode();
                h = h * 31 + sh[2, 5].GetHashCode();
            }
            return h;
        }
    }

    static IEnumerable<LightProbeGroup> FindAllSceneLightProbeGroups()
    {
#if UNITY_2023_1_OR_NEWER
        return UnityEngine.Object.FindObjectsByType<LightProbeGroup>(FindObjectsSortMode.None);
#else
        return UnityEngine.Object.FindObjectsOfType<LightProbeGroup>();
#endif
    }
}