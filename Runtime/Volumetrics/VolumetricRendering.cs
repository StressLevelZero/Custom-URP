﻿using System.Collections;
using System.Collections.Generic;
//using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif

//TODO: Add semi dynamic lighting which is generated in the clipmap and not previously baked out. Will need smarter clipmap gen to avoid hitching.
//Add cascading clipmaps to have higher detail up close and include father clipping without exploding memory.

//[RequireComponent(typeof( Camera ) )]
[ExecuteInEditMode]
public class VolumetricRendering : MonoBehaviour
{
    //  public float tempOffset = 0;
     Texture3D BlackTex; //Temp texture for 

    public Camera cam; //Main camera to base settings on
    public VolumetricData volumetricData;

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

    //Required shaders
    [SerializeField, HideInInspector] ComputeShader FroxelFogCompute;
    [SerializeField, HideInInspector] ComputeShader FroxelStackingCompute;
    [SerializeField, HideInInspector] ComputeShader ClipmapCompute;

    //Texture buffers
    RenderTexture ClipmapBufferA;  //Sampling and combining baked maps asynchronously
    RenderTexture FroxelTexture;   //Single froxel projection use for scattering and history reprojection
    RenderTexture StackTexture;    //Integration and stereo reprojection


    /// Dynamic Light Projection///      
    [SerializeField, HideInInspector] List<Light> Lights; // TODO: Make this a smart dynamic list not living here
    public struct LightObject
    {
        public Matrix4x4 LightProjectionMatrix;
        public Vector3 LightPosition;
        public Vector4 LightColor;
        public int LightCookie; //TODO: Add general light cookie system to render engine
    }

    //Figure out how much data is in the struct above
    int LightObjectStride = sizeof(float) * 4*4 + sizeof(float) * 3 + sizeof(float) * 4 + sizeof(int);

     Texture2DArray LightProjectionTextures; // TODO: Make this a smart dynamic list pulling from light cookies

    private static List<LightObject> LightObjects;
    ComputeBuffer LightBuffer;

    /// END Dynamic Light Projection/// 
    /// 

   // public Texture2D BlueNoise; //Temp ref

    //AABB 

    //Stored compute shader IDs and numbers

    protected int FogFroxelKernel = 0;
    protected int StackFroxelKernal = 0;

    Matrix4x4 matScaleBias;
    Vector3 ThreadsToDispatch;

    //Stored shader variable name IDs

    //Froxel Ids
    int CameraProjectionMatrixID = Shader.PropertyToID("CameraProjectionMatrix");
    int TransposedCameraProjectionMatrixID = Shader.PropertyToID("TransposedCameraProjectionMatrix");
    int inverseCameraProjectionMatrixID = Shader.PropertyToID("inverseCameraProjectionMatrix");
    int PreviousFrameMatrixID = Shader.PropertyToID("PreviousFrameMatrix");
    int Camera2WorldID = Shader.PropertyToID("Camera2World");
    int CameraPositionID = Shader.PropertyToID("CameraPosition");

    //Clipmap IDs
    int CameraMotionVectorID = Shader.PropertyToID("CameraMotionVector");
    int ClipmapTextureID = Shader.PropertyToID("_ClipmapTexture");
    int ClipmapTextureID2 = Shader.PropertyToID("_VolumetricClipmapTexture"); //TODO: Make these two the same name
    int ClipmapScaleID = Shader.PropertyToID("_ClipmapScale");
    int ClipmapTransformID = Shader.PropertyToID("_ClipmapPosition");

    int LightObjectsID = Shader.PropertyToID("LightObjects");
    
    //Temp Jitter stuff
    int tempjitter = 0; //TEMP jitter switcher thing 
    [Header("Extra variables")]
    public float jitterDistance = .25f; //figure out disance based on froxel depth
    float[] jitters = new float[2] { 0.0f, 0.5f };


    //Previous view matrix data

    Matrix4x4 PreviousFrameMatrix = Matrix4x4.identity;
    Vector3 PreviousCameraPosition;
    Vector3 previousPos;
    Quaternion previousQuat;

    //General fog settings

    public float HomogeneousMediumDensity = 0.01f;




    private void Awake()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return;
#endif
         Shader.EnableKeyword("_VOLUMETRICS"); //Enable volumetrics. Double check to see if works in build
        //  cam = GetComponent<Camera>();

    }
    void Start() {
#if UNITY_EDITOR
        if (!Application.isPlaying) return;
#endif
        Intialize();
    }
    void CheckCookieList()
    {
        if (LightProjectionTextures != null) return;
        LightProjectionTextures = new Texture2DArray(1, 1, 1, TextureFormat.RGBA32, false);
        Debug.Log("Made blank cookie sheet");
    }

    bool VerifyVolumetricRegisters()
    {
        //Add realtime light check here too
        if (VolumetricRegisters.volumetricAreas.Count > 0)
        {
            Debug.Log(VolumetricRegisters.volumetricAreas.Count + " Volumes ready to render");
            return true;
        }
        Debug.Log("No Volumetric volumes in " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + ". Disabling froxel rendering.");
        this.enabled = false;
        return false;
    }

    void CheckOverrideVolumes()
    {
        var stack = VolumeManager.instance.stack;

        var Volumetrics = stack.GetComponent<Volumetrics>();
        if (Volumetrics != null)
        Volumetrics.PushFogShaderParameters();
    }

    void Intialize()
    {
        CheckOverrideVolumes();
        if (VerifyVolumetricRegisters() == false) return; //Check registers to see if there's anything to render. If not, then disable system.
        CheckCookieList();
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
        rtdiscrpt.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        rtdiscrpt.width = volumetricData.FroxelWidthResolution;
        rtdiscrpt.height = volumetricData.FroxelHeightResolution;
        rtdiscrpt.volumeDepth = volumetricData.FroxelDepthResolution;
        rtdiscrpt.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
        rtdiscrpt.msaaSamples = 1;

        FroxelTexture = new RenderTexture(rtdiscrpt);
        FroxelTexture.Create();


      rtdiscrpt.width = volumetricData.FroxelWidthResolution * 2; // Make double wide texture for stereo use. Make smarter for non VR use case?
        StackTexture = new RenderTexture(rtdiscrpt);
        StackTexture.format = RenderTextureFormat.ARGB32;
        StackTexture.enableRandomWrite = true;
        StackTexture.Create();


        LightObjects = new List<LightObject>();

        FogFroxelKernel = FroxelFogCompute.FindKernel("Scatter");
        FroxelFogCompute.SetTexture(FogFroxelKernel, "Result", FroxelTexture);

        //First Compute pass setup

        SetupClipmap();

        FroxelFogCompute.SetFloat("ClipmapScale", volumetricData.ClipmapScale);
        UpdateClipmap();
        FroxelFogCompute.SetTexture(FogFroxelKernel, ClipmapTextureID, ClipmapBufferA);

        FroxelFogCompute.SetTexture(FogFroxelKernel, "LightProjectionTextureArray",  LightProjectionTextures); // temp light cookie array. TODO: Make dynamic. Add to lighting engine too.
   //     FroxelFogCompute.SetTexture(FogFroxelKernel, "BlueNoise", BlueNoise); // temp light cookie array. TODO: Make dynamic. Add to lighting engine too.

        ///Second compute pass setup

        StackFroxelKernal = FroxelStackingCompute.FindKernel("StepAdd");
        FroxelStackingCompute.SetTexture(StackFroxelKernal, "Result", StackTexture);
        FroxelStackingCompute.SetTexture(StackFroxelKernal, "InLightingTexture", FroxelTexture);

        //Make view projection matricies

        Matrix4x4 CenterProjectionMatrix = matScaleBias * Matrix4x4.Perspective(cam.fieldOfView, cam.aspect, volumetricData.near, volumetricData.far);
        Matrix4x4 LeftProjectionMatrix = matScaleBias * Matrix4x4.Perspective(cam.fieldOfView, cam.aspect, volumetricData.near, volumetricData.far) * Matrix4x4.Translate(new Vector3(cam.stereoSeparation * 0.5f , 0, 0)); //temp ipd scaler. Combine factors when confirmed
        Matrix4x4 RightProjectionMatrix = matScaleBias * Matrix4x4.Perspective(cam.fieldOfView, cam.aspect, volumetricData.near, volumetricData.far) * Matrix4x4.Translate(new Vector3(-cam.stereoSeparation * 0.5f , 0, 0));

        //Debug.Log(cam.stereoSeparation);

        FroxelStackingCompute.SetMatrix("LeftEyeMatrix", LeftProjectionMatrix * CenterProjectionMatrix.inverse);
        FroxelStackingCompute.SetMatrix("RightEyeMatrix", RightProjectionMatrix * CenterProjectionMatrix.inverse);


        //Global Variable setup

        Shader.SetGlobalTexture("_VolumetricResult", StackTexture);
        //   Shader.SetGlobalTexture("_3dTex", StackTexture);
       // DebugRenderCube.material.SetTexture("_3dTexture", StackTexture);

        ThreadsToDispatch = new Vector3(
             Mathf.CeilToInt(volumetricData.FroxelWidthResolution / 4.0f),
             Mathf.CeilToInt(volumetricData.FroxelHeightResolution / 4.0f),
             Mathf.CeilToInt(volumetricData.FroxelDepthResolution / 4.0f)
            );

        Shader.SetGlobalVector("_VolumePlaneSettings", new Vector4(volumetricData.near, volumetricData.far, volumetricData.far - volumetricData.near, volumetricData.near * volumetricData.far) );

        float zBfP1 = 1.0f - volumetricData.far / volumetricData.near;
        float zBfP2 = volumetricData.far / volumetricData.near;
        Shader.SetGlobalVector("_ZBufferParams", new Vector4(zBfP1, zBfP2, zBfP1 / volumetricData.far, zBfP2 / volumetricData.far) );

        Debug.Log("Dispatching " + ThreadsToDispatch);
    }

    void UpdateLights()
    {
        LightObjects.Clear(); //clear and rebuild for now. TODO: Make a smarter constructor
        if (LightBuffer != null) LightBuffer.Release();

        for (int i = 0; i < Lights.Count; i++)
        {
            LightObject lightObject = new LightObject();
            lightObject.LightPosition = Lights[i].transform.position;
            lightObject.LightColor = new Color(
                Lights[i].color.r * Lights[i].intensity,
                Lights[i].color.g * Lights[i].intensity,
                Lights[i].color.b * Lights[i].intensity,
                Lights[i].color.a);
            lightObject.LightProjectionMatrix = matScaleBias 
                * Matrix4x4.Perspective(Lights[i].spotAngle, 1, 0.1f, Lights[i].range) 
                * Matrix4x4.Rotate(Lights[i].transform.rotation).inverse;

            LightObjects.Add(lightObject);
        }        
        LightBuffer = new ComputeBuffer(LightObjects.Count, LightObjectStride);
        LightBuffer.SetData(LightObjects);
        FroxelFogCompute.SetBuffer(FogFroxelKernel, LightObjectsID , LightBuffer); // TODO: move to an int
    }
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
        ClipmapBufferA.Create();        
        ClipmapBufferB = new RenderTexture(ClipRTdiscrpt);
        ClipmapBufferB.Create();

        Shader.SetGlobalTexture("_VolumetricClipmapTexture", ClipmapBufferA); //Set clipmap for
        Shader.SetGlobalFloat("_ClipmapScale", volumetricData.ClipmapScale);
    }
    void CheckClipmap() //Check distance from previous sample and recalulate if over threshold. TODO: make it resample chunks
    {
        if (Vector3.Distance(ClipmapCurrentPos, cam.transform.position) > volumetricData.ClipmapResampleThreshold) UpdateClipmap();
    }

    RenderTexture ClipmapBufferB;  //Second buffer for mobile. Don't seem to need this with the PC version TODO: make this platform dependent?
    bool FlipFlopBuffer = true;
    void UpdateClipmap()
    {
        //TODO: chache ids 
        int ClipmapKernal = ClipmapCompute.FindKernel("ClipMapGen");
        int ClearClipmapKernal = ClipmapCompute.FindKernel("ClipMapClear");
        ClipmapTransform = cam.transform.position; //don't need this recast ¯\_(ツ)_/¯
        //TODO: bake out variables at start to avoid extra math per clip gen

        //Clipmap variables
        ClipmapCompute.SetVector("ClipmapWorldPosition", ClipmapTransform - (0.5f * volumetricData.ClipmapScale * Vector3.one));
        ClipmapCompute.SetFloat("ClipmapScale", volumetricData.ClipmapScale);


        //Clear previous capture
        ClipmapCompute.SetTexture(ClearClipmapKernal, "Result", ClipmapBufferA);
        ClipmapCompute.Dispatch(ClearClipmapKernal, volumetricData.ClipMapResolution / 4, volumetricData.ClipMapResolution / 4, volumetricData.ClipMapResolution / 4);
        ClipmapCompute.SetTexture(ClearClipmapKernal, "Result", ClipmapBufferB);
        ClipmapCompute.Dispatch(ClearClipmapKernal, volumetricData.ClipMapResolution / 4, volumetricData.ClipMapResolution / 4, volumetricData.ClipMapResolution / 4);

       

        //Loop through bake texture volumes and put into clipmap //TODO: Add daynamic pass for static unbaked elements
        for (int i=0; i < VolumetricRegisters.volumetricAreas.Count; i++)
        {
                        FlipFlopBuffer = !FlipFlopBuffer;

            if (FlipFlopBuffer)
            {
                ClipmapCompute.SetTexture(ClipmapKernal, "PreResult", ClipmapBufferB);
                ClipmapCompute.SetTexture(ClipmapKernal, "Result", ClipmapBufferA);
            }
            else
            {
                ClipmapCompute.SetTexture(ClipmapKernal, "PreResult", ClipmapBufferA);
                ClipmapCompute.SetTexture(ClipmapKernal, "Result", ClipmapBufferB);
            }

            //Volumetric variables
            ClipmapCompute.SetTexture(ClipmapKernal, "VolumeMap", VolumetricRegisters.volumetricAreas[i].bakedTexture); 
            ClipmapCompute.SetVector("VolumeWorldSize", VolumetricRegisters.volumetricAreas[i].NormalizedScale);
            ClipmapCompute.SetVector("VolumeWorldPosition", VolumetricRegisters.volumetricAreas[i].Corner);

            ClipmapCompute.Dispatch(ClipmapKernal, volumetricData.ClipMapResolution / 4, volumetricData.ClipMapResolution / 4, volumetricData.ClipMapResolution / 4);
            //Debug.Log("Rendered " + VolumetricRegisters.volumetricAreas[i].bakedTexture.name);
            // ClipmapCompute.SetTexture(ClipmapKernal, "PreResult", ClipmapTexture);
            //Graphics.CopyTexture(ClipmapTexture, ClipmapTexture2); //How heavy is this?? Should I make a copy compute shader or swap inputs?
        }


        if (FlipFlopBuffer)
        {
            SetClipmap(ClipmapBufferA, volumetricData.ClipmapScale, ClipmapTransform);
        }
        else
        {
            SetClipmap(ClipmapBufferB, volumetricData.ClipmapScale, ClipmapTransform);
        }
        ClipmapCurrentPos = ClipmapTransform; //Set History
    }
    void SetClipmap(Texture ClipmapTexture, float ClipmapScale, Vector3 ClipmapTransform)
    {
        //TODO COMBINE THESE
        Shader.SetGlobalTexture(ClipmapTextureID2, ClipmapTexture); //Set clipmap for
        Shader.SetGlobalTexture(ClipmapTextureID, ClipmapTexture); //Set clipmap for
        Shader.SetGlobalFloat(ClipmapScaleID, ClipmapScale);
        Shader.SetGlobalVector(ClipmapTransformID, ClipmapTransform);
    }
    #endregion
    void Update()
    {
        CheckOverrideVolumes();

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            return;
        }
#endif

        //Make this jitter with different values over time instead of just back and forth

        if (tempjitter >= jitters.Length - 1) {
            tempjitter = 0;
        }
        else
        {
            tempjitter++;    
        }

    //    float jitterOffet = Mathf.Lerp(0, 1, jitters[tempjitter]); //loop through jitters

       // Debug.Log(tempjitter + " " + jitterOffet);

        Matrix4x4 projectionMatrix = Matrix4x4.Perspective(cam.fieldOfView, cam.aspect, volumetricData.near, volumetricData.far) * Matrix4x4.Rotate(cam.transform.rotation).inverse;
        projectionMatrix = matScaleBias * projectionMatrix ; 
        
        //Previous frame's matrix//!!!!!!!!!

        FroxelFogCompute.SetMatrix(PreviousFrameMatrixID, PreviousFrameMatrix);///
        //   FroxelFogCompute.SetMatrix(PreviousFrameMatrixID, PreviousFrameMatrix );///
        ///!!!!!!!!!!!!!!
        ///

        //     UpdateLights();

        CheckClipmap(); // UpdateClipmap();

        //  Matrix4x4 lightMatrix = matScaleBias * Matrix4x4.Perspective(LightPosition.spotAngle, 1, 0.1f, LightPosition.range) * Matrix4x4.Rotate(LightPosition.transform.rotation).inverse;

        FroxelFogCompute.SetTexture(FogFroxelKernel, "PreviousFrameLighting", FroxelTexture); //Send as another ref to use built-in sampler
        FroxelStackingCompute.SetFloat("HomogeneousMediumDensity", HomogeneousMediumDensity);
        
        FroxelFogCompute.SetMatrix(inverseCameraProjectionMatrixID, projectionMatrix.inverse);
        FroxelFogCompute.SetMatrix(Camera2WorldID, cam.transform.worldToLocalMatrix);
        //FroxelFogCompute.SetMatrix("LightProjectionMatrix", lightMatrix);
        //FroxelFogCompute.SetVector("LightPosition", LightPosition.transform.position);
        //FroxelFogCompute.SetVector("LightColor", LightPosition.color * LightPosition.intensity);
        //FroxelFogCompute.SetVector("Jittery", new Vector3(0.5f, 0.5f, tempOffset)); //Loop through jitters. 
        //jitters[tempjitter]


        Shader.SetGlobalMatrix(CameraProjectionMatrixID,  projectionMatrix);
        Shader.SetGlobalMatrix(TransposedCameraProjectionMatrixID,  projectionMatrix.transpose); //Fragment shaders require the transposed version
        Shader.SetGlobalVector(CameraPositionID, cam.transform.position); //Can likely pack this into the 4th row of the projection matrix 
        Shader.SetGlobalVector(CameraMotionVectorID, cam.transform.position - PreviousCameraPosition); //Extract a motion vector per frame

        PreviousFrameMatrix = projectionMatrix;
        PreviousCameraPosition = cam.transform.position;

        FroxelFogCompute.Dispatch(FogFroxelKernel, (int)ThreadsToDispatch.x, (int)ThreadsToDispatch.y, (int)ThreadsToDispatch.z);
    //    FroxelStackingCompute.DispatchIndirect
        //CONVERT TO DISPATCH INDIRECT to avoid CPU callback?

        FroxelStackingCompute.Dispatch(StackFroxelKernal, (int)ThreadsToDispatch.x * 2, (int)ThreadsToDispatch.y, (int)ThreadsToDispatch.z); //x2 for stereo

    }

    private void OnEnable()
    {
        Shader.EnableKeyword("_VOLUMETRICS_ENABLED");
#if UNITY_EDITOR
        if (!Application.isPlaying) return;
#endif
        Intialize();
    }
    private void OnDisable() //Disable this if we decide to just pause rendering instead of removing. 
    {
        ReleaseAssets();
    }
    private void OnDestroy()
    {
        ReleaseAssets();
    }


    void SetComputeVariables()
    {


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
        Gizmos.DrawFrustum(Vector3.zero, cam.fieldOfView, volumetricData.near, volumetricData.far, cam.aspect);

        Gizmos.color = Color.cyan;
        Gizmos.matrix = Matrix4x4.TRS(ClipmapCurrentPos, Quaternion.identity, Vector3.one * volumetricData.ClipmapScale);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

    }

    void ReleaseAssets()
    {
        Shader.DisableKeyword("_VOLUMETRICS_ENABLED");
        if (ClipmapBufferA!= null) ClipmapBufferA.Release();
        if (FroxelTexture != null) FroxelTexture.Release();   
        if (StackTexture != null)StackTexture.Release();
    }

#if UNITY_EDITOR
    private void Reset()
    {
        cam = GetComponentInChildren<Camera>();
        //Get shaders and seri
        if (FroxelFogCompute == null)
            FroxelFogCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/VolumetricScattering.compute");
        if (FroxelStackingCompute == null)
            FroxelStackingCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/StepAdd.compute");
        if (ClipmapCompute == null)
            ClipmapCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/ClipMapGenerator.compute");
    }
#endif

    private void OnValidate()
    {
#if UNITY_EDITOR
        //Black Texture in editor to not get in the way. Isolated h ere because shaders should skip volumetric tex in precompute otherwise. 
        // TODO: Add proper scene preview feature
         if (!UnityEditor.EditorApplication.isPlaying && BlackTex == null ) BlackTex = (Texture3D)MakeBlack3DTex();
//        UnityEditor.SceneManagement.EditorSceneManager.sceneUnloaded += UnloadKeyword; //adding function when scene is unloaded 

#endif
        //if (cam == null) cam = GetComponent<Camera>();
        //if (volumetricData.near < cam.nearClipPlane || volumetricData.far > cam.farClipPlane)
        //{
        //    //Auto clamp to inside of the camera's clip planes
        //    volumetricData.near = Mathf.Max(volumetricData.near, cam.nearClipPlane);
        //    volumetricData.far = Mathf.Min(volumetricData.far, cam.farClipPlane);
        //}

        Shader.EnableKeyword("_VOLUMETRICS_ENABLED"); //enabling here so the editor knows that it exists
    }

    Texture MakeBlack3DTex()
    {
        Debug.Log("Made blank texture");

        int size = 1;

        Texture3D BlackTex = new Texture3D(1, 1, 1, TextureFormat.ARGB32, false);
        var cols = new Color[size * size * size];
        float mul = 1.0f / (size - 1);
        int idx = 0;
        Color c = Color.white;
        for (int z = 0; z < size; ++z)
        {
            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x, ++idx)
                {
                    c.r = 0;
                    c.g = 0;
                    c.b = 0;
                    cols[idx] = c;
                }
            }
        }

        BlackTex.SetPixels(cols);
        BlackTex.Apply();
        // SetClipmap(BlackTex, 50, Vector3.zero);

        Shader.SetGlobalTexture("_VolumetricResult", BlackTex);

        //    Shader.SetGlobalTexture("_VolumetricClipmapTexture", BlackTex); //Set clipmap for
        return BlackTex;
    }


    //public void UnloadKeyword<Scene>(Scene scene)
    //{
    //    Shader.DisableKeyword("_VOLUMETRICS_ENABLED");

    //    print("The scene was unloaded!");
    //}


}