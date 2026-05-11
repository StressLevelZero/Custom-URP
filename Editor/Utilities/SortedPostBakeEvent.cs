using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using SLZ;

namespace SLZ.SLZEditorTools
{
public class SortedPostBakeEvent : StaticSortedEvent<float>
{
    const string K_PendingInvoke = "SortedPostBakeEvent.PendingInvoke";
    static bool PendingInvoke
    {
        get => SessionState.GetBool(K_PendingInvoke, false);
        set => SessionState.SetBool(K_PendingInvoke, value);
    }

    static int s_stableFrames;
    static bool s_pollSubscribed;
    static LightProbes s_lastLP;
    static int s_lastCountSelf, s_lastBakedLen, s_lastPosLen;
    const int RequiredStableFrames = 6;

    [InitializeOnLoadMethod]
    static void RegisterEvents()
    {
        Lightmapping.bakeCompleted += OnBakeCompleted;
#if UNITY_2023_1_OR_NEWER
       // Lightmapping.bakeCancelled   += OnBakeCancelled;
#endif
        EditorSceneManager.sceneClosed += _ => GarbageCollect();

        // Resume if a domain reload landed mid-poll.
        if (PendingInvoke) SubscribePoll();
    }

    static void OnBakeCompleted()
    {
        PendingInvoke = true;
        s_stableFrames = 0;
        s_lastLP = null;
        s_lastCountSelf = s_lastBakedLen = s_lastPosLen = -1;
        SubscribePoll();
    }

    static void OnBakeCancelled()
    {
        PendingInvoke = false;
        UnsubscribePoll();
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
        if (!PendingInvoke) { UnsubscribePoll(); return; }

        if (IsLightingDataStableThisFrame())
        {
            if (++s_stableFrames >= RequiredStableFrames)
            {
                PendingInvoke = false;
                s_stableFrames = 0;
                UnsubscribePoll();
                EditorApplication.delayCall += Invoke;  // run all sorted handlers
            }
        }
        else
        {
            s_stableFrames = 0;
        }
    }

    static bool IsLightingDataStableThisFrame()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return false;
        if (Lightmapping.isRunning) return false;

        var lp = LightmapSettings.lightProbes;
        if (lp == null) return false;

        var baked = lp.bakedProbes;
        if (baked == null || baked.Length == 0) return false;

        int countSelf = lp.count;
        if (countSelf <= 0) return false;

        int bakedLen = baked.Length;
        int posLen;
        try { posLen = lp.positions.Length; } catch { return false; }

        if (bakedLen != countSelf || posLen != countSelf) return false;

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
}
}