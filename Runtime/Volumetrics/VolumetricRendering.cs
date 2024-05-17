using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
//using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using Unity.Profiling;
using System.Runtime.CompilerServices;

#if UNITY_EDITOR
using UnityEditor;
#endif

class VolumeRenderingUtils //Importing some functions from HDRP to have simular terms   
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
    static ProfilingSampler profileUpdateClipmap = new ProfilingSampler("VolumetricRendering.UpdateClipmap");

    public float tempOffset = 0;
    Texture3D BlackTex; //Temp texture for 
    Color clearColor = new Color(0.0f, 0.0f, 0.0f, 0f);

    static VolumetricRendering lastClipmapUpdate;
    static VolumetricRendering lastBlur;
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

    [HideInInspector]
    public enum BlurType {None, Gaussian};
    public BlurType FroxelBlur = BlurType.None;
    [Range(0, 1)]
    public float SliceDistributionUniformity = 0.5f;

    [HideInInspector] public bool enableEditorPreview = false;
    //public Texture skytex;
    //[Header("Volumetric camera settings")]
    //[Tooltip("Near Clip plane")]
    //public float near = 1;
    //[Tooltip("Far Clip plane")]
    //public float far = 40;
    //[Tooltip("Resolution")]
    //public int FroxelWidthResolution = 128;
    //[Tooltip("Resolution")]
    //public int FroxelHeightResolution = 128;
    //[Tooltip("Resolution")]
    //public int FroxelDepthResolution = 64;
    ////[Tooltip("Controls the bias of the froxel dispution. A value of 1 is linear. ")]
    ////public float FroxelDispution;

    //[Header("Prebaked clipmap settings")]
    //[Tooltip("Textile resolution per unit")]
    //public int ClipMapResolution = 128;
    //[Tooltip("Size of clipmap in units")]
    //public float ClipmapScale = 80;
    //[Tooltip("Distance (m) from previous sampling point to trigger resampling clipmap")]
    //public float ClipmapResampleThreshold = 1;


    Vector3 ClipmapTransform; //Have this follow the camera and resample when the camera moves enough 
    Vector3 ClipmapCurrentPos; //chached location of previous sample point
    
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
    [SerializeField, HideInInspector] ComputeShader BlurCompute;

    //Texture buffers
    RenderTexture ClipmapBufferA;  //Sampling and combining baked maps asynchronously
    RenderTexture ClipmapBufferB;  //Sampling and combining baked maps asynchronously
    RenderTexture ClipmapBufferC;  //Sampling and combining baked maps asynchronously
    RenderTexture ClipmapBufferD;  //Sampling and combining baked maps asynchronously //TODO: get rid of this extra buffer and bool
    bool FlipClipBufferNear = true;
    bool FlipClipBufferFar = true;


    RenderTexture FroxelBufferA;   //Single froxel projection use for scattering and history reprojection
    RenderTexture FroxelBufferB;   //for history reprojection

    RenderTexture IntegrationBuffer;    //Integration and stereo reprojection
                                        //  RenderTexture IntegrationBufferB;    //Integration and stereo reprojection
    RenderTexture BlurBuffer;    //blur
    RenderTexture BlurBufferB;    //blur

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
    //camera.fieldOfView is unreliable because the physical camera toggle will return the incorrect fov.
    // float CamFieldOfView = XRSettings.vi


    //Unity implemented their own cookie method, so we'll just tie into that system instead. This is no longer needed. 
    /// Dynamic Light Projection///      
    // [SerializeField, HideInInspector] List<Light> Lights; // TODO: Make this a smart dynamic list not living here
    // public struct LightObject
    // {
    //     public Matrix4x4 LightProjectionMatrix;
    //     public Vector3 LightPosition;
    //     public Vector4 LightColor;
    //     public int LightCookie; //TODO: Add general light cookie system to render engine
    // }

    //Figure out how much data is in the struct above
    // int LightObjectStride = sizeof(float) * 4 * 4 + sizeof(float) * 3 + sizeof(float) * 4 + sizeof(int);
    // Texture2DArray LightProjectionTextures; // TODO: Make this a smart dynamic list pulling from light cookies
    // private static List<LightObject> LightObjects;
    // ComputeBuffer LightBuffer;

    /// END Dynamic Light Projection/// 
    /// 

    // public Texture2D BlueNoise; //Temp ref

    //AABB 

    //Stored compute shader IDs and numbers

    protected int ScatteringKernel = 0;
    protected int IntegrateKernel = 0;
    protected int BlurKernelX = 0;
    protected int BlurKernelY = 0;

    Matrix4x4 matScaleBias;
    Vector3 ThreadsToDispatch;

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
    int ID_VolumetricClipmapTexture = Shader.PropertyToID("_VolumetricClipmapTexture");
    int ID_VolumetricClipmapTexture2 = Shader.PropertyToID("_VolumetricClipmapTexture2");
    int ID_PreResult = Shader.PropertyToID("PreResult");
    int ID_VolumeMap = Shader.PropertyToID("VolumeMap");
    int ID_PreviousFrameLighting = Shader.PropertyToID("PreviousFrameLighting");
    int ID_HistoryBuffer = Shader.PropertyToID("HistoryBuffer");
    int ID_LeftEyeMatrix = Shader.PropertyToID("LeftEyeMatrix");
    int ID_RightEyeMatrix = Shader.PropertyToID("RightEyeMatrix");
    int ID_ClipmapScale0 = Shader.PropertyToID("ClipmapScale");
    int ID_ClipmapScale1 = Shader.PropertyToID("_ClipmapScale");
    int ID_ClipmapScale2 = Shader.PropertyToID("_ClipmapScale2");
    int ID_ClipmapWorldPosition = Shader.PropertyToID("ClipmapWorldPosition");
    int ID_VBufferUnitDepthTexelSpacing = Shader.PropertyToID("_VBufferUnitDepthTexelSpacing");
    int ID_VolZBufferParams = Shader.PropertyToID("_VolZBufferParams");
    int ID_GlobalExtinction = Shader.PropertyToID("_GlobalExtinction");
    int ID_StaticLightMultiplier = Shader.PropertyToID("_StaticLightMultiplier");
    int ID_GlobalScattering = Shader.PropertyToID("_GlobalScattering");
    int ID_VolumeWorldSize = Shader.PropertyToID("VolumeWorldSize");
    int ID_VolumeWorldPosition = Shader.PropertyToID("VolumeWorldPosition");

    private int ID_media_sphere_buffer_length = Shader.PropertyToID("media_sphere_buffer_length");
    private int ID_media_sphere_buffer = Shader.PropertyToID("media_sphere_buffer");

    int ID_ClipMapGenKern;
    int ID_ClipMapClearKern;
    int ID_ClipMapHeightKern;

    //Froxel Ids
    int PerFrameConstBufferID = Shader.PropertyToID("PerFrameCB");

    //int CameraProjectionMatrixID = Shader.PropertyToID("CameraProjectionMatrix");
    //int TransposedCameraProjectionMatrixID = Shader.PropertyToID("TransposedCameraProjectionMatrix");
    //int inverseCameraProjectionMatrixID = Shader.PropertyToID("inverseCameraProjectionMatrix");
    int PreviousFrameMatrixID = Shader.PropertyToID("PreviousFrameMatrix");
    //int Camera2WorldID = Shader.PropertyToID("Camera2World");
    //int CameraPositionID = Shader.PropertyToID("CameraPosition");
    //Clipmap IDs
    //int CameraMotionVectorID = Shader.PropertyToID("CameraMotionVector");
    //int ClipmapTextureID = Shader.PropertyToID("_ClipmapTexture");
    //int ClipmapTextureID2 = Shader.PropertyToID("_VolumetricClipmapTexture"); //TODO: Make these two the same name
    
    int ClipmapScaleID = Shader.PropertyToID("_ClipmapScale");
    int ClipmapTransformID = Shader.PropertyToID("_ClipmapPosition");

    // int LightObjectsID = Shader.PropertyToID("LightObjects");

    //Temp Jitter stuff
    int tempjitter = 0; //TEMP jitter switcher thing 
    [Header("Extra variables"), Range(0, 1)]
    float[] jitters = new float[2] { 0.0f, 0.5f };

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
    //float Extinction;
    //Color ExtinctionColor;

    //General fog settings
    // [HideInInspector]
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

    private static float[] VolStructToArray<T>(T rawData, int count, int size) where T : struct
    {
        var pinnedRawData = GCHandle.Alloc(rawData, GCHandleType.Pinned);
        try
        {
            var pinnedRawDataPtr = pinnedRawData.AddrOfPinnedObject();
            float[] data = new float[size];
            Marshal.Copy(pinnedRawDataPtr, data, 0, count);
            return data;
        }
        finally
        {
            pinnedRawData.Free();
        }
    }


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

    // bool createdLightProjectionTexture = false;
    // void CheckCookieList()
    // {
    //     if (LightProjectionTextures != null) return;
    //     LightProjectionTextures = new Texture2DArray(1, 1, 1, TextureFormat.RGBA32, false);
    //     LightProjectionTextures.hideFlags = HideFlags.DontSave;
    //     LightProjectionTextures.name = activeCam.name + " Volumetric Light Cookies";
    //     createdLightProjectionTexture = true;
    //     //Debug.Log("Made blank cookie sheet");
    // }

    //void dedbugRTC()
    //{
    //    RenderTexture.active = (RenderTexture)skytex;
    //    GL.Clear(true, true, Color.yellow);
    //    RenderTexture.active = null;

    //}
    //void SetSkyTexture(Texture cubemap)
    //{
    //  //  cam.RenderToCubemap((Cubemap)cubemap);
    // //   dedbugRTC();
    //    Shader.SetGlobalTexture("_SkyTexture", cubemap);
    //}

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

    void IntializeBlur(RenderTextureDescriptor rtdiscrpt)
    {
        BlurBuffer = new RenderTexture(rtdiscrpt);
        BlurBuffer.name = activeCam.name + "_BlurBuffer";
        BlurBuffer.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
        BlurBuffer.enableRandomWrite = true;
        BlurBuffer.Create();
        Clear3DTexture(BlurBuffer);


        BlurBufferB = new RenderTexture(rtdiscrpt);
        BlurBuffer.name = activeCam.name + "_BlurBufferB";
        BlurBufferB.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
        BlurBufferB.enableRandomWrite = true;
        BlurBufferB.Create();
        Clear3DTexture(BlurBufferB);

        BlurKernelX = BlurCompute.FindKernel("VolBlurX");
        BlurKernelY = BlurCompute.FindKernel("VolBlurY");
    }

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
     //   if (VerifyVolumetricRegisters() == false) return; //Check registers to see if there's anything to render. If not, then disable system. TODO: Remove this 
      //  CheckCookieList();


        //   SetSkyTexture( skytex);

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
        //  IntegrationBuffer.format = RenderTextureFormat.ARGB32;
        IntegrationBuffer.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
        IntegrationBuffer.filterMode = FilterMode.Trilinear;
        IntegrationBuffer.enableRandomWrite = true;
        IntegrationBuffer.Create();

        //IntegrationBufferB = new RenderTexture(rtdiscrpt);
        //IntegrationBufferB.format = RenderTextureFormat.ARGB32;
        //IntegrationBufferB.enableRandomWrite = true;
        //IntegrationBufferB.Create();

        //Extinction = VolumeRenderingUtils.ExtinctionFromMeanFreePath(meanFreePath);
        //ExtinctionColor = albedo * Extinction;

        if (FroxelBlur == BlurType.Gaussian) IntializeBlur(rtdiscrpt);

        // LightObjects = new List<LightObject>();

        ScatteringKernel = FroxelFogCompute.FindKernel("Scatter");

        ZPlaneTexelSpacing = ComputZPlaneTexelSpacing(1, activeCam.fieldOfView, volumetricData.FroxelHeightResolution);


        //UpdateClipmap(Clipmap.Far);
       // FroxelFogCompute.SetTexture(ScatteringKernel, ClipmapTextureID, ClipmapBufferA);
       // temp light cookie array. TODO: Make dynamic. Add to lighting engine too.
                                                                                                              //     FroxelFogCompute.SetTexture(FogFroxelKernel, "BlueNoise", BlueNoise); // temp light cookie array. TODO: Make dynamic. Add to lighting engine too.

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

        if (FroxelBlur == BlurType.Gaussian)
        {
            //Shader.SetGlobalTexture(ID_VolumetricResult, BlurBufferB);
            VolumetricResult = BlurBufferB;
        }
        else
        {
            //FroxelFogCompute.SetTexture(ScatteringKernel, ID_VolumetricResult, IntegrationBuffer);
            //Shader.SetGlobalTexture(ID_VolumetricResult, IntegrationBuffer);
            VolumetricResult = IntegrationBuffer;
        }

        ThreadsToDispatch = new Vector3(
             Mathf.Max(Mathf.CeilToInt(volumetricData.FroxelWidthResolution / 4.0f), 1.0f),
              Mathf.Max(Mathf.CeilToInt(volumetricData.FroxelHeightResolution / 4.0f), 1.0f),
              Mathf.Max(Mathf.CeilToInt(volumetricData.FroxelDepthResolution / 4.0f), 1.0f)
            );

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

        //Debug.Log("Dispatching " + ThreadsToDispatch);

        SkyManager.CheckSky();

        Clear3DTexture(FroxelBufferA);
        Clear3DTexture(FroxelBufferB);
        Clear3DTexture(IntegrationBuffer);

        SetVariables();
        SetupClipmap();
        UpdateClipmaps();
        SetFroxelFogUniforms(true);
        SetFroxelIntegrationUniforms(true);
        SetBlurUniforms(true);

        hasInitialized = true;
        VolumetricRegisters.RegisterVolumetricRenderer(this);
        //RenderPipelineManager.beginCameraRendering += UpdatePreRender;
    }

    void SetFroxelFogUniforms(bool forceUpdate = false)
    {
        if (lastFroxelFog != this || forceUpdate)
        {
            FroxelFogCompute.SetFloat(ID_VBufferUnitDepthTexelSpacing, ZPlaneTexelSpacing);
            FroxelFogCompute.SetFloat(ID_ClipmapScale1, volumetricData.ClipmapScale);
            FroxelFogCompute.SetFloat(ID_ClipmapScale2, volumetricData.ClipmapScale2);
            //FroxelFogCompute.SetFloat(ID_GlobalExtinction, Extinction);
            //FroxelFogCompute.SetFloat(ID_StaticLightMultiplier, StaticLightMultiplier);
            FroxelFogCompute.SetTexture(ScatteringKernel, ID_Result, FroxelBufferA);
            // CheckCookieList();
            // FroxelFogCompute.SetTexture(ScatteringKernel, ID_LightProjectionTextureArray, LightProjectionTextures);
            FroxelFogCompute.SetConstantBuffer(PerFrameConstBufferID, StepAddPerFrameConstantBuffer, 0, StepAddPerFrameCount * sizeof(float));
            lastFroxelFog = this;
        }
        if (lastClipmapUpdate != this || forceUpdate)
        {
            FroxelFogCompute.SetFloat(ClipmapScaleID, volumetricData.ClipmapScale);
            FroxelFogCompute.SetVector(ClipmapTransformID, ClipmapTransform);
            if (FlipClipBufferNear)
            {
                FroxelFogCompute.SetTexture(ScatteringKernel, ID_VolumetricClipmapTexture, ClipmapBufferB);
            }
            else
            {
                FroxelFogCompute.SetTexture(ScatteringKernel, ID_VolumetricClipmapTexture, ClipmapBufferA);
            }
            if (FlipClipBufferFar)
            {
                FroxelFogCompute.SetTexture(ScatteringKernel, ID_VolumetricClipmapTexture2, ClipmapBufferC);
            }
            else
            {
                FroxelFogCompute.SetTexture(ScatteringKernel, ID_VolumetricClipmapTexture2, ClipmapBufferD);
            }
        }
    }

    void SetFroxelIntegrationUniforms(bool forceUpdate = false)
    {
        if (lastFroxelIntegrate != this || forceUpdate)
        {
            FroxelIntegrationCompute.SetMatrix(ID_LeftEyeMatrix, LeftEyeMatrix);
            FroxelIntegrationCompute.SetMatrix(ID_RightEyeMatrix, RightEyeMatrix);
            FroxelIntegrationCompute.SetVector(ID_VolZBufferParams, VolZBufferParams);
            //FroxelIntegrationCompute.SetVector(ID_GlobalScattering, ExtinctionColor);
            FroxelIntegrationCompute.SetTexture(IntegrateKernel, ID_Result, IntegrationBuffer);
            FroxelIntegrationCompute.SetTexture(IntegrateKernel, ID_InLightingTexture, FroxelBufferA);
            FroxelIntegrationCompute.SetConstantBuffer(PerFrameConstBufferID, StepAddPerFrameConstantBuffer, 0, StepAddPerFrameCount * sizeof(float));
            lastFroxelIntegrate = this;
        }
    }

    void SetBlurUniforms(bool forceUpdate = false)
    {
        if (FroxelBlur == BlurType.Gaussian && (lastBlur != this || forceUpdate))
        {
            BlurCompute.SetTexture(BlurKernelX, ID_InTex, IntegrationBuffer);
            BlurCompute.SetTexture(BlurKernelX, ID_Result, BlurBuffer);
            BlurCompute.SetTexture(BlurKernelY, ID_InTex, BlurBuffer);
            BlurCompute.SetTexture(BlurKernelY, ID_Result, BlurBufferB);
            lastBlur = this;
        }
    }

    public void ClearAllBuffers()
    {
        ClearClipmap(ClipmapBufferA);
        ClearClipmap(ClipmapBufferB);
        ClearClipmap(ClipmapBufferC);
        ClearClipmap(ClipmapBufferD);

        Clear3DTexture(FroxelBufferA);
        Clear3DTexture(FroxelBufferB);
        Clear3DTexture(IntegrationBuffer);
    }

    // void UpdateLights()
    // {
    //     LightObjects.Clear(); //clear and rebuild for now. TODO: Make a smarter constructor
    //     if (LightBuffer != null) LightBuffer.Release();
    //
    //     for (int i = 0; i < Lights.Count; i++)
    //     {
    //         LightObject lightObject = new LightObject();
    //         lightObject.LightPosition = Lights[i].transform.position;
    //         lightObject.LightColor = new Color(
    //             Lights[i].color.r * Lights[i].intensity,
    //             Lights[i].color.g * Lights[i].intensity,
    //             Lights[i].color.b * Lights[i].intensity,
    //             Lights[i].color.a);
    //         lightObject.LightProjectionMatrix = matScaleBias
    //             * Matrix4x4.Perspective(Lights[i].spotAngle, 1, 0.1f, Lights[i].range)
    //             * Matrix4x4.Rotate(Lights[i].transform.rotation).inverse;
    //
    //         LightObjects.Add(lightObject);
    //     }
    //     LightBuffer = new ComputeBuffer(LightObjects.Count, LightObjectStride);
    //     LightBuffer.SetData(LightObjects);
    //     FroxelFogCompute.SetBuffer(ScatteringKernel, LightObjectsID, LightBuffer); 
    // }
    
    
#region Clipmap funtions
    void SetupClipmap()
    {

        RenderTextureDescriptor ClipRTdiscrpt = new RenderTextureDescriptor();
        ClipRTdiscrpt.enableRandomWrite = true;
        ClipRTdiscrpt.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        ClipRTdiscrpt.width = volumetricData.ClipMapResolution;
        ClipRTdiscrpt.height = volumetricData.ClipMapResolution;    
        ClipRTdiscrpt.volumeDepth = volumetricData.ClipMapResolution;
        ClipRTdiscrpt.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
        ClipRTdiscrpt.msaaSamples = 1;
        
        ClipmapBufferA = new RenderTexture(ClipRTdiscrpt);
        ClipmapBufferA.name = activeCam.name + "_ClipmapBufferA";
        ClipmapBufferA.Create();
        ClipmapBufferB = new RenderTexture(ClipRTdiscrpt);
        ClipmapBufferB.name = activeCam.name + "_ClipmapBufferB";
        ClipmapBufferB.Create();        
        ClipmapBufferC = new RenderTexture(ClipRTdiscrpt);        
        ClipmapBufferC.name = activeCam.name + "_ClipmapBufferC";
        ClipmapBufferC.Create();        
        ClipmapBufferD = new RenderTexture(ClipRTdiscrpt); 
        ClipmapBufferD.name = activeCam.name + "_ClipmapBufferD";
        ClipmapBufferD.Create();

        ////TODO: Loop through and remove one of the buffers

        ClearClipmap(ClipmapBufferA);
        ClearClipmap(ClipmapBufferB);
        ClearClipmap(ClipmapBufferC);
        ClearClipmap(ClipmapBufferD);

    }

    void ClearClipmap(RenderTexture buffer)
    {
        int clipMapDispatchNum = Mathf.Max(volumetricData.ClipMapResolution / 4, 1);

        ClipmapCompute.SetTexture(ID_ClipMapClearKern, ID_Result, buffer);
        ClipmapCompute.Dispatch(ID_ClipMapClearKern, clipMapDispatchNum, clipMapDispatchNum, clipMapDispatchNum);
    }

    bool ClipFar = false;
    void CheckClipmap() //Check distance from previous sample and recalulate if over threshold. TODO: make it resample chunks
    {

        if (Vector3.Distance(ClipmapCurrentPos, activeCam.transform.position) > volumetricData.ClipmapResampleThreshold || VolumetricRegisterEmpty || VolumetricRegisterForceRefresh)
        {
            //TODO: seperate the frames where this is rendered
            UpdateClipmaps();
            //if (ClipFar == false) UpdateClipmap(Clipmap.Near);
            //else {
            //    UpdateClipmap(Clipmap.Far);
            //    ClipFar = false;
            //    };
        }
    }

    public void UpdateClipmaps()
    {
        //Debug.Log("Clipmap Update: " + activeCam.transform.position);
        if (VolumetricRegisters.volumetricAreas.Count == 0)
        {
            VolumetricRegisterEmpty = true;
            return;
        }
        else if (VolumetricRegisterEmpty)
        {
            VolumetricRegisterEmpty = false;
        }
        UpdateClipmap(Clipmap.Near);
        UpdateClipmap(Clipmap.Far);
        if (VolumetricRegisterForceRefresh) VolumetricRegisterForceRefresh = false;
    }

    public enum Clipmap { Near,Far};


    public void UpdateClipmap(Clipmap clipmap)
    {
        ClipmapTransform = activeCam.transform.position;

        float farscale = volumetricData.ClipmapScale2;

        RenderTexture BufferA;
        RenderTexture BufferB;
        //TODO: bake out variables at start to avoid extra math per clip gen

        //ClipmapCompute.SetFloat(ID_GlobalExtinction, Extinction);

        if (clipmap == Clipmap.Near)
        {
            BufferA = ClipmapBufferB;
            BufferB = ClipmapBufferA;
            ClipmapCompute.SetFloat(ID_ClipmapScale0, volumetricData.ClipmapScale);
            ClipmapCompute.SetVector(ID_ClipmapWorldPosition, ClipmapTransform - (0.5f * volumetricData.ClipmapScale * Vector3.one));
        }
        else
        {
            BufferA = ClipmapBufferC;
            BufferB = ClipmapBufferD;

            ClipmapCompute.SetFloat(ID_ClipmapScale0, volumetricData.ClipmapScale2);
            ClipmapCompute.SetVector(ID_ClipmapWorldPosition, ClipmapTransform - (0.5f * volumetricData.ClipmapScale2 * Vector3.one));

        }

        //Clipmap variables
        //ClipmapCompute.SetVector("ClipmapWorldPosition", ClipmapTransform - (0.5f * volumetricData.ClipmapScale * Vector3.one));
    //    ClipmapCompute.SetFloat("ClipmapScale", volumetricData.ClipmapScale);

        bool FlipClipBuffer = false;
        //Clear previous capture
        int clipMapDispatchNum = Mathf.Max(volumetricData.ClipMapResolution / 4, 1);
     //   ClipmapCompute.SetVector("clearColor", RenderSettings.ambientProbe.Evaluate);


        ClipmapCompute.SetTexture(ID_ClipMapClearKern, ID_Result, BufferA);
        //Debug.Log("Dispatching 0");
        ClipmapCompute.Dispatch(ID_ClipMapClearKern, clipMapDispatchNum, clipMapDispatchNum, clipMapDispatchNum);
        ClipmapCompute.SetTexture(ID_ClipMapClearKern, ID_Result, BufferB);
        //Debug.Log("Dispatching 1");
        ClipmapCompute.Dispatch(ID_ClipMapClearKern, clipMapDispatchNum, clipMapDispatchNum, clipMapDispatchNum);

        //ClipmapCompute.SetFloat("VolumeDensity", 0); //

        //Loop through bake texture volumes and put into clipmap //TODO: Add pass for static unbaked elements
        //Debug.Log("VolumetricRegisters.volumetricAreas.Count: " + VolumetricRegisters.volumetricAreas.Count);
        for (int i = 0; i < VolumetricRegisters.volumetricAreas.Count; i++)
        {
            FlipClipBuffer = !FlipClipBuffer;

            if (FlipClipBuffer)
            {
                ClipmapCompute.SetTexture(ID_ClipMapGenKern, ID_PreResult, BufferB);
                ClipmapCompute.SetTexture(ID_ClipMapGenKern, ID_Result, BufferA);
            }
            else
            {
                ClipmapCompute.SetTexture(ID_ClipMapGenKern, ID_PreResult, BufferA);
                ClipmapCompute.SetTexture(ID_ClipMapGenKern, ID_Result, BufferB);
            }

            //Volumetric variables
            ClipmapCompute.SetTexture(ID_ClipMapGenKern, ID_VolumeMap, VolumetricRegisters.volumetricAreas[i].bakedTexture);
            ClipmapCompute.SetVector(ID_VolumeWorldSize, VolumetricRegisters.volumetricAreas[i].NormalizedScale);
            ClipmapCompute.SetVector(ID_VolumeWorldPosition, VolumetricRegisters.volumetricAreas[i].Corner);
            //Debug.Log("Dispatching 2");
            ClipmapCompute.Dispatch(ID_ClipMapGenKern, clipMapDispatchNum, clipMapDispatchNum, clipMapDispatchNum);
        }
        
        //Height Densitiy

        //FlipClipBuffer = !FlipClipBuffer;

        //if (FlipClipBuffer)
        //{
        //    ClipmapCompute.SetTexture(HeightClipmapKernal, "PreResult", BufferB);
        //    ClipmapCompute.SetTexture(HeightClipmapKernal, "Result", BufferA);
        //}
        //else
        //{
        //    ClipmapCompute.SetTexture(HeightClipmapKernal, "PreResult", BufferA);
        //    ClipmapCompute.SetTexture(HeightClipmapKernal, "Result", BufferB);
        //}

        ////Volumetric variables
        ////ClipmapCompute.SetTexture(HeightClipmapKernal, "VolumeMap", VolumetricRegisters.volumetricAreas[i].bakedTexture);
        ////ClipmapCompute.SetVector("VolumeWorldSize", VolumetricRegisters.volumetricAreas[i].NormalizedScale);
        ////ClipmapCompute.SetVector("VolumeWorldPosition", VolumetricRegisters.volumetricAreas[i].Corner);

        //ClipmapCompute.Dispatch(HeightClipmapKernal, clipMapDispatchNum, clipMapDispatchNum, clipMapDispatchNum);

        //End Height Densitiy

        if (FlipClipBuffer)
        {
            SetClipmap(BufferA, volumetricData.ClipmapScale, ClipmapTransform, clipmap);
        }
        else
        {
            SetClipmap(BufferB, volumetricData.ClipmapScale, ClipmapTransform, clipmap);
        }
        
        switch (clipmap)
        {
            case Clipmap.Near:
                FlipClipBufferNear = FlipClipBuffer;
                break;
            case Clipmap.Far:
                FlipClipBufferFar = FlipClipBuffer;
                break;
            default:
                break;
        }

        ClipmapCurrentPos = ClipmapTransform; //Set History
        lastClipmapUpdate = this;
    }



    void SetClipmap(RenderTexture ClipmapTexture, float ClipmapScale, Vector3 ClipmapTransform, Clipmap clipmap)
    {
        FroxelFogCompute.SetFloat(ClipmapScaleID, ClipmapScale);
        FroxelFogCompute.SetVector(ClipmapTransformID, ClipmapTransform);
        
        if (clipmap == Clipmap.Far)
        {
            FroxelFogCompute.SetTexture(ScatteringKernel, ID_VolumetricClipmapTexture2, ClipmapTexture);
         //   Debug.Log("Added clipmap far :" + ClipmapTexture.name);
        }
        else
        {        //TODO COMBINE THESE
            FroxelFogCompute.SetTexture(ScatteringKernel, ID_VolumetricClipmapTexture, ClipmapTexture); //Set clipmap for
            //FroxelFogCompute.SetTexture(ScatteringKernel, ClipmapTextureID, ClipmapTexture); //Set clipmap for
        }
    }
#endregion

    bool FlopIntegralBuffer = false;
    void FlopIntegralBuffers(){

        FlopIntegralBuffer = !FlopIntegralBuffer;

        if (FlopIntegralBuffer)
        {
            FroxelFogCompute.SetTexture(ScatteringKernel, ID_PreviousFrameLighting, FroxelBufferA);
            FroxelFogCompute.SetTexture(ScatteringKernel, ID_Result, FroxelBufferB);
            FroxelIntegrationCompute.SetTexture(IntegrateKernel, ID_InLightingTexture, FroxelBufferB);
        }
        else
        {
            FroxelFogCompute.SetTexture(ScatteringKernel, ID_PreviousFrameLighting, FroxelBufferB);
            FroxelFogCompute.SetTexture(ScatteringKernel, ID_Result, FroxelBufferA);
            FroxelIntegrationCompute.SetTexture(IntegrateKernel, ID_InLightingTexture, FroxelBufferA);
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
        }
    }

    float GetAspectRatio()
    {
        if (activeCam.stereoTargetEye == StereoTargetEyeMask.None) return activeCam.aspect;
        return XRSettings.eyeTextureHeight == 0 ? activeCam.aspect : (float)XRSettings.eyeTextureHeight / (float)XRSettings.eyeTextureWidth;
    }

//    void Update()
//    {
//#if UNITY_EDITOR
//        if (Application.isPlaying)
//        {
//            UpdateFunc();
//        }
//#else
//        UpdateFunc();
//#endif
//    }

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

            //Previous frame's matrix//!!!!!!!!!
            FroxelFogCompute.SetMatrix(PreviousFrameMatrixID, PreviousFrameMatrix);///
            //   FroxelFogCompute.SetMatrix(PreviousFrameMatrixID, PreviousFrameMatrix );///
            //            var controller = hdCamera.volumeStack.GetComponent<Fog>(); //TODO: Link with controller
            //     UpdateLights();

            CheckClipmap(); // UpdateClipmap();

            SetFroxelFogUniforms();
            SetFroxelIntegrationUniforms();
            SetBlurUniforms();

            FlopIntegralBuffers();
            //  Matrix4x4 lightMatrix = matScaleBias * Matrix4x4.Perspective(LightPosition.spotAngle, 1, 0.1f, LightPosition.range) * Matrix4x4.Rotate(LightPosition.transform.rotation).inverse;
            VBufferParameters vbuff = new VBufferParameters(
                                            new Vector3Int(volumetricData.FroxelWidthResolution, volumetricData.FroxelWidthResolution, volumetricData.FroxelDepthResolution),
                                            volumetricData.far,
                                            activeCam.nearClipPlane,
                                            activeCam.farClipPlane,
                                            activeCam.fieldOfView,
                                            SliceDistributionUniformity);

            //     Vector2Int sharedBufferSize = new Vector2Int(volumetricData.FroxelWidthResolution, volumetricData.FroxelHeightResolution); //Taking scaler functuion from HDRP for reprojection
            //     Shader.SetGlobalVector("_VBufferSharedUvScaleAndLimit", vbuff.ComputeUvScaleAndLimit(sharedBufferSize) ); //Just assuming same scale

            Vector4 vres = new Vector4(volumetricData.FroxelWidthResolution, volumetricData.FroxelHeightResolution, 1.0f / volumetricData.FroxelWidthResolution, 1.0f / volumetricData.FroxelHeightResolution);
            //Vector4  vres = new Vector4(cam.pixelWidth, cam.pixelHeight, 1.0f / cam.pixelWidth, cam.pixelHeight);

            Matrix4x4 PixelCoordToViewDirWS = ComputePixelCoordToWorldSpaceViewDirectionMatrix(activeCam, vres);

            GetHexagonalClosePackedSpheres7(m_xySeq);
            int sampleIndex = Time.renderedFrameCount % 7;
            Vector3 seqOffset = new Vector3(m_xySeq[sampleIndex].x, m_xySeq[sampleIndex].y, m_zSeq[sampleIndex]);

            Span<ShaderConstants> shaderConsts = stackalloc ShaderConstants[1];
            shaderConsts[0].TransposedCameraProjectionMatrix = projectionMatrix.transpose;
            shaderConsts[0].CameraProjectionMatrix = projectionMatrix;
            shaderConsts[0]._VBufferDistanceEncodingParams = vbuff.depthEncodingParams;
            shaderConsts[0]._VolumetricResultDim = new Vector3(FroxelBlur != BlurType.Gaussian ? volumetricData.FroxelWidthResolution * 2 : volumetricData.FroxelWidthResolution,
                volumetricData.FroxelHeightResolution, volumetricData.FroxelDepthResolution);
            shaderConsts[0]._VolCameraPos = activeCam.transform.position;
            if (ShaderConstantBuffer == null)
            {
                ShaderConstantBuffer = new ComputeBuffer(ShaderConstantsCount, sizeof(float), ComputeBufferType.Constant);
                //Shader.SetGlobalConstantBuffer(ID_VolumetricsCB, ShaderConstantBuffer, 0, ShaderConstantsSize);
                //Debug.Log("Created New Compute Buffer");
            }
            ShaderConstantBuffer.SetData<ShaderConstants>(shaderConsts);


            Span<StepAddPerFrameConstants> stepAddConst = stackalloc StepAddPerFrameConstants[1];
            stepAddConst[0] = new StepAddPerFrameConstants();
            stepAddConst[0]._VBufferDistanceDecodingParams = vbuff.depthDecodingParams;
            stepAddConst[0].SeqOffset = seqOffset;
            if (StepAddPerFrameConstantBuffer == null)
            {
                StepAddPerFrameConstantBuffer = new ComputeBuffer(1, StepAddPerFrameCount * sizeof(float), ComputeBufferType.Constant);
                //Shader.SetGlobalConstantBuffer(PerFrameConstBufferID, StepAddPerFrameConstantBuffer, 0, StepAddPerFrameCount * sizeof(float));
                //Debug.Log("Created New Compute Buffer");
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
            //float[] VolScatteringCBArray = VolStructToArray(VolScatteringCB, PerFrameConstantsCount, PerFrameConstantsSize);
            //Debug.Log(VolScatteringCB._VBufferCoordToViewDirWS);
            //Debug.Log(VolScatteringCBArray[4] + " " + VolScatteringCBArray[5] + " " + VolScatteringCBArray[6] + " " + VolScatteringCBArray[7]);
            if (ComputePerFrameConstantBuffer == null)
            {
                ComputePerFrameConstantBuffer = new ComputeBuffer(1, ScatterPerFrameCount * sizeof(float), ComputeBufferType.Constant);
                //Debug.Log("Created New Compute Buffer");
            }
            ComputePerFrameConstantBuffer.SetData<ScatteringPerFrameConstants>(VolScatteringCB);
            FroxelFogCompute.SetConstantBuffer(PerFrameConstBufferID, ComputePerFrameConstantBuffer, 0, ScatterPerFrameCount * sizeof(float));

            /*
            int mediaCount = VolumetricRegisters.VolumetricMediaEntities.Count;
            int maxCount = Math.Max(mediaCount, 1);

            if (object.ReferenceEquals(participatingMediaSphereBuffer, null) || participatingMediaSphereBuffer == null)
            {
                participatingMediaSphereBuffer = new ComputeBuffer(maxCount, MediaSphereStride, ComputeBufferType.Structured);
                //Debug.Log("Created New Compute Buffer");
            }
            else if (maxCount > MediaCount)
            {
                participatingMediaSphereBuffer.Release();
                participatingMediaSphereBuffer = new ComputeBuffer(maxCount, MediaSphereStride, ComputeBufferType.Structured);
                MediaCount = maxCount;
            }

            
            MediaSphere[] mediadata = new MediaSphere[maxCount];

            if (mediaCount < 1)
            {
                //mediadata[0].CenterPosition = Vector3.zero;
                //mediadata[0].LocalExtinction = 0;
                //mediadata[0].LocalRange = 0; 
                //mediadata[0].LocalFalloff = 0;
            }
            else
            {
                for (int i = 0; i < mediadata.Length; i++)
                {
                    //TODO: generalize the strut between the classes so we don't have to recast it here 
                    mediadata[i].CenterPosition = VolumetricRegisters.VolumetricMediaEntities[i].gameObject.transform.position;
                    mediadata[i].LocalExtinction = VolumetricRegisters.VolumetricMediaEntities[i].LocalExtinction();
                    mediadata[i].LocalRange = VolumetricRegisters.VolumetricMediaEntities[i].Scale.magnitude; // temp mag
                    mediadata[i].LocalFalloff = VolumetricRegisters.VolumetricMediaEntities[i].falloffDistance;
                }
            }

            if (participatingMediaSphereBuffer != null)
            {
                participatingMediaSphereBuffer.SetData(mediadata);
                FroxelFogCompute.SetBuffer(ScatteringKernel, ID_media_sphere_buffer, participatingMediaSphereBuffer);
                FroxelFogCompute.SetFloat(ID_media_sphere_buffer_length, mediaCount);
            }
            

            /*
            if (VolumetricConstantBuffer != null && projectionMatrix != null && activeCam != null && vbuff.depthEncodingParams != null)
            {
                VolumetricConstants vConst = new VolumetricConstants();
                vConst.CameraProjectionMatrix = projectionMatrix.transpose;
                vConst.TransposedCameraProjectionMatrix = projectionMatrix;
                vConst._VBufferDistanceEncodingParams = vbuff.depthEncodingParams;
                vConst._VolCameraPos = activeCam.transform.position;
                float[] vConstArray = VolStructToArray(vConst);
                VolumetricConstantBuffer.SetData(vConstArray);
                Shader.SetGlobalConstantBuffer("VolumetricCB", VolumetricConstantBuffer, 0, VolCBCount * sizeof(float));
            }
            */
            PreviousFrameMatrix = projectionMatrix;
            PreviousCameraPosition = activeCam.transform.position;
            ////MATRIX
            ///
            ///camera.projectionMatrix is ALSO broken and returns the final viewport's projection rather than the center XR projection.
            ///cam.GetStereoProjectionMatrix returns the skewed XR projection matrix per eye. Just doing our own calulation
            var gpuProj = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(activeCam.fieldOfView, CamAspectRatio, activeCam.nearClipPlane, 100000f), true);
            PrevViewProjMatrix = gpuProj * activeCam.worldToCameraMatrix;
            //Debug.Log("Dispatching 3");
            FroxelFogCompute.Dispatch(ScatteringKernel, (int)ThreadsToDispatch.x, (int)ThreadsToDispatch.y, (int)ThreadsToDispatch.z);
            //    FroxelStackingCompute.DispatchIndirect
            //CONVERT TO DISPATCH INDIRECT to avoid CPU callback?
            //Debug.Log("Dispatching 4");
            FroxelIntegrationCompute.Dispatch(IntegrateKernel, (int)ThreadsToDispatch.x * 2, (int)ThreadsToDispatch.y, (int)ThreadsToDispatch.z); //x2 for stereo

            if (FroxelBlur == BlurType.Gaussian)
            {
                BlurCompute.Dispatch(BlurKernelX, (int)ThreadsToDispatch.x * 2, (int)ThreadsToDispatch.y, (int)ThreadsToDispatch.z); // Final blur
                BlurCompute.Dispatch(BlurKernelY, (int)ThreadsToDispatch.x * 2, (int)ThreadsToDispatch.y, (int)ThreadsToDispatch.z); // Final blur
            }
            /* Give the shader constant buffer and volumetric render texture to the
             * additional camera data so that on render the camera can set them as
             * globals
             */
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
//#if UNITY_EDITOR
//        if (enableEditorPreview && !Application.isPlaying) RenderPipelineManager.beginCameraRendering += UpdatePreRender;
//#else
        RenderPipelineManager.beginCameraRendering += UpdatePreRender;
//#endif
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

    private void DestroyAllTextureAssets()
    {
        ClipmapBufferA.Clear();
        ClipmapBufferB.Clear();
        ClipmapBufferC.Clear();
        ClipmapBufferD.Clear();

        FroxelBufferA.Clear();
        FroxelBufferB.Clear();
        IntegrationBuffer.Clear();

        BlurBuffer.Clear();
        BlurBufferB.Clear();
        
       // if (createdLightProjectionTexture && LightProjectionTextures != null) { CoreUtils.Destroy(LightProjectionTextures); }
    }



    Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(Camera cam, Vector4 resolution)
    {
        //   var proj = cam.projectionMatrix; //  GL.GetGPUProjectionMatrix(cameraProj, true); //Use this if we run into platform issues
        //bandaid fix. There's an issue with the far clip plane in the matrix projection. 
        var proj = Matrix4x4.Perspective(cam.fieldOfView, CamAspectRatio, cam.nearClipPlane, 100000f); 
        var view = cam.worldToCameraMatrix ;

        var invViewProjMatrix = (proj * view).inverse;

        var transform = Matrix4x4.Scale(new Vector3(-1.0f, -1.0f, -1.0f)) * invViewProjMatrix; // (gpuProj * gpuView).inverse
     //   transform = transform * Matrix4x4.Scale(new Vector3(1.0f, -1.0f, 1.0f));
        transform = transform * Matrix4x4.Translate(new Vector3(-1.0f, -1.0f, 0.0f));
        transform = transform * Matrix4x4.Scale(new Vector3(2.0f * resolution.z, 2.0f * resolution.w, 1.0f)) ;

        return transform.transpose;
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
        if (buffer != null && data != null && stride != null)
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

    /// <summary>
    /// Editor
    /// </summary>
    /// 
    private void OnDrawGizmosSelected()
    {
        if (cam == null || volumetricData == null) return;

        Gizmos.color = Color.black;
;
        Gizmos.matrix = Matrix4x4.TRS(cam.transform.position, cam.transform.rotation, Vector3.one);
        Gizmos.DrawFrustum(Vector3.zero, cam.fieldOfView, volumetricData.near, volumetricData.far, CamAspectRatio);

        Gizmos.color = Color.cyan;
        Gizmos.matrix = Matrix4x4.TRS(ClipmapCurrentPos, Quaternion.identity, Vector3.one * volumetricData.ClipmapScale);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

        Gizmos.color = Color.blue;
        Gizmos.matrix = Matrix4x4.TRS(ClipmapCurrentPos, Quaternion.identity, Vector3.one * volumetricData.ClipmapScale2);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);


        //Gizmos.color = Color.red;
        //Gizmos.matrix = cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
        //Gizmos.DrawWireCube(Vector3.zero, Vector3.one);        

        //Gizmos.color = Color.yellow;
        //Gizmos.matrix = cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);
        //Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

        //Gizmos.color = Color.green;
        //Gizmos.matrix = Matrix4x4.Perspective(cam.fieldOfView, CamAspectRatio, cam.nearClipPlane, 100000f);
        //Gizmos.DrawWireCube(Vector3.zero, Vector3.one);        
        
        //Gizmos.color = Color.magenta;
        //Gizmos.matrix = Matrix4x4.Perspective(cam.fieldOfView, CamAspectRatio, volumetricData.near, volumetricData.far) * Matrix4x4.Translate(new Vector3(cam.stereoSeparation * 0.5f, 0, 0));
        //Gizmos.DrawWireCube(Vector3.zero, Vector3.one);


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
        if (BlurCompute == null)
            BlurCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/VolumetricBlur.compute");
    }



    private void Reset()
    {
        cam = GetComponentInChildren<Camera>();
        assignVaris();
    }


    private void OnValidate()
    {

        //Black Texture in editor to not get in the way. Isolated h ere because shaders should skip volumetric tex in precompute otherwise. 
        // TODO: Add proper scene preview feature
        if (BlackTex == null) BlackTex = CoreUtils.blackVolumeTexture; //(Texture3D)MakeBlack3DTex();
        
        //        UnityEditor.SceneManagement.EditorSceneManager.sceneUnloaded += UnloadKeyword; //adding function when scene is unloaded 
        assignVaris();
        //if (cam == null) cam = GetComponent<Camera>();
        //if (volumetricData.near < cam.nearClipPlane || volumetricData.far > cam.farClipPlane)
        //{
        //    //Auto clamp to inside of the camera's clip planes
        //    volumetricData.near = Mathf.Max(volumetricData.near, cam.nearClipPlane);
        //    volumetricData.far = Mathf.Min(volumetricData.far, cam.farClipPlane);
        //}
        
        //Shader.EnableKeyword(VolumetricsKW); //enabling here so the editor knows that it exists
    }
#endif

    //Using core blackVolumeTexture instead
    //Texture MakeBlack3DTex()
    //{
    //    Debug.Log("Made blank texture");

    //    int size = 1;

    //    Texture3D BlackTex = new Texture3D(1, 1, 1, TextureFormat.ARGB32, false);
    //    var cols = new Color[size * size * size];
    //    float mul = 1.0f / (size - 1);
    //    int idx = 0;
    //    Color c = Color.white;
    //    for (int z = 0; z < size; ++z)
    //    {
    //        for (int y = 0; y < size; ++y)
    //        {
    //            for (int x = 0; x < size; ++x, ++idx)
    //            {
    //                c.r = 0;
    //                c.g = 0;
    //                c.b = 0;
    //                c.a = 1;
    //                cols[idx] = c;
    //            }
    //        }
    //    }

    //    BlackTex.SetPixels(cols);
    //    BlackTex.Apply();
    //    // SetClipmap(BlackTex, 50, Vector3.zero);

    //    Shader.SetGlobalTexture(ID_VolumetricResult, BlackTex);

    //    //    Shader.SetGlobalTexture("_VolumetricClipmapTexture", BlackTex); //Set clipmap for
    //    return BlackTex;
    //}


    //public void UnloadKeyword<Scene>(Scene scene)
    //{
    //    Shader.DisableKeyword("_VOLUMETRICS_ENABLED");

    //    print("The scene was unloaded!");
    //}

    void Clear3DTexture(RenderTexture buffer)
    {
        ClipmapCompute.SetTexture(ID_ClipMapClearKern, ID_Result, buffer);
        ClipmapCompute.Dispatch(ID_ClipMapClearKern, Mathf.Max(buffer.width / 4, 1), Mathf.Max(buffer.height / 4, 1), Mathf.Max(buffer.volumeDepth / 4, 1));

        //RenderTexture activeRT = RenderTexture.active;
        //RenderTexture.active = rt;
        //GL.sRGBWrite = rt.sRGB;
        //if (rt.dimension == TextureDimension.Tex3D)
        //{
        //    CoreUtils.SetRenderTarget(
        //        command,
        //        rt,
        //        ClearFlag.Color, Color.clear,
        //        0, CubemapFace.Unknown, -1
        //    );

        //}
        //else if (rt.dimension == TextureDimension.Cube)
        //{    
        //    Graphics.SetRenderTarget(rt, 0, CubemapFace.PositiveX, 0);
        //    GL.Clear(false, true, color);
        //    Graphics.SetRenderTarget(rt, 0, CubemapFace.PositiveY, 0);
        //    GL.Clear(false, true, color);
        //    Graphics.SetRenderTarget(rt, 0, CubemapFace.PositiveZ, 0);
        //    GL.Clear(false, true, color);
        //    Graphics.SetRenderTarget(rt, 0, CubemapFace.NegativeX, 0);
        //    GL.Clear(false, true, color);
        //    Graphics.SetRenderTarget(rt, 0, CubemapFace.NegativeY, 0);
        //    GL.Clear(false, true, color);
        //    Graphics.SetRenderTarget(rt, 0, CubemapFace.NegativeZ, 0);
        //    GL.Clear(false, true, color);
        //}
        //        CoreUtils.SetRenderTarget(
        //           rt,
        //           BuiltinRenderTextureType.CameraTarget
        //);
        //        RenderTexture.active = activeRT;
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
