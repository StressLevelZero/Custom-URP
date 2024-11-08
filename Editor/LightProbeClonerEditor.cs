using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class LightProbeClonerEditor : Editor
{
    // Add a menu item to clone light probe positions and create a new GameObject with SkyOcclusionProbes
    [MenuItem("Tools/Clone Light Probe Positions and Create Occlusion Probes Object")]
    public static void CloneLightProbePositionsAndCreateObject()
    {
        // Find all LightProbeGroup objects in the scene
        LightProbeGroup[] lightProbeGroups = Object.FindObjectsOfType<LightProbeGroup>();

        // Create a list to store all light probe positions
        List<Vector3> lightProbePositions = new List<Vector3>();

        // Loop through each LightProbeGroup and collect the probe positions
        foreach (LightProbeGroup group in lightProbeGroups)
        {
            lightProbePositions.AddRange(group.probePositions);
        }

        // Convert the list to an array
        Vector3[] clonedPositions = lightProbePositions.ToArray();

        // Create a new GameObject and add SkyOcclusionProbes component
        GameObject newObject = new GameObject("Sky Occlusion Probes Object");
        SkyOcclusionProbes skyOcclusionProbes = newObject.AddComponent<SkyOcclusionProbes>();

        // Assign the cloned light probe positions to the probePositions field
        skyOcclusionProbes.probePositions = clonedPositions;

        // Optionally, select the new GameObject in the hierarchy
        Selection.activeObject = newObject;

        Debug.Log($"Created new GameObject with {clonedPositions.Length} light probe positions.");
    }
}