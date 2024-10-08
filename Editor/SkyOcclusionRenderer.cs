using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
//using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine.Experimental.Rendering;

#endif

public static class SkyOcclusionRenderer 
{
    
    public static RenderTexture CurrentTexture;

    private static RenderTexture _blackCubemap;
    
     // Class to hold processing state for each group
     // Class to hold processing state for each group
     private class ProcessingGroup
     {
         public SkyOcclusionProbes probesGroup;
         public int totalTasks;
         public int currentTask;
         public MonoSH[] skySH;
         public int currentIndex;

         public SkyOcclusionData ToSkyOcclusionData()
         {
             SkyOcclusionData data = new SkyOcclusionData
             {
                 skyOccPos = probesGroup.probePositions,
                 SkySH = skySH
             };

             // Calculate min and max
             if (data.skyOccPos != null && data.skyOccPos.Length > 0)
             {
                 Vector3 min = data.skyOccPos[0];
                 Vector3 max = data.skyOccPos[0];
                 foreach (var pos in data.skyOccPos)
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
     }


    private static List<ProcessingGroup> groupsToProcess;
    private static ProcessingGroup currentProcessingGroup;
    private static List<SkyOcclusionData> processedData;

    public static void RenderSkyOcclusion()
    {
        // Find currently active probes groups
        SkyOcclusionProbes[] skyProbeGroups = Object.FindObjectsOfType<SkyOcclusionProbes>(false);

        if (skyProbeGroups.Length == 0)
        {
            Debug.LogWarning("No SkyOcclusionProbes found in the scene.");
            return;
        }

        groupsToProcess = new List<ProcessingGroup>();
        processedData = new List<SkyOcclusionData>();

        // Initialize processing groups
        foreach (var probesGroup in skyProbeGroups)
        {
            var positions = probesGroup.probePositions;
            if (positions == null || positions.Length == 0)
            {
                Debug.LogWarning($"SkyOcclusionProbes '{probesGroup.name}' has no probe positions.");
                continue;
            }

            var processingGroup = new ProcessingGroup
            {
                probesGroup = probesGroup,
                totalTasks = positions.Length,
                currentTask = 0,
                currentIndex = 0,
                skySH = new MonoSH[positions.Length],
            };

            groupsToProcess.Add(processingGroup);
        }

        if (groupsToProcess.Count == 0)
        {
            Debug.LogWarning("No valid SkyOcclusionProbes groups to process.");
            return;
        }

        // Start processing the first group
        currentProcessingGroup = groupsToProcess[0];
        groupsToProcess.RemoveAt(0);

        // Subscribe to the update event
        EditorApplication.update += OnEditorUpdate;

        // Display the initial progress bar
        EditorUtility.DisplayProgressBar("Rendering Occlusion",
            $"Processing group '{currentProcessingGroup.probesGroup.name}' (0/{currentProcessingGroup.totalTasks})",
            0f);
    }

    private static void OnEditorUpdate()
    {
        if (currentProcessingGroup == null)
        {
            // All groups have been processed
            EditorApplication.update -= OnEditorUpdate;
            EditorUtility.ClearProgressBar();

            // Save combined data asset
            SaveCombinedSkyOcclusionData();

            Debug.Log("Sky occlusion rendering completed for all groups.");
            return;
        }

        // Number of items to process per update to prevent freezing
        int itemsPerUpdate = 10; // Adjust as needed

        var probesGroup = currentProcessingGroup.probesGroup;
        var positions = probesGroup.probePositions;

        for (int i = 0; i < itemsPerUpdate && currentProcessingGroup.currentIndex < currentProcessingGroup.totalTasks; i++)
        {
            // Process the current item
            currentProcessingGroup.skySH[currentProcessingGroup.currentIndex] =
                RenderMonoSH(positions[currentProcessingGroup.currentIndex]);

            // Update progress
            currentProcessingGroup.currentTask++;
            currentProcessingGroup.currentIndex++;
        }

        // Update the progress bar
        float progress = (float)currentProcessingGroup.currentTask / currentProcessingGroup.totalTasks;
        EditorUtility.DisplayProgressBar("Rendering Occlusion",
            $"Processing group '{probesGroup.name}' ({currentProcessingGroup.currentTask}/{currentProcessingGroup.totalTasks})",
            progress);

        // Check if the current group has been fully processed
        if (currentProcessingGroup.currentIndex >= currentProcessingGroup.totalTasks)
        {
            // Collect data
            SkyOcclusionData data = currentProcessingGroup.ToSkyOcclusionData();
            processedData.Add(data);

            // Process the next group if available
            if (groupsToProcess.Count > 0)
            {
                currentProcessingGroup = groupsToProcess[0];
                groupsToProcess.RemoveAt(0);

                // Reset progress bar for the next group
                EditorUtility.DisplayProgressBar("Rendering Occlusion",
                    $"Processing group '{currentProcessingGroup.probesGroup.name}' (0/{currentProcessingGroup.totalTasks})",
                    0f);
            }
            else
            {
                // No more groups to process
                currentProcessingGroup = null;
            }
        }
    }
    
            public static MonoSH RenderMonoSH(Vector3 position)
        {
            //return RaytraceSkyOcclusion(position);

            //TODO: Do this completely differently.
            //We should do an actual raytrace with secondary bounces and directly get the monochromatic coefficients  
            
            
            // Create a temporary camera
            GameObject tempCameraObject = new GameObject("TempStaticGeometryCamera");
            Camera tempCamera = tempCameraObject.AddComponent<Camera>();
            
            //Reducing density because certain skybox shaders render fog contribution which would double up the effect 
            float oldFogDensity = RenderSettings.fogDensity;
            RenderSettings.fogDensity = 100 ;
            var skytex = Shader.GetGlobalTexture("_SkyTexture");
            SkyManager.SetSkyTexture(GetOrCreateBlackCubemap(1,1));
            // Set up the camera
            tempCamera.clearFlags = CameraClearFlags.SolidColor;
            tempCamera.backgroundColor = Color.white;
            tempCamera.cullingMask = ~0; // Render everything for now
            tempCamera.transform.position = position;
            
            int cubemapSize = 16;
            
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
            
            //Setting variables back
            RenderSettings.fogDensity = oldFogDensity;
            if (skytex!=null) SkyManager.SetSkyTexture(skytex);
            
            SkyManager.ApplyScattering(CurrentTexture, .25f, 4 );
            SphericalHarmonicsL2 l2 = SkyManager.BakeCubemapToSH(CurrentTexture, 64);
            
            // Clean up
            Object.DestroyImmediate(tempCameraObject);
            
            return  MonoSH.MonochromaticSHFromL2(l2);
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
                    Camera tempCamera = new GameObject("TempCamera").AddComponent<Camera>();
                    tempCamera.clearFlags = CameraClearFlags.SolidColor;
                    tempCamera.backgroundColor = Color.black;
                    tempCamera.cullingMask = 0; // Don't render any objects, just the clear color

                    // Render black to the cubemap
                    tempCamera.RenderToCubemap(_blackCubemap);

                    // Clean up the temporary camera
                    Object.DestroyImmediate(tempCamera.gameObject);
                }

                return _blackCubemap;
            }
            
            private static void SaveCombinedSkyOcclusionData()
            {
                if (processedData.Count == 0)
                {
                    Debug.LogWarning("No data to save.");
                    return;
                }

                // Create the SkyOcclusionDataAsset
                SkyOcclusionDataAsset dataAsset = ScriptableObject.CreateInstance<SkyOcclusionDataAsset>();
                dataAsset.skyOcclusionData = processedData.ToArray();
                dataAsset.CombineSkyOcclusionData(); //Combining duplicates 

                // Get the active scene
                var activeScene = EditorSceneManager.GetActiveScene();
                if (!activeScene.IsValid())
                {
                    Debug.LogError("Active scene is not valid. Please ensure a scene is open and valid.");
                    return;
                }

                // Get the scene asset path (relative to the Assets folder)
                string sceneAssetPath = activeScene.path; // This is relative to the project folder

                if (string.IsNullOrEmpty(sceneAssetPath))
                {
                    Debug.LogError("Scene is not saved. Please save the scene before proceeding.");
                    return;
                }

                // Normalize paths
                sceneAssetPath = sceneAssetPath.Replace("\\", "/");
                string dataPath = Application.dataPath.Replace("\\", "/");

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

}
