using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Profiling;

[InitializeOnLoad] 
#endif
public static class SkyManager
{
    public static Texture skytexture;
    private static ComputeShader _scatteringComputeShader;

    public static readonly int ID_SkyTexture = Shader.PropertyToID("_SkyTexture");
    public static readonly int ID_SkyMipCount = Shader.PropertyToID("_SkyMipCount");
    public static readonly int ID_MipFogParam = Shader.PropertyToID("_MipFogParameters");
    
    private static int _kernelIndex;
    private static readonly int ID_SHMonoCoefficients = Shader.PropertyToID("_SHMonoCoefficients");
    
    //Todo: Change to a tetrahedralize look-up
    public static KdTree<MonoSH> tree; 
    private static bool _kdtreevalid = false;
    private static SkyOcclusionData _skyOcclusionData;
    private static float[] _skyMonoSHCoefficients = new float[9];

    private static int _skyOccCount = 0;
    private static bool _skyChanged;

    public static int SkyOccCount
    {
        get { return _skyOccCount; }
        set
        {
            if (_skyOccCount != value) // Only trigger the function if the value actually changes
            {
                _skyOccCount = value;
                _skyChanged = true;
                if (_skyOccCount !=0) InitializeSkyOcclusion();
            }
        }
    }
    static SkyManager()
    {
        if (IsBuildingPlayer()) return;
        LoadComputeShader();
        SetSkyMips(new Vector4(0, 1, 1, 0));
#if UNITY_EDITOR
        GenerateSkyTexture();
        EditorApplication.delayCall += DelayedCheckSky; //Delaying first call when loaded
        EditorSceneManager.sceneOpened -= SceneOpenedCallback;
        EditorSceneManager.sceneOpened += SceneOpenedCallback;
#endif
        //Double checking that this doesn't exist. We purposely don't unregister it because we need it constantly called whenever there's a change.
        SceneManager.sceneLoaded -= OnSceneLoaded; 
        SceneManager.sceneLoaded += OnSceneLoaded;
        InitializeSkyOcclusion();
    }
    
    static bool IsBuildingPlayer()
    {
#if UNITY_EDITOR
        return BuildPipeline.isBuildingPlayer;
#else
    return false;
#endif
    }

    public static void InitializeSkyOcclusion()
    {

        if (_skyOccCount == 0 || VolumetricRegisters.SkyOcclusionDataAssets.Count == 0) return;

        SkyOcclusionData[] skyoccdatas = new SkyOcclusionData[VolumetricRegisters.SkyOcclusionDataAssets.Count];

        //Combine data
        for (int i = 0; i < VolumetricRegisters.SkyOcclusionDataAssets.Count; i++)
        {
            skyoccdatas[i] = (SkyOcclusionData.CombineSkyOcclusionData(VolumetricRegisters.SkyOcclusionDataAssets[i].skyOcclusionData) );
        }
        _skyOcclusionData = SkyOcclusionData.CombineSkyOcclusionData(skyoccdatas);

        //add function to rendering 
        Application.onBeforeRender -= SkyUpdate;
        Application.onBeforeRender += SkyUpdate;
    }
    public static void SkyUpdate()
    {
        if (!_kdtreevalid)KDStart();
        
        Vector3 worldpos;
#if UNITY_EDITOR
        if (!Application.isPlaying)
            worldpos = SceneView.GetAllSceneCameras()[0].transform.position;
        else
            worldpos = Camera.main.transform.position;
#else
        worldpos = Camera.main.transform.position;
#endif
        MonoSH occlusionResult;

        try
        {
            Profiler.BeginSample("KDUpdate");
            occlusionResult = KDUpdate(worldpos);
            Profiler.EndSample();

        }
        catch
        {
            _kdtreevalid = false;
            return;
        }
        SetSkyOcclusion(occlusionResult);

    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
        {
            GenerateSkyTexture();
        }
    }
    
    
#if UNITY_EDITOR
    static void SceneOpenedCallback(Scene scene, OpenSceneMode mode)
    {
        Debug.Log(mode + " : " +scene);
        if (!EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            RegenerateSkyTexture();
        }
        else
        {
            EditorApplication.delayCall += DelayedCheckSky;
        }
    }
    
    static void DelayedCheckSky()
    {
        RegenerateSkyTexture();
        EditorApplication.delayCall -= DelayedCheckSky;
    }


#endif
    public static void RegenerateSkyTexture()
    {
        if (RenderSettings.defaultReflectionMode != DefaultReflectionMode.Custom)
        {
            if (skytexture)
            {
#if UNITY_EDITOR
        if (Application.isPlaying)    Object.Destroy(skytexture);
        else Object.DestroyImmediate(skytexture);
#else
               Object.Destroy(skytexture);
#endif
                skytexture = null;
                GenerateSkyTexture();
            }
        }
    }

    public static void GenerateSkyTexture()
    {
        //Generate Skybox
        RenderTexture cubetex = new RenderTexture(32, 32, 1, RenderTextureFormat.DefaultHDR);
        cubetex.enableRandomWrite = true;
        cubetex.useMipMap = true;
        cubetex.dimension = UnityEngine.Rendering.TextureDimension.Cube;
        cubetex.autoGenerateMips = false; //Do this after the scattering
        cubetex.name = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "_MipSky";
        //cubetex.
        //    cubetex.Create();

        //Reducing density because certain skybox shaders render fog contribution which would double up the effect 
        float oldFogDensity = RenderSettings.fogDensity;
        RenderSettings.fogDensity = 0 ;
        
        Camera renderCam = new GameObject().AddComponent<Camera>();
        renderCam.gameObject.hideFlags = HideFlags.DontSave;
        renderCam.enabled = false;
        renderCam.cullingMask = 0;
        renderCam.backgroundColor = Color.black;
        renderCam.clearFlags = CameraClearFlags.Skybox;
        renderCam.allowHDR = true;
        renderCam.RenderToCubemap(cubetex);
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            Object.Destroy(renderCam.gameObject);
        }
        else
        {
            Object.DestroyImmediate(renderCam.gameObject);
        }
#else
        Object.Destroy(renderCam.gameObject);
#endif
        RenderSettings.fogDensity = oldFogDensity;
        
        //Multi pass scattering
       ApplyScattering(cubetex, 0.23f, 100);
       ApplyScattering(cubetex, 0.47f, 500);
       cubetex.GenerateMips();
        
        skytexture = cubetex;
        Debug.Log("Generated sky: " + cubetex.name );
        SetSkyTexture(skytexture);
        SetMonoSHToWhite(); //Clear sky occlusion
    }
    
    
    public static void CheckSky()
    {

        if (RenderSettings.defaultReflectionMode == DefaultReflectionMode.Custom)
        {
            if (RenderSettings.customReflectionTexture != null && RenderSettings.customReflectionTexture.GetType() == typeof(Cubemap))
            {
                SetSkyTexture(RenderSettings.customReflectionTexture);
            }
            else SetSkyTexture(CoreUtils.blackCubeTexture);
        }
        else //DefaultReflectionMode.Skybox
        {
            if (skytexture == null)
            {
                GenerateSkyTexture();
            }

            if (skytexture != null)
            {
                SetSkyTexture(skytexture);
            }
            else SetSkyTexture(CoreUtils.blackCubeTexture);
        }
    }

    static public void SetSkyMips(Vector4 MipFogParam)
    {
        Shader.SetGlobalVector(ID_MipFogParam, MipFogParam);
    }

    static public void SetSkyTexture(Texture SkyTex)
    {
        Shader.SetGlobalTexture(ID_SkyTexture, SkyTex);
        Shader.SetGlobalInt(ID_SkyMipCount, SkyTex.mipmapCount);
    }
    
    // // // // // // // /// 
    static void LoadComputeShader()
    {
        // Load the ComputeShader from the Resources folder or using Shader.Find
        //TODO: put the shader in a static path within the package and remove from resources
        _scatteringComputeShader = Resources.Load<ComputeShader>("SkyboxScattering");
        
        if (_scatteringComputeShader != null)
        {
            _kernelIndex = _scatteringComputeShader.FindKernel("CSMain");
        }
        else
        {
            Debug.LogError("ComputeShader not found. Make sure it is located in the Resources folder or properly referenced.");
        }
    }
    static void CopyArrayToCubemap(RenderTexture textureArray, RenderTexture cubemap)
    {
        for (int i = 0; i < 6; i++)
        {
            // Copy the slice of the texture array to the corresponding cubemap face
            Graphics.CopyTexture(textureArray, i, 0, cubemap, i, 0);
        }
    }

    // Method to apply scattering and blurring to a cubemap using a compute shader
    public static void ApplyScattering(RenderTexture sourceCubemap, float scatteringFactor, int sampleCount)
    {
        int resolution = sourceCubemap.width;

        // Create a RenderTexture as a 2D array to hold each cubemap face
        // Compute in shaders don't have a RWTextureCube so we have to output to a tex array instead and copy over
        RenderTexture textureArray = new RenderTexture(resolution, resolution, 0)
        {
            dimension = TextureDimension.Tex2DArray,
            volumeDepth = 6,  // 6 layers for cubemap faces
            enableRandomWrite = true,  
            format = sourceCubemap.format,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        textureArray.Create();

        if (_scatteringComputeShader == null)
        {
            _scatteringComputeShader = Resources.Load<ComputeShader>("SkyboxScattering");
            _kernelIndex = _scatteringComputeShader.FindKernel("CSMain");
        }

        // Set the compute shader parameters
        _scatteringComputeShader.SetTexture(_kernelIndex, "SourceCubeMap", sourceCubemap);
        _scatteringComputeShader.SetTexture(_kernelIndex, "ResultTextureArray", textureArray); 
        _scatteringComputeShader.SetFloat("ScatteringFactor", scatteringFactor);
        _scatteringComputeShader.SetInt("Resolution", resolution);  
        _scatteringComputeShader.SetInt("sampleCount", sampleCount);  

        // Dispatch the compute shader
        _scatteringComputeShader.Dispatch(_kernelIndex, resolution / 8, resolution / 8, 6);

        // Copy the processed texture array back to the cubemap
        CopyArrayToCubemap(textureArray, sourceCubemap);

        // Clean up the texture array
        if (Application.isPlaying) Object.Destroy(textureArray);
        else Object.DestroyImmediate(textureArray);
     
    }
    public static SphericalHarmonicsL2 BakeCubemapToSH(RenderTexture sourceCubemap, int sampleCount )
    {
        SHTools.RawSphericalHarmonicsL2 rawSHL2 = SHTools.RawSphericalHarmonicsL2.ProjectCubemapIntoSHRiemann(sourceCubemap, sampleCount, sampleCount, true);
        return rawSHL2.AsUnityConvention();
    }
    public static void SetSkyOcclusion(MonoSH monoSH)
    {
        // Pass the monochromatic SH coefficients to the shader
        Shader.SetGlobalFloatArray(ID_SHMonoCoefficients, monoSH.ToArray(_skyMonoSHCoefficients));
//        Debug.Log(monoSH.ToString());
    }
    public static void SetMonoSHToWhite()
    {
        Shader.SetGlobalFloatArray(ID_SHMonoCoefficients, MonoSH.White().ToArray());
    }

    static void KDStart()
    {
        try
        {
            if (_skyOcclusionData.skyOccPos.ToList().Count != _skyOcclusionData.SkySH.ToList().Count) _kdtreevalid = false;

            tree = new KdTree<MonoSH>(_skyOcclusionData.skyOccPos.ToList(), _skyOcclusionData.SkySH.ToList());
        
            _kdtreevalid = true;
            Debug.Log("KDTree has been successfully initialized.");
        }
        catch (Exception ex)
        {
            _kdtreevalid = false;
            Debug.LogError("KDTree initialization failed: " + ex.Message);
        }
    }
    
    // Declare the list 
    private static List<(Vector3 point, MonoSH data)> _nearestPointsData = new List<(Vector3 point, MonoSH data)>(4);
    private static MonoSH _interpolatedSH = MonoSH.White();

    static MonoSH KDUpdate(Vector3 targetPosition)
    {
        
        // Declare or reuse the pre-allocated list
        // Ensure it's declared outside the method if you plan to reuse it to avoid allocations
        if (_nearestPointsData == null)
            _nearestPointsData = new List<(Vector3 point, MonoSH data)>(4);
        else
            _nearestPointsData.Clear();

        // Call the updated method
        tree.KNearestNeighbors(targetPosition, 4, _nearestPointsData);

        if (_nearestPointsData.Count < 4)
        {
           // Debug.LogError("Not enough points found.");
            return MonoSH.White();
        }

        // Extract points and data
        Vector3 a = _nearestPointsData[0].point;
        Vector3 b = _nearestPointsData[1].point;
        Vector3 c = _nearestPointsData[2].point;
        Vector3 d = _nearestPointsData[3].point;

        MonoSH dataA = _nearestPointsData[0].data;
        MonoSH dataB = _nearestPointsData[1].data;
        MonoSH dataC = _nearestPointsData[2].data;
        MonoSH dataD = _nearestPointsData[3].data;

        Vector4 barycentricCoords = SkyOcclusion.ComputeBarycentricCoordinates(targetPosition, a, b, c, d);

        if (barycentricCoords == Vector4.zero)
        {
            //Debug.LogError("Failed to compute barycentric coordinates.");
            return MonoSH.White();
        }

        // Interpolate data
        return MonoSH.Interpolate(dataA, dataB, dataC, dataD, barycentricCoords, _interpolatedSH);
    }
    public static (float t, int index1, int index2) GetInterpolationFactor(Vector3 providedPos)
    {
        // Step 1: Find the two closest positions and their indices
        Vector3 pos1 = Vector3.zero;
        Vector3 pos2 = Vector3.zero;
        int index1 = -1;
        int index2 = -1;
        float minDist1 = float.MaxValue;
        float minDist2 = float.MaxValue;

        for (int i = 0; i < _skyOcclusionData.skyOccPos.Length; i++)
        {
            Vector3 pos = _skyOcclusionData.skyOccPos[i];
            float dist = Vector3.Distance(providedPos, pos);
            if (dist < minDist1)
            {
                minDist2 = minDist1;
                pos2 = pos1;
                index2 = index1;

                minDist1 = dist;
                pos1 = pos;
                index1 = i;
            }
            else if (dist < minDist2)
            {
                minDist2 = dist;
                pos2 = pos;
                index2 = i;
            }
        }

        // Step 2: Calculate the interpolation factor
        Vector3 dir = pos2 - pos1;
        Vector3 v = providedPos - pos1;
        float t = Vector3.Dot(v, dir) / Vector3.Dot(dir, dir);
        t = Mathf.Clamp01(t); // Clamp between 0 and 1

        return (t, index1, index2);
    }
}