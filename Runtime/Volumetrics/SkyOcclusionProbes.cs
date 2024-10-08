using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// This is use to bake out the 
/// </summary>
public class SkyOcclusionProbes : MonoBehaviour
{
    //Positions that will be baked out
    public Vector3[] probePositions;

    [SerializeField] public SkyOcclusionDataAsset SkyOcclusionDataAsset;

    private void OnDrawGizmosSelected()
    {
        for (int i = 0; i < probePositions.Length; i++)
        {
            Gizmos.DrawSphere( probePositions[i], 0.1f);
        }
    }

    private void Awake()
    {
       if (SkyOcclusionDataAsset!=null) VolumetricRegisters.RegisterSkyOcclusionProbes(this);
    }
    
    private void OnDestroy()
    {
        if (SkyOcclusionDataAsset!=null) VolumetricRegisters.UnregisterSkyOcclusionProbes(this);
    }
}
