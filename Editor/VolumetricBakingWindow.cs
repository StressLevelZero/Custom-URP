using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;


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
    public float AreaLightSamples = 8;
    bool saveWarning = false;
    bool Running = false;
    private void OnGUI()
    {
        DisplayProgress();

        if (GUILayout.Button("Bake Volumetrics"))
        {
            ClearWarning();
            if (VerifySettings() == false) return; //Check settings and return if something is wrong
            RebuildMeshObjectBuffers();
            BakeLights();
            ReleaseBuffers();
        };
        //     EditorGUILayout.FloatField(AreaLightSamples);

        EditorGUILayout.LabelField(BakingStatus);
        WarningGUI();
        if (saveWarning) if (GUILayout.Button("Save scene")) SaveScene();

        //      EditorGUILayout.HelpBox("Some warning text", MessageType.Warning); //Todo: add warning box to window
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
        if (SceneManager.sceneCount > 1)
        {
            UpdateWarning( "More than one scene open. Did not bake volumetrics.");
        }

        if (VolumetricRegisters.volumetricAreas.Count < 1)
        {
            UpdateWarning("No volumetric areas in the scene. Nothing to bake.");
            return false;
        }

        if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty)
        {
            UpdateWarning( "Save your scene before baking.");
            saveWarning = true;
            return false;
        }

        else return true;
    }

    void SaveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

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
    ///////////////
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

            int3 Texels = VolumetricRegisters.volumetricAreas[j].NormalizedTexelDensity;

            RenderTextureDescriptor rtdiscrpt = new RenderTextureDescriptor();
            rtdiscrpt.enableRandomWrite = true;
            rtdiscrpt.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
            rtdiscrpt.width = Texels.x;
            rtdiscrpt.height = Texels.y;
            rtdiscrpt.volumeDepth = Texels.z;
            rtdiscrpt.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat;
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

            Vector4[] mediaPos = new Vector4[VolumetricRegisters.participatingMediaEntities.Count];
            Vector4[] mediaAbs = new Vector4[VolumetricRegisters.participatingMediaEntities.Count];

            for (int i = 0; i < mediaPos.Length; i++)
            {
                mediaPos[i] = VolumetricRegisters.participatingMediaEntities[i].transform.position;
                mediaAbs[i] = new Vector4(VolumetricRegisters.participatingMediaEntities[i].Absorption, 0, 0, 0); //Only Absorption for now
            }


            //Setup and dispatch Baking compute shader

        //    Debug.Log("Baking " + PointLights.Length + " lights");
       //     Debug.Log("Baking " + mediaPos.Length + " Media");

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
         

            BakingShader.Dispatch(shaderKernel, (int)ThreadsToDispatch.x, (int)ThreadsToDispatch.y, (int)ThreadsToDispatch.z);


          //  while 
            
            //Sending data back to the CPU to check if work is done before writing to disk
         //   BakeBuffer.GetData(debuger);
     //       Debug.Log("Shot " + debuger[0].DebugCcounter + " rays");
            BakeBuffer.Release(); //Avoiding memory leak
            ///

            //Define path and save 3d texture
            string path = SceneManager.GetActiveScene().path;
            path = path.Replace(".unity", "") + "/" + "Volumemap-" + j; //TODO Check to make sure path exsits
            RT3d.SaveToTexture3D(path);

            RT3d.Release();

            VolumetricRegisters.volumetricAreas[j].bakedTexture = (Texture3D) AssetDatabase.LoadAssetAtPath(path + ".asset", typeof(Texture3D) );

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

    ComputeShader BakingShader;

    void BakeLights()
    {
        System.DateTime startTime = System.DateTime.Now;
         BakingShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/VolumetricBaking.compute");
        UpdateStatus("Baking " + SceneManager.GetActiveScene().name);

        Running = true;

        for (int j = 0; j < VolumetricRegisters.volumetricAreas.Count; j++)
        {
        //    EditorUtility.DisplayProgressBar("Baking Volumetrics... ", VolumetricRegisters.volumetricAreas[j].name, (float)j / (float)VolumetricRegisters.volumetricAreas.Count);
            
            int3 Texels = VolumetricRegisters.volumetricAreas[j].NormalizedTexelDensity;
            RenderTextureDescriptor rtdiscrpt = new RenderTextureDescriptor();
            rtdiscrpt.enableRandomWrite = true;
            rtdiscrpt.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
            rtdiscrpt.width = Texels.x;
            rtdiscrpt.height = Texels.y;
            rtdiscrpt.volumeDepth = Texels.z;
            rtdiscrpt.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat;
            rtdiscrpt.msaaSamples = 1;

            RenderTexture RT3d = new RenderTexture(rtdiscrpt);
            RT3d.Create();
  
            Light[] PointLights = GatherBakedLights(LightType.Point);
            for (int i = 0; i < PointLights.Length; i++)
            {
                UpdateProgress("Baking " + (j + 1) + "/" + VolumetricRegisters.volumetricAreas.Count + " "
                + VolumetricRegisters.volumetricAreas[j].name + " "
                + (System.DateTime.Now.Minute - startTime.Minute) + ":" + (System.DateTime.Now.Second - startTime.Second) //TODO: Format correctly
                , PointLights[i].name, (float)i / PointLights.Length);

                //TODO: Do some checking to only render lights affecting the area. AABB?                
                DispatchLight(PointLights[i], RT3d, Texels, j);
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
            string path = SceneManager.GetActiveScene().path;
            path = path.Replace(".unity", "") + "/" + "Volumemap-" + j; //TODO Check to make sure path exsits
            RT3d.SaveToTexture3D(path);

            RT3d.Release();

            VolumetricRegisters.volumetricAreas[j].bakedTexture = (Texture3D)AssetDatabase.LoadAssetAtPath(path + ".asset", typeof(Texture3D));
        }

        Running = false;
        EditorUtility.ClearProgressBar();

        System.DateTime endTime = System.DateTime.Now;

        UpdateStatus(" Volumetric bake took " + (endTime.Minute - startTime.Minute) + " Minutes and " +
            (endTime.Second - startTime.Second) + " Seconds. Baked " + VolumetricRegisters.volumetricAreas.Count + " areas."
            );

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

    }

    void DispatchLight(Light light, RenderTexture RT3d, int3 Texels, int AreaID) //Used to render each light indavidually. TODO Generalize into pure pathtracing
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
            //case LightType.Disc:
            //    shaderKernel = BakingShader.FindKernel("DiskLight");
            //    BakingShader.SetVector("AreaSize", light.areaSize);
            //    break;
            default:
                return;
        }


        BakingShader.SetTexture(shaderKernel, "AccumulatedLights", RT3d);

        BakingShader.SetVector("LightColor", lightColor);
        BakingShader.SetVector("LightPosition", lightPos);

        BakingShader.SetVector("LightDirection", light.transform.rotation * Vector3.forward);
        BakingShader.SetVector("Size", VolumetricRegisters.volumetricAreas[AreaID].NormalizedScale);
        BakingShader.SetVector("Position", VolumetricRegisters.volumetricAreas[AreaID].Corner);


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
            Debug.Log("Counter " + debuger[0].DebugCcounter);
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


    public void RebuildMeshObjectBuffers()
    {
        if (!VolumetricRegisters._meshObjectsNeedRebuilding)
        {
            return;
        }

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

        for (int i = 0; i < staticGOs.Length; i++)
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
            _indices.AddRange(indices.Select(index => index + firstVertex));

            // Add the object itself
            _meshObjects.Add(new MeshObject()
            {
                localToWorldMatrix = staticGOs[i].transform.localToWorldMatrix,
                indices_offset = firstIndex,
                indices_count = indices.Length
            });
        }

        //  Debug.Log(_meshObjects.Count);

        CreateComputeBuffer(ref _meshObjectBuffer, _meshObjects, 72);
        CreateComputeBuffer(ref _vertexBuffer, _vertices, 12);
        CreateComputeBuffer(ref _indexBuffer, _indices, 4);
    }

    void ReleaseBuffers()
    {
        _meshObjectBuffer.Release();
        _vertexBuffer.Release();
        _indexBuffer.Release();
    }

    private static void CreateComputeBuffer<T>(ref ComputeBuffer buffer, List<T> data, int stride)
    where T : struct
    {
  //      Debug.Log("Making computebuffer ");

        // Do we already have a compute buffer?
        if (buffer != null && data != null && stride != null)
        {
            // If no data or buffer doesn't match the given criteria, release it
            if (data.Count == 0 || buffer.count != data.Count || buffer.stride != stride)
            {
   //             Debug.Log("Buffer count = " + buffer.count);

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
