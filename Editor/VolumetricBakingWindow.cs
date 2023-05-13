using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using System.IO;
using Unity.Collections;
using SLZ.SLZEditorTools;
using UnityEngine.Rendering;
using static UnityEditor.Experimental.GraphView.GraphView;

public class VolumetricBaking : EditorWindow
{
    // Add menu item named "My Window" to the Window menu
    [MenuItem("Stress Level Zero/Volumetric Baking")]
    public static void ShowWindow()
    {
        //Show existing window instance. If one doesn't exist, make one.
        GetWindow(typeof(VolumetricBaking));
        //   BuildComboList();
        //   BuildSelectionGrid();
    }

    //TODO: Save to scene asset file 
    //Public variables
    [Range(1, 2048)]
    public int AreaLightSamples = 256;
    [Range(4, 128), Tooltip("Size of the render buckets.")]
    public int BucketSize = 32;
    public bool DXRAcceletration = true;
    public bool SkyboxContribution = false;
    public Cubemap CustomEnvorment;
    public int EnvLightSamples = 2048;

    public float VolExposure = 0.05f;

    //interal
    bool saveWarning = false;
    bool Running = false;
    private void OnGUI()
    {
        DisplayProgress();
        EditorGUILayout.LabelField("System Settings", EditorStyles.boldLabel);
        GUI.enabled = !DXRAcceletration;
        BucketSize = EditorGUILayout.IntSlider("Bucket Size", BucketSize, 4, 256);
        GUI.enabled = true;
        VolExposure = EditorGUILayout.Slider("Debug Exposure", RefreshExposure(VolExposure), 0, 2);
        DXRAcceletration = EditorGUILayout.Toggle("DXR Acceletration", DXRAcceletration);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Light Settings", EditorStyles.boldLabel);
        AreaLightSamples = EditorGUILayout.IntSlider("Area Samples", AreaLightSamples, 1, 1024);

        EditorGUILayout.Space();
        GUI.enabled = DXRAcceletration;
        EditorGUILayout.LabelField("Enviorment Settings", EditorStyles.boldLabel);
        SkyboxContribution = EditorGUILayout.Toggle("Skybox Contribution", SkyboxContribution);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Custom Skybox", EditorStyles.label);
        CustomEnvorment = (Cubemap)EditorGUILayout.ObjectField(CustomEnvorment, typeof(Cubemap), true);
        EditorGUILayout.EndHorizontal();
        EnvLightSamples = EditorGUILayout.IntSlider("Environmental Samples", EnvLightSamples, 1, 8192 ); ;
        GUI.enabled = true;
        //       EditorGUILayout.IntField(AreaLightSamples, "Area light samples" );


        if (GUILayout.Button("Bake Volumetrics"))
        {
            ClearWarning();
            if (VerifySettings() == false) return; //Check settings and return if something is wrong
            if (DXRAcceletration)
            {
                BakeDXR();
            }
            else
            {
                RebuildMeshObjectBuffers();
                BakeLights();
                ReleaseBuffers();
            }

        };

        EditorGUILayout.LabelField(BakingStatus);
        WarningGUI();
        if (saveWarning) if (GUILayout.Button("Save scene(s)")) SaveScenes();

        //      EditorGUILayout.HelpBox("Some warning text", MessageType.Warning); //Todo: add warning box to window
    }

    float RefreshExposure(float Exposure)
    {
        Shader.SetGlobalFloat("_VolExposure", Exposure);
        return Exposure;
    }

    struct WarningStatus
    {
        public bool Display;
        public string Text;
    }

    WarningStatus warningStatus;

    void WarningGUI()
    {
        if (warningStatus.Display == false) return;
        EditorGUILayout.HelpBox(warningStatus.Text, MessageType.Warning);
    }

    struct Progress{
        public string title;
        public string info;
        public float percent;
    }
    Progress progress;
    double ProgressTimeStart;
    void DisplayProgress()
    {
        if (Running)
        {
            EditorUtility.DisplayProgressBar(progress.title + EditorApplication.timeSinceStartup, progress.info, progress.percent);
        }
    }

    private void UpdateProgress(string title, string info, float percent)
    {
        progress.title = title;
        progress.info = info;
        progress.percent = percent;

        DisplayProgress();

    }
    string BakingStatus;

     void UpdateStatus(string Status)
    {
        BakingStatus = Status;
        Debug.Log(Status);
        Repaint();
    }

    void UpdateWarning(string text)
    {
        warningStatus.Display = true;
        warningStatus.Text = text;
        Debug.LogWarning(warningStatus.Text);
    }

    void ClearWarning()
    {
        warningStatus.Display = false;
        warningStatus.Text = null;
        saveWarning = false;
    }

    bool VerifySettings()
    {
        if (DXRAcceletration && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Direct3D12)
        {
            UpdateWarning("DirectX 12 not in use. DXR Acceletration requires it.");
            return false;
        }


        if (VolumetricRegisters.volumetricAreas.Count < 1) //Checking volumes
        {
            UpdateWarning("No volumetric areas in the scene. Nothing to bake.");
            return false;
        }

        if (AreScenesDirty() == true) //Checking if scenes are dirty then ask to save
        {
           bool notCancelled = UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            if (notCancelled)
            {
                if (AreScenesDirty()) //checking AGAIN because only sets to false when cancelled
                {
                    UpdateWarning("Save your scene before baking."); //This is not techincally required, but it helps save progress incase of crash
                    saveWarning = true;
                    return false;
                }
            }
            
            else
            {
                return false; //cancelled
            }
        }

        if (SceneManager.sceneCount > 1) //Checking scene count
        {
            UpdateWarning("More than one scene open. Saving to active scene.");
        }

        return true; //everything checks out
    }
    bool AreScenesDirty()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).isDirty) return true;
        }
        return false;
    }

    void SaveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

        ClearWarning();
    }

    void SaveScenes()
    {

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene currentS = SceneManager.GetSceneAt(i);
            if (currentS.isDirty)
            {
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(currentS);
                Debug.Log("Saved " + currentS.name);
            }
        }
        AssetDatabase.SaveAssets();
        ClearWarning();
    }


   // public int tex1Res = 64;
   //  ComputeShader BakingShader; // Baking shader
   // ComputeShader slicer; //Slicer shader for saving
   //public Light[] BakeLights;

    //  public Vector3 DirectionalLight = new Vector3(0,1,0);

    public Vector3 Size = Vector3.one;

    public float AmbientMedia = 0;

    public GameObject[] Spheres;

    //Data Lists
    public struct LightStruct
    {
        public Color[] color;
        public float[] extinction;
        public Vector3[] Position;
    }


    /// <summary>
    /// Triangle Object
    /// </summary>

    struct MeshObject
    {
        public Matrix4x4 localToWorldMatrix;
        public int indices_offset;
        public int indices_count;
    }

    private static List<MeshObject> _meshObjects = new List<MeshObject>();
    private static List<Vector3> _vertices = new List<Vector3>();
    private static List<int> _indices = new List<int>();
    private ComputeBuffer _meshObjectBuffer;
    private ComputeBuffer _vertexBuffer;
    private ComputeBuffer _indexBuffer;

    struct Debuger
    {
        public int DebugCcounter;
    }
    /////////////// Unused
    public void BakeVolumetrics()
    {
        System.DateTime startTime = System.DateTime.Now;

        UpdateStatus("Baking " + SceneManager.GetActiveScene().name);

        //Make RT
        ComputeShader BakingShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/VolumetricFog/Shaders/VolumetricBaking.compute");

        //float displayProgress = 0;

        //EditorUtility.DisplayProgressBar("Baking Volumetrics: ", "Shows a progress bar for the given seconds", displayProgress);


        for (int j = 0; j < VolumetricRegisters.volumetricAreas.Count; j++)
        {
       //     EditorUtility.DisplayProgressBar("Baking Volumetrics... ", VolumetricRegisters.volumetricAreas[j].name, (float)j / (float)VolumetricRegisters.volumetricAreas.Count );

            Vector3Int Texels = VolumetricRegisters.volumetricAreas[j].NormalizedTexelDensity;

            RenderTextureDescriptor rtdiscrpt = new RenderTextureDescriptor();
            rtdiscrpt.enableRandomWrite = true;
            rtdiscrpt.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
            rtdiscrpt.width = Texels.x;
            rtdiscrpt.height = Texels.y;
            rtdiscrpt.volumeDepth = Texels.z;
            rtdiscrpt.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat; //R32G32B32A32_SFloat is excessive, the compressed formats won't befefit and even lightmaps are saved as 16 bit EXRs
			rtdiscrpt.msaaSamples = 1;

            RenderTexture RT3d = new RenderTexture(rtdiscrpt);
            RT3d.Create();

          //  Color[] colorstring = new Color[RT3d.width * RT3d.height * RT3d.depth];
            //for (int c = 0; c < colorstring.Length; c++) colorstring[c] = Color.cyan;

            //Setup light data

            Light[] PointLights = GatherBakedLights(LightType.Point);
            Light[] DirectionalLights = GatherBakedLights(LightType.Directional);

            Vector4[] lightColors = new Vector4[PointLights.Length];
            Vector4[] lightPos = new Vector4[PointLights.Length];

            for (int i = 0; i < PointLights.Length; i++)
            {
                lightColors[i] = PointLights[i].color * PointLights[i].intensity;
                lightPos[i] = PointLights[i].transform.position;
            }

            //participatingMediaEntities = FindObjectsOfType<ParticipatingMediaEntity>();

            Vector4[] mediaPos = new Vector4[VolumetricRegisters.VolumetricMediaEntities.Count];
            Vector4[] mediaAbs = new Vector4[VolumetricRegisters.VolumetricMediaEntities.Count];

            for (int i = 0; i < mediaPos.Length; i++)
            {
                mediaPos[i] = VolumetricRegisters.VolumetricMediaEntities[i].transform.position;
              //  mediaAbs[i] = new Vector4(VolumetricRegisters.VolumetricMediaEntities[i].Absorption, 0, 0, 0); //Only Absorption for now
            }


            //Setup and dispatch Baking compute shader

        //    Debug.Log("Baking " + PointLights.Length + " lights");
       //     Debug.Log("Baking " + mediaPos.Length + " Media");

            //Setting shader variables

            int shaderKernel = BakingShader.FindKernel("VolumetricAreaBake");
            BakingShader.SetTexture(shaderKernel, "AccumulatedLights", RT3d);
            BakingShader.SetVectorArray("LightColor", lightColors);
            BakingShader.SetVectorArray("LightPosition", lightPos);
            BakingShader.SetInt("LightCount", lightColors.Length);

            BakingShader.SetVectorArray("MediaSphere", mediaPos);
            BakingShader.SetVectorArray("MediaSphereAbsorption", mediaAbs);
            BakingShader.SetFloat("AmbientMedia", AmbientMedia);
            BakingShader.SetInt("MediaSphereCount", mediaPos.Length);

            BakingShader.SetVector("Size", VolumetricRegisters.volumetricAreas[j].NormalizedScale); 
            BakingShader.SetVector("Position", VolumetricRegisters.volumetricAreas[j].Corner); 
            //Only select the first directional light. Should only have one in a scene, but will add support for more later for artists
            BakingShader.SetVector("DirectionalLightDirection", DirectionalLights[0].transform.rotation.eulerAngles);
            BakingShader.SetVector("DirectionalLightColor", DirectionalLights[0].color * DirectionalLights[0].intensity);
            //Temp Shadow spheres

            //Vector4[] SpherePos = new Vector4[Spheres.Length];
            Vector4[] SpherePos = new Vector4[0];

            //for (int i = 0; i < SpherePos.Length; i++)
            //{
            //    SpherePos[i] = new Vector4(
            //        Spheres[i].transform.position.x,
            //        Spheres[i].transform.position.y,
            //        Spheres[i].transform.position.z,
            //        Spheres[i].transform.lossyScale.z * 0.5f);
            //}

            // SetComputeBuffer("_Spheres", _sphereBuffer);
            SetComputeBuffer("_MeshObjects", BakingShader, shaderKernel, _meshObjectBuffer);
            SetComputeBuffer("_Vertices", BakingShader, shaderKernel, _vertexBuffer);
            SetComputeBuffer("_Indices", BakingShader, shaderKernel, _indexBuffer);

            BakingShader.SetVectorArray("OpaqueSphere", SpherePos);
            BakingShader.SetInt("SphereCount", SpherePos.Length);

            Vector3 ThreadsToDispatch = new Vector3(
            Mathf.CeilToInt((float)Texels.x / 4.0f),
            Mathf.CeilToInt((float)Texels.y / 4.0f),
            Mathf.CeilToInt((float)Texels.z / 4.0f)
            );

            ///GPU Baking
            ///    
            /// 

            ///Sending data to the GPU
            Debuger[] debuger = new Debuger[1];
            debuger[0].DebugCcounter = 0;

            int DebugCountStride = sizeof(int); //Size of debug strut
            int DebugID = Shader.PropertyToID("DebugBuffer");

            ComputeBuffer BakeBuffer = new ComputeBuffer(1, DebugCountStride);
            BakeBuffer.SetData(debuger);
            BakingShader.SetBuffer(shaderKernel, DebugID, BakeBuffer);
            ///

         //   Graphics.CreateGraphicsFence ?
         

            //DISPATCHING
            
            BakingShader.Dispatch(shaderKernel, (int)ThreadsToDispatch.x, (int)ThreadsToDispatch.y, (int)ThreadsToDispatch.z);


          //  while 
            
            //Sending data back to the CPU to check if work is done before writing to disk
         //   BakeBuffer.GetData(debuger);
     //       Debug.Log("Shot " + debuger[0].DebugCcounter + " rays");
            BakeBuffer.Release(); //Avoiding memory leak
            ///

            //Define path and save 3d texture
            string path = CheckDirectoryAndReturnPath() + j;
			//RT3d.SaveToTexture3D(path);
			Texture3D ReadBackTex = ReadRT2Tex3D(RT3d);
			RT3d.Release();
			Vol3d.WriteTex3DToVol3D(ReadBackTex, path + Vol3d.fileExtension);
			AssetDatabase.ImportAsset(path + Vol3d.fileExtension);

            VolumetricRegisters.volumetricAreas[j].bakedTexture = (Texture3D) AssetDatabase.LoadAssetAtPath(path + Vol3d.fileExtension, typeof(Texture3D) );

            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
            propertyBlock.SetTexture("_3dTexture", VolumetricRegisters.volumetricAreas[j].bakedTexture);
            //    VolumetricRegisters.volumetricAreas[j].DebugCube.SetPropertyBlock(propertyBlock);
            Repaint();

        }

        EditorUtility.ClearProgressBar();

        System.DateTime endTime = System.DateTime.Now;

        UpdateStatus(" Volumetric bake took " + (endTime.Minute - startTime.Minute) + " Minutes and " +
            (endTime.Second - startTime.Second) + " Seconds. Baked "+ VolumetricRegisters.volumetricAreas.Count + " areas."
            );

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }


	Texture3D ReadRT2Tex3D(RenderTexture rt)
	{
		GraphicsFormat gfmt = rt.graphicsFormat;
		TextureFormat tfmt = GraphicsFormatUtility.GetTextureFormat(gfmt);
		int width = rt.width;
		int height = rt.height;
		int depth = rt.volumeDepth;

		Texture3D output = new Texture3D(width, height, depth, gfmt, TextureCreationFlags.None);
		NativeArray<byte> outputRaw = output.GetPixelData<byte>(0);

		AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(rt, 0);
		request.WaitForCompletion();
		if (request.done)
		{
			int layers = request.layerCount;
			if (layers != depth)
			{
				Debug.LogError("Unexpected number of layers: " + layers);
			}
			int currPtr = 0;
			for (int i = 0; i < layers; i++)
			{
				NativeArray<byte> readBackRaw = request.GetData<byte>(i);
				NativeArray<byte>.Copy(readBackRaw, 0, outputRaw, currPtr, readBackRaw.Length);
				currPtr += readBackRaw.Length;
				readBackRaw.Dispose();
			}
		}
		else
		{
			Debug.LogError("Request never completed");
		}
		//Texture2D temp = new Texture2D(width, height, gfmt, TextureCreationFlags.None);
		//Rect sliceRect = new Rect(0, 0, width, height);
		//int sliceSize = width * height;
		//RenderTexture oldActive = RenderTexture.active;
		//RenderTexture.active = rt;
		//int outputRawPtr = 0;
		//for (int depthSlice = 0; depthSlice < depth; depthSlice++)
		//{
		//	Graphics.SetRenderTarget(rt, 0, CubemapFace.Unknown, depthSlice);
		//	temp.ReadPixels(sliceRect,0,0);
		//	temp.Apply(false);
		//	NativeArray<byte> tempNative = temp.GetPixelData<byte>(0);
		//	NativeArray<byte>.Copy(tempNative, 0, outputRaw, outputRawPtr, tempNative.Length);
		//	outputRawPtr += tempNative.Length;
		//}
		//Graphics.SetRenderTarget(oldActive, 0, CubemapFace.Unknown, 0);
		//RenderTexture.active = oldActive;
		return output;
	}
    //////

    ComputeShader BakingShader;


    void BakeLights()
    {
        System.DateTime startTime = System.DateTime.Now;
        BakingShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/VolumetricBaking.compute");
        UpdateStatus("Baking " + SceneManager.GetActiveScene().name);
        ComputeShader BlitShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/3dBlit.compute");
        int BlitBucketKernal = BlitShader.FindKernel("BlitBucket");

        Running = true;

        for (int j = 0; j < VolumetricRegisters.volumetricAreas.Count; j++)
        {
        //    EditorUtility.DisplayProgressBar("Baking Volumetrics... ", VolumetricRegisters.volumetricAreas[j].name, (float)j / (float)VolumetricRegisters.volumetricAreas.Count);
            
            Vector3Int Texels = VolumetricRegisters.volumetricAreas[j].NormalizedTexelDensity;
            RenderTextureDescriptor rtdiscrpt = new RenderTextureDescriptor();
            rtdiscrpt.enableRandomWrite = true;
            rtdiscrpt.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
            rtdiscrpt.width = Texels.x;
            rtdiscrpt.height = Texels.y;
            rtdiscrpt.volumeDepth = Texels.z;
            rtdiscrpt.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat; 
            rtdiscrpt.msaaSamples = 1;

            //Target buffer
            RenderTexture RT3d = new RenderTexture(rtdiscrpt);
            RT3d.Create();
            

            //Bucket
            rtdiscrpt.width = BucketSize;
            rtdiscrpt.height = BucketSize;
            rtdiscrpt.volumeDepth = BucketSize;

            RenderTexture bucketBuffer = new RenderTexture(rtdiscrpt);
            bucketBuffer.Create();

            Light[] PointLights = GatherBakedLights(LightType.Point);

            //Figuring out the buckets per dimension

            int bx = Mathf.CeilToInt((float)Texels.x / BucketSize);
            int by = Mathf.CeilToInt((float)Texels.y / BucketSize);
            int bz = Mathf.CeilToInt((float)Texels.z / BucketSize);

            //Total number of buckets
            int BucketCount = bx * by * bz;

            Debug.Log(BucketCount + " buckets");

            BlitShader.SetTexture(BlitBucketKernal, "BucketBuffer", bucketBuffer);
            BlitShader.SetTexture(BlitBucketKernal, "Result", RT3d);


            //Bucket Loop
            for (int b = 0; b < BucketCount; b++)
            {

                Vector3 ThreadsToDispatch = new Vector3(
                            Mathf.CeilToInt((float)Texels.x / 4.0f),
                            Mathf.CeilToInt((float)Texels.y / 4.0f),
                            Mathf.CeilToInt((float)Texels.z / 4.0f) );

                Vector3 BucketThreads = new Vector3(
                            Mathf.CeilToInt((float)BucketSize / 4.0f),
                            Mathf.CeilToInt((float)BucketSize / 4.0f),
                            Mathf.CeilToInt((float)BucketSize / 4.0f));


                //Generate cell offset
              
                int x = b % bx;
                int y = (b / bx) % by;
                int z = b / (by * bx);

                Vector3Int CellOffset = new Vector3Int(x, y, z);
                Vector3Int TextileOffset = CellOffset * BucketSize;

            //    Debug.Log(TextileOffset + ", size:" + BucketSize);

                ///    BlitShader.set
                ///    
                //Clear buffer
                BakingShader.SetTexture(BakingShader.FindKernel("ClearBuffer"), "AccumulatedLights", bucketBuffer);
                BakingShader.Dispatch(BakingShader.FindKernel("ClearBuffer"), (int)BucketThreads.x, (int)BucketThreads.y, (int)BucketThreads.z);

                //Light Loop
                for (int i = 0; i < PointLights.Length; i++)
                {
                    UpdateProgress("Baking " + (j + 1) + "/" + VolumetricRegisters.volumetricAreas.Count + " "
                    + VolumetricRegisters.volumetricAreas[j].name + " "
                    + (System.DateTime.Now.Minute - startTime.Minute) + ":" + (System.DateTime.Now.Second - startTime.Second) //TODO: Format correctly
                    , PointLights[i].name, (float)i / PointLights.Length);

                    //TODO: Do some checking to only render lights affecting the area. AABB?      

                    //Render to bucket buffer
                    DispatchLight(PointLights[i], bucketBuffer, new Vector3Int(BucketSize, BucketSize, BucketSize), j, TextileOffset);                
                }

                //Blit from bucket to larger buffer
                BlitShader.SetTexture(BlitBucketKernal, "BucketBuffer", bucketBuffer); //bucket buffer
                BlitShader.SetTexture(BlitBucketKernal, "Result", RT3d); //target buffer
                BlitShader.SetVector( "BucketOffset", (Vector3)TextileOffset) ;
                BlitShader.SetInt( "BucketSize", BucketSize) ;
                BlitShader.Dispatch(BlitBucketKernal, (int)ThreadsToDispatch.x, (int)ThreadsToDispatch.y, (int)ThreadsToDispatch.z);

                //     RT3d

            }
            //do
            //{

            //} while (i < 0);
            //bool dothething = true;
            //while (dothething)
            //{
            //    UpdateProgress("Baking " + (j + 1) + "/" + VolumetricRegisters.volumetricAreas.Count + " "
            //    + VolumetricRegisters.volumetricAreas[j].name + " "
            //    + (System.DateTime.Now.Minute - startTime.Minute) + ":" + (System.DateTime.Now.Second - startTime.Second) //TODO: Format correctly
            //    , PointLights[i].name, (float)i / PointLights.Length);

            //    //TODO: Do some checking to only render lights affecting the area. AABB?                
            //    DispatchLight(PointLights[i], RT3d, Texels, j);

            //    Debug.Log("");

            //    return;
            //}

            //Define path and save 3d texture
            string path = CheckDirectoryAndReturnPath() + j;
			Texture3D ReadBackTex = ReadRT2Tex3D(RT3d);
			RT3d.Release();
			Vol3d.WriteTex3DToVol3D(ReadBackTex, path + Vol3d.fileExtension);

            bucketBuffer.Release();
			AssetDatabase.ImportAsset(path + Vol3d.fileExtension);
			VolumetricRegisters.volumetricAreas[j].bakedTexture = (Texture3D)AssetDatabase.LoadAssetAtPath(path + Vol3d.fileExtension, typeof(Texture3D));
		 //   Debug.Log(VolumetricRegisters.volumetricAreas[j].gameObject.scene.name + VolumetricRegisters.volumetricAreas[j].name);
			UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(VolumetricRegisters.volumetricAreas[j].gameObject.scene);

        }

        Running = false;
        EditorUtility.ClearProgressBar();

        System.DateTime endTime = System.DateTime.Now;

        UpdateStatus(" Volumetric bake took " + (endTime.Minute - startTime.Minute) + " Minutes and " +
            (endTime.Second - startTime.Second) + " Seconds. Baked " + VolumetricRegisters.volumetricAreas.Count + " areas."
            );

     //   UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

    }

    string CheckDirectoryAndReturnPath()
    {
        //Define path 
        string path = SceneManager.GetActiveScene().path;
        path = path.Replace(".unity", "");
        if (!Directory.Exists(path)) //Check if path exists
        {
            Directory.CreateDirectory(path); //if it doesn't, create it
            Debug.Log("Made Directory " + path);
            AssetDatabase.Refresh();
        }
        return path + "/" + "Volumemap-";
    }

    RenderTexture initializeVolume(int i)
    {

        Vector3Int Texels = VolumetricRegisters.volumetricAreas[i].NormalizedTexelDensity;
        RenderTextureDescriptor rtdiscrpt = new RenderTextureDescriptor();
        rtdiscrpt.enableRandomWrite = true;
        rtdiscrpt.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        rtdiscrpt.width = Texels.x;
        rtdiscrpt.height = Texels.y;
        rtdiscrpt.volumeDepth = Texels.z;
        rtdiscrpt.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
        rtdiscrpt.msaaSamples = 1;

        //Target buffer
        RenderTexture RT3d = new RenderTexture(rtdiscrpt);
        RT3d.Create();

        return RT3d;
    }
    Texture MakeEnvironmentalCubemap()
    {
        //Black Background
        if (SkyboxContribution != true){

            RenderTexture cubetex = new RenderTexture(32, 32, 1, RenderTextureFormat.DefaultHDR);
            cubetex.enableRandomWrite = true;
            cubetex.dimension = UnityEngine.Rendering.TextureDimension.Cube;
            cubetex.Create();

            Camera renderCam = new GameObject().AddComponent<Camera>();
            renderCam.cullingMask = 0;
            renderCam.backgroundColor = Color.black;
            renderCam.clearFlags = CameraClearFlags.Color;
            renderCam.RenderToCubemap(cubetex);
            DestroyImmediate(renderCam.gameObject);
            return cubetex;
        }

        //Generate Skybox
        if (CustomEnvorment == null)
        {
            RenderTexture cubetex = new RenderTexture(256, 256, 1, RenderTextureFormat.DefaultHDR);
            cubetex.enableRandomWrite = true;
            cubetex.dimension = UnityEngine.Rendering.TextureDimension.Cube;
            cubetex.Create();

            Camera renderCam = new GameObject().AddComponent<Camera>();
            renderCam.cullingMask = 0;
            renderCam.backgroundColor = Color.black;
            renderCam.clearFlags = CameraClearFlags.Skybox;
            renderCam.RenderToCubemap(cubetex);
            DestroyImmediate(renderCam.gameObject);
            return cubetex;
        }
        //Provided skybox
        else
        {
            return CustomEnvorment;
        }
    }

    void RefreshDebuggers()
    {
        for (int i = 0; i < VolumetricRegisters.volumetricAreas.Count; i++)
        {
            if (VolumetricRegisters.volumetricAreas[i].DEBUG) VolumetricRegisters.volumetricAreas[i].RefreshDebugMesh();
        }
    }

    Color ColorExtraction(Light light)
    {
        Color colorModulation = light.color.linear;
        if (light.useColorTemperature) colorModulation *= Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
        colorModulation *= light.intensity;
        colorModulation *= light.gameObject.GetComponent<UniversalAdditionalLightData>().volumetricDimmer;
        return colorModulation;
    }

    void BakeDXR()
    {

        System.DateTime startTime = System.DateTime.Now;
        UpdateStatus("Baking " + SceneManager.GetActiveScene().name);
        //ComputeShader BlitShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/3dBlit.compute");

        RayTracingShader rtshader = AssetDatabase.LoadAssetAtPath<RayTracingShader>("Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/DXR-Volumebaker.raytrace");
        rtshader.SetShaderPass("BakedRaytrace");

        RayTracingAccelerationStructure accelerationStructure = new RayTracingAccelerationStructure(); ;
        Renderer[] renderers = GatherStaticRenderers();
        for (int i = 0; i < renderers.Length; i++) accelerationStructure.AddInstance(renderers[i]);
        accelerationStructure.Build();
        rtshader.SetAccelerationStructure("g_SceneAccelStruct", accelerationStructure);

        Running = true;

        Vector3Int threads = new Vector3Int();

        for (int j = 0; j < VolumetricRegisters.volumetricAreas.Count; j++)
        {
            threads = VolumetricRegisters.volumetricAreas[j].NormalizedTexelDensity;
            RenderTexture RT3d = initializeVolume(j);
            rtshader.SetTexture("g_Output", RT3d);

            ///
            /// 
            /// 

            List<Light> PointLights, ConeLights, DirectionalLights, AreaLights;

            Light[] Lights = GatherBakedLights();

            PointLights = new List<Light>();
            ConeLights = new List<Light>();
            DirectionalLights = new List<Light>();
            AreaLights = new List<Light>();

            for (int i = 0; i < Lights.Length; i++)
            {

                switch (Lights[i].type)
                {
                    case LightType.Point:
                        PointLights.Add(Lights[i]);
                        break;

                    case LightType.Spot:
                        ConeLights.Add(Lights[i]);
                        break;

                    case LightType.Directional:
                        DirectionalLights.Add(Lights[i]);
                        break;

                    case LightType.Area:
                        AreaLights.Add(Lights[i]);
                        break;

                    case LightType.Disc:
                        AreaLights.Add(Lights[i]); //Stacking area and disc
                        break;
                    default:

                        break;
                }
            }

            Debug.Log(PointLights.Count + "Point Lights, " + ConeLights.Count + " Cone Lights, " + DirectionalLights.Count + " Dir Lights, " + AreaLights.Count + " area lights.");

            //Set up buffers with data stride. Keeping a min count of 1 to keep buffer valid. Get's skipped in shader.
            ComputeBuffer pointBuffer = new ComputeBuffer(Mathf.Max(PointLights.Count,1), (3 + 4) * 4);
            ComputeBuffer coneBuffer = new ComputeBuffer(Mathf.Max(ConeLights.Count,1), (3 + 4 + 3 + 2) * 4);
            ComputeBuffer dirBuffer = new ComputeBuffer(Mathf.Max(DirectionalLights.Count,1), (3 + 4) * 4);
            ComputeBuffer areaBuffer = new ComputeBuffer(Mathf.Max(AreaLights.Count,1), (4 * 4 + 4 * 4 + 3 + 4 + 3) * 4);

            PointLightData[] PointLDatas = new PointLightData[PointLights.Count];
            ConeLightData[] ConeLDatas =  new ConeLightData[ConeLights.Count] ;
            DirLightData[] DirLDatas = new DirLightData[DirectionalLights.Count];
            AreaLightData[] AreaLDatas = new AreaLightData[AreaLights.Count];

            for (int i = 0; i < PointLights.Count; i++)
            {
                PointLDatas[i].PointLightsPos = PointLights[i].transform.position;
                PointLDatas[i].PointLightsColors = ColorExtraction(PointLights[i]);
            }

            for (int i = 0; i < ConeLights.Count; i++)
            {
                ConeLDatas[i].ConeLightsWS = ConeLights[i].transform.position;
                ConeLDatas[i].ConeLightsColors = ColorExtraction(ConeLights[i]);
                ConeLDatas[i].ConeLightsDir = ConeLights[i].transform.forward;

                float flPhiDot = Mathf.Clamp01(Mathf.Cos(ConeLights[i].spotAngle * 0.5f * Mathf.Deg2Rad)); // outer cone
                float flThetaDot = Mathf.Clamp01(Mathf.Cos(ConeLights[i].innerSpotAngle * 0.5f * Mathf.Deg2Rad)); // inner cone

                ConeLDatas[i].ConeLightsPram = new Vector4(flPhiDot, 1.0f / Mathf.Max(0.01f, flThetaDot - flPhiDot), 0, 0);
            }

            for (int i = 0; i < DirectionalLights.Count; i++)
            {
                DirLDatas[i].DirLightsDir = DirectionalLights[i].transform.forward;
                DirLDatas[i].DirLightsColors = ColorExtraction(DirectionalLights[i]);
            }

            for (int i = 0; i < AreaLights.Count; i++)
            {
                AreaLDatas[i].AreaLightsPos = AreaLights[i].transform.position;
                AreaLDatas[i].AreaLightsMatrix = Matrix4x4.TRS(AreaLights[i].transform.position, AreaLights[i].transform.rotation, Vector3.one);
                AreaLDatas[i].AreaLightsMatrixInv = AreaLDatas[i].AreaLightsMatrix.inverse;
                AreaLDatas[i].AreaLightsColors = ColorExtraction(AreaLights[i]);
                AreaLDatas[i].AreaLightsSize = new Vector3(AreaLights[i].areaSize.x, AreaLights[i].areaSize.y, AreaLights[i].type == LightType.Disc ? 1 : 0); //Packing for area or disc logic
            }

            pointBuffer.SetData(PointLDatas);
            coneBuffer.SetData(ConeLDatas);
            dirBuffer.SetData(DirLDatas);
            areaBuffer.SetData(AreaLDatas);


            //General
            rtshader.SetVector("Size", VolumetricRegisters.volumetricAreas[j].NormalizedScale);
            rtshader.SetVector("WPosition", VolumetricRegisters.volumetricAreas[j].Corner);
            rtshader.SetFloat("_Seed", (Random.Range(0.0f, 64.0f)));

            //Point
            rtshader.SetInt("PointLightCount", PointLDatas.Length); //Add a stack overflow loop or computebuffer
            rtshader.SetBuffer("PLD", pointBuffer); ;

            //Cone
            rtshader.SetInt("ConeLightCount", ConeLDatas.Length); //Add a stack overflow loop or computebuffer
            rtshader.SetBuffer("CLD", coneBuffer);

            //Directional
            rtshader.SetInt("DirLightCount", DirLDatas.Length); //Add a stack overflow loop or computebuffer
            rtshader.SetBuffer("DLD", dirBuffer);

            //Area
            rtshader.SetInt("AreaLightCount", AreaLDatas.Length); //Add a stack overflow loop or computebuffer
            rtshader.SetInt("AreaLightSamples", System.Convert.ToInt32(AreaLightSamples));
            rtshader.SetBuffer("ALD", areaBuffer);

            //Env
            rtshader.SetTexture("_SkyTexture", MakeEnvironmentalCubemap());
            rtshader.SetInt("EnvLightSamples", System.Convert.ToInt32(EnvLightSamples));

            //Dispatching
            rtshader.Dispatch("MainRayGenShader", threads.x, threads.y, threads.z);

            pointBuffer.Release();
            coneBuffer.Release();
            dirBuffer.Release();
            areaBuffer.Release();
            ///
            /// 
            ///

            string path = CheckDirectoryAndReturnPath() + j;
			Texture3D ReadBackTex = ReadRT2Tex3D(RT3d);
			RT3d.Release();
			Vol3d.WriteTex3DToVol3D(ReadBackTex, path + Vol3d.fileExtension);
			AssetDatabase.ImportAsset(path + Vol3d.fileExtension);
			VolumetricRegisters.volumetricAreas[j].bakedTexture = (Texture3D)AssetDatabase.LoadAssetAtPath(path + Vol3d.fileExtension, typeof(Texture3D));
            //   Debug.Log(VolumetricRegisters.volumetricAreas[j].gameObject.scene.name + VolumetricRegisters.volumetricAreas[j].name);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(VolumetricRegisters.volumetricAreas[j].gameObject.scene);

        }

        Running = false;
        EditorUtility.ClearProgressBar();

        accelerationStructure.Dispose();

        System.DateTime endTime = System.DateTime.Now;

        UpdateStatus(" Volumetric bake took " + (endTime.Minute - startTime.Minute) + " Minutes and " +
            (endTime.Second - startTime.Second) + " Seconds. Baked " + VolumetricRegisters.volumetricAreas.Count + " areas."
            );

        RefreshDebuggers();

    }


    void DispatchLight(Light light, RenderTexture RT3d, Vector3Int Texels, int AreaID, Vector3Int TextileOffset ) //Used to render each light indavidually. TODO Generalize into pure pathtracing
    {

        //Setup light data

        Vector4 lightColor = light.color * light.intensity;
        Vector4 lightPos = light.transform.position;
        int shaderKernel;

        //Setup and dispatch Baking compute shader
        switch (light.type)
        {
            case LightType.Point :
                
                    shaderKernel = BakingShader.FindKernel("PointLight");
                break;
            case LightType.Spot :                
                    shaderKernel = BakingShader.FindKernel("SpotLight");

                float flPhiDot = Mathf.Clamp01(Mathf.Cos(light.spotAngle * 0.5f * Mathf.Deg2Rad)); // outer cone
                float flThetaDot = Mathf.Clamp01(Mathf.Cos(light.innerSpotAngle * 0.5f * Mathf.Deg2Rad)); // inner cone


                BakingShader.SetFloat("SpotPram", flPhiDot);
                BakingShader.SetFloat("InnerSpotPram", 1.0f / Mathf.Max(0.01f, flThetaDot - flPhiDot));

                break;
            case LightType.Directional:
             shaderKernel = BakingShader.FindKernel("DirectionalLight");
                break;
            case LightType.Rectangle:
           //     Debug.Log("baked " + light.name);
                shaderKernel = BakingShader.FindKernel("RectangleLight");
                BakingShader.SetVector("AreaSize", light.areaSize);
                BakingShader.SetFloat("AreaLightSamples", AreaLightSamples);
                BakingShader.SetMatrix("AreaMatrix", Matrix4x4.Rotate(light.transform.rotation));

                BakingShader.SetFloat("_Seed", Random.value);

                break;
            case LightType.Disc:
                shaderKernel = BakingShader.FindKernel("DiscLight");
                BakingShader.SetVector("AreaSize", light.areaSize); //Only the first float is used for radius
                BakingShader.SetFloat("AreaLightSamples", AreaLightSamples);
                BakingShader.SetMatrix("AreaMatrix", Matrix4x4.Rotate(light.transform.rotation));

                BakingShader.SetFloat("_Seed", Random.value);
                break;
            default:
                return;
        }


        BakingShader.SetTexture(shaderKernel, "AccumulatedLights", RT3d);

        BakingShader.SetVector("LightColor", lightColor);
        BakingShader.SetVector("LightPosition", lightPos);

        BakingShader.SetVector("LightDirection", light.transform.rotation * Vector3.forward);
        //  BakingShader.SetVector("Size", VolumetricRegisters.volumetricAreas[AreaID].NormalizedScale);

        Vector3 BucketScaler =  (Vector3)VolumetricRegisters.volumetricAreas[AreaID].NormalizedTexelDensity / (float)BucketSize ;
      //  BucketScaler = Vector3.one * 1.5f;

        //Debug.Log("Bucket Scaler" + BucketScaler+
        //    "NormalizedTexelDensity" + (VolumetricRegisters.volumetricAreas[AreaID].NormalizedTexelDensity)+
        //    "BucketSize" + BucketSize       );

        //Offset each cell in world space based on the textiles 
        Vector3 D = VolumetricRegisters.volumetricAreas[AreaID].NormalizedScale;
        Vector3 T = (Vector3)VolumetricRegisters.volumetricAreas[AreaID].NormalizedTexelDensity;
        Vector3 PositionOffset = new Vector3( (D.x / T.x) * TextileOffset.x, (D.y / T.y) * TextileOffset.y, (D.z / T.z) * TextileOffset.z);

        BakingShader.SetVector("Size", new Vector3(VolumetricRegisters.volumetricAreas[AreaID].NormalizedScale.x / BucketScaler.x,
            VolumetricRegisters.volumetricAreas[AreaID].NormalizedScale.y / BucketScaler.y,
            VolumetricRegisters.volumetricAreas[AreaID].NormalizedScale.z / BucketScaler.z) );
        //  BakingShader.SetVector("Size", (Vector3)(Vector3Int.one * BucketSize) );
        BakingShader.SetVector("Position", VolumetricRegisters.volumetricAreas[AreaID].Corner + PositionOffset);


        ///Temp Shadow spheres
        Vector4[] SpherePos = new Vector4[0];
        SetComputeBuffer("_MeshObjects", BakingShader, shaderKernel, _meshObjectBuffer);
        SetComputeBuffer("_Vertices", BakingShader, shaderKernel, _vertexBuffer);
        SetComputeBuffer("_Indices", BakingShader, shaderKernel, _indexBuffer);

        BakingShader.SetVectorArray("OpaqueSphere", SpherePos);
        BakingShader.SetInt("SphereCount", SpherePos.Length);
        ///


        Vector3 ThreadsToDispatch = new Vector3(
        Mathf.CeilToInt(Texels.x / 4.0f),
        Mathf.CeilToInt(Texels.y / 4.0f),
        Mathf.CeilToInt(Texels.z / 4.0f)
        );

        int DebugCountStride = sizeof(int); //Size of debug strut
        int DebugID = Shader.PropertyToID("DebugBuffer");

        Debuger[] debuger = new Debuger[1];
        debuger[0].DebugCcounter = 0;

        ComputeBuffer BakeBuffer = new ComputeBuffer(1, DebugCountStride);
        BakingShader.SetBuffer(shaderKernel, DebugID, BakeBuffer);

        BakingShader.Dispatch(shaderKernel, (int)ThreadsToDispatch.x, (int)ThreadsToDispatch.y, (int)ThreadsToDispatch.z);

        while (debuger[0].DebugCcounter == 0)
        {
            BakeBuffer.GetData(debuger);
         //   Debug.Log("Counter " + debuger[0].DebugCcounter);
        }

        BakeBuffer.Release(); //Avoiding memory leak
      //  return RT3d;
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


    Light[] GatherBakedLights(LightType lightType)
    {
        Light[] lights = FindObjectsOfType<Light>(); //TODO: Make it smarter to find only baked lights affecting zone.
        List<Light> FilteredLights = new List<Light>();

        for (int i = 0; i < lights.Length; i++)
        {
            //TODO: Handle mixed lights differently in the future
            if (lights[i].lightmapBakeType == LightmapBakeType.Baked || lights[i].lightmapBakeType == LightmapBakeType.Mixed)
            {
                if (lights[i].enabled) FilteredLights.Add(lights[i]); 
            }
        }

        return FilteredLights.ToArray();
    }


    Light[] GatherBakedLights()
    {
        Light[] lights = FindObjectsOfType<Light>(); //TODO: Make it smarter to find only baked lights affecting zone.
        List<Light> FilteredLights = new List<Light>();

        for (int i = 0; i < lights.Length; i++)
        {
            //TODO: Handle mixed lights differently in the future
            if (lights[i].lightmapBakeType == LightmapBakeType.Baked || lights[i].lightmapBakeType == LightmapBakeType.Mixed)
            {
                if (lights[i].enabled) FilteredLights.Add(lights[i]);
            }
        }

        return FilteredLights.ToArray();
    }

    GameObject[] GatherStaticObjects()    {

        List<GameObject> StatcGameobject = new List<GameObject>();
        Renderer[] AllRenderers = FindObjectsOfType<Renderer>();

        StaticEditorFlags staticFlag = StaticEditorFlags.ContributeGI;

        //Loop through GO's and see if the correct static flag is enabled. If it is, then add it to the list;
        for (int i = 0; i < AllRenderers.Length; i++){
            if (GameObjectUtility.AreStaticEditorFlagsSet(AllRenderers[i].gameObject, staticFlag) && AllRenderers[i].shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) {
                StatcGameobject.Add(AllRenderers[i].gameObject);
              }
        }

        return StatcGameobject.ToArray();
    }
    Renderer[] GatherStaticRenderers()
    {
        List<Renderer> StatcRenderer = new List<Renderer>();
        Renderer[] AllRenderers = FindObjectsOfType<Renderer>();
        StaticEditorFlags staticFlag = StaticEditorFlags.ContributeGI;

        //Loop through GO's and see if the correct static flag is enabled. If it is, then add it to the list;
        for (int i = 0; i < AllRenderers.Length; i++)
        {
            if (GameObjectUtility.AreStaticEditorFlagsSet(AllRenderers[i].gameObject, staticFlag) && AllRenderers[i].shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
            {
                StatcRenderer.Add(AllRenderers[i]);
            }
        }
        return StatcRenderer.ToArray();
    }



    public void RebuildMeshObjectBuffers()
    {
        //if (!VolumetricRegisters._meshObjectsNeedRebuilding)
        //{
        //    return;
        //}

    //    VolumetricBakingRegisters._meshObjectsNeedRebuilding = false;
        int _currentSample = 0;

        // Clear all lists
        _meshObjects.Clear();
        _vertices.Clear();
        _indices.Clear();

        GameObject[] staticGOs = GatherStaticObjects();        

        // Loop over all objects and gather their data
        for (int i=0; i< staticGOs.Length; i++)
        {
            Mesh mesh = staticGOs[i].GetComponent<MeshFilter>().sharedMesh;

            if (mesh == null) return;
            // Add vertex data
            int firstVertex = _vertices.Count;

         //   if (_vertices.Count == 0) return;

            _vertices.AddRange(mesh.vertices);
            // Add index data - if the vertex buffer wasn't empty before, the
            // indices need to be offset
//
          //  Debug.Log(_indices.Count + " index");
            int firstIndex = _indices.Count;
            var indices = mesh.GetIndices(0); //Extend to support submeshes
            //    _indices.AddRange(indices.Select(index => index + firstVertex))
            _indices.AddRange( indices.Select(index => index + firstVertex) );

            // Add the object itself
            _meshObjects.Add(new MeshObject()
            {
                localToWorldMatrix = staticGOs[i].transform.localToWorldMatrix,
                indices_offset = firstIndex,
                indices_count = indices.Length
            });
        }


        //TODO: Covert terrain data into an ingestable format!!
        //for (int i = 0; i < staticGOs.Length; i++)
        //{
        //    Mesh mesh = staticGOs[i].GetComponent<MeshFilter>().sharedMesh;

        //    if (mesh == null) return;
        //    // Add vertex data
        //    int firstVertex = _vertices.Count;

        //    //   if (_vertices.Count == 0) return;

        //    _vertices.AddRange(mesh.vertices);
        //    // Add index data - if the vertex buffer wasn't empty before, the
        //    // indices need to be offset
        //    //
        //    //  Debug.Log(_indices.Count + " index");
        //    int firstIndex = _indices.Count;
        //    var indices = mesh.GetIndices(0); //Extend to support submeshes
        //    //    _indices.AddRange(indices.Select(index => index + firstVertex))
        //    _indices.AddRange(indices.Select(index => index + firstVertex));

        //    // Add the object itself
        //    _meshObjects.Add(new MeshObject()
        //    {
        //        localToWorldMatrix = staticGOs[i].transform.localToWorldMatrix,
        //        indices_offset = firstIndex,
        //        indices_count = indices.Length
        //    });
        //}

        //  Debug.Log(_meshObjects.Count);


        CreateComputeBuffer(ref _meshObjectBuffer, _meshObjects, 72);
        CreateComputeBuffer(ref _vertexBuffer, _vertices, 12);
        CreateComputeBuffer(ref _indexBuffer, _indices, 4);
    }

    void ReleaseBuffers()
    {
        _meshObjectBuffer.Release();
        _meshObjectBuffer = null;
        _vertexBuffer.Release();
        _vertexBuffer = null;
        _indexBuffer.Release();
        _indexBuffer = null;
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

    struct PointLightData
    {
        //Point
        public Vector3 PointLightsPos;
        public Vector4 PointLightsColors;
    }

    struct ConeLightData
    {
        public Vector3 ConeLightsWS;
        public Vector4 ConeLightsColors;
        public Vector3 ConeLightsDir;
        public Vector2 ConeLightsPram;
    }

    struct DirLightData
    {
        public Vector3 DirLightsDir;
        public Vector4 DirLightsColors;
    }
    struct AreaLightData
    {
        public Matrix4x4 AreaLightsMatrix;
        public Matrix4x4 AreaLightsMatrixInv;
        public Vector3 AreaLightsPos;
        public Vector4 AreaLightsColors;
        public Vector3 AreaLightsSize;
    }


    //public static void RepaintInspector(System.Type t)
    //{
    //    Editor[] ed = (Editor[])Resources.FindObjectsOfTypeAll<Editor>();
    //    for (int i = 0; i < ed.Length; i++)
    //    {
    //        if (ed[i].GetType() == t)
    //        {
    //            ed[i].Repaint();
    //            return;
    //        }
    //    }
    //}

}
