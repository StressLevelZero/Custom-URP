using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Saved volumetric settings
/// </summary>
[System.Serializable, CreateAssetMenu(fileName = "Volumetric Rendering Settings", menuName = "Rendering/Volumetric Rendering Settings", order = 10)]
public class VolumetricData : ScriptableObject
{
    [System.Serializable]
    public struct ClipmapLevelData
    {
        [Tooltip("Textile resolution per unit")]
        public int ClipMapResolution;
        [Tooltip("Size of inner clipmap in units")]
        public float ClipmapScale;
        [Tooltip("Distance (m) from previous sampling point to trigger resampling clipmap")]
        public float ClipmapResampleThresholdDistance;
    }
    
    [Header("Volumetric camera settings")]
    [Tooltip("Near Clip plane")]
    public float near = 0.01f;
    [Tooltip("Far Clip plane")]
    public float far = 80f;
    [Tooltip("Resolution")]
    public int FroxelWidthResolution = 32;
    [Tooltip("Resolution")]
    public int FroxelHeightResolution = 32;
    [Tooltip("Resolution")]
    public int FroxelDepthResolution = 24;

    [Header("Prebaked clipmap settings - Controls both cascades")]

    public ClipmapLevelData ClipmapLevel0;
    public ClipmapLevelData ClipmapLevel1;
    public ClipmapLevelData ClipmapLevel2;
    public ClipmapLevelData ClipmapLevel3;

    public Texture3D DefaultTurbulentNoise;
}


