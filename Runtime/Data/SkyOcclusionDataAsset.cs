using System.Collections.Generic;
using UnityEngine;
/// <summary>
/// Saved scene's sky occlusion settings
/// </summary>
/// 
[System.Serializable]
[PreferBinarySerialization]
public class SkyOcclusionDataAsset : ScriptableObject
{
    public SkyOcclusionData skyOcclusionData;
    
    
    //Baking into a single super dataset for now. May switch back to an array of data later.
    // Method to combine unique SkyOcclusionData within the asset
    // public void CombineSkyOcclusionData()
    // {
    //     // Create a dictionary to track unique SkyOcclusionData
    //     Dictionary<SkyOcclusionData, List<SkyOcclusionData>> dataMap = new Dictionary<SkyOcclusionData, List<SkyOcclusionData>>();
    //     List<SkyOcclusionData> combinedData = new List<SkyOcclusionData>();
    //
    //     foreach (SkyOcclusionData data in skyOcclusionData)
    //     {
    //         if (!dataMap.ContainsKey(data))
    //         {
    //             // Add the unique SkyOcclusionData to the dictionary
    //             dataMap[data] = new List<SkyOcclusionData> { data };
    //             combinedData.Add(data); // Add to the result list
    //         }
    //     }
    //
    //     // Update the skyOcclusionData array with the combined unique data
    //     skyOcclusionData = combinedData.ToArray();
    // }
    
    
    // Static method to combine arrays of SkyOcclusionDataAsset
    // public static SkyOcclusionDataAsset[] CombineSkyOcclusionDataAssets(List<SkyOcclusionDataAsset> assetArray)
    // {
    //     // Dictionary to track unique SkyOcclusionDataAsset references
    //     Dictionary<SkyOcclusionDataAsset, List<SkyOcclusionDataAsset>> assetMap = new Dictionary<SkyOcclusionDataAsset, List<SkyOcclusionDataAsset>>();
    //     List<SkyOcclusionDataAsset> combinedAssets = new List<SkyOcclusionDataAsset>();
    //
    //     foreach (SkyOcclusionDataAsset asset in assetArray)
    //     {
    //         if (!assetMap.ContainsKey(asset))
    //         {
    //             // Add unique asset to the dictionary
    //             assetMap[asset] = new List<SkyOcclusionDataAsset> { asset };
    //             combinedAssets.Add(asset); // Add unique asset to the combined list
    //         }
    //         else
    //         {
    //             // Handle duplicate asset case here, if needed
    //             Debug.Log($"Duplicate SkyOcclusionDataAsset found: {asset.name}");
    //         }
    //     }
    //
    //     // Return the combined unique assets as an array
    //     return combinedAssets.ToArray();
    // }
}

