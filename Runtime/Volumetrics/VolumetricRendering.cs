using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using System.Runtime.CompilerServices;


#if UNITY_EDITOR
using UnityEditor;
#endif

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

//TODO: Add semi dynamic lighting which is generated in the clipmap and not previously baked out. Will need smarter clipmap gen to avoid hitching.
//Add cascading clipmaps to have higher detail up close and include father clipping without exploding memory.
//Convert this to a render feature. This should remove the need for the platform switcher too because that would be handled by the quality settings pipeline asset instead


//[RequireComponent(typeof( Camera ) )]
[ExecuteInEditMode]
public class VolumetricRendering : MonoBehaviour
{

    #region variables
    static ProfilingSampler profileUpdateFunc = new ProfilingSampler("VolumetricRendering.UpdateFunc");
    //static ProfilingSampler profileUpdateClipmap = new ProfilingSampler("VolumetricRendering.UpdateClipmap");

    //public float tempOffset = 0;
    Texture3D BlackTex; //Temp texture for 
    //Color clearColor = new Color(0.0f, 0.0f, 0.0f, 0f);

    static VolumetricRendering lastClipmapUpdate;
    //static VolumetricRendering lastBlur;
    static VolumetricRendering lastFroxelFog;
    static VolumetricRendering lastFroxelIntegrate;


    public Camera cam; //Main camera to base settings on
    private Camera activeCam;
    private UniversalAdditionalCameraData activeCamData;
    // Prevent script from trying to initialize itself twice
    bool hasInitialized;

    // Sometimes, the volumetric register gets filled after the volumetric script initializes.
    // This means that the clipmaps will be empty until the player moves far enough to trigger
    // a clipmap update. Instead, set a bool that triggers the clipmaps to try to update every
    // frame until the volumtric registry contains >0 volumes
    bool VolumetricRegisterEmpty;
    
    [HideInInspector] public bool VolumetricRegisterForceRefresh = false;


    // Debug counter to print a message every x frames
    int debugHeartBeatCount = 30;
    int debugHeartBeat = 0;

    public VolumetricData volumetricData;
    [Range(0, 1)]
    public float reprojectionAmount = 0.95f;
    //   [Tooltip("Does a final blur pass on the rendered fog")]
    //    public bool FroxelBlur = false;

    //[HideInInspector]
    // public enum BlurType {None, Gaussian};
    // public BlurType FroxelBlur = BlurType.None;
    [Range(0, 1)]
    public float SliceDistributionUniformity = 0.5f;
    public float4 _FoveaCenterUV = new float4(.5f,.5f,.5f,.5f);   // per-eye [0..1], default (0.5, 0.5)
    public float  _FoveaStrength = 1.0f;   // a >= 0, 0 disables (try 0.3..0.8 on Quest)
    public  bool _FoveationEnabled =  true;
    [Range(0,1)]
    public float _FoveationInnerRadius =  0.3f;
    [Range(0,1)]

    public float _FoveationOuterRadius =  0.4f;
    [HideInInspector] public bool enableEditorPreview = false;
    
    public float _bakedTurbulence = 5f;
    
    Vector3 ClipmapTransform; //Have this follow the camera and resample when the camera moves enough 
    // ==== Internals (replace old A/B/C/D + flip booleans) ====
    const int kClipLevels = 4;
    RenderTexture[] clipmapFinal;           // one final 3D RT per level
    RenderTexture clipScratchA, clipScratchB; // shared scratch pair (recreated on demand)
    int scratchRes = -1;                    // current scratch resolution

    Vector3[] lastBuildCenters;             // per-level last camera center used to build
    [SerializeField, Range(1,4)]
    int maxClipLevelsPerFrame = 1;          // smear builds over time
    int nextLevelIndex = 0;                 // round-robin pointer


// Copy kernel (bind once in Intialize)
    int ID_ClipMapCopyKern;
    
    private ComputeBuffer participatingMediaSphereBuffer;
    
    [StructLayout(LayoutKind.Sequential)]
    struct MediaSphere
    {
        public Vector3 CenterPosition;
        public float LocalExtinction;
        public float LocalFalloff;
        public float LocalRange;
    }

    private const int MediaSphereStride = (3 + 1 + 1 + 1) * sizeof(float);
    int MediaCount;

    
    //public Matrix4x4 randomatrix;

    //Required shaders
    [SerializeField, HideInInspector] ComputeShader FroxelFogCompute;
    [SerializeField, HideInInspector] ComputeShader FroxelIntegrationCompute;
    [SerializeField, HideInInspector] ComputeShader FroxelLocalFogCompute;
    [SerializeField, HideInInspector] ComputeShader ClipmapCompute;
    //[SerializeField, HideInInspector] ComputeShader BlurCompute;

    //Texture buffers

    // bool FlipClipBufferNear = true;
    // bool FlipClipBufferFar = true;


    RenderTexture FroxelBufferA;   //Single froxel projection use for scattering and history reprojection
    RenderTexture FroxelBufferB;   //for history reprojection

    RenderTexture IntegrationBuffer;    //Integration and stereo reprojection
                                        //  RenderTexture IntegrationBufferB;    //Integration and stereo reprojection
    // RenderTexture BlurBuffer;    //blur
    // RenderTexture BlurBufferB;    //blur

    RenderTexture VolumetricResult;

    // This is a sequence of 7 equidistant numbers from 1/14 to 13/14.
    // Each of them is the centroid of the interval of length 2/14.
    // They've been rearranged in a sequence of pairs {small, large}, s.t. (small + large) = 1.
    // That way, the running average position is close to 0.5.
    // | 6 | 2 | 4 | 1 | 5 | 3 | 7 |
    // |   |   |   | o |   |   |   |
    // |   | o |   | x |   |   |   |
    // |   | x |   | x |   | o |   |
    // |   | x | o | x |   | x |   |
    // |   | x | x | x | o | x |   |
    // | o | x | x | x | x | x |   |
    // | x | x | x | x | x | x | o |
    // | x | x | x | x | x | x | x |
    float[] m_zSeq = { 7.0f / 14.0f, 3.0f / 14.0f, 11.0f / 14.0f, 5.0f / 14.0f, 9.0f / 14.0f, 1.0f / 14.0f, 13.0f / 14.0f };


    // Ref: https://en.wikipedia.org/wiki/Close-packing_of_equal_spheres
    // The returned {x, y} coordinates (and all spheres) are all within the (-0.5, 0.5)^2 range.
    // The pattern has been rotated by 15 degrees to maximize the resolution along X and Y:
    // https://www.desmos.com/calculator/kcpfvltz7c
    static void GetHexagonalClosePackedSpheres7(Vector2[] coords)
    {

        float r = 0.17054068870105443882f;
        float d = 2 * r;
        float s = r * Mathf.Sqrt(3);

        // Try to keep the weighted average as close to the center (0.5) as possible.
        //  (7)(5)    ( )( )    ( )( )    ( )( )    ( )( )    ( )(o)    ( )(x)    (o)(x)    (x)(x)
        // (2)(1)(3) ( )(o)( ) (o)(x)( ) (x)(x)(o) (x)(x)(x) (x)(x)(x) (x)(x)(x) (x)(x)(x) (x)(x)(x)
        //  (4)(6)    ( )( )    ( )( )    ( )( )    (o)( )    (x)( )    (x)(o)    (x)(x)    (x)(x)
        coords[0] = new Vector2(0, 0);
        coords[1] = new Vector2(-d, 0);
        coords[2] = new Vector2(d, 0);
        coords[3] = new Vector2(-r, -s);
        coords[4] = new Vector2(r, s);
        coords[5] = new Vector2(r, -s);
        coords[6] = new Vector2(-r, s);

        // Rotate the sampling pattern by 15 degrees.
        const float cos15 = 0.96592582628906828675f;
        const float sin15 = 0.25881904510252076235f;

        for (int i = 0; i < 7; i++)
        {
            Vector2 coord = coords[i];

            coords[i].x = coord.x * cos15 - coord.y * sin15;
            coords[i].y = coord.x * sin15 + coord.y * cos15;
        }
        
    }

    Vector2[] m_xySeq = new Vector2[7];

    //camera.aspect no longer returns the XR aspect ratio but rather the final viewport's. Rather worthless now.
    float CamAspectRatio;


    //AABB 

    //Stored compute shader IDs and numbers

    protected int ScatteringKernel = 0;
    protected int IntegrateKernel = 0;
    // protected int BlurKernelX = 0;
    // protected int BlurKernelY = 0;

    Matrix4x4 matScaleBias;
    //Vector3 ThreadsToDispatch;
    private int gxs, gys, gzs , igx ,igy;
    private Vector3 invDimensions; //inverse dimensions
    private Vector3 invDimensionsStereo; //inverse dimensions
    

    //Stored shader variable name IDs

    // Constants so the VolumetricConstant script can access the names
    // The texture/buffers associated with each name will get set
    // as shader globals just before the camera associated with this
    // script renders by the render pipeline, so only that camera uses
    // the volumetrics rendered by this script. 
    public const string resultTextureName = "_VolumetricResult";
    public const string shaderCBName = "VolumetricsCB";
    public const string volumetricKWName = "_VOLUMETRICS_ENABLED";

    int ID_VolumetricResult = Shader.PropertyToID(resultTextureName);
    int ID_VolumetricsCB = Shader.PropertyToID(shaderCBName); // not actually used now since this script doesn't set the constant buffer as the global

    int ID_Result = Shader.PropertyToID("Result");
    int ID_InLightingTexture = Shader.PropertyToID("InLightingTexture");
    int ID_InTex = Shader.PropertyToID("InTex");
    int ID_LightProjectionTextureArray = Shader.PropertyToID("LightProjectionTextureArray");
    int ID_VolumetricClipmapTexture0 = Shader.PropertyToID("_VolumetricClipmapTexture0");
    int ID_VolumetricClipmapTexture1 = Shader.PropertyToID("_VolumetricClipmapTexture1");
    int ID_VolumetricClipmapTexture2 = Shader.PropertyToID("_VolumetricClipmapTexture2");
    int ID_VolumetricClipmapTexture3 = Shader.PropertyToID("_VolumetricClipmapTexture3");
    int ID_PreResult = Shader.PropertyToID("PreResult");
    int ID_VolumeMap = Shader.PropertyToID("VolumeMap");
    int ID_PreviousFrameLighting = Shader.PropertyToID("PreviousFrameLighting");
    int ID_HistoryBuffer = Shader.PropertyToID("HistoryBuffer");
    int ID_LeftEyeMatrix = Shader.PropertyToID("LeftEyeMatrix");
    int ID_RightEyeMatrix = Shader.PropertyToID("RightEyeMatrix");
    int ID_ClipmapScale0 = Shader.PropertyToID("_ClipmapScale0");
    int ID_ClipmapScale1 = Shader.PropertyToID("_ClipmapScale1");
    int ID_ClipmapScale2 = Shader.PropertyToID("_ClipmapScale2");
    int ID_ClipmapScale3 = Shader.PropertyToID("_ClipmapScale3");
    // ✨ Also keep a separate one for the *generator* compute (it uses "ClipmapScale")
    int ID_Gen_ClipmapScale       = Shader.PropertyToID("ClipmapScale");
    int ID_ClipmapWorldPosition = Shader.PropertyToID("ClipmapWorldPosition");
    int ID_VBufferUnitDepthTexelSpacing = Shader.PropertyToID("_VBufferUnitDepthTexelSpacing");
    int ID_VolZBufferParams = Shader.PropertyToID("_VolZBufferParams");
    int ID_invDimensions = Shader.PropertyToID("_invDimensions");
    int ID_FroxelDepthCount = Shader.PropertyToID("_FroxelDepthCount");
    int ID_GlobalExtinction = Shader.PropertyToID("_GlobalExtinction");
    int ID_StaticLightMultiplier = Shader.PropertyToID("_StaticLightMultiplier");
    int ID_GlobalScattering = Shader.PropertyToID("_GlobalScattering");
    int ID_VolumeWorldSize = Shader.PropertyToID("VolumeWorldSize");
    int ID_VolumeWorldPosition = Shader.PropertyToID("VolumeWorldPosition");
    int ID_RegionOffset = Shader.PropertyToID("_RegionOffset");
    int ID_RegionSize = Shader.PropertyToID("_RegionSize");
    
    int ID_StereoBaseline = Shader.PropertyToID("_StereoBaseline");
    int ID_StereoFocalLen = Shader.PropertyToID("_StereoFocalLen");
    
    private static readonly int VolumeDensity = Shader.PropertyToID("VolumeDensity");
    private static readonly int VolumeFalloff = Shader.PropertyToID("VolumeFalloff");

    private int ID_media_sphere_buffer_length = Shader.PropertyToID("media_sphere_buffer_length");
    private int ID_media_sphere_buffer = Shader.PropertyToID("media_sphere_buffer");
    
    int ID_FoveaCenterUV = Shader.PropertyToID("_FoveaCenterUV");
    int ID_FoveaStrength = Shader.PropertyToID("_FoveaStrength");


    int ID_ClipMapGenKern;
    int ID_ClipMapClearKern;
    int ID_ClipMapHeightKern;

    //Froxel Ids
    int PerFrameConstBufferID = Shader.PropertyToID("PerFrameCB");

    int PreviousFrameMatrixID = Shader.PropertyToID("PreviousFrameMatrix");

    
    int ClipmapScaleID = Shader.PropertyToID("_ClipmapScale");
    int ClipmapPositionID_0 = Shader.PropertyToID("_ClipmapPosition0");
    int ClipmapPositionID_1 = Shader.PropertyToID("_ClipmapPosition1");
    int ClipmapPositionID_2 = Shader.PropertyToID("_ClipmapPosition2");
    int ClipmapPositionID_3 = Shader.PropertyToID("_ClipmapPosition3");

    //Temp Jitter stuff
    //int tempjitter = 0; //TEMP jitter switcher thing 
    //[Header("Extra variables"), Range(0, 1)]
    //float[] jitters = new float[2] { 0.0f, 0.5f };

    //GlobalKeyword VolumetricsKW;
    //Previous view matrix data

    Matrix4x4 PreviousFrameMatrix = Matrix4x4.identity;
    Matrix4x4 LeftEyeMatrix;
    Matrix4x4 RightEyeMatrix;
    Vector3 PreviousCameraPosition;
    Vector3 previousPos;
    Quaternion previousQuat;
    Vector4 VolZBufferParams;

    float ZPlaneTexelSpacing;

    //General fog settings

    [Header("Base values that are overridden by Volumes")]
    public Color albedo = Color.white;
    //    public Color extinctionTint = Color.white;
    public float meanFreePath = 15.0f;
    public float StaticLightMultiplier = 1.0f;

    private ComputeBuffer ShaderConstantBuffer;
    private ComputeBuffer ComputePerFrameConstantBuffer;
    private ComputeBuffer StepAddPerFrameConstantBuffer;

    [StructLayout(LayoutKind.Sequential)]
    struct ShaderConstants
    {
        public Matrix4x4 TransposedCameraProjectionMatrix;
        public Matrix4x4 CameraProjectionMatrix;
        public Vector4 _VBufferDistanceEncodingParams;
        public Vector4 _VolumetricResultDim;
        public Vector3 _VolCameraPos;
    }
    public const int ShaderConstantsCount = 43;
    public const int ShaderConstantsSize = ShaderConstantsCount * sizeof(float);


    [StructLayout(LayoutKind.Sequential)]
    struct ScatteringPerFrameConstants
    {
        public Matrix4x4    _VBufferCoordToViewDirWS;
        public Matrix4x4    _PrevViewProjMatrix;
        public Matrix4x4    _ViewMatrix;
        public Matrix4x4    TransposedCameraProjectionMatrix;
        public Matrix4x4    CameraProjectionMatrix;
        public Vector4      _VBufferDistanceEncodingParams;
        public Vector4      _VBufferDistanceDecodingParams;
        public Vector4      SeqOffset;
        public Vector4      CameraPosition;
        public Vector4      CameraMotionVector;
    }

    private const int ScatterPerFrameCount = 100;


    [StructLayout(LayoutKind.Sequential)]
    struct StepAddPerFrameConstants
    {
        public Vector4 _VBufferDistanceDecodingParams;
        public Vector3 SeqOffset;
    }

    private const int StepAddPerFrameCount = 7;

    // private static float[] VolStructToArray<T>(T rawData, int count, int size) where T : struct
    // {
    //     var pinnedRawData = GCHandle.Alloc(rawData, GCHandleType.Pinned);
    //     try
    //     {
    //         var pinnedRawDataPtr = pinnedRawData.AddrOfPinnedObject();
    //         float[] data = new float[size];
    //         Marshal.Copy(pinnedRawDataPtr, data, 0, count);
    //         return data;
    //     }
    //     finally
    //     {
    //         pinnedRawData.Free();
    //     }
    // }

    
    // === New Clipmap fields ===
    // Vector3 activeSampleCenter;   // what the scatter pass uses (stable)
    // Vector3 rebuildCenter;        // the center the clipmaps are being rebuilt toward
    // bool    generationInProgress; // true while any levels for the new center are pending
    // int     levelsRemaining;      // countdown for the current generation

    #endregion

    private void Awake()
    {
#if UNITY_EDITOR

        if (Application.isPlaying || activeCam == null)
        {
            activeCam = cam;
        }
        else
        {
            //Debug.Log("Volumetric Editor On Awake");
            activeCam = SceneView.lastActiveSceneView.camera;
        }
#else
        activeCam = cam;
        activeCamData = cam.GetComponent<UniversalAdditionalCameraData>();
        if (activeCam.usePhysicalProperties == true) Debug.LogError("Physical camera is not properlly supportted by Unity and WILL mess up XR calulations like voulmetrics and LoDs");
        //  cam = GetComponent<Camera>();
#endif
    }

    void Start() {
//#if !UNITY_EDITOR
        Intialize();
//#endif
    }   

  

    bool VerifyVolumetricRegisters()
    {
        //Add realtime light check here too
        if (VolumetricRegisters.volumetricAreas.Count > 0) //brute force check
                                                           //  if (VolumetricRegisters.volumetricAreas.Count > 0)
        {
            Debug.Log(VolumetricRegisters.volumetricAreas.Count + " Volumes ready to render");
            return true;
        }
        Debug.Log("No Volumetric volumes in " + SceneManager.GetActiveScene().name + ". Disabling froxel rendering.");
        this.enabled = false;
        return false;
    }

    void CheckOverrideVolumes() //TODO: Is there a better way to do this?
    {
        var stack = VolumeManager.instance.stack;

        var Volumetrics = stack.GetComponent<Volumetrics>();
        if (Volumetrics != null)
            Volumetrics.PushFogShaderParameters();
    }

    // void IntializeBlur(RenderTextureDescriptor rtdiscrpt)
    // {
    //     BlurBuffer = new RenderTexture(rtdiscrpt);
    //     BlurBuffer.name = activeCam.name + "_BlurBuffer";
    //     BlurBuffer.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
    //     BlurBuffer.enableRandomWrite = true;
    //     BlurBuffer.Create();
    //     Clear3DTexture(BlurBuffer);
    //
    //
    //     BlurBufferB = new RenderTexture(rtdiscrpt);
    //     BlurBuffer.name = activeCam.name + "_BlurBufferB";
    //     BlurBufferB.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
    //     BlurBufferB.enableRandomWrite = true;
    //     BlurBufferB.Create();
    //     Clear3DTexture(BlurBufferB);
    //
    //     // BlurKernelX = BlurCompute.FindKernel("VolBlurX");
    //     // BlurKernelY = BlurCompute.FindKernel("VolBlurY");
    // }

    void Intialize()
    {
        if (hasInitialized)
        {
            return;
        }
        if (cam == null)
        {
            Debug.LogWarning("Volumetric Rendering Script with no camera assigned, disabling");
            this.enabled = false;
            return;
        }

#if UNITY_EDITOR
        AssemblyReloadEvents.beforeAssemblyReload += CleanupOnReload;
#endif
        activeCam = cam;
        activeCamData = activeCam?.GetComponent<UniversalAdditionalCameraData>();
#if UNITY_EDITOR
        if (!Application.isPlaying && !enableEditorPreview)
        {
            //Debug.Log("Intialize disabled volumetrics");
            disable();
            return;
        }
#endif
        if (activeCamData == null)
        {
            activeCam = null;
            Debug.LogWarning("Volumetric Rendering: Assigned camera is missing a Universal Additional Camera Data component, disabling");
            this.enabled = false;
            return;
        }


        //Debug.Log("Volumetric Renderer Initialized");
        //DebugPrintTextureIDs();
        ShaderConstantBuffer = new ComputeBuffer(1, ShaderConstantsSize, ComputeBufferType.Constant);
        ComputePerFrameConstantBuffer = new ComputeBuffer(1, ScatterPerFrameCount * sizeof(float), ComputeBufferType.Constant);
        StepAddPerFrameConstantBuffer = new ComputeBuffer(1, StepAddPerFrameCount * sizeof(float), ComputeBufferType.Constant);
        int mediaCount = VolumetricRegisters.VolumetricMediaEntities.Count;
        MediaCount = Math.Max(mediaCount, 1);
        participatingMediaSphereBuffer = new ComputeBuffer(MediaCount, MediaSphereStride, ComputeBufferType.Structured);


        //activeCameraState = activeCam.isActiveAndEnabled;
        CheckOverrideVolumes();

        //Making prescaled matrix 
        matScaleBias = Matrix4x4.identity;
        matScaleBias.m00 = -0.5f;
        matScaleBias.m11 = -0.5f;
        matScaleBias.m22 = 0.5f;
        matScaleBias.m03 = 0.5f;
        matScaleBias.m13 = 0.5f;
        matScaleBias.m23 = 0.5f;

        //Create 3D Render Texture 1
        RenderTextureDescriptor rtdiscrpt = new RenderTextureDescriptor();
        rtdiscrpt.enableRandomWrite = true;
        rtdiscrpt.dimension = TextureDimension.Tex3D;
        rtdiscrpt.width = volumetricData.FroxelWidthResolution;
        rtdiscrpt.height = volumetricData.FroxelHeightResolution;
        rtdiscrpt.volumeDepth = volumetricData.FroxelDepthResolution;
        rtdiscrpt.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
        rtdiscrpt.msaaSamples = 1;

        FroxelBufferA = new RenderTexture(rtdiscrpt);
        FroxelBufferA.name = activeCam.name + "_FroxelBufferA";

        FroxelBufferA.Create();

        //Ugh... extra android buffer mess. Can I use a custom RT double buffer instead?
        FroxelBufferB = new RenderTexture(rtdiscrpt);
        FroxelBufferB.name = activeCam.name + "_FroxelBufferB";
        FroxelBufferB.Create();

        rtdiscrpt.width = volumetricData.FroxelWidthResolution * 2; // Make double wide texture for stereo use. Make smarter for non VR use case?
        IntegrationBuffer = new RenderTexture(rtdiscrpt);
        IntegrationBuffer.name = activeCam.name + "_IntegrationBuffer";
        IntegrationBuffer.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
        IntegrationBuffer.filterMode = FilterMode.Trilinear;
        IntegrationBuffer.enableRandomWrite = true;
        IntegrationBuffer.Create();

 

    //    if (FroxelBlur == BlurType.Gaussian) IntializeBlur(rtdiscrpt);

        // LightObjects = new List<LightObject>();

        ScatteringKernel = FroxelFogCompute.FindKernel("Scatter");

        ZPlaneTexelSpacing = ComputZPlaneTexelSpacing(1, activeCam.fieldOfView, volumetricData.FroxelHeightResolution);
        ///Second compute pass setup

        IntegrateKernel = FroxelIntegrationCompute.FindKernel("StepAdd");
        

        //Make view projection matricies

        Matrix4x4 CenterProjectionMatrix = matScaleBias * Matrix4x4.Perspective(activeCam.fieldOfView, CamAspectRatio, volumetricData.near, volumetricData.far);
        Matrix4x4 LeftProjectionMatrix = matScaleBias * Matrix4x4.Perspective(activeCam.fieldOfView, CamAspectRatio, volumetricData.near, volumetricData.far) * Matrix4x4.Translate(new Vector3(activeCam.stereoSeparation * 0.5f, 0, 0)); //temp ipd scaler. Combine factors when confirmed
        Matrix4x4 RightProjectionMatrix = matScaleBias * Matrix4x4.Perspective(activeCam.fieldOfView, CamAspectRatio, volumetricData.near, volumetricData.far) * Matrix4x4.Translate(new Vector3(-activeCam.stereoSeparation * 0.5f, 0, 0));


        Matrix4x4 CenterProjectionMatrixInverse = CenterProjectionMatrix.inverse;
        LeftEyeMatrix = LeftProjectionMatrix * CenterProjectionMatrixInverse;
        RightEyeMatrix = RightProjectionMatrix * CenterProjectionMatrixInverse;
       


        //Global Variable setup

        // if (FroxelBlur == BlurType.Gaussian)
        // {
        //     VolumetricResult = BlurBufferB;
        // }
        // else
        // {
            VolumetricResult = IntegrationBuffer;
        //}

        invDimensions.x = 1.0f / volumetricData.FroxelWidthResolution;
        invDimensions.y = 1.0f / volumetricData.FroxelHeightResolution;
        invDimensions.z = 1.0f / volumetricData.FroxelDepthResolution;


        invDimensionsStereo.x = invDimensions.x * .5f;
        invDimensionsStereo.y = invDimensions.y;
        invDimensionsStereo.z = invDimensions.z ;
        

        // Scatter: [numthreads(4,4,4)]
        uint sx, sy, sz;
        FroxelFogCompute.GetKernelThreadGroupSizes(ScatteringKernel, out sx, out sy, out sz);
         gxs = Mathf.CeilToInt((float)volumetricData.FroxelWidthResolution  / sx);
         gys = Mathf.CeilToInt((float)volumetricData.FroxelHeightResolution / sy);
         gzs = Mathf.CeilToInt((float)volumetricData.FroxelDepthResolution  / sz);
         
         
    // StepAdd: [numthreads(8,8,1)] — loops Z internally → 2D dispatch, stereo-wide
         uint isx, isy, isz;
         FroxelIntegrationCompute.GetKernelThreadGroupSizes(IntegrateKernel, out isx, out isy, out isz);
         int totalWidth = volumetricData.FroxelWidthResolution * 2;
         igx = Mathf.CeilToInt((float)totalWidth / isx);
         igy = Mathf.CeilToInt((float)volumetricData.FroxelHeightResolution / isy);
         //FroxelIntegrationCompute.Dispatch(IntegrateKernel, igx, igy, 1);

        //    ComputZPlaneTexelSpacing(1.0f, vFoV, parameters.resolution.y);

        // Unused as far as I can tell, declared in the VolumetricCore but not actually used
        //Shader.SetGlobalVector("_VolumePlaneSettings", new Vector4(volumetricData.near, volumetricData.far, volumetricData.far - volumetricData.near, volumetricData.near * volumetricData.far));

        VolZBufferParams = new Vector4();
        VolZBufferParams.x = 1.0f - volumetricData.far / volumetricData.near;
        VolZBufferParams.y = volumetricData.far / volumetricData.near;
        VolZBufferParams.z = VolZBufferParams.x / volumetricData.far;
        VolZBufferParams.w = VolZBufferParams.y / volumetricData.far;


        ID_ClipMapGenKern = ClipmapCompute.FindKernel("ClipMapGen");
        ID_ClipMapClearKern = ClipmapCompute.FindKernel("ClipMapClear");
        ID_ClipMapHeightKern = ClipmapCompute.FindKernel("ClipMapHeight");
        ID_ClipMapCopyKern = ClipmapCompute.FindKernel("ClipMapCopy");

        //Debug.Log("Dispatching " + ThreadsToDispatch);

        SkyManager.CheckSky();

        Clear3DTexture(FroxelBufferA);
        Clear3DTexture(FroxelBufferB);
        Clear3DTexture(IntegrationBuffer);

        SetVariables();
        SetupClipmap();
        //TickClipmapScheduler(true);
        SetFroxelFogUniforms(true);
        SetFroxelIntegrationUniforms(true);
       
        var L0 = Levels()[0];
        var start = SnapToVoxel(activeCam.transform.position, L0.ClipmapScale, L0.ClipMapResolution);
        //activeSampleCenter = rebuildCenter = start;
        // generationInProgress = false;
        // levelsRemaining = 0;

// Build once for the starting center
        for (int i = 0; i < kClipLevels; i++) pendingLevel[i] = true;
        //levelsRemaining = kClipLevels;
        ForceFullClipmapUpdate(); // will start the first builds immediately
        hasInitialized = true;
        VolumetricRegisters.RegisterVolumetricRenderer(this);
    }

    void SetFroxelFogUniforms(bool forceUpdate = false)
    {
        // required per-dispatch constants for Scatter
        FroxelFogCompute.SetFloat(ID_VBufferUnitDepthTexelSpacing, ZPlaneTexelSpacing);
        FroxelFogCompute.SetVector(ID_invDimensions, invDimensions);
        // Scales (match shader names)
        FroxelFogCompute.SetFloat(ID_ClipmapScale0, volumetricData.ClipmapLevel0.ClipmapScale); // _ClipmapScale
        FroxelFogCompute.SetFloat(ID_ClipmapScale1, volumetricData.ClipmapLevel1.ClipmapScale);
        FroxelFogCompute.SetFloat(ID_ClipmapScale2, volumetricData.ClipmapLevel2.ClipmapScale);
        FroxelFogCompute.SetFloat(ID_ClipmapScale3, volumetricData.ClipmapLevel3.ClipmapScale);

        // per-level centers
        FroxelFogCompute.SetVector(ClipmapPositionID_0, activeCenter[0]);
        FroxelFogCompute.SetVector(ClipmapPositionID_1, activeCenter[1]);
        FroxelFogCompute.SetVector(ClipmapPositionID_2, activeCenter[2]);
        FroxelFogCompute.SetVector(ClipmapPositionID_3, activeCenter[3]);

        // Final 3D textures for each level
        FroxelFogCompute.SetTexture(ScatteringKernel, ID_VolumetricClipmapTexture0,  clipmapFinal[0]);
        FroxelFogCompute.SetTexture(ScatteringKernel, ID_VolumetricClipmapTexture1, clipmapFinal[1]);
        FroxelFogCompute.SetTexture(ScatteringKernel, ID_VolumetricClipmapTexture2, clipmapFinal[2]);
        FroxelFogCompute.SetTexture(ScatteringKernel, ID_VolumetricClipmapTexture3, clipmapFinal[3]);
        
        Shader.SetGlobalInt("_FrameIndex", Time.renderedFrameCount);
        FroxelFogCompute.SetInt("_FrameIndex", Time.renderedFrameCount%4);
		// realtimes

        // FroxelFogCompute.SetVector("pointpos", VolumetricRegisters.realtimeVolumetricLights[0].transform.position);
        // FroxelFogCompute.SetVector("pointcolorint",VolumetricRegisters.realtimeVolumetricLights[0].color*VolumetricRegisters.realtimeVolumetricLights[0].intensity);
        // FroxelFogCompute.SetFloat("pointrange", VolumetricRegisters.realtimeVolumetricLights[0].range);
        FroxelFogCompute.SetTexture(ScatteringKernel, "Noise3d", volumetricData.DefaultTurbulentNoise);

      //  FroxelFogCompute.SetVector("_FoveaCenterUV", _FoveaCenterUV);
        FroxelFogCompute.SetBool("_FoveationEnabled", _FoveationEnabled);
        FroxelFogCompute.SetFloat("_FoveationInnerRadius", _FoveationInnerRadius);
        FroxelFogCompute.SetFloat("_FoveationOuterRadius", _FoveationOuterRadius);
        FroxelFogCompute.SetFloat("_bakedTurbulence", _bakedTurbulence);
    }


    void SetFroxelIntegrationUniforms(bool forceUpdate = false)
    {
        if (lastFroxelIntegrate != this || forceUpdate)
        {
            FroxelIntegrationCompute.SetMatrix(ID_LeftEyeMatrix, LeftEyeMatrix);
            FroxelIntegrationCompute.SetMatrix(ID_RightEyeMatrix, RightEyeMatrix);
            FroxelIntegrationCompute.SetVector(ID_VolZBufferParams, VolZBufferParams);
            FroxelIntegrationCompute.SetVector(ID_invDimensions, invDimensionsStereo);
            FroxelIntegrationCompute.SetInt(ID_FroxelDepthCount, volumetricData.FroxelDepthResolution);
            FroxelIntegrationCompute.SetTexture(IntegrateKernel, ID_Result, IntegrationBuffer);
            FroxelIntegrationCompute.SetTexture(IntegrateKernel, ID_InLightingTexture, FroxelBufferA);
            FroxelIntegrationCompute.SetConstantBuffer(PerFrameConstBufferID, StepAddPerFrameConstantBuffer, 0, StepAddPerFrameCount * sizeof(float));
            
            // --- Stereo parameters (geometrically correct) ---

            // IPD (eye-to-eye in world units)
            float ipdEyeToEye = activeCam.stereoSeparation;

            // we want center→eye baseline (B/2)
            float baselineCenterToEye = ipdEyeToEye * 0.5f;

            // vertical FOV of the camera
            float fovY = activeCam.fieldOfView * Mathf.Deg2Rad;

            // your XR-aware aspect ratio
            float aspect = CamAspectRatio;

            // horizontal FOV
            float fovX = 2.0f * Mathf.Atan(Mathf.Tan(fovY * 0.5f) * aspect);

            // focal length in NDC units
            float focalLen = 1.0f / Mathf.Tan(fovX * 0.5f);

            FroxelIntegrationCompute.SetFloat(ID_StereoBaseline, baselineCenterToEye);
            FroxelIntegrationCompute.SetFloat(ID_StereoFocalLen, focalLen);
            
            lastFroxelIntegrate = this;
        }
    }

    public void ClearAllBuffers()
    {
        if (clipmapFinal != null)
            for (int i = 0; i < clipmapFinal.Length; i++)
                if (clipmapFinal[i]) ClearClipmap(clipmapFinal[i]);

        if (clipScratchA) ClearClipmap(clipScratchA);
        if (clipScratchB) ClearClipmap(clipScratchB);

        Clear3DTexture(FroxelBufferA);
        Clear3DTexture(FroxelBufferB);
        Clear3DTexture(IntegrationBuffer);
    }


   
    
#region Clipmap funtions

    // Snap so clip voxels align across frames
    static Vector3 SnapToVoxel(Vector3 center, float scale, int res)
    {
        float v = scale / Mathf.Max(res, 1);
        return new Vector3(
            Mathf.Floor(center.x / v) * v,
            Mathf.Floor(center.y / v) * v,
            Mathf.Floor(center.z / v) * v
        );
    }

    static Vector3 SnapWithStride(Vector3 p, float scale, int res, int strideVoxels)
    {
        float vox = scale / res;
        float step = vox * Mathf.Max(1, strideVoxels);
        return new Vector3(
            Mathf.Round(p.x / step) * step,
            Mathf.Round(p.y / step) * step,
            Mathf.Round(p.z / step) * step
        );
    }

    bool NeedRecenter(Vector3 camPos, Vector3 center, float scale, int res, float deadbandVox)
    {
        float vox = scale / res;
        float d   = Mathf.Max(Mathf.Abs(camPos.x - center.x),
            Mathf.Abs(camPos.y - center.y),
            Mathf.Abs(camPos.z - center.z));
        return d >= deadbandVox * vox;
    }
    VolumetricData.ClipmapLevelData[] Levels()
    {
        return new VolumetricData.ClipmapLevelData[]
            { volumetricData.ClipmapLevel0, volumetricData.ClipmapLevel1, 
                volumetricData.ClipmapLevel2, volumetricData.ClipmapLevel3 };
    }
    void SetupClipmap()
    {
        var lvls = Levels();

        clipmapFinal = new RenderTexture[kClipLevels];
        lastBuildCenters = new Vector3[kClipLevels];
        pendingLevel = new bool[kClipLevels];

        for (int i = 0; i < kClipLevels; i++)
        {
            int res = Mathf.Max(1, lvls[i].ClipMapResolution);

            var desc = new RenderTextureDescriptor
            {
                enableRandomWrite = true,
                dimension = TextureDimension.Tex3D,
                width = res,
                height = res,
                volumeDepth = res,
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                msaaSamples = 1,
                useMipMap = true
            };

            clipmapFinal[i] = new RenderTexture(desc) { name = $"{activeCam.name}_ClipmapL{i}" };
            clipmapFinal[i].Create();
            ClearClipmap(clipmapFinal[i]);

            lastBuildCenters[i] = Vector3.negativeInfinity;
            pendingLevel[i] = true; // force initial build
        }

        // shared scratch will be created on demand by EnsureScratch(res)
        scratchRes = -1;
    }

    public void ForceFullClipmapUpdate()
    {
        for (int i = 0; i < kClipLevels; i++)
        {
            pendingLevel[i] = true;
        }
    }
    void EnsureScratch(int res)
    {
        if (scratchRes == res && clipScratchA && clipScratchB) return;

        // (Re)allocate scratch pair for this resolution
        if (clipScratchA) { clipScratchA.Release(); DestroyImmediate(clipScratchA); }
        if (clipScratchB) { clipScratchB.Release(); DestroyImmediate(clipScratchB); }

        var desc = new RenderTextureDescriptor
        {
            enableRandomWrite = true,
            dimension = TextureDimension.Tex3D,
            width = res,
            height = res,
            volumeDepth = res,
            graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
            msaaSamples = 1
        };

        clipScratchA = new RenderTexture(desc) { name = $"{activeCam.name}_ClipScratchA_{res}" };
        clipScratchB = new RenderTexture(desc) { name = $"{activeCam.name}_ClipScratchB_{res}" };
        clipScratchA.Create();
        clipScratchB.Create();

        ClearClipmap(clipScratchA);
        ClearClipmap(clipScratchB);
        scratchRes = res;
    }
// helper to compute intersection region in voxel space
    bool ComputeClipmapRegion(
        float3 clipMin, float clipScale, int res,
        float3 volMin, float3 volSize,
        out Vector3Int regionOffset, out Vector3Int regionSize)
    {
        float3 clipMax = clipMin + new float3(1,1,1) * clipScale;
        float3 volMax  = volMin  + volSize;

        // world-space intersection
        float3 interMin = Vector3.Max(clipMin, volMin);
        float3 interMax = Vector3.Min(clipMax, volMax);

        // no overlap
        if (interMin.x >= interMax.x || interMin.y >= interMax.y || interMin.z >= interMax.z)
        {
            regionOffset = default;
            regionSize   = default;
            return false;
        }

        float3 clipSize = clipMax - clipMin; // = clipScale * (1,1,1)

        // Convert to [0..1] clip UV
        float3 uvMin = (interMin - clipMin) / clipSize;
        float3 uvMax = (interMax - clipMin) / clipSize;

        // Convert to voxel indices [0..res)
        var minIdx = new Vector3Int(
            Mathf.Clamp(Mathf.FloorToInt(uvMin.x * res), 0, res - 1),
            Mathf.Clamp(Mathf.FloorToInt(uvMin.y * res), 0, res - 1),
            Mathf.Clamp(Mathf.FloorToInt(uvMin.z * res), 0, res - 1)
        );

        var maxIdx = new Vector3Int(
            Mathf.Clamp(Mathf.CeilToInt(uvMax.x * res) - 1, 0, res - 1),
            Mathf.Clamp(Mathf.CeilToInt(uvMax.y * res) - 1, 0, res - 1),
            Mathf.Clamp(Mathf.CeilToInt(uvMax.z * res) - 1, 0, res - 1)
        );

        var size = maxIdx - minIdx + Vector3Int.one;

        if (size.x <= 0 || size.y <= 0 || size.z <= 0)
        {
            regionOffset = default;
            regionSize   = default;
            return false;
        }

        regionOffset = minIdx;
        regionSize   = size;
        return true;
    }

 void BuildClipmapLevel(int level)
{
    var lvls = Levels();
    var L    = lvls[level];
    int res  = Mathf.Max(1, L.ClipMapResolution);

    EnsureScratch(res);

    var build = clipScratchA; // single build buffer

    var centerSnapped = targetCenter[level];
    var origin        = centerSnapped - 0.5f * L.ClipmapScale * Vector3.one;

    int groupsFull = Mathf.Max(res / 4, 1);

    // Clear whole clipmap level (full rebuild)
    ClipmapCompute.SetTexture(ID_ClipMapClearKern, ID_Result, build);
    ClipmapCompute.Dispatch(ID_ClipMapClearKern, groupsFull, groupsFull, groupsFull);

    // per-level params for kernels that live in ClipmapCompute
    ClipmapCompute.SetFloat (ID_Gen_ClipmapScale,   L.ClipmapScale);
    ClipmapCompute.SetVector(ID_ClipmapWorldPosition, origin);

    // -------- Local media volumes (per-entity region dispatch) --------
    for (int i = 0; i < VolumetricRegisters.VolumetricMediaEntities.Count; i++)
    {
        var media   = VolumetricRegisters.VolumetricMediaEntities[i];
        var mediaCS = media.computeShader;
        
        int mediaid =  mediaCS.FindKernel("ClipMapDensity");
        LocalKeyword textureSampleEnabled = new LocalKeyword(mediaCS, "_TextureSampleEnabled");
        LocalKeyword textureShapeType = new LocalKeyword(mediaCS, "_Sphere");
        
        // World AABB of this media
        Vector3 volMin  = media.Corner;
        Vector3 volSize = media.NormalizedScale;

        if (!ComputeClipmapRegion(origin, L.ClipmapScale, res,
                                  volMin, volSize,
                                  out var regionOffset, out var regionSize))
            continue; // this volume doesn't touch this clip level

        int groupsX = Mathf.CeilToInt(regionSize.x * 0.25f); // inverse of / 4.0f for numthreads(4,4,4)
        int groupsY = Mathf.CeilToInt(regionSize.y * 0.25f);
        int groupsZ = Mathf.CeilToInt(regionSize.z * 0.25f);

        mediaCS.SetFloat (ID_Gen_ClipmapScale,   L.ClipmapScale);
        mediaCS.SetVector(ID_ClipmapWorldPosition, origin);

        mediaCS.SetTexture(mediaid, ID_Result, build);

        mediaCS.SetVector(ID_VolumeWorldSize,     media.NormalizedScale);
        mediaCS.SetVector(ID_VolumeWorldPosition, media.Corner);
        
        if (media.volumeTexture != null)
        {
            mediaCS.SetTexture(mediaid, ID_VolumeMap, media.volumeTexture);
            mediaCS.SetKeyword(textureSampleEnabled, true);
        }
        else mediaCS.SetKeyword(textureSampleEnabled, false);

        if (media.shapeType == LocalVolumetricFog.ShapeType.Sphere)
        {
            mediaCS.SetKeyword(textureShapeType, true);
        }
        else mediaCS.SetKeyword(textureShapeType, false);

        
        mediaCS.SetFloat (VolumeDensity,        media.LocalExtinction());
        mediaCS.SetFloat(VolumeFalloff, media.falloffDistance);

        mediaCS.SetInts(ID_RegionOffset, regionOffset.x, regionOffset.y, regionOffset.z);
        mediaCS.SetInts(ID_RegionSize,   regionSize.x,   regionSize.y,   regionSize.z);

        mediaCS.Dispatch(mediaid, groupsX, groupsY, groupsZ);
        
    }

    // -------- Baked volumetric areas (same idea, different kernel) --------
    for (int i = 0; i < VolumetricRegisters.volumetricAreas.Count; i++)
    {
        var area = VolumetricRegisters.volumetricAreas[i];

        Vector3 volMin  = area.Corner;
        Vector3 volSize = area.NormalizedScale;

        if (!ComputeClipmapRegion(origin, L.ClipmapScale, res,
                                  volMin, volSize,
                                  out var regionOffset, out var regionSize))
            continue;

        int groupsX = Mathf.CeilToInt(regionSize.x * 0.25f);
        int groupsY = Mathf.CeilToInt(regionSize.y * 0.25f);
        int groupsZ = Mathf.CeilToInt(regionSize.z * 0.25f);

        ClipmapCompute.SetTexture(ID_ClipMapGenKern, ID_Result,    build);
        ClipmapCompute.SetTexture(ID_ClipMapGenKern, ID_VolumeMap, area.bakedTexture);
        ClipmapCompute.SetVector (ID_VolumeWorldSize,     area.NormalizedScale);
        ClipmapCompute.SetVector (ID_VolumeWorldPosition, area.Corner);

        ClipmapCompute.SetInts(ID_RegionOffset, regionOffset.x, regionOffset.y, regionOffset.z);
        ClipmapCompute.SetInts(ID_RegionSize,   regionSize.x,   regionSize.y,   regionSize.z);

        ClipmapCompute.Dispatch(ID_ClipMapGenKern, groupsX, groupsY, groupsZ);
    }

    // -------- Commit build buffer to final for this level --------
    ClipmapCompute.SetTexture(ID_ClipMapCopyKern, ID_PreResult, build);
    ClipmapCompute.SetTexture(ID_ClipMapCopyKern, ID_Result,    clipmapFinal[level]);
    ClipmapCompute.Dispatch(ID_ClipMapCopyKern, groupsFull, groupsFull, groupsFull);
    clipmapFinal[level].GenerateMips();
}



    Vector3 lastTriggerCenter; 

    Vector3[] activeCenter      = new Vector3[kClipLevels];  // what shaders currently sample for level i
    Vector3[] targetCenter      = new Vector3[kClipLevels];  // where we want level i to move next
    Vector3[] lastBuildCenter   = new Vector3[kClipLevels];  // what level i was last built for
    bool[]    pendingLevel      = new bool[kClipLevels];
    float[]   levelUrgency      = new float[kClipLevels]; // urgency buffer (parallel to levels)

    static float DistInf(Vector3 a, Vector3 b) // L∞ norm is cheap & grid-friendly
    {
        Vector3 d = a - b;
        return Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z));
    }


    int SelectMostUrgentPendingLevel(bool[] pending, float[] urgency)
    {
        int best = -1; float bestScore = -1f;
        for (int i = 0; i < pending.Length; i++)
            if (pending[i] && urgency[i] > bestScore) { best = i; bestScore = urgency[i]; }
        return best;
    }

    // Optional: prevent pathological divergence across neighbors
    bool NeedsNeighborGuard(int i, Vector3[] centers, float[] coarseVoxSize, float guardMul = 0.5f)
    {
        // keep center deltas <= half of the coarser voxel; adjust to your blend width
        if (i + 1 < centers.Length)
        {
            float maxD = guardMul * coarseVoxSize[i + 1];
            if (DistInf(centers[i], centers[i + 1]) > maxD) return true;
        }
        return false;
    }

    // --- scheduler -----------------------------------------------------

    private Vector3 _vec3halfOffset = Vector3.one * 0.5f;

    void TickClipmapScheduler()
    {
        var lvls   = Levels();
        var camPos = activeCam.transform.position;

        // 1) Decide which levels need work (per-level thresholds)
        for (int i = 0; i < kClipLevels; i++)
        {
            if (pendingLevel[i]) continue; // already queued

            float drift   = DistInf(camPos, activeCenter[i]);
            float thresh  = Mathf.Max(1e-6f, lvls[i].ClipmapResampleThresholdDistance);

            if (drift >= thresh)
            {
                // Snap *for this level* to keep grids congruent
                var snapped = SnapToVoxel(camPos, lvls[i].ClipmapScale, lvls[i].ClipMapResolution) + _vec3halfOffset;

                // Only do work if this would change what we built last time
                if (snapped != lastBuildCenter[i])
                {
                    targetCenter[i] = snapped;
                    pendingLevel[i] = true;

                    // Urgency: how far beyond threshold we are
                    levelUrgency[i] = drift / thresh;
                }
            }
        }

        // 2) Build up to your frame budget, most-urgent first
        int built = 0;
        while (built < maxClipLevelsPerFrame)
        {
            int li = SelectMostUrgentPendingLevel(pendingLevel, levelUrgency);
            if (li < 0) break;

            // Optional neighbor guard: if li would diverge too far from li+1, queue li+1 too
            // (lightweight nudge to keep blend regions happy)
            // if (NeedsNeighborGuard(li, activeCenter, /* per-level voxel sizes: */ lvlsVoxSize))
            // {
            //     int j = li + 1;
            //     if (!pendingLevel[j])
            //     {
            //         var snapJ = SnapToVoxel(camPos, lvls[j].ClipmapScale, lvls[j].ClipMapResolution);
            //         if (snapJ != lastBuildCenter[j])
            //         {
            //             targetCenter[j]   = snapJ;
            //             pendingLevel[j]   = true;
            //             float driftJ      = DistInf(camPos, activeCenter[j]);
            //             float threshJ     = Mathf.Max(1e-6f, lvls[j].ClipmapResampleThresholdDistance);
            //             levelUrgency[j]   = driftJ / threshJ; // lower than li usually, but ensures it will get picked soon
            //         }
            //     }
            // }

            // Build only this level, centered at its own target
            BuildClipmapLevel(li);  
            pendingLevel[li]   = false;
            lastBuildCenter[li]= targetCenter[li];

            // 3) Commit per-level immediately (no global atomic flip)
            activeCenter[li]   = targetCenter[li];

            built++;
        }
    }

    void ClearClipmap(RenderTexture buffer)
    {
        int gx = Mathf.Max(buffer.width  / 4, 1);
        int gy = Mathf.Max(buffer.height / 4, 1);
        int gz = Mathf.Max(buffer.volumeDepth / 4, 1);

        ClipmapCompute.SetTexture(ID_ClipMapClearKern, ID_Result, buffer);
        ClipmapCompute.Dispatch(ID_ClipMapClearKern, gx, gy, gz);
    }
    
    bool ClipFar = false;

#endregion

    bool FlopIntegralBuffer = false;
    void FlopIntegralBuffers(){

        FlopIntegralBuffer = !FlopIntegralBuffer;

        if (FlopIntegralBuffer)
        {
            FroxelFogCompute.SetTexture(ScatteringKernel, ID_PreviousFrameLighting, FroxelBufferA);
            FroxelFogCompute.SetTexture(ScatteringKernel, ID_Result, FroxelBufferB);
            FroxelIntegrationCompute.SetTexture(IntegrateKernel, ID_InLightingTexture, FroxelBufferB);
            Shader.SetGlobalTexture(ID_InLightingTexture, FroxelBufferB);
        }
        else
        {
            FroxelFogCompute.SetTexture(ScatteringKernel, ID_PreviousFrameLighting, FroxelBufferB);
            FroxelFogCompute.SetTexture(ScatteringKernel, ID_Result, FroxelBufferA);
            FroxelIntegrationCompute.SetTexture(IntegrateKernel, ID_InLightingTexture, FroxelBufferA);
            Shader.SetGlobalTexture(ID_InLightingTexture, FroxelBufferA);

        }

        FroxelIntegrationCompute.SetTexture(IntegrateKernel, ID_HistoryBuffer, IntegrationBuffer);
        FroxelIntegrationCompute.SetTexture(IntegrateKernel, ID_Result, IntegrationBuffer);
    }

    Matrix4x4 PrevViewProjMatrix = Matrix4x4.identity;



    public void SetVariables()
    {
        //Global multiplier for static lights
        //ScatteringFromExtinctionAndAlbedo
        //THESE ARE GLOBAL VARIABLES. THEY NEED TO STAY GLOBAL

        // The volumetrics script should be in charge of setting these,
        // if there's no volume component then all volumetric
        // scripts will use the last camera to be enabled's values
        // Not ideal, but for now it should be fine

        if (!Volumetrics.hasSetGlobals) // Added check so volumetric rendering scripts don't overwrite the volumetrics scripts values
        {
            float extinction = VolumeRenderingUtils.ExtinctionFromMeanFreePath(meanFreePath);
            Shader.SetGlobalFloat(ID_GlobalExtinction, extinction); //ExtinctionFromMeanFreePath
            Shader.SetGlobalFloat(ID_StaticLightMultiplier, StaticLightMultiplier); //Global multiplier for static lights
            Shader.SetGlobalVector(ID_FoveaCenterUV, _FoveaCenterUV);
            Shader.SetGlobalFloat(ID_FoveaStrength, _FoveaStrength);
        }
    }

    float GetAspectRatio()
    {
        if (activeCam.stereoTargetEye == StereoTargetEyeMask.None) return activeCam.aspect;
        return XRSettings.eyeTextureHeight == 0 ? activeCam.aspect : (float)XRSettings.eyeTextureHeight / (float)XRSettings.eyeTextureWidth;
    }



    void UpdatePreRender(ScriptableRenderContext ctxt, Camera cam1)
    {
        if (activeCam == cam1) UpdateFunc();
    }

    void UpdateFunc()
    {
        using (new ProfilingScope(null, profileUpdateFunc))
        {
            if (!hasInitialized)
            {
                //Debug.LogWarning("Volumetric Rendering: Volumetrics trying to render without initializing");
                return;
            }
            if (activeCam == null)
            {
                Debug.LogError("Volumetric Rendering: Active camera destroyed or de-assigned, disabling");
                this.enabled = false;
                return;
            }
#if UNITY_EDITOR
            if ((Application.isPlaying && !activeCam.isActiveAndEnabled && !enableEditorPreview))
#else
        if (!activeCam.isActiveAndEnabled)
#endif
            {
                return;
            }




            CheckOverrideVolumes();
            //camera.aspect no longer returns the correct value & this workaround only works when XR is fully intialized otherwise it returns 0 and divs by 0; >W<
            //bleh
            CamAspectRatio = GetAspectRatio();

            Matrix4x4 projectionMatrix = Matrix4x4.Perspective(activeCam.fieldOfView, CamAspectRatio, activeCam.nearClipPlane, volumetricData.far) * Matrix4x4.Rotate(activeCam.transform.rotation).inverse;
            projectionMatrix = matScaleBias * projectionMatrix;

            FroxelFogCompute.SetMatrix(PreviousFrameMatrixID, PreviousFrameMatrix);///

            // CheckClipmap(); // UpdateClipmap();
            TickClipmapScheduler();
            SetFroxelFogUniforms();
            SetFroxelIntegrationUniforms();
            //SetBlurUniforms();

            FlopIntegralBuffers();
            //  Matrix4x4 lightMatrix = matScaleBias * Matrix4x4.Perspective(LightPosition.spotAngle, 1, 0.1f, LightPosition.range) * Matrix4x4.Rotate(LightPosition.transform.rotation).inverse;
            VBufferParameters vbuff = new VBufferParameters(
                                            new Vector3Int(volumetricData.FroxelWidthResolution, volumetricData.FroxelWidthResolution, volumetricData.FroxelDepthResolution),
                                            volumetricData.far,
                                            activeCam.nearClipPlane,
                                            activeCam.farClipPlane,
                                            activeCam.fieldOfView,
                                            SliceDistributionUniformity);


            Vector4 vres = new Vector4(volumetricData.FroxelWidthResolution, volumetricData.FroxelHeightResolution, 1.0f / volumetricData.FroxelWidthResolution, 1.0f / volumetricData.FroxelHeightResolution);

            Matrix4x4 PixelCoordToViewDirWS = ComputePixelCoordToWorldSpaceViewDirectionMatrix(activeCam, vres);

            GetHexagonalClosePackedSpheres7(m_xySeq);

            int sampleIndex = Time.renderedFrameCount % 7;
            // ReSharper disable once PossibleLossOfFraction
            Vector4 seqOffset = new Vector4(m_xySeq[sampleIndex].x, m_xySeq[sampleIndex].y, m_zSeq[sampleIndex],- Time.renderedFrameCount % 70 / 10); //intential int div to only count up after 7 frames
          //  Vector4[] seqOffsetArray = new Vector4[7];
            
          //  Shader.SetGlobalVectorArray("seqOffset", seqOffset);

            Span<ShaderConstants> shaderConsts = stackalloc ShaderConstants[1];
            shaderConsts[0].TransposedCameraProjectionMatrix = projectionMatrix.transpose;
            shaderConsts[0].CameraProjectionMatrix = projectionMatrix;
            shaderConsts[0]._VBufferDistanceEncodingParams = vbuff.depthEncodingParams;
            // shaderConsts[0]._VolumetricResultDim = new Vector3(FroxelBlur != BlurType.Gaussian ? volumetricData.FroxelWidthResolution * 2 : volumetricData.FroxelWidthResolution,
            //     volumetricData.FroxelHeightResolution, volumetricData.FroxelDepthResolution);
            shaderConsts[0]._VolumetricResultDim = new Vector3( volumetricData.FroxelWidthResolution * 2, volumetricData.FroxelHeightResolution, volumetricData.FroxelDepthResolution);
            shaderConsts[0]._VolCameraPos = activeCam.transform.position;
            if (ShaderConstantBuffer == null)
            {
                ShaderConstantBuffer = new ComputeBuffer(ShaderConstantsCount, sizeof(float), ComputeBufferType.Constant);
            }
            ShaderConstantBuffer.SetData<ShaderConstants>(shaderConsts);


            Span<StepAddPerFrameConstants> stepAddConst = stackalloc StepAddPerFrameConstants[1];
            stepAddConst[0] = new StepAddPerFrameConstants();
            stepAddConst[0]._VBufferDistanceDecodingParams = vbuff.depthDecodingParams;
            stepAddConst[0].SeqOffset = seqOffset;
            if (StepAddPerFrameConstantBuffer == null)
            {
                StepAddPerFrameConstantBuffer = new ComputeBuffer(1, StepAddPerFrameCount * sizeof(float), ComputeBufferType.Constant);

            }
            StepAddPerFrameConstantBuffer.SetData<StepAddPerFrameConstants>(stepAddConst);

            Span<ScatteringPerFrameConstants> VolScatteringCB = stackalloc ScatteringPerFrameConstants[1];
            VolScatteringCB[0] = new ScatteringPerFrameConstants()
            {
                _VBufferCoordToViewDirWS = PixelCoordToViewDirWS,
                _PrevViewProjMatrix = PrevViewProjMatrix,
                _ViewMatrix = activeCam.worldToCameraMatrix,
                TransposedCameraProjectionMatrix = projectionMatrix.transpose,
                CameraProjectionMatrix = projectionMatrix,
                _VBufferDistanceEncodingParams = vbuff.depthEncodingParams,
                _VBufferDistanceDecodingParams = vbuff.depthDecodingParams,
                SeqOffset = seqOffset,
                CameraPosition = activeCam.transform.position,
                CameraMotionVector = activeCam.transform.position - PreviousCameraPosition
            };
            if (ComputePerFrameConstantBuffer == null)
            {
                ComputePerFrameConstantBuffer = new ComputeBuffer(1, ScatterPerFrameCount * sizeof(float), ComputeBufferType.Constant);
            }
            ComputePerFrameConstantBuffer.SetData<ScatteringPerFrameConstants>(VolScatteringCB);
            FroxelFogCompute.SetConstantBuffer(PerFrameConstBufferID, ComputePerFrameConstantBuffer, 0, ScatterPerFrameCount * sizeof(float));

         
            PreviousFrameMatrix = projectionMatrix;
            PreviousCameraPosition = activeCam.transform.position;
            ////MATRIX
  
            var gpuProj = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(activeCam.fieldOfView, CamAspectRatio, activeCam.nearClipPlane, 100000f), true);
            PrevViewProjMatrix = gpuProj * activeCam.worldToCameraMatrix;
            FroxelFogCompute.Dispatch(ScatteringKernel, gxs, gys, gzs);
            FroxelIntegrationCompute.Dispatch(IntegrateKernel, igx, igy, 1);
            
            //Force mip gen on all
            // clipmapFinal[0].GenerateMips();
            // clipmapFinal[1].GenerateMips();
            // clipmapFinal[2].GenerateMips();
            // clipmapFinal[3].GenerateMips();
            
            SetCameraData();
        }
    }


    //Coping the parms from HDRP to get the log encoded depth.
    struct VBufferParameters 
    {
        public Vector3Int viewportSize;
        public Vector4 depthEncodingParams;
        public Vector4 depthDecodingParams;

        public VBufferParameters(Vector3Int viewportResolution, float depthExtent, float camNear, float camFar, float camVFoV, float sliceDistributionUniformity)
        {
            viewportSize = viewportResolution;

            // The V-Buffer is sphere-capped, while the camera frustum is not.
            // We always start from the near plane of the camera.

            float aspectRatio = viewportResolution.x / (float)viewportResolution.y;
            float farPlaneHeight = 2.0f * Mathf.Tan(0.5f * camVFoV) * camFar;
            float farPlaneWidth = farPlaneHeight * aspectRatio;
            float farPlaneMaxDim = Mathf.Max(farPlaneWidth, farPlaneHeight);
            float farPlaneDist = Mathf.Sqrt(camFar * camFar + 0.25f * farPlaneMaxDim * farPlaneMaxDim);

            float nearDist = camNear;
            float farDist = Mathf.Min(nearDist + depthExtent, farPlaneDist);

            float c = 2 - 2 * sliceDistributionUniformity; // remap [0, 1] -> [2, 0]
            c = Mathf.Max(c, 0.001f);                // Avoid NaNs

            depthEncodingParams = ComputeLogarithmicDepthEncodingParams(nearDist, farDist, c);
            depthDecodingParams = ComputeLogarithmicDepthDecodingParams(nearDist, farDist, c);
        }

        internal Vector4 ComputeUvScaleAndLimit(Vector2Int bufferSize)
        {
            // The slice count is fixed for now.
            return ComputeUvScaleAndLimitFun(new Vector2Int(viewportSize.x, viewportSize.y), bufferSize);
        }

        internal float ComputeLastSliceDistance(int sliceCount)
        {
            float d = 1.0f - 0.5f / sliceCount;
            float ln2 = 0.69314718f;

            // DecodeLogarithmicDepthGeneralized(1 - 0.5 / sliceCount)
            return depthDecodingParams.x * Mathf.Exp(ln2 * d * depthDecodingParams.y) + depthDecodingParams.z;
        }

        // See EncodeLogarithmicDepthGeneralized().
        static Vector4 ComputeLogarithmicDepthEncodingParams(float nearPlane, float farPlane, float c)
        {
            Vector4 depthParams = new Vector4();

            float n = nearPlane;
            float f = farPlane;

            depthParams.y = 1.0f / Mathf.Log(c * (f - n) + 1, 2);
            depthParams.x = Mathf.Log(c, 2) * depthParams.y;
            depthParams.z = n - 1.0f / c; // Same
            depthParams.w = 0.0f;

            return depthParams;
        }

        // See DecodeLogarithmicDepthGeneralized().
        static Vector4 ComputeLogarithmicDepthDecodingParams(float nearPlane, float farPlane, float c)
        {
            Vector4 depthParams = new Vector4();

            float n = nearPlane;
            float f = farPlane;

            depthParams.x = 1.0f / c;
            depthParams.y = Mathf.Log(c * (f - n) + 1, 2);
            depthParams.z = n - 1.0f / c; // Same
            depthParams.w = 0.0f;

            return depthParams;
        }
    }

    internal static float ComputZPlaneTexelSpacing(float planeDepth, float verticalFoV, float resolutionY)
    {
        float tanHalfVertFoV = Mathf.Tan(0.5f * verticalFoV);
        return tanHalfVertFoV * (2.0f / resolutionY) * planeDepth;
    }

    internal static Vector4 ComputeUvScaleAndLimitFun(Vector2Int viewportResolution, Vector2Int bufferSize)
    {
        Vector2 rcpBufferSize = new Vector2(1.0f / bufferSize.x, 1.0f / bufferSize.y);

        // vp_scale = vp_dim / tex_dim.
        Vector2 uvScale = new Vector2(viewportResolution.x * rcpBufferSize.x,
                                      viewportResolution.y * rcpBufferSize.y);

        // clamp to (vp_dim - 0.5) / tex_dim.
        Vector2 uvLimit = new Vector2((viewportResolution.x - 0.5f) * rcpBufferSize.x,
                                      (viewportResolution.y - 0.5f) * rcpBufferSize.y);

        return new Vector4(uvScale.x, uvScale.y, uvLimit.x, uvLimit.y);
    }

    public void disable()
    {
        //Debug.Log("Volumetric Rendering: Disable Called");
        hasInitialized = false;
#if UNITY_EDITOR
            RenderPipelineManager.beginCameraRendering -= UpdatePreRender;
            AssemblyReloadEvents.beforeAssemblyReload -= CleanupOnReload;
#else
            RenderPipelineManager.beginCameraRendering -= UpdatePreRender;
#endif
        ReleaseAssets();
        CleanupCameraData();
        VolumetricRegisters.UnregisterVolumetricRenderer(this);
    }

    public void enable()
    {
        StartSceneViewRendering();
        Intialize();
    }

    public void StartSceneViewRendering()
    {
        RenderPipelineManager.beginCameraRendering += UpdatePreRender;
    }

    public void UpdateStateAfterReload()
    {
        if (enableEditorPreview && this.isActiveAndEnabled)
        {
            enable();
        }
        else
        {
            disable();
        }
    }

    public void CleanupOnReload()
    {
        disable();
    }

    private void OnEnable()
    {
        
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // Every time scripts get re-compiled, everything gets reset without calling OnDisable or OnDestroy, and the keyword gets left on 
            enableEditorPreview = false;
            AssemblyReloadEvents.afterAssemblyReload += UpdateStateAfterReload;
        }
        else
        {
            enable();
        }
#else
        enable();
#endif
    }
    private void OnDisable() //Disable this if we decide to just pause rendering instead of removing. 
    {
        disable();
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            AssemblyReloadEvents.afterAssemblyReload -= UpdateStateAfterReload;
        }
#endif
    }
    private void OnDestroy()
    {
        disable();
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            AssemblyReloadEvents.afterAssemblyReload -= UpdateStateAfterReload;
        }
#endif

    }

    void DestroyAllTextureAssets()
    {
        // finals
        if (clipmapFinal != null)
        {
            for (int i = 0; i < clipmapFinal.Length; i++)
            {
                if (clipmapFinal[i])
                {
                    clipmapFinal[i].Release();
                    DestroyImmediate(clipmapFinal[i]);
                }
            }
            clipmapFinal = null;
        }

        // shared scratch
        if (clipScratchA) { clipScratchA.Release(); DestroyImmediate(clipScratchA); clipScratchA = null; }
        if (clipScratchB) { clipScratchB.Release(); DestroyImmediate(clipScratchB); clipScratchB = null; }
        scratchRes = -1;

        // other buffers (keep your existing Froxel / Integration / Blur clears if needed)
        if (FroxelBufferA) FroxelBufferA.Release();
        if (FroxelBufferB) FroxelBufferB.Release();
        if (IntegrationBuffer) IntegrationBuffer.Release();
        // if (BlurBuffer) BlurBuffer.Release();
        // if (BlurBufferB) BlurBufferB.Release();
    }




    Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(Camera cam, Vector4 resolution)
    {
        //   var proj = cam.projectionMatrix; //  GL.GetGPUProjectionMatrix(cameraProj, true); //Use this if we run into platform issues
        //bandaid fix. There's an issue with the far clip plane in the matrix projection. 
        var proj = Matrix4x4.Perspective(cam.fieldOfView, CamAspectRatio, cam.nearClipPlane, 100000f); 
        var view = cam.worldToCameraMatrix ;

        var invViewProjMatrix = (proj * view).inverse;

        var viewProjMatrix = Matrix4x4.Scale(new Vector3(-1.0f, -1.0f, -1.0f)) * invViewProjMatrix; // (gpuProj * gpuView).inverse
     //   transform = transform * Matrix4x4.Scale(new Vector3(1.0f, -1.0f, 1.0f));
        viewProjMatrix *= Matrix4x4.Translate(new Vector3(-1.0f, -1.0f, 0.0f));
        viewProjMatrix *= Matrix4x4.Scale(new Vector3(2.0f * resolution.z, 2.0f * resolution.w, 1.0f)) ;

        return viewProjMatrix.transpose;
    }


    void SetComputeVariables()
    {


    }
    
    void SetComputeBuffer(string name, ComputeShader shader, int kernel, ComputeBuffer buffer)
    {
        //   Debug.Log("Setting buffer");
        if (buffer != null)
        {
            shader.SetBuffer(kernel, name, buffer);

            //     Debug.Log(name + " set");
        }
    }
    
    private static void CreateComputeBuffer<T>(ref ComputeBuffer buffer, List<T> data, int stride)
        where T : struct
    {
        //Debug.Log("Making computebuffer ");
        //buffer = new ComputeBuffer(data.Count, stride);

        // Do we already have a compute buffer?
        if (buffer != null && data != null )
        {
            // If no data or buffer doesn't match the given criteria, release it
            if (data.Count == 0 || buffer.count != data.Count || buffer.stride != stride)
            {
                //    Debug.Log("Buffer count = " + buffer.count);

                buffer.Release();
                buffer = null;
            }
        }

        if (data.Count != 0)
        {
            // If the buffer has been released or wasn't there to
            // begin with, create it
            if (buffer == null)
            {
                buffer = new ComputeBuffer(data.Count, stride);

                //        Debug.Log("Buffer count = " + buffer.count);

            }

            // Set data on the buffer
            buffer.SetData(data);
        }
    }

    void ReleaseAssets()
    {
        DestroyAllTextureAssets();

        if (ComputePerFrameConstantBuffer != null)
        {
            ComputePerFrameConstantBuffer.Release();
            ComputePerFrameConstantBuffer = null;
        }
        if (StepAddPerFrameConstantBuffer != null)
        { 
            StepAddPerFrameConstantBuffer.Release();
            StepAddPerFrameConstantBuffer = null;
        }
        if (ShaderConstantBuffer != null)
        {
            ShaderConstantBuffer.Release();
            ShaderConstantBuffer = null;
        }
        if (participatingMediaSphereBuffer != null)
        {
            participatingMediaSphereBuffer.Release();
        }
    }

    void CleanupCameraData()
    {
        if (activeCamData != null)
        {
            activeCamData.m_EnableVolumetrics = false;
            activeCamData.m_VolumetricClipMap = null;
            activeCamData.m_VolumetricShaderGlobals = null;
        }
    }

    void SetCameraData()
    {
        if (activeCamData != null && VolumetricResult != null && ShaderConstantBuffer != null)
        {
            activeCamData.m_EnableVolumetrics = true;
            activeCamData.m_VolumetricClipMap = VolumetricResult;
            activeCamData.m_VolumetricShaderGlobals = ShaderConstantBuffer;
        }
        else
        {
            Debug.LogWarning("Volumetric Rendering: Null extra camera data, volumetric result, or constant buffer!");
            activeCamData.m_EnableVolumetrics = false;
        }
    }

#if UNITY_EDITOR

    void assignVaris()
    {
        
        //cam = GetComponentInChildren<Camera>();
        //Get shaders and seri
        if (FroxelFogCompute == null)
            FroxelFogCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/VolumetricScattering.compute");
        if (FroxelFogCompute == null)
            FroxelFogCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/VolumetricScattering.compute");
        if (FroxelIntegrationCompute == null)
            FroxelIntegrationCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/StepAdd.compute");
        if (ClipmapCompute == null)
            ClipmapCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/ClipMapGenerator.compute");
        // if (BlurCompute == null)
        //     BlurCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/VolumetricBlur.compute");
    }



    private void Reset()
    {
        cam = GetComponentInChildren<Camera>();
        assignVaris();
    }


    private void OnValidate()
    {

        if (BlackTex == null) BlackTex = CoreUtils.blackVolumeTexture; //(Texture3D)MakeBlack3DTex();
        assignVaris();

        
    }
#endif


    void Clear3DTexture(RenderTexture buffer)
    {
        ClipmapCompute.SetTexture(ID_ClipMapClearKern, ID_Result, buffer);
        ClipmapCompute.Dispatch(ID_ClipMapClearKern, Mathf.Max(buffer.width / 4, 1), Mathf.Max(buffer.height / 4, 1), Mathf.Max(buffer.volumeDepth / 4, 1));

    }

    void RefreshOnSceneChange(Scene oldS, Scene newS)
    {
        if (hasInitialized && oldS != null)
        {
            Debug.Log("Volumetric Rendering: Refreshing after scene swap");
            this.disable();
            this.enable();
        }
    } 
}
