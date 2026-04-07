using System.Runtime.CompilerServices;
using UnityEngine;

class VolumeRenderingUtils //Importing some functions from HDRP to have similar terms   
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float MeanFreePathFromExtinction(float extinction)
    {
        return 1.0f / extinction;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ExtinctionFromMeanFreePath(float meanFreePath)
    {
        return 1.0f / meanFreePath;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 AbsorptionFromExtinctionAndScattering(float extinction, Vector3 scattering)
    {
        return new Vector3(extinction, extinction, extinction) - scattering;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 ScatteringFromExtinctionAndAlbedo(float extinction, Vector3 albedo)
    {
        return extinction * albedo;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 AlbedoFromMeanFreePathAndScattering(float meanFreePath, Vector3 scattering)
    {
        return meanFreePath * scattering;
    }
}