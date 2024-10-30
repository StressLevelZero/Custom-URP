using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// This is use to bake out the 
/// </summary>
public class SkyOcclusionProbes : MonoBehaviour
{
    //Not needed at runtime
#if UNITY_EDITOR
    //Positions that will be baked out
    public Vector3[] probePositions = 
        new []{Vector3.zero,  Vector3.one, Vector3.up, Vector3.right, Vector3.forward, 
            Vector3.forward+Vector3.right,Vector3.forward+Vector3.up, Vector3.up+Vector3.right   };
#endif
    [SerializeField] public SkyOcclusionDataAsset skyOcclusionDataAsset;
    [SerializeField, HideInInspector] public int dataIndex;

    //public int DEBUGINDEX;
    //public Vector3 DEBUGTRANSFORM;

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (probePositions == null || probePositions.Length < 1)  return;
        Bounds probebounds = new Bounds();
        //Need to set a starting bounds because Encapsulate is additive which then includes the initialized zero position 
        probebounds.SetMinMax(transform.position+probePositions[0],transform.position+probePositions[0]);
        for (int i = 0; i < probePositions.Length; i++)
        {
            Gizmos.DrawSphere( transform.position+probePositions[i], 0.1f);
            probebounds.Encapsulate(transform.position+probePositions[i]);
        }
        
        Gizmos.DrawWireCube(probebounds.center, probebounds.size);
        
        
        // if (skyOcclusionDataAsset != null)
        // {
        //     Gizmos.DrawWireCube(DEBUGTRANSFORM, Vector3.one*0.25f);
        //     
        //     SkyOcclusionData skydata = skyOcclusionDataAsset.skyOcclusionData[0];
        //     drawTetrahedron(skydata.tetrahedrons[DEBUGINDEX],skydata.skyOccPos, new Color(0,0,1,0.5f));
        //     List<int> visitedTets;
        //     
        //     int containingTetIndex = skydata.FindContainingTetrahedron(DEBUGTRANSFORM,DEBUGINDEX, out visitedTets);
        //     drawString(containingTetIndex.ToString(),DEBUGTRANSFORM);
        //     
        //     Color giz = new Color(0,1,0.05f,0.02f);
        //     foreach (var visitedTet in visitedTets)
        //     {
        //         drawTetrahedron(skydata.tetrahedrons[visitedTet],skydata.skyOccPos, giz);
        //     }
        //     
        //     if (containingTetIndex == -1) return;
        //     drawTetrahedron(skydata.tetrahedrons[containingTetIndex],skydata.skyOccPos, Color.yellow);
        //
        //     
        //      giz = new Color(1,0,0,0.1f);
        //
        //     foreach (int neighborIndex in skydata.tetrahedrons[containingTetIndex].Neighbors)
        //     {
        //         // Check if the neighbor index is valid
        //         if (neighborIndex >= 0 && neighborIndex < skydata.tetrahedrons.Length)
        //         {
        //             drawTetrahedron(skydata.tetrahedrons[neighborIndex],skydata.skyOccPos, giz);
        //         }
        //         else
        //         {
        //             // Handle invalid neighbor indices if necessary
        //             Debug.LogWarning($"Invalid neighbor index: {neighborIndex}");
        //         }
        //     }
        // }
    }

    // void drawTetrahedron(Tetrahedron tet, Vector3[] pos, Color color)
    // {
    //     Vector3 v0 = pos[tet.Vert0]
    //         , v1 =   pos[tet.Vert1]
    //         , v2 =   pos[tet.Vert2]
    //         , v3 =   pos[tet.Vert3];
    //     
    //     Gizmos.color = color;
    //     Gizmos.DrawLine(v0, v1);
    //     Gizmos.DrawLine(v0, v2);
    //     Gizmos.DrawLine(v0, v3);
    //     Gizmos.DrawLine(v1, v2);
    //     Gizmos.DrawLine(v1, v3);
    //     Gizmos.DrawLine(v2, v3);
    // }
    //
    // static void drawString(string text, Vector3 worldPos, Color? colour = null) {
    //     UnityEditor.Handles.BeginGUI();
    //     if (colour.HasValue) GUI.color = colour.Value;
    //     var view = UnityEditor.SceneView.currentDrawingSceneView;
    //     Vector3 screenPos = view.camera.WorldToScreenPoint(worldPos);
    //     Vector2 size = GUI.skin.label.CalcSize(new GUIContent(text));
    //     GUI.Label(new Rect(screenPos.x - (size.x / 2), -screenPos.y + view.position.height + 4, size.x, size.y), text);
    //     UnityEditor.Handles.EndGUI();
    // }

#endif
    private void Awake()
    {
       if (skyOcclusionDataAsset!=null) VolumetricRegisters.RegisterSkyOcclusionProbes(this);
    }
    
    private void OnDestroy()
    {
        if (skyOcclusionDataAsset!=null) VolumetricRegisters.UnregisterSkyOcclusionProbes(this);
    }
}
