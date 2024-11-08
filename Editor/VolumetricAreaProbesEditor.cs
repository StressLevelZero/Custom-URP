using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class VolumetricAreaProbesEditor : Editor
{
    // Add a menu item to create SkyOcclusionProbes for each BakedVolumetricArea in all open scenes
    [MenuItem("Tools/Create Sky Occlusion Probes for Volumetric Areas")]
    public static void CreateSkyOcclusionProbesForVolumetricAreas()
    {
        // Find all BakedVolumetricArea objects in all open scenes
        BakedVolumetricArea[] volumetricAreas = Object.FindObjectsOfType<BakedVolumetricArea>();

        foreach (BakedVolumetricArea area in volumetricAreas)
        {
            // Create a new GameObject for the SkyOcclusionProbes and attach it to the volumetric area's transform
            GameObject newObject = new GameObject("Sky Occlusion Probes");
            newObject.transform.SetParent(area.transform, false);

            // Add the SkyOcclusionProbes component
            SkyOcclusionProbes skyOcclusionProbes = newObject.AddComponent<SkyOcclusionProbes>();

            // Calculate the normalized scale
            Vector3 normalizedScale = Vector3.Scale(area.transform.localScale, area.BoxScale);

            Vector3 areaPosition = area.transform.position;
            
            // Determine the bounds of the area using the position and normalized scale
            Vector3 minBound = areaPosition - normalizedScale * 0.5f;
            Vector3 maxBound = areaPosition + normalizedScale * 0.5f;

            // Define the distance between probes in the grid (adjust as needed)
            float probeSpacing = 5.0f;

            // Create a list to store the positions of the probes
            List<Vector3> probePositions = new List<Vector3>();

            // Populate the area with probes in a 3D grid
            for (float x = minBound.x; x <= maxBound.x; x += probeSpacing)
            {
                for (float y = minBound.y; y <= maxBound.y; y += probeSpacing)
                {
                    for (float z = minBound.z; z <= maxBound.z; z += probeSpacing)
                    {
                        Vector3 probePosition = new Vector3(x, y, z);
                        probePositions.Add(probePosition - areaPosition);
                    }
                }
            }

            // Assign the generated positions to the SkyOcclusionProbes component
            skyOcclusionProbes.probePositions = probePositions.ToArray();

            Debug.Log($"Created SkyOcclusionProbes with {probePositions.Count} positions for {area.name}.");
        }

        Debug.Log("Finished creating Sky Occlusion Probes for all Volumetric Areas.");
    }
}
