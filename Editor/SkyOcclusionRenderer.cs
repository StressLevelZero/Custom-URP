using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
[InitializeOnLoad]
#endif
public static class SkyOcclusionRenderer
{
    public static RenderTexture CurrentTexture;

    private static RenderTexture _blackCubemap;

    // Processing variables
    private static List<Vector3> combinedProbePositions;
    private static MonoSH[] skySH;
    private static int totalTasks;
    private static int currentTask;
    private static int currentIndex;
    private static Dictionary<SkyOcclusionProbes, (int startIndex, int count)> groupIndices;
    private static SkyOcclusionDataAsset dataAsset; // To store the asset

    public static void RenderSkyOcclusion()
    {
#if UNITY_EDITOR
        // Find currently active probes groups
        SkyOcclusionProbes[] skyProbeGroups = Object.FindObjectsOfType<SkyOcclusionProbes>(false);

        if (skyProbeGroups.Length == 0)
        {
            Debug.LogWarning("No SkyOcclusionProbes found in the scene.");
            return;
        }

        // Combine probe positions from all groups
        combinedProbePositions = CombineProbesFromList(skyProbeGroups);

        if (combinedProbePositions == null || combinedProbePositions.Count == 0)
        {
            Debug.LogWarning("No probe positions to process.");
            return;
        }

        // Initialize processing variables
        totalTasks = combinedProbePositions.Count;
        currentTask = 0;
        skySH = new MonoSH[totalTasks];
        currentIndex = 0;

        // Keep track of indices for each group
        groupIndices = new Dictionary<SkyOcclusionProbes, (int startIndex, int count)>();

        int currentPositionIndex = 0;
        foreach (var group in skyProbeGroups)
        {
            var positions = group.probePositions;
            if (positions == null || positions.Length == 0)
            {
                Debug.LogWarning($"SkyOcclusionProbes '{group.name}' has no probe positions.");
                continue;
            }

            groupIndices[group] = (currentPositionIndex, positions.Length);
            currentPositionIndex += positions.Length;
        }

        // Subscribe to the update event
        EditorApplication.update += OnEditorUpdate;

        // Display the initial progress bar
        EditorUtility.DisplayProgressBar("Rendering Occlusion",
            $"Processing combined probe positions (0/{totalTasks})",
            0f);
#endif
    }

#if UNITY_EDITOR
    private static void OnEditorUpdate()
    {
        // Number of items to process per update to prevent freezing
        int itemsPerUpdate = 10;

        for (int i = 0; i < itemsPerUpdate && currentIndex < totalTasks; i++)
        {
            // Process the current item
            skySH[currentIndex] = RenderMonoSH(combinedProbePositions[currentIndex]);

            // Update progress
            currentTask++;
            currentIndex++;
        }

        // Update the progress bar
        float progress = (float)currentTask / totalTasks;
        EditorUtility.DisplayProgressBar("Rendering Occlusion",
            $"Processing combined probe positions ({currentTask}/{totalTasks})",
            progress);

        // Check if all positions have been processed
        if (currentIndex >= totalTasks)
        {
            // All processing done
            EditorApplication.update -= OnEditorUpdate;
            EditorUtility.ClearProgressBar();

            // Create SkyOcclusionData
            SkyOcclusionData data = CreateSkyOcclusionData();

            // Save the combined data
            SaveCombinedSkyOcclusionData(data);

            // Assign the data asset and index to each SkyOcclusionProbes component
            AssignDataToProbes();

            Debug.Log("Sky occlusion rendering completed for all combined positions.");
        }
    }

    private static SkyOcclusionData CreateSkyOcclusionData()
    {
        SkyOcclusionData data = new SkyOcclusionData
        {
            skyOccPos = combinedProbePositions.ToArray(),
            SkySH = skySH,
        };

        if (data.skyOccPos != null && data.skyOccPos.Length > 0)
        {
            Vector3[] originalPositions = data.skyOccPos;

            // Perform the tetrahedralization
            int[] outIndices;
            Vector3[] outPositions;
            Lightmapping.Tetrahedralize(originalPositions, out outIndices, out outPositions);

            // Store the tetrahedralization results in SkyOcclusionData
            data.tetrahedrons = Tetrahedron.ConvertToTetrahedrons(outIndices);
            data.skyOccPos = outPositions;

            // Check if positions have changed due to duplicate removal
            bool positionsAreSame = ArePositionsEqual(originalPositions, outPositions);

            if (!positionsAreSame)
            {
                // Positions have changed, re-sort SkySH to match outPositions
                data.SkySH = ReSortSkySH(originalPositions, data.SkySH, outPositions);
            }

            data.PrecomputeBarycentricMatrices();
            Tetrahedron.CalculateNeighbors(data.tetrahedrons);

            // Calculate min and max using the updated positions
            Vector3 min = outPositions[0];
            Vector3 max = outPositions[0];
            foreach (var pos in outPositions)
            {
                min = Vector3.Min(min, pos);
                max = Vector3.Max(max, pos);
            }
            data.min = min;
            data.max = max;
        }
        else
        {
            data.min = Vector3.zero;
            data.max = Vector3.zero;
        }

        return data;
    }

    // Helper method to compare two arrays of Vector3 positions
    private static bool ArePositionsEqual(Vector3[] originalPositions, Vector3[] newPositions, float tolerance = 0.0001f)
    {
        if (originalPositions.Length != newPositions.Length)
            return false;

        for (int i = 0; i < originalPositions.Length; i++)
        {
            if (!Vector3ApproximatelyEqual(originalPositions[i], newPositions[i], tolerance))
                return false;
        }
        return true;
    }

    // Helper method to re-sort SkySH array to match outPositions
    private static MonoSH[] ReSortSkySH(Vector3[] originalPositions, MonoSH[] originalSkySH, Vector3[] outPositions)
    {
        // Create a new SkySH array matching the length of outPositions
        var newSkySH = new MonoSH[outPositions.Length];

        // For each position in outPositions, find the corresponding index in originalPositions
        for (int i = 0; i < outPositions.Length; i++)
        {
            Vector3 pos = outPositions[i];
            int originalIndex = FindPositionIndex(originalPositions, pos);

            if (originalIndex != -1)
            {
                newSkySH[i] = originalSkySH[originalIndex];
            }
            else
            {
                // Handle the case where the position is not found (should not occur)
                newSkySH[i] = new MonoSH();
                Debug.LogWarning($"Position {pos} not found in original positions.");
            }
        }

        return newSkySH;
    }

    // Helper method to find the index of a position within an array of positions
    private static int FindPositionIndex(Vector3[] positions, Vector3 targetPosition, float tolerance = 0.0001f)
    {
        for (int i = 0; i < positions.Length; i++)
        {
            if (Vector3ApproximatelyEqual(positions[i], targetPosition, tolerance))
                return i;
        }
        return -1; // Position not found
    }

    // Helper method for approximate Vector3 comparison
    private static bool Vector3ApproximatelyEqual(Vector3 a, Vector3 b, float tolerance = 0.0001f)
    {
        return Mathf.Abs(a.x - b.x) < tolerance &&
               Mathf.Abs(a.y - b.y) < tolerance &&
               Mathf.Abs(a.z - b.z) < tolerance;
    }

    private static void SaveCombinedSkyOcclusionData(SkyOcclusionData combinedData)
    {
        // Create the SkyOcclusionDataAsset
        dataAsset = ScriptableObject.CreateInstance<SkyOcclusionDataAsset>();
        dataAsset.skyOcclusionData = combinedData;

        // Get the active scene
        var activeScene = EditorSceneManager.GetActiveScene();
        if (!activeScene.IsValid())
        {
            Debug.LogError("Active scene is not valid. Please ensure a scene is open and valid.");
            return;
        }

        // Get the scene asset path
        string sceneAssetPath = activeScene.path;

        if (string.IsNullOrEmpty(sceneAssetPath))
        {
            Debug.LogError("Scene is not saved. Please save the scene before proceeding.");
            return;
        }

        // Normalize paths
        sceneAssetPath = sceneAssetPath.Replace("\\", "/");

        // Ensure the scene is inside the Assets folder
        if (!sceneAssetPath.StartsWith("Assets/"))
        {
            Debug.LogError("Scene is not inside the Assets folder. Please ensure the scene is saved within the project's Assets folder.");
            return;
        }

        // Get the folder containing the scene
        string sceneFolder = System.IO.Path.GetDirectoryName(sceneAssetPath);
        string sceneName = System.IO.Path.GetFileNameWithoutExtension(sceneAssetPath);

        // Construct the asset path
        string assetName = $"{sceneName}_SkyOcclusionData.asset";
        string assetPath = System.IO.Path.Combine(sceneFolder, assetName).Replace("\\", "/");

        // Create or overwrite the asset
        AssetDatabase.CreateAsset(dataAsset, assetPath);
        EditorUtility.SetDirty(dataAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Saved combined sky occlusion data to '{assetPath}'.");
    }

    private static void AssignDataToProbes()
    {
        // Assign the data asset and indices to each SkyOcclusionProbes component
        foreach (var kvp in groupIndices)
        {
            var group = kvp.Key;
            var indices = kvp.Value;

            group.skyOcclusionDataAsset = dataAsset;
            group.dataIndex = 0; // Only one SkyOcclusionData in the asset
            // group.positionIndex = indices.startIndex;
            // group.positionCount = indices.count;

            EditorUtility.SetDirty(group);
        }

        // Save changes to the scene
        var activeScene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);
        AssetDatabase.SaveAssets();
    }

    private static List<Vector3> CombineProbesFromList(SkyOcclusionProbes[] skyProbeGroups)
    {
        List<Vector3> combinedPositions = new List<Vector3>();
        foreach (var group in skyProbeGroups)
        {
            var positions = group.probePositions;
            if (positions != null && positions.Length > 0)
            {
                combinedPositions.AddRange(positions);
            }
        }
        return combinedPositions;
    }

    public static MonoSH RenderMonoSH(Vector3 position)
    {
        // Implement your actual ray tracing logic here.
        // The following is a placeholder implementation.

        // Create a temporary camera
        GameObject tempCameraObject = new GameObject("TempStaticGeometryCamera");
        Camera tempCamera = tempCameraObject.AddComponent<Camera>();

        // Store the old fog density and sky texture
        float oldFogDensity = RenderSettings.fogDensity;
        RenderSettings.fogDensity = 100;
        var skytex = Shader.GetGlobalTexture("_SkyTexture");
        SkyManager.SetSkyTexture(GetOrCreateBlackCubemap(1, 1));

        // Set up the camera
        tempCamera.clearFlags = CameraClearFlags.SolidColor;
        tempCamera.backgroundColor = Color.white;
        tempCamera.cullingMask = ~0; // Render everything
        tempCamera.transform.position = position;

        int cubemapSize = 16;

        // Create or reuse the render texture
        if (CurrentTexture == null)
        {
            // Create a cubemap render texture
            CurrentTexture = new RenderTexture(cubemapSize, cubemapSize, 24);
            CurrentTexture.dimension = UnityEngine.Rendering.TextureDimension.Cube;
            CurrentTexture.filterMode = FilterMode.Bilinear;
            CurrentTexture.wrapMode = TextureWrapMode.Clamp;
            CurrentTexture.format = RenderTextureFormat.DefaultHDR;
            CurrentTexture.name = "SkyOcclusion_" + GUID.Generate();
            CurrentTexture.useMipMap = true;
        }

        // Render to cubemap
        tempCamera.RenderToCubemap(CurrentTexture);

        // Restore the fog density and sky texture
        RenderSettings.fogDensity = oldFogDensity;
        if (skytex != null) SkyManager.SetSkyTexture(skytex);

        // Apply scattering and bake to SH
        SkyManager.ApplyScattering(CurrentTexture, .25f, 4);
        SphericalHarmonicsL2 l2 = SkyManager.BakeCubemapToSH(CurrentTexture, 64);

        // Clean up
        Object.DestroyImmediate(tempCameraObject);

        return MonoSH.MonochromaticSHFromL2(l2);
    }

    public static RenderTexture GetOrCreateBlackCubemap(int cubemapSize = 64, int depth = 24)
    {
        // Check if the black cubemap already exists
        if (_blackCubemap == null)
        {
            // Create a new cubemap
            _blackCubemap = new RenderTexture(cubemapSize, cubemapSize, depth);
            _blackCubemap.dimension = UnityEngine.Rendering.TextureDimension.Cube;
            _blackCubemap.filterMode = FilterMode.Bilinear;
            _blackCubemap.wrapMode = TextureWrapMode.Clamp;
            _blackCubemap.useMipMap = false;

            // Create a temporary camera to render black color
            GameObject tempCameraObject = new GameObject("TempCamera");
            Camera tempCamera = tempCameraObject.AddComponent<Camera>();
            tempCamera.clearFlags = CameraClearFlags.SolidColor;
            tempCamera.backgroundColor = Color.black;
            tempCamera.cullingMask = 0; // Don't render any objects, just the clear color

            // Render black to the cubemap
            tempCamera.RenderToCubemap(_blackCubemap);

            // Clean up the temporary camera
            Object.DestroyImmediate(tempCameraObject);
        }

        return _blackCubemap;
    }
#endif
}
