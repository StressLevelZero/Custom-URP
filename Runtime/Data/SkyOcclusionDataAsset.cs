using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Saved scene's sky occlusion settings
/// </summary>
[System.Serializable]
public class SkyOcclusionDataAsset : ScriptableObject
{
    public SkyOcclusionData[] skyOcclusionData;
    
    // Method to combine unique SkyOcclusionData within the asset
    public void CombineSkyOcclusionData()
    {
        // Create a dictionary to track unique SkyOcclusionData
        Dictionary<SkyOcclusionData, List<SkyOcclusionData>> dataMap = new Dictionary<SkyOcclusionData, List<SkyOcclusionData>>();
        List<SkyOcclusionData> combinedData = new List<SkyOcclusionData>();

        foreach (SkyOcclusionData data in skyOcclusionData)
        {
            if (!dataMap.ContainsKey(data))
            {
                // Add the unique SkyOcclusionData to the dictionary
                dataMap[data] = new List<SkyOcclusionData> { data };
                combinedData.Add(data); // Add to the result list
            }
        }

        // Update the skyOcclusionData array with the combined unique data
        skyOcclusionData = combinedData.ToArray();
    }
    
    
    // Static method to combine arrays of SkyOcclusionDataAsset
    public static SkyOcclusionDataAsset[] CombineSkyOcclusionDataAssets(List<SkyOcclusionDataAsset> assetArray)
    {
        // Dictionary to track unique SkyOcclusionDataAsset references
        Dictionary<SkyOcclusionDataAsset, List<SkyOcclusionDataAsset>> assetMap = new Dictionary<SkyOcclusionDataAsset, List<SkyOcclusionDataAsset>>();
        List<SkyOcclusionDataAsset> combinedAssets = new List<SkyOcclusionDataAsset>();

        foreach (SkyOcclusionDataAsset asset in assetArray)
        {
            if (!assetMap.ContainsKey(asset))
            {
                // Add unique asset to the dictionary
                assetMap[asset] = new List<SkyOcclusionDataAsset> { asset };
                combinedAssets.Add(asset); // Add unique asset to the combined list
            }
            else
            {
                // Handle duplicate asset case here, if needed
                Debug.Log($"Duplicate SkyOcclusionDataAsset found: {asset.name}");
            }
        }

        // Return the combined unique assets as an array
        return combinedAssets.ToArray();
    }
}

[System.Serializable]
public class SkyOcclusionData 
{
    public Vector3[] skyOccPos;
    public MonoSH[] SkySH;

    public Vector3 min;
    public Vector3 max;


    public static SkyOcclusionData CombineSkyOcclusionData(SkyOcclusionData[] skyOcclusionDataArray)
    {
        SkyOcclusionData newData = new SkyOcclusionData();

        // Initialize lists to combine the arrays
        List<Vector3> combinedSkyOccPos = new List<Vector3>();
        List<MonoSH> combinedSkySH = new List<MonoSH>();

        foreach (SkyOcclusionData data in skyOcclusionDataArray)
        {
            combinedSkyOccPos.AddRange(data.skyOccPos); // Add the arrays to the lists
            combinedSkySH.AddRange(data.SkySH);

            // Combine the min and max vectors
            newData.min = Vector3.Min(newData.min, data.min);
            newData.max = Vector3.Max(newData.max, data.max);
        }

        // Assign the combined lists back to the arrays
        newData.skyOccPos = combinedSkyOccPos.ToArray();
        newData.SkySH = combinedSkySH.ToArray();

        return newData;
    }
    
    public static SkyOcclusionData CombineSkyOcclusionData(List<SkyOcclusionData> skyOcclusionDataArray)
    {
        SkyOcclusionData newData = new SkyOcclusionData();

        // Initialize lists to combine the arrays
        List<Vector3> combinedSkyOccPos = new List<Vector3>();
        List<MonoSH> combinedSkySH = new List<MonoSH>();

        foreach (SkyOcclusionData data in skyOcclusionDataArray)
        {
            combinedSkyOccPos.AddRange(data.skyOccPos); // Add the arrays to the lists
            combinedSkySH.AddRange(data.SkySH);

            // Combine the min and max vectors
            newData.min = Vector3.Min(newData.min, data.min);
            newData.max = Vector3.Max(newData.max, data.max);
        }

        // Assign the combined lists back to the arrays
        newData.skyOccPos = combinedSkyOccPos.ToArray();
        newData.SkySH = combinedSkySH.ToArray();

        return newData;
    }

    public static SkyOcclusionData[] CheckForDupes(SkyOcclusionData[] skyOcclusionDataArray)
    {
        // Create a dictionary to group SkyOcclusionData by reference
        Dictionary<SkyOcclusionData, List<SkyOcclusionData>> dataMap = new Dictionary<SkyOcclusionData, List<SkyOcclusionData>>();
        List<SkyOcclusionData> combinedData = new List<SkyOcclusionData>();

        foreach (SkyOcclusionData data in skyOcclusionDataArray)
        {
            if (!dataMap.ContainsKey(data))
            {
                // Add the data to the dictionary as a new unique entry
                dataMap[data] = new List<SkyOcclusionData> { data };
                combinedData.Add(data); // Add the unique data to the result list
            }
        }
        // Return the unique combined data as an array
        return combinedData.ToArray();    
    }
    
    

}
