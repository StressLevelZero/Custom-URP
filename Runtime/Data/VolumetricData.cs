using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Saved volumetric settings
/// </summary>
[System.Serializable, CreateAssetMenu(fileName = "Volumetric Rendering Settings", menuName = "Rendering/Volumetric Rendering Settings", order = 10)]
public class VolumetricData : ScriptableObject
{
    [Header("Volumetric camera settings")]
    [Tooltip("Near Clip plane")]
    public float near = 1;
    [Tooltip("Far Clip plane")]
    public float far = 40;
    [Tooltip("Resolution")]
    public int FroxelWidthResolution = 128;
    [Tooltip("Resolution")]
    public int FroxelHeightResolution = 128;
    [Tooltip("Resolution")]
    public int FroxelDepthResolution = 64;
    //[Tooltip("Controls the bias of the froxel dispution. A value of 1 is linear. ")]
    //public float FroxelDispution;

    [Header("Prebaked clipmap settings - Controls both cascades")]
    [Tooltip("Textile resolution per unit")]
    public int ClipMapResolution = 128;
    [Tooltip("Size of inner clipmap in units. Outter clipmap is 5x the size")]
    public float ClipmapScale = 80;
    [Tooltip("Distance (m) from previous sampling point to trigger resampling clipmap")]
    public float ClipmapResampleThreshold = 1;


}
