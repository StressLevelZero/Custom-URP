// Assets/Editor/SHDeringBakeHook.cs
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SLZ.SLZEditorTools;

[InitializeOnLoad]
internal static class SHDeringBakeHook
{
    // ---- Persistent keys (survive domain reload within a session) ----
    // Only span the bakeStarted -> post-bake window. The poll/pending state
    // that used to live here now belongs to SortedPostBakeEvent.
    const string K_Armed          = "SHDeringBakeHook.Armed";
    const string K_AnyDeringWasOn = "SHDeringBakeHook.AnyDeringWasOn";
    const string K_PreBakeHash    = "SHDeringBakeHook.PreBakeHash";

    // Instance-id dictionary of dering flags we flipped. Rebuilt fresh each bake.
    // If lost to a domain reload, RestoreDeringFlags silently no-ops — the on-disk
    // scene state already reflects the flip, so there's nothing to undo without
    // the snapshot anyway.
    static readonly Dictionary<int, bool> s_prevDeringByInstanceId = new();

    // ---- SessionState-backed flags ----
    static bool Armed          { get => SessionState.GetBool(K_Armed, false);          set => SessionState.SetBool(K_Armed, value); }
    static bool AnyDeringWasOn { get => SessionState.GetBool(K_AnyDeringWasOn, false); set => SessionState.SetBool(K_AnyDeringWasOn, value); }
    static int  PreBakeHash    { get => SessionState.GetInt (K_PreBakeHash, 0);        set => SessionState.SetInt (K_PreBakeHash, value); }

    static SHDeringBakeHook()
    {
        Lightmapping.bakeStarted   += OnBakeStarted;
#if UNITY_2023_1_OR_NEWER
        Lightmapping.bakeCancelled += OnBakeCancelled;
#endif

        // SortedPostBakeEvent gates on lighting-data stability and dispatches
        // handlers in ascending order. Our custom dering runs synchronously
        // here — downstream consumers register at PostBakeOrder values
        // greater than SHDeringHook to see the de-ringed probe data.
        SortedPostBakeEvent.Unregister(OnPostBake, PostBakeOrder.SHDering);
        SortedPostBakeEvent.Register  (OnPostBake, PostBakeOrder.SHDering);
    }
    static void OnBakeStarted()
    {
        if (Armed) return; // re-entry guard

        s_prevDeringByInstanceId.Clear();

        bool any = false;
        foreach (var lpg in FindAllSceneLightProbeGroups())
        {
            if (lpg == null) continue;
            // TODO: global override toggle to dering even without the checkbox.
            // if (!lpg.dering) continue;

            any = true;
            s_prevDeringByInstanceId[lpg.GetInstanceID()] = true;
            lpg.dering = false; // bypass Unity's default dering
        }

        AnyDeringWasOn = any;
        Armed          = any;
        PreBakeHash    = any ? ComputeBakedProbesHash() : 0;
    }

    static void OnBakeCancelled()
    {
        if (!Armed) return;

        RestoreDeringFlags();
        Armed          = false;
        AnyDeringWasOn = false;
    }

    static void OnPostBake()
    {
        if (!Armed) return;

        RestoreDeringFlags();

        bool probesActuallyChanged =
            AnyDeringWasOn &&
            ComputeBakedProbesHash() != PreBakeHash;

        Armed          = false;
        AnyDeringWasOn = false;

        // No-op bakes (probes unchanged) skip the dering pass but still let
        // later SortedPostBakeEvent handlers run — Invoke iterates the rest
        // of the dictionary regardless of what we do here.
        if (probesActuallyChanged)
            SHDeringMenu.RunCustomDering_OnlyTickedLPGs_ActiveScene();

        // Exceptions propagate to SortedPostBakeEvent.Invoke, which logs
        // per-handler and continues with the next order key.
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