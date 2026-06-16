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

[InitializeOnLoad] 
#endif
public static class SkyManager
{
    public static Texture skytexture;
    private static ComputeShader _scatteringComputeShader;
    private static ComputeShader _skyRadianceComputeShader;
    
    private static bool _isGeneratingSkyTexture = false;

    
    private static int _scatteringkernelIndex;
    private static int _skyRadiancekernelIndex;

    public static readonly int ID_SkyTexture = Shader.PropertyToID("_SkyTexture");
    public static readonly int ID_SkyMipCount = Shader.PropertyToID("_SkyMipCount");
    public static readonly int ID_MipFogParam = Shader.PropertyToID("_MipFogParameters");

    private static readonly int ID_SHMonoCoefficients = Shader.PropertyToID("_SHMonoCoefficients");
    
    public static KdTree<MonoSH> tree; 
    //private static bool _kdtreevalid = false;
    private static SkyOcclusionData _skyOcclusionData;
    private static float[] _skyMonoSHCoefficients = new float[9];
    
    static SLZ.VolumetricSceneBindings bindings;
    static bool _searchedBindingsThisScene;
    static int _searchedBindingsSceneHandle = -1;
    
    private const int RuntimeFallbackMeanSkySamples = 256;

    private static readonly int ID_EnvLightSamples     = Shader.PropertyToID("EnvLightSamples");
    private static readonly int ID_PerDispatchRayCount = Shader.PropertyToID("PerDispatchRayCount");
    private static readonly int ID_StartRayIdx         = Shader.PropertyToID("StartRayIdx");
    private static readonly int ID_OutColor            = Shader.PropertyToID("_OutColor");
    private static readonly int ID_GlobalSeed          = Shader.PropertyToID("_GlobalSeed");
    private static readonly int ID_MipLevel            = Shader.PropertyToID("_MipLevel");

    private static ComputeBuffer _skyMeanRadianceBuffer;
    private static readonly Vector4[] _skyMeanRadianceZero = { Vector4.zero };
    private static readonly Vector4[] _skyMeanRadianceReadback = new Vector4[1];

    private static SLZ.BakedVolumetricsData _runtimeFallbackBakedVolumetricsData;

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
        EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChangedInEditMode;

#endif
        //Double checking that this doesn't exist. We purposely don't unregister it because we need it constantly called whenever there's a change.
        // SceneManager.sceneLoaded -= OnSceneLoaded; 
        // SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        InitializeSkyOcclusion();
    }
    
    static bool IsBuildingPlayer()
    {
#if UNITY_EDITOR
        return BuildPipeline.isBuildingPlayer || Application.isBatchMode;
#else
    return false;
#endif
    }

    public static void InitializeSkyOcclusion()
    {

        if (_skyOccCount == 0 || VolumetricRegisters.SkyOcclusionDataAssets.Count == 0) return;

        //SkyOcclusionData[] skyoccdatas = new SkyOcclusionData[VolumetricRegisters.SkyOcclusionDataAssets.Count];

        //Combine data
        // for (int i = 0; i < VolumetricRegisters.SkyOcclusionDataAssets.Count; i++)
        // {
        //     skyoccdatas[i] = (SkyOcclusionData.CombineSkyOcclusionData(VolumetricRegisters.SkyOcclusionDataAssets[i].skyOcclusionData) );
        // }
        // _skyOcclusionData = SkyOcclusionData.CombineSkyOcclusionData(skyoccdatas);
        //TODO: Account for more data. Either we will retetrahedralize or jocky between multiple
        _skyOcclusionData = VolumetricRegisters.SkyOcclusionDataAssets[0].skyOcclusionData; //Assume there's one for now.

        //add function to rendering 
        Application.onBeforeRender -= SkyUpdate;
        Application.onBeforeRender += SkyUpdate;
    }
    public static void SkyUpdate()
    {
       // if (!_kdtreevalid)KDStart();
        
        Vector3 worldpos;

        try
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                worldpos = SceneView.GetAllSceneCameras()[0].transform.position;
            else
                worldpos = Camera.main.transform.position; //todo: Not this! :(
#else
        worldpos = Camera.main.transform.position;
#endif
        }
        catch
        {
            return;
        }

        MonoSH occlusionResult;
        Profiler.BeginSample("TetrahedronUpdate");
        occlusionResult = TetrahedronUpdate(worldpos);
        Profiler.EndSample();
        // try
        // {
        //     Profiler.BeginSample("KDUpdate");
        //     occlusionResult = KDUpdate(worldpos);
        //     Profiler.EndSample();
        //
        // }
        // catch
        // {
        //     _kdtreevalid = false;
        //     return;
        // }
//        Debug.Log(occlusionResult);
        SetSkyOcclusion(occlusionResult);

    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
        {
            InvalidateBakedVolumetricsLookup();
            GenerateSkyTexture();
        }
    }
    
    // Callback method that gets called when the active scene changes
    private static void OnActiveSceneChanged(Scene previousScene, Scene newScene)
    {
        InvalidateBakedVolumetricsLookup();
        GenerateSkyTexture();
    }
    
    

#if UNITY_EDITOR
    static void SceneOpenedCallback(Scene scene, OpenSceneMode mode)
    {
        //Debug.Log(mode + " : " +scene);
        if (!EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GenerateSkyTexture();
          //  RegenerateSkyTexture();
        }
        else
        {
            EditorApplication.delayCall += DelayedCheckSky;
        }
    }

    private static void OnActiveSceneChangedInEditMode(Scene previousScene, Scene newScene)
    {
        if (!EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GenerateSkyTexture();
            //  RegenerateSkyTexture();
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
            if (skytexture)
            {
                CoreUtils.Destroy(skytexture);
                skytexture = null;
                GenerateSkyTexture();
            }
    }
    //
    #if UNITY_EDITOR
    [MenuItem("CONTEXT/Light/Regen sky")]
    public static void SkyRegen()
    {
        GenerateSkyTexture();
    }
    #endif

    public static void GenerateSkyTexture()
    {
        if (_isGeneratingSkyTexture) return;
        _isGeneratingSkyTexture = true;
    
        try
        {
        //Generate Skybox
        RenderTexture cubetex = new RenderTexture(32, 32, 1, GraphicsFormat.R16G16B16A16_SFloat,0);
        cubetex.enableRandomWrite = true;
        cubetex.useMipMap = true;
        cubetex.dimension = UnityEngine.Rendering.TextureDimension.Cube;
        cubetex.autoGenerateMips = false; //Do this after the scattering
        cubetex.name = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "_MipSky";
        cubetex.depth = 0;
        
        //Reducing density because certain skybox shaders render fog contribution which would double up the effect 
        float oldFogDensity = RenderSettings.fogDensity;
        RenderSettings.fogDensity = 0 ;
        
        Camera renderCam = new GameObject().AddComponent<Camera>();
        renderCam.gameObject.AddComponent<SkipVolumetricsTag>();  // <—Making sure we skip volumetrics
        renderCam.gameObject.hideFlags = HideFlags.DontSave;
        renderCam.enabled = false;
        renderCam.cullingMask = 0;
        renderCam.backgroundColor = Color.black;
        renderCam.clearFlags = CameraClearFlags.Skybox;
        renderCam.allowHDR = true;
        renderCam.RenderToCubemap(cubetex);
        
        CoreUtils.Destroy(renderCam.gameObject);
        
        RenderSettings.fogDensity = oldFogDensity;
        
        //Multi pass scattering
        ApplyScattering(cubetex, 0.23f, 100);
        ApplyScattering(cubetex, 0.47f, 500);
      //cubetex.GenerateMips(); //Borked!
        GenerateSeamlessMips(cubetex);
        skytexture = cubetex;
        _skyChanged = true;
        //Debug.Log("Generated sky: " + cubetex.name );
        SetSkyTexture(skytexture);
        SetMonoSHToWhite(); //Clear sky occlusion

       // RenderSettings.ambientProbe = BakeCubemapToSH(cubetex,256);
        }
        finally
        {
            _isGeneratingSkyTexture = false;
        }
    }
    
    public static Color GetAmbientSkyColor()
    {
        var volumetricbinds = TryGetBakedVolumetricsData();
        return volumetricbinds ? volumetricbinds.meanSkyRadianceLinear  : Color.black;
    }
    public static void CheckSky()
    {

        // if (RenderSettings.defaultReflectionMode == DefaultReflectionMode.Custom)
        // {
        //     if (RenderSettings.customReflectionTexture != null && RenderSettings.customReflectionTexture.GetType() == typeof(Cubemap))
        //     {
        //         SetSkyTexture(RenderSettings.customReflectionTexture);
        //     }
        //     else SetSkyTexture(CoreUtils.blackCubeTexture);
        // }
        // else //DefaultReflectionMode.Skybox
        // {
            if (skytexture == null && !_isGeneratingSkyTexture)
            {
                GenerateSkyTexture();
            }

            if (skytexture != null)
            {
                SetSkyTexture(skytexture);
            }
            else SetSkyTexture(CoreUtils.blackCubeTexture);
      //  }
    }
    
    static void InvalidateBakedVolumetricsLookup()
    {
        bindings = null;
        _searchedBindingsThisScene = false;
        _searchedBindingsSceneHandle = -1;
    }

    static SLZ.VolumetricSceneBindings GetSceneBindingsOnce()
    {
        Scene activeScene = SceneManager.GetActiveScene();

        if (_searchedBindingsSceneHandle != activeScene.handle)
        {
            bindings = null;
            _searchedBindingsThisScene = false;
            _searchedBindingsSceneHandle = activeScene.handle;
        }

        if (!_searchedBindingsThisScene)
        {
            _searchedBindingsThisScene = true;
            bindings = Object.FindFirstObjectByType<SLZ.VolumetricSceneBindings>();
        }

        return bindings;
    }

    public static SLZ.BakedVolumetricsData TryGetBakedVolumetricsData()
    {
        var sceneBindings = GetSceneBindingsOnce();

        if (sceneBindings && sceneBindings.BakedVolumetricsData)
            return sceneBindings.BakedVolumetricsData;

        return GetOrCreateRuntimeFallbackBakedVolumetricsData();
    }
    
    static SLZ.BakedVolumetricsData GetOrCreateRuntimeFallbackBakedVolumetricsData()
    {
        if (!_runtimeFallbackBakedVolumetricsData)
        {
            _runtimeFallbackBakedVolumetricsData = ScriptableObject.CreateInstance<SLZ.BakedVolumetricsData>();
            _runtimeFallbackBakedVolumetricsData.hideFlags = HideFlags.HideAndDontSave;
            _skyChanged = true;
        }

        if (_skyChanged)
        {
            if (TryPopulateRuntimeFallbackMeanSkyRadiance(_runtimeFallbackBakedVolumetricsData))
                _skyChanged = false;
        }

        return _runtimeFallbackBakedVolumetricsData;
    }

    static bool TryPopulateRuntimeFallbackMeanSkyRadiance(SLZ.BakedVolumetricsData target)
    {
        if (target == null)
            return false;

        if (_skyRadianceComputeShader == null || _skyRadiancekernelIndex < 0)
            LoadComputeShader();

        if (_skyRadianceComputeShader == null || _skyRadiancekernelIndex < 0)
        {
            target.meanSkyRadianceLinear = Color.black;
            target.environmentSampleCount = 0;
            target.sourceScenePath = SceneManager.GetActiveScene().path;
            target.lastBakeUtcTicks = DateTime.UtcNow.Ticks;
            return false;
        }

        CheckSky();

        Texture sourceSky = skytexture ? skytexture : CoreUtils.blackCubeTexture;
        if (!sourceSky)
        {
            target.meanSkyRadianceLinear = Color.black;
            target.environmentSampleCount = 0;
            target.sourceScenePath = SceneManager.GetActiveScene().path;
            target.lastBakeUtcTicks = DateTime.UtcNow.Ticks;
            return false;
        }

        if (_skyMeanRadianceBuffer == null)
            _skyMeanRadianceBuffer = new ComputeBuffer(1, sizeof(float) * 4, ComputeBufferType.Structured);

        _skyMeanRadianceBuffer.SetData(_skyMeanRadianceZero);

        CommandBuffer cmd = CommandBufferPool.Get("SkyManager Mean Sky Radiance");
        cmd.Clear();

        cmd.SetComputeTextureParam(_skyRadianceComputeShader, _skyRadiancekernelIndex, ID_SkyTexture, sourceSky);
        cmd.SetComputeIntParam(_skyRadianceComputeShader, ID_EnvLightSamples, RuntimeFallbackMeanSkySamples);
        cmd.SetComputeIntParam(_skyRadianceComputeShader, ID_PerDispatchRayCount, RuntimeFallbackMeanSkySamples);
        cmd.SetComputeIntParam(_skyRadianceComputeShader, ID_StartRayIdx, 0);
        cmd.SetComputeIntParam(_skyRadianceComputeShader, ID_GlobalSeed, 0x51A7C3D);
        cmd.SetComputeFloatParam(_skyRadianceComputeShader, ID_MipLevel, 0.0f);
        cmd.SetComputeBufferParam(_skyRadianceComputeShader, _skyRadiancekernelIndex, ID_OutColor, _skyMeanRadianceBuffer);

        cmd.DispatchCompute(_skyRadianceComputeShader, _skyRadiancekernelIndex, 1, 1, 1);

        Graphics.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);

        _skyMeanRadianceBuffer.GetData(_skyMeanRadianceReadback);

        Vector4 result = _skyMeanRadianceReadback[0];
        target.meanSkyRadianceLinear = new Color(result.x, result.y, result.z, result.w);
        target.environmentSampleCount = RuntimeFallbackMeanSkySamples;
        target.sourceScenePath = SceneManager.GetActiveScene().path;
        target.lastBakeUtcTicks = DateTime.UtcNow.Ticks;

        return true;
    }

    public static void MarkSkyRadianceDirty()
    {
        _skyChanged = true;
    }

    static void ReleaseSkyManagerRuntimeResources()
    {
        if (_skyMeanRadianceBuffer != null)
        {
            _skyMeanRadianceBuffer.Release();
            _skyMeanRadianceBuffer = null;
        }

        if (_runtimeFallbackBakedVolumetricsData)
        {
            if (Application.isPlaying)
                Object.Destroy(_runtimeFallbackBakedVolumetricsData);
            else
                Object.DestroyImmediate(_runtimeFallbackBakedVolumetricsData);

            _runtimeFallbackBakedVolumetricsData = null;
        }
        
        InvalidateBakedVolumetricsLookup();
      //  bindings = null;
    }
    
    public static void CheckSkyNull()
    {
        if (skytexture is null) SetSkyTexture(CoreUtils.blackCubeTexture);
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
        // Load the ComputeShader from the Resources folder
        // TODO: serialize and remove from resources
        _scatteringComputeShader  = Resources.Load<ComputeShader>("SkyboxScattering");
        _skyRadianceComputeShader = Resources.Load<ComputeShader>("SkyRadiance");
        
        if (_scatteringComputeShader is not null)
            _scatteringkernelIndex = _scatteringComputeShader.FindKernel("CSMain");
        else Debug.LogError("ComputeShader not found. Make sure it is located in the Resources folder or properly referenced.");
        
        if (_skyRadianceComputeShader is not null)
            _skyRadiancekernelIndex = _skyRadianceComputeShader.FindKernel("KSkyNoGeo_MeanRadiance");
        else Debug.LogError("ComputeShader not found. Make sure it is located in the Resources folder or properly referenced.");
        
        
    }
    static void CopyArrayToCubemap(RenderTexture textureArray, RenderTexture cubemap)
    {
        for (int i = 0; i < 6; i++)
        {
            // Copy the slice of the texture array to the corresponding cubemap face
            Graphics.CopyTexture(textureArray, i, 0, cubemap, i, 0);
        }
    }
    static void GenerateSeamlessMips(RenderTexture cube)
    {
        int mipCount = cube.mipmapCount;

        // We’ll read from mip 0 (already scattered) and write each lower mip.
        for (int mip = 1; mip < mipCount; mip++)
        {
            int res = Mathf.Max(1, cube.width >> mip);

            var tmp = new RenderTexture(res, res, 0, cube.format)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 6,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            tmp.Create();

            _scatteringComputeShader.SetTexture(_scatteringkernelIndex, "SourceCubeMap", cube);
            _scatteringComputeShader.SetTexture(_scatteringkernelIndex, "ResultTextureArray", tmp);

            _scatteringComputeShader.SetInt("Resolution", res);
            _scatteringComputeShader.SetInt("SourceMip", 0);
            
            float sigma = 0.5f * mip;          // start small, tune by eye
            int samples = Mathf.Clamp(48*mip, 8, 512);
            _scatteringComputeShader.SetFloat("ScatteringFactor", sigma);
            _scatteringComputeShader.SetInt("sampleCount", samples);

            int gx = (res + 7) / 8;
            int gy = (res + 7) / 8;
            _scatteringComputeShader.Dispatch(_scatteringkernelIndex, gx, gy, 6);

            for (int face = 0; face < 6; face++)
                Graphics.CopyTexture(tmp, face, 0, cube, face, mip);

            if (Application.isPlaying) Object.Destroy(tmp);
            else Object.DestroyImmediate(tmp);
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
            _scatteringkernelIndex = _scatteringComputeShader.FindKernel("CSMain");
        }

        // Set the compute shader parameters
        _scatteringComputeShader.SetTexture(_scatteringkernelIndex, "SourceCubeMap", sourceCubemap);
        _scatteringComputeShader.SetTexture(_scatteringkernelIndex, "ResultTextureArray", textureArray); 
        _scatteringComputeShader.SetFloat("ScatteringFactor", scatteringFactor);
        _scatteringComputeShader.SetInt("Resolution", resolution);  
        _scatteringComputeShader.SetInt("sampleCount", sampleCount);  

        // Dispatch the compute shader
        int gx = (resolution + 7) / 8;
        int gy = (resolution + 7) / 8;
        _scatteringComputeShader.Dispatch(_scatteringkernelIndex, gx, gy, 6);

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

    private static List<(Vector3 point, MonoSH data)> _nearestPointsData = new List<(Vector3 point, MonoSH data)>(4);
    private static MonoSH _interpolatedSH = MonoSH.White();
    private static int _previousTetIndex = -1;
    static MonoSH TetrahedronUpdate(Vector3 targetPosition)
    {
        // Create an instance of TetrahedralMesh and populate it

        // Populate tetMesh.Vertices and tetMesh.Tetrahedra
        // Ensure that for each Tetrahedron, you set up the correct neighbor indices

        // Precompute the Barycentric matrices
        //_skyOcclusionData.PrecomputeBarycentricMatrices();

        // Optionally, if we have a previous tetrahedron index
        //int previousTetIndex = -1;
        //List<int> visitedTetsint;
        // Find the containing tetrahedron
        Profiler.BeginSample("FindContainingTetrahedron");
        int containingTetIndex = _skyOcclusionData.FindContainingTetrahedron(targetPosition, _previousTetIndex/*, out visitedTetsint*/);
        _previousTetIndex = containingTetIndex;
        Profiler.EndSample();
//        Debug.Log(containingTetIndex);
        if (containingTetIndex != -1)
        {
            // Point is inside the mesh, and containingTetIndex is the index of the containing tetrahedron
            Tetrahedron containingTet = _skyOcclusionData.tetrahedrons[containingTetIndex];
            // Compute barycentric coordinates
            Vector4 barycentricCoords = _skyOcclusionData.ComputeBarycentricCoordinates(targetPosition, containingTet);
            // use baryCoords to interpolate values
            if (barycentricCoords == Vector4.zero)
            {
                //Debug.LogError("Failed to compute barycentric coordinates.");
                return MonoSH.White();
            }

            // Interpolate data
            return MonoSH.Interpolate(
                _skyOcclusionData.SkySH[containingTet.Vert0],
                _skyOcclusionData.SkySH[containingTet.Vert1],
                _skyOcclusionData.SkySH[containingTet.Vert2],
                _skyOcclusionData.SkySH[containingTet.Vert3], barycentricCoords, _interpolatedSH);
        }
        else
        {
            // Point is outside the mesh
            return MonoSH.White();
        }
    }
    // static void KDStart()
    // {
    //
    //     try
    //     {
    //         if (_skyOcclusionData.skyOccPos.ToList().Count != _skyOcclusionData.SkySH.ToList().Count) _kdtreevalid = false;
    //
    //         tree = new KdTree<MonoSH>(_skyOcclusionData.skyOccPos.ToList(), _skyOcclusionData.SkySH.ToList());
    //     
    //         _kdtreevalid = true;
    //         Debug.Log("KDTree has been successfully initialized.");
    //     }
    //     catch (Exception ex)
    //     {
    //         _kdtreevalid = false;
    //         Debug.LogError("KDTree initialization failed: " + ex.Message);
    //     }
    // }
    
    // static MonoSH KDUpdate(Vector3 targetPosition)
    // {
    //     
    //     // Declare or reuse the pre-allocated list
    //     // Ensure it's declared outside the method if you plan to reuse it to avoid allocations
    //     if (_nearestPointsData == null)
    //         _nearestPointsData = new List<(Vector3 point, MonoSH data)>(4);
    //     else
    //         _nearestPointsData.Clear();
    //
    //     // Call the updated method
    //     tree.KNearestNeighbors(targetPosition, 4, _nearestPointsData);
    //
    //     if (_nearestPointsData.Count < 4)
    //     {
    //        // Debug.LogError("Not enough points found.");
    //         return MonoSH.White();
    //     }
    //
    //     // Extract points and data
    //     Vector3 a = _nearestPointsData[0].point;
    //     Vector3 b = _nearestPointsData[1].point;
    //     Vector3 c = _nearestPointsData[2].point;
    //     Vector3 d = _nearestPointsData[3].point;
    //
    //     MonoSH dataA = _nearestPointsData[0].data;
    //     MonoSH dataB = _nearestPointsData[1].data;
    //     MonoSH dataC = _nearestPointsData[2].data;
    //     MonoSH dataD = _nearestPointsData[3].data;
    //
    //     Vector4 barycentricCoords = SkyOcclusion.ComputeBarycentricCoordinates(targetPosition, a, b, c, d);
    //
    //     if (barycentricCoords == Vector4.zero)
    //     {
    //         //Debug.LogError("Failed to compute barycentric coordinates.");
    //         return MonoSH.White();
    //     }
    //
    //     // Interpolate data
    //     return MonoSH.Interpolate(dataA, dataB, dataC, dataD, barycentricCoords, _interpolatedSH);
    // }
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

    #if UNITY_EDITOR
    [InitializeOnLoadMethod]
    static void EditorRegisterCleanup()
    {
        AssemblyReloadEvents.beforeAssemblyReload += ReleaseSkyManagerRuntimeResources;
    }
    #endif
}