#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class SHDeringMenu
{
    private const string MenuRoot = "Stress Level Zero/Lighting Tools/SH Dering/";

    private const int   DefaultSamples = 128;
    private const float DefaultMinAllowed = -0.01f;
    private const int   DefaultBinaryIters = 12;
    private const bool  DefaultReduceL1IfStillNegative = true;

    // Matching tolerance (meters). If your probes are on neat grids, 1e-3 is usually fine.
    private const float MatchEpsilon = 0.2f;
    private const float SpatialCell  = 1e-2f; // spatial hash cell size (meters)

    [MenuItem(MenuRoot + "Run Custom Dering (ALL Baked Probes)")]
    public static void RunCustomDering_All()
    {
        ApplyToIndices(null);
    }

    [MenuItem(MenuRoot + "Run Custom Dering (Only LPGs with Remove Ringing Ticked - Active Scene)")]
    public static void RunCustomDering_OnlyTickedLPGs_ActiveScene()
    {
       // var active = SceneManager.GetActiveScene();
        var lp = LightmapSettings.lightProbes;
        if (lp == null || lp.bakedProbes == null || lp.bakedProbes.Length == 0)
        {
            EditorUtility.DisplayDialog("SH Dering", "No baked probes found. Bake lighting first.", "OK");
            return;
        }

        var groups = Object.FindObjectsByType<LightProbeGroup>(FindObjectsSortMode.None);
        var ticked = new List<LightProbeGroup>();
        foreach (var g in groups)
        {
            if (g != null /* && g.gameObject.scene == active*/ && g.dering) // dering is the LPG checkbox 
                ticked.Add(g);
        }

        if (ticked.Count == 0)
        {
            EditorUtility.DisplayDialog("SH Dering", "No LightProbeGroups in the active scene have Remove Ringing enabled.", "OK");
            return;
        }

        var indices = CollectBakedProbeIndicesForGroups(lp, ticked, SpatialCell, MatchEpsilon);
        if (indices.Count == 0)
        {
            EditorUtility.DisplayDialog("SH Dering",
                "Could not match any LPG probe positions to baked probe positions.\n" +
                "If you have multiple scenes loaded additively or unusual probe workflows, increase MatchEpsilon/SpatialCell.",
                "OK");
            return;
        }

        ApplyToIndices(indices);
    }

    /// <summary>
    /// Applies SHDering to either all probes (indices == null) or a subset.
    /// </summary>
    public static void ApplyToIndices(HashSet<int> indices)
    {
        var activeScene = SceneManager.GetActiveScene();
        var lp = LightmapSettings.lightProbes;
        if (lp == null || lp.bakedProbes == null || lp.bakedProbes.Length == 0)
            return;

        Undo.RecordObject(lp, "SH Dering Light Probes");

        var baked = lp.bakedProbes;
        var outProbes = (SphericalHarmonicsL2[])baked.Clone();

        int processed = 0;
        for (int i = 0; i < outProbes.Length; i++)
        {
            if (indices != null && !indices.Contains(i))
                continue;

            if ((processed % 64) == 0)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                        "SH Dering",
                        $"Processing {processed + 1} probes...",
                        outProbes.Length > 0 ? (float)i / outProbes.Length : 1f))
                    break;
            }

            outProbes[i] = SHDering.DeringScaleL2Only(
                outProbes[i],
                samples: DefaultSamples,
                minAllowed: DefaultMinAllowed,
                binarySearchIters: DefaultBinaryIters,
                reduceL1IfStillNegative: DefaultReduceL1IfStillNegative);

            processed++;
        }

        EditorUtility.ClearProgressBar();

        lp.bakedProbes = outProbes;

        EditorUtility.SetDirty(lp);
        EditorSceneManager.MarkSceneDirty(activeScene);

        Debug.Log(indices == null
            ? $"[SH Dering] Applied to ALL baked probes ({processed})."
            : $"[SH Dering] Applied to subset of baked probes ({processed}).");
    }

    // ---- matching helper ----

    static HashSet<int> CollectBakedProbeIndicesForGroups(
        LightProbes lp,
        List<LightProbeGroup> groups,
        float cellSize,
        float epsilon)
    {
        // Baked probe positions (world space) 
        Vector3[] bakedPos;
        // Prefer GetPositionsSelf (Unity 6), fallback to deprecated .positions if needed.
        bakedPos = lp.positions;
        //bakedPos = lp.GetPositionsSelf();

        var grid = new Dictionary<Vector3Int, List<int>>(bakedPos.Length);
        Vector3Int Key(Vector3 p)
        {
            return new Vector3Int(
                Mathf.RoundToInt(p.x / cellSize),
                Mathf.RoundToInt(p.y / cellSize),
                Mathf.RoundToInt(p.z / cellSize));
        }

        for (int i = 0; i < bakedPos.Length; i++)
        {
            var k = Key(bakedPos[i]);
            if (!grid.TryGetValue(k, out var list))
            {
                list = new List<int>(1);
                grid.Add(k, list);
            }
            list.Add(i);
        }

        float eps2 = epsilon * epsilon;
        var result = new HashSet<int>();

        foreach (var g in groups)
        {
            // LPG probePositions are local-space and Editor-only 
            var local = g.probePositions;
            var t = g.transform;

            for (int p = 0; p < local.Length; p++)
            {
                Vector3 wp = t.TransformPoint(local[p]);
                var baseKey = Key(wp);

                // Search this cell + neighbors (3x3x3) to be tolerant.
                int bestIdx = -1;
                float bestD2 = float.PositiveInfinity;

                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var k = new Vector3Int(baseKey.x + dx, baseKey.y + dy, baseKey.z + dz);
                    if (!grid.TryGetValue(k, out var candidates))
                        continue;

                    for (int c = 0; c < candidates.Count; c++)
                    {
                        int idx = candidates[c];
                        float d2 = (bakedPos[idx] - wp).sqrMagnitude;
                        if (d2 < bestD2)
                        {
                            bestD2 = d2;
                            bestIdx = idx;
                        }
                    }
                }

                if (bestIdx >= 0 && bestD2 <= eps2)
                    result.Add(bestIdx);
            }
        }

        return result;
    }
}
#endif
