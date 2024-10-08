﻿using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
//using Unity.Mathematics;

public class VolumetricRegisters
{
    public static List<VolumetricMedia> VolumetricMediaEntities = new List<VolumetricMedia>();
    public static List<BakedVolumetricArea> volumetricAreas = new List<BakedVolumetricArea>();

    public static List<VolumetricRendering> volumetricRenderers = new List<VolumetricRendering>();
    
    public static List<SkyOcclusionProbes> skyOcclusionProbes = new List<SkyOcclusionProbes>();

    public static List<SkyOcclusionDataAsset> SkyOcclusionDataAssets = new List<SkyOcclusionDataAsset>();

    public static bool _meshObjectsNeedRebuilding = true;


    public static void RegisterVolumetricArea(BakedVolumetricArea volumetricArea)
    {
#if UNITY_EDITOR
        if (volumetricArea.bakedTexture == null && Application.isPlaying) return; //quick check to make sure that this is valid
#else
        if (volumetricArea.bakedTexture == null) return; //quick check to make sure that this is valid
#endif
        volumetricAreas.Add(volumetricArea);
        ForceRefreshClipmaps();

    }
    public static void UnregisterVolumetricArea(BakedVolumetricArea volumetricArea)
    {
        volumetricAreas.Remove(volumetricArea);
        ForceRefreshClipmaps();
    }

    public static void RegisterParticipatingMedia(VolumetricMedia volumetricMedia)
    {
        VolumetricMediaEntities.Add(volumetricMedia);
    }
    public static void UnregisterParticipatingMedia(VolumetricMedia volumetricMedia)
    {
        VolumetricMediaEntities.Remove(volumetricMedia);
    }
    
    
    public static void RegisterVolumetricRenderer(VolumetricRendering volumetricRenderer)
    {
        if (!volumetricRenderers.Contains(volumetricRenderer)) volumetricRenderers.Add(volumetricRenderer);
    }
    public static void UnregisterVolumetricRenderer(VolumetricRendering volumetricRenderer)
    {
        if (volumetricRenderers.Contains(volumetricRenderer)) volumetricRenderers.Remove(volumetricRenderer);
    }
    
    public static void ForceRefreshClipmaps()
    {
        foreach (VolumetricRendering VolumetricRenderer in volumetricRenderers)
        {
            VolumetricRenderer.VolumetricRegisterForceRefresh = true;
        }
    }
    
    
    public static void RegisterSkyOcclusionProbes(SkyOcclusionProbes skyOcclusionProbe)
    {
        if (!skyOcclusionProbes.Contains(skyOcclusionProbe) )
        {
            skyOcclusionProbes.Add(skyOcclusionProbe);
            SkyManager.SkyOccCount = skyOcclusionProbes.Count;
            RebuildSkyOccAssetList();
        }
    }
    public static void UnregisterSkyOcclusionProbes(SkyOcclusionProbes skyOcclusionProbe)
    {
        if (skyOcclusionProbes.Contains(skyOcclusionProbe))
        {
            skyOcclusionProbes.Remove(skyOcclusionProbe);
            SkyManager.SkyOccCount = skyOcclusionProbes.Count;
            RebuildSkyOccAssetList();
        }

    }

    public static void RebuildSkyOccAssetList()
    {
        SkyOcclusionDataAssets.Clear();
        
        foreach (SkyOcclusionProbes SOProbe in skyOcclusionProbes)
        {
            if (!SkyOcclusionDataAssets.Contains(SOProbe.SkyOcclusionDataAsset)) 
                SkyOcclusionDataAssets.Add(SOProbe.SkyOcclusionDataAsset);
        }

        SkyManager.InitializeSkyOcclusion();
    }

}
