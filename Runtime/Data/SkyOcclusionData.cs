using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class SkyOcclusionData
{
    public Tetrahedron[] tetrahedrons;
    public Vector3[] skyOccPos;
    public MonoSH[] SkySH;

    //Bounding
    public Vector3 min;
    public Vector3 max;


    // public static SkyOcclusionData CombineSkyOcclusionData(SkyOcclusionData[] skyOcclusionDataArray)
    // {
    //     SkyOcclusionData newData = new SkyOcclusionData();
    //
    //     // Initialize lists to combine the arrays
    //     List<Vector3> combinedSkyOccPos = new List<Vector3>();
    //     List<MonoSH> combinedSkySH = new List<MonoSH>();
    //     List<Tetrahedron> combinedTetrahedrons = new List<Tetrahedron>();
    //
    //     foreach (SkyOcclusionData data in skyOcclusionDataArray)
    //     {
    //         combinedSkyOccPos.AddRange(data.skyOccPos); // Add the arrays to the lists
    //         combinedSkySH.AddRange(data.SkySH);
    //         combinedTetrahedrons.AddRange(data.tetrahedrons);
    //
    //         // Combine the min and max vectors
    //         newData.min = Vector3.Min(newData.min, data.min);
    //         newData.max = Vector3.Max(newData.max, data.max);
    //     }
    //
    //     // Assign the combined lists back to the arrays
    //     newData.skyOccPos = combinedSkyOccPos.ToArray();
    //     newData.SkySH = combinedSkySH.ToArray();
    //     newData.tetrahedrons = combinedTetrahedrons.ToArray();
    //     return newData;
    // }
    //
    // public static SkyOcclusionData CombineSkyOcclusionData(List<SkyOcclusionData> skyOcclusionDataArray)
    // {
    //     SkyOcclusionData newData = new SkyOcclusionData();
    //
    //     // Initialize lists to combine the arrays
    //     List<Vector3> combinedSkyOccPos = new List<Vector3>();
    //     List<MonoSH> combinedSkySH = new List<MonoSH>();
    //
    //     foreach (SkyOcclusionData data in skyOcclusionDataArray)
    //     {
    //         combinedSkyOccPos.AddRange(data.skyOccPos); // Add the arrays to the lists
    //         combinedSkySH.AddRange(data.SkySH);
    //
    //         // Combine the min and max vectors
    //         newData.min = Vector3.Min(newData.min, data.min);
    //         newData.max = Vector3.Max(newData.max, data.max);
    //     }
    //
    //     // Assign the combined lists back to the arrays
    //     newData.skyOccPos = combinedSkyOccPos.ToArray();
    //     newData.SkySH = combinedSkySH.ToArray();
    //
    //     return newData;
    // }

    // public static SkyOcclusionData[] CheckForDupes(SkyOcclusionData[] skyOcclusionDataArray)
    // {
    //     // Create a dictionary to group SkyOcclusionData by reference
    //     Dictionary<SkyOcclusionData, List<SkyOcclusionData>> dataMap =
    //         new Dictionary<SkyOcclusionData, List<SkyOcclusionData>>();
    //     List<SkyOcclusionData> combinedData = new List<SkyOcclusionData>();
    //
    //     foreach (SkyOcclusionData data in skyOcclusionDataArray)
    //     {
    //         if (!dataMap.ContainsKey(data))
    //         {
    //             // Add the data to the dictionary as a new unique entry
    //             dataMap[data] = new List<SkyOcclusionData> {data};
    //             combinedData.Add(data); // Add the unique data to the result list
    //         }
    //     }
    //
    //     // Return the unique combined data as an array
    //     return combinedData.ToArray();
    // }

    public void PrecomputeBarycentricMatrices()
    {
        foreach (var tetrahedrons in tetrahedrons)
        {
            tetrahedrons.ComputeBarycentricMatrix(skyOccPos);
        }
    }

    bool IsPointInsideTetrahedron(Vector4 baryCoords)
    {
        return baryCoords.x >= 0 && baryCoords.y >= 0 && baryCoords.z >= 0 && baryCoords.w >= 0;
    }
    
    public static List<Vector3> CombineAndRemoveDuplicatePoints(IEnumerable<List<Vector3>> vectorLists, float threshold)
    {
        // Create a list to store the final result
        List<Vector3> resultList = new List<Vector3>();

        // Iterate through all the lists provided in the collection
        foreach (var list in vectorLists)
        {
            foreach (var currentPoint in list)
            {
                bool isDuplicate = false;

                // Check if the current point is near any point in the result list within the threshold
                for (int j = 0; j < resultList.Count; j++)
                {
                    if (Vector3.Distance(currentPoint, resultList[j]) <= threshold)
                    {
                        // If a duplicate is found, replace the existing point with the average of the two points
                        resultList[j] = (resultList[j] + currentPoint) / 2f;
                        isDuplicate = true;
                        break;
                    }
                }

                // If no duplicate was found, add the current point to the result list
                if (!isDuplicate)
                {
                    resultList.Add(currentPoint);
                }
            }
        }
        return resultList;
    }

    // Compute barycentric coordinates for a point within a tetrahedron using precomputed matrices
    public Vector4 ComputeBarycentricCoordinates(Vector3 point, Tetrahedron tet)
    {
        Vector3 v3 = skyOccPos[tet.Vert3];
    
        // Use the precomputed BaryMatrix
        Vector3 coords = tet.BaryMatrix.MultiplyPoint3x4(point - v3);
    
        float w = 1.0f - coords.x - coords.y - coords.z;
    
        return new Vector4(coords.x, coords.y, coords.z, w);
    }
    
    // Compute barycentric coordinates for a point within a tetrahedron
//     public  Vector4 ComputeBarycentricCoordinates(
//         Vector3 point,  Tetrahedron tet)
//     {
//         Vector3 v0 = skyOccPos[tet.Vert0]; 
//         Vector3 v1 = skyOccPos[tet.Vert1]; 
//         Vector3 v2 = skyOccPos[tet.Vert2]; 
//         Vector3 v3 = skyOccPos[tet.Vert3];
//
//         Vector4 o = new Vector4();
// // Compute vectors relative to v3
//         Vector3 v0v3 = v0 - v3;
//         Vector3 v1v3 = v1 - v3;
//         Vector3 v2v3 = v2 - v3;
//         Vector3 pv3 = point - v3;
//
// // Compute dot products
//         float d00 = Vector3.Dot(v0v3, v0v3);
//         float d01 = Vector3.Dot(v0v3, v1v3);
//         float d02 = Vector3.Dot(v0v3, v2v3);
//         float d11 = Vector3.Dot(v1v3, v1v3);
//         float d12 = Vector3.Dot(v1v3, v2v3);
//         float d22 = Vector3.Dot(v2v3, v2v3);
//
//         float dp0 = Vector3.Dot(pv3, v0v3);
//         float dp1 = Vector3.Dot(pv3, v1v3);
//         float dp2 = Vector3.Dot(pv3, v2v3);
//
// // Compute the denominator
//         float denom = 1/ (d00 * (d11 * d22 - d12 * d12) - d01 * (d01 * d22 - d12 * d02) + d02 * (d01 * d12 - d11 * d02));
//
// // Compute barycentric coordinates
//         float u = (dp0 * (d11 * d22 - d12 * d12) - dp1 * (d01 * d22 - d12 * d02) + dp2 * (d01 * d12 - d11 * d02)) * denom;
//         float v = (d00 * (dp1 * d22 - dp2 * d12) - d01 * (dp0 * d22 - dp2 * d02) + d02 * (dp0 * d12 - dp1 * d02)) * denom;
//         float w = (d00 * (d11 * dp2 - d12 * dp1) - d01 * (d01 * dp2 - d12 * dp0) + d02 * (d01 * dp1 - d11 * dp0)) * denom;
//         float t = 1.0f - u - v - w;
//
//         o.x = u;
//         o.y = v;
//         o.z = w;
//         o.w = t;
//
//         return o;
//     }


    // public int FindContainingTetrahedron(Vector3 point, int startTetIndex)
    // {
    //     List<int> nulllist;
    //     return FindContainingTetrahedron(point, startTetIndex, out nulllist);
    // }

    public int FindContainingTetrahedron(Vector3 point, int startTetIndex/*, [CanBeNull] out List<int> visitedTetsint*/)
    {
        int tetIndex = startTetIndex;

        // If no starting index is provided or it's invalid, start from tetrahedron 0
        if (tetIndex < 0 || tetIndex >= tetrahedrons.Length)
        {
            tetIndex = 0;
        }

        // Keep track of visited tetrahedra to avoid infinite loops
        // HashSet<int> visitedTets = new HashSet<int>();
       //visitedTetsint = new List<int>();
        
        //limiting to a certain length in case we hit a fail state somehow
        for (int i = 0; i < tetrahedrons.Length; i++)
        {

            // if (visitedTets.Contains(tetIndex))
            // {
            //     // We've already visited this tetrahedron, exit to prevent infinite loop
            //     return -1;
            // }
            //visitedTets.Add(tetIndex);
           //visitedTetsint.Add(tetIndex);

            Tetrahedron tet = tetrahedrons[tetIndex];

            // Compute barycentric coordinates using the precomputed BaryMatrix
            //Vector4 baryCoords = ComputeBarycentricCoordinates(point, tet);
            Vector4 baryCoords = ComputeBarycentricCoordinates(point, tet);

            // Check if the point is inside the tetrahedron
            if (IsPointInsideTetrahedron(baryCoords))
            {
                // Found the containing tetrahedron
                return tetIndex;
            }

            // Identify the barycentric coordinate with the most negative value
            int mostNegativeIndex = -1;
            float mostNegativeValue = 0f;

            if (baryCoords.x < mostNegativeValue)
            {
                mostNegativeValue = baryCoords.x;
                mostNegativeIndex = 3;
            }

            if (baryCoords.y < mostNegativeValue)
            {
                mostNegativeValue = baryCoords.y;
                mostNegativeIndex = 2;
            }

            if (baryCoords.z < mostNegativeValue)
            {
                mostNegativeValue = baryCoords.z;
                mostNegativeIndex = 1;
            }

            if (baryCoords.w < mostNegativeValue)
            {
                //mostNegativeValue = baryCoords.w; //not used after this
                mostNegativeIndex = 0;
            }

            // If no negative barycentric coordinates, but still not inside, exit
            if (mostNegativeIndex == -1)
            {
                return -1;
            }

            // Ensure correct mapping between barycentric indices and neighbor indices
            int neighborIndex = tet.Neighbors[mostNegativeIndex];

            if (neighborIndex >= 0 && neighborIndex < tetrahedrons.Length /*&& !visitedTets.Contains(neighborIndex)*/)
            {
                //Setting the best neighbor to check next
                tetIndex = neighborIndex; 
            }
            else
            {
                // No valid neighbor to move to
                return -1;
            }
        }
        // If we reach here, we didn't find a containing tetrahedron within the maximum iterations
        return -1;
    }
}


