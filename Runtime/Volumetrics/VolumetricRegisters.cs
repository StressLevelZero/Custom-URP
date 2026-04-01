using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
//using Unity.Mathematics;

public class VolumetricRegisters
{
    public static List<LocalVolumetricFog> VolumetricMediaEntities = new List<LocalVolumetricFog>();
    public static List<BakedVolumetricArea> volumetricAreas = new List<BakedVolumetricArea>();

   // public static List<VolumetricRendering> volumetricRenderers = new List<VolumetricRendering>();
    
    public static List<SkyOcclusionProbes> skyOcclusionProbes = new List<SkyOcclusionProbes>();

    public static List<SkyOcclusionDataAsset> SkyOcclusionDataAssets = new List<SkyOcclusionDataAsset>();
    public static List<Light> realtimeVolumetricLights = new List<Light>();

    //public static bool _meshObjectsNeedRebuilding = true;
    public static uint ClipmapRevision { get; private set; } = 1;
    private static bool volumetricAreasNeedSort;


#region VolumeAreas

    public static void RegisterVolumetricArea(BakedVolumetricArea volumetricArea)
    {
        if (volumetricAreas.Contains(volumetricArea)) return;
#if UNITY_EDITOR
        if (volumetricArea.bakedTexture == null && Application.isPlaying) return; //quick check to make sure that this is valid
#else
        if (volumetricArea.bakedTexture == null) return; //quick check to make sure that this is valid
#endif
        volumetricAreas.Add(volumetricArea);
        volumetricAreasNeedSort = true;
        MarkClipmapDirty();
    }
    public static void UnregisterVolumetricArea(BakedVolumetricArea volumetricArea)
    {
        volumetricAreas.Remove(volumetricArea);
        MarkClipmapDirty();
    }

    public static void RegisterParticipatingMedia(LocalVolumetricFog volumetricMedia)
    {
        VolumetricMediaEntities.Add(volumetricMedia);
        MarkClipmapDirty();

    }
    public static void UnregisterParticipatingMedia(LocalVolumetricFog volumetricMedia)
    {
        VolumetricMediaEntities.Remove(volumetricMedia);
        MarkClipmapDirty();
    }
    
    /// <summary>
    ///Sorting from lowest to highest Texel Density so the generator biases towards the higher detail in-case of overlaps
    /// </summary>
    public static void EnsureVolumetricAreasSorted()
    {
        if (!volumetricAreasNeedSort) return;

        volumetricAreas.Sort((a, b) => a.TexelDensity.CompareTo(b.TexelDensity));
        volumetricAreasNeedSort = false;
    }
    
    // public static void RegisterVolumetricRenderer(VolumetricRendering volumetricRenderer)
    // {
    //     if (!volumetricRenderers.Contains(volumetricRenderer)) volumetricRenderers.Add(volumetricRenderer);
    // }
    // public static void UnregisterVolumetricRenderer(VolumetricRendering volumetricRenderer)
    // {
    //     if (volumetricRenderers.Contains(volumetricRenderer)) volumetricRenderers.Remove(volumetricRenderer);
    // }
    //

    #endregion
    
    
    [System.Obsolete("Deprecated. Use MarkClipmapDirty instead")]
    public static void ForceRefreshClipmaps()
    {
        MarkClipmapDirty();
        //TODO: Force the manager
        // foreach (VolumetricRendering VolumetricRenderer in volumetricRenderers)
        // {
        //     VolumetricRenderer.VolumetricRegisterForceRefresh = true;
        // }
    }
    
    public static void MarkClipmapDirty()
    {
        unchecked { ClipmapRevision++; }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        UnityEditor.SceneView.RepaintAll();
#endif
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
            if (!SkyOcclusionDataAssets.Contains(SOProbe.skyOcclusionDataAsset)) 
                SkyOcclusionDataAssets.Add(SOProbe.skyOcclusionDataAsset);
        }

        SkyManager.InitializeSkyOcclusion();
    }
    
    
    
#region VolumetricLights


/// <summary>Epsilon threshold for considering a light's volumetric contribution "on".</summary>
public static float VolumetricLightEpsilon = 1e-4f;

/// <summary>Returns true if this Light should be in the volumetric list.</summary>

public static bool IsRealtimeVolumetricLightEligible(Light l)
{
    if (l == null) return false;
    if (!l.isActiveAndEnabled) return false;
   // if (l.lightmapBakeType != LightmapBakeType.Realtime) return false;

    var uald = l.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
    if (uald == null) return false;

    return uald.volumetricDimmer > VolumetricLightEpsilon; // getter returns 0 when useVolumetric == false
}

public static bool RegisterVolumetricLight(Light l)
{
    if (l == null) return false;
    if (!IsRealtimeVolumetricLightEligible(l)) return false;

    if (!realtimeVolumetricLights.Contains(l))
    {
        realtimeVolumetricLights.Add(l);
        return true;
    }
    return false;
}

public static bool UnregisterVolumetricLight(Light l)
{
    if (l == null) return false;
    return realtimeVolumetricLights.Remove(l);
}

public static void ReevaluateVolumetricLight(Light l)
{
    if (IsRealtimeVolumetricLightEligible(l))
        RegisterVolumetricLight(l);
    else
        UnregisterVolumetricLight(l);
}

public static void PruneVolumetricLights()
{
    realtimeVolumetricLights.RemoveAll(x => x == null);
}

#endregion

}
