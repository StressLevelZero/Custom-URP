using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.EditorTools;

[EditorTool("Show Volumetrics")]
public class VolumetricDebugTool : EditorTool
{
    [SerializeField]
    Texture2D m_ToolIcon;
    GUIContent m_IconContent;
    VisualElement toolWindow;
    VolumetricRendering VolumetricScript;
    static bool isActive = false;
    static bool isActive2 = false;
    float exposure = 1f;
    float extinction;
    Color scattering;
    static List<Camera> SceneCameras;

    SceneView ActiveView;

    GameObject placeholderGO;
    Camera placeholderCam;
    Camera selectedCamera;


    void OnEnable()
    {
        m_IconContent = new GUIContent()
        {
            image = m_ToolIcon,
            text = "Preview Volumetrics",
            tooltip = "Preview Volumetrics"
        };
    
    }

    public override GUIContent toolbarIcon
    {
        get { return m_IconContent; }
    }

    public override void OnActivated()
    {
        //EditorWindow view = EditorWindow.GetWindow<SceneView>();
        //view.Repaint();
        toolWindow = new VisualElement();
        toolWindow.style.width = 256;
        toolWindow.style.height = 120;
        Color backgroundColor = new Color(0.11f, 0.11f, 0.11f, 0.8f);
        toolWindow.style.backgroundColor = backgroundColor;
        toolWindow.style.marginLeft = 10f;
        toolWindow.style.marginBottom = 10f;
        toolWindow.style.paddingTop = 8f;
        toolWindow.style.paddingRight = 8f;
        toolWindow.style.paddingLeft = 8f;
        toolWindow.style.paddingBottom = 8f;
        
        if (placeholderGO == null)
        {
            placeholderGO = new GameObject();
            placeholderGO.hideFlags = HideFlags.HideAndDontSave;
            placeholderCam = placeholderGO.AddComponent<Camera>();
            placeholderCam.enabled = false;
            placeholderCam.name = "Active Scene View";
        } 

        ActiveView = SceneView.lastActiveSceneView;

        Label titleLabel = new Label("Preview Volumetrics");
        titleLabel.style.fontSize = 14;
        titleLabel.style.paddingBottom = 8f;
        titleLabel.style.unityTextAlign = TextAnchor.UpperCenter;
        VolumetricScript = Camera.main.GetComponent<VolumetricRendering>();

        Toggle activeToggle = new Toggle("Multi-View");
        activeToggle.value = isActive;
        activeToggle.tooltip = "Simple raymarched volumetrics that works in all views simultaneously, but is not perfectly accurate to how the volumetrics will look in game and will double-add overlapping volumes";
        
        Toggle activeToggle2 = new Toggle("Game-Accurate");
        activeToggle2.value = isActive2;
        activeToggle2.tooltip = "Volumetrics rendered exactly how the game will render them. Rendered from the direction of one camera, all other views will see the volumetrics projected flat on to the world";
        
        updateCameraList();
        InitializeFogParams();

        UnityEditor.UIElements.PopupField<Camera> CameraPopup = new UnityEditor.UIElements.PopupField<Camera>("Active Camera");
        CameraPopup.choices = SceneCameras;
        CameraPopup.value = placeholderCam;
        selectedCamera = placeholderCam;

        UnityEditor.UIElements.FloatField exposureField = new UnityEditor.UIElements.FloatField("Exposure");
        exposureField.value = exposure;

        Slider exposureSlider = new Slider("");
        exposureSlider.value = exposure;
        exposureSlider.showInputField = false;
        exposureSlider.highValue = 2;

        Label BlankLabel = new Label(" ");


        toolWindow.Add(titleLabel);
        if (VolumetricScript == null || Application.isPlaying)
        {
            Label NoCameraWarning = Application.isPlaying ? new Label("Preview not available in play mode") : new Label("No volumetric script on main camera");
            NoCameraWarning.style.fontSize = 14;
            NoCameraWarning.style.color = Color.red;
            NoCameraWarning.style.paddingBottom = 8f;
            //titleLabel.style.unityTextAlign = TextAnchor.UpperCenter;
            toolWindow.Add(NoCameraWarning);
            ActiveView.rootVisualElement.Add(toolWindow);
            ActiveView.rootVisualElement.style.flexDirection = FlexDirection.ColumnReverse;
            return;
        }

        

        toolWindow.Add(activeToggle);
        toolWindow.Add(activeToggle2);
        
        if (isActive)
        {
            toolWindow.Add(exposureField);
            toolWindow.Add(exposureSlider);
        }
        else if (isActive2)
        {
            toolWindow.Add(BlankLabel);
            toolWindow.Add(CameraPopup);
        }

        //toolWindow.Add(exposureField);
        //toolWindow.Add(exposureSlider);

        activeToggle.RegisterCallback<ChangeEvent<bool>>(e=>
        {
            isActive = activeToggle.value;
            BakedVolumetricArea.VisStateGlobal = isActive;
            if (isActive)
            {
                isActive2 = false;
                activeToggle2.value = false;
                VolumetricScript.disable();
                toolWindow.Add(exposureField);
                toolWindow.Add(exposureSlider);
                Shader.SetGlobalFloat("_VolExposure2", exposure);
                updateCameraList();
            }
            else
            {
                toolWindow.Remove(exposureField);
                toolWindow.Remove(exposureSlider);
            }

            ActiveView.Repaint();
        }
        );

        activeToggle2.RegisterCallback<ChangeEvent<bool>>(e=>
        {
            isActive2 = activeToggle2.value;
           
            if (isActive2)
            {
                isActive = false;
                activeToggle.value = false;
                BakedVolumetricArea.VisStateGlobal = false;
                toolWindow.Add(BlankLabel);
                toolWindow.Add(CameraPopup);
                VolumetricScript.activeCam = CameraPopup.value == placeholderCam ? SceneView.lastActiveSceneView.camera : CameraPopup.value;
                VolumetricScript.enableEditorPreview = true;
                VolumetricScript.enable();
            }
            else
            {
                VolumetricScript.enableEditorPreview = false;
                toolWindow.Remove(BlankLabel);
                toolWindow.Remove(CameraPopup);
                VolumetricScript.disable();
            }
           
            ActiveView.Repaint();
        }
        );

        CameraPopup.RegisterCallback<ChangeEvent<Camera>>(e=>
        {
            VolumetricScript.activeCam = CameraPopup.value == placeholderCam ? SceneView.lastActiveSceneView.camera : CameraPopup.value;
            selectedCamera = CameraPopup.value;
            if (isActive2)
            {
                VolumetricScript.disable();
                VolumetricScript.enable();
            }
        }
        );

         exposureField.RegisterCallback<ChangeEvent<float>>(e=>
        {
            exposureField.value = Mathf.Clamp(exposureField.value, 0, 2);
            exposure = exposureField.value;
            exposureSlider.value = exposure;
            Shader.SetGlobalFloat("_VolExposure2", exposure);
            ActiveView.Repaint();
        }
        );

        exposureSlider.RegisterCallback<ChangeEvent<float>>(e=>
        {
            exposure = exposureSlider.value;
            exposureField.value = exposure;
            Shader.SetGlobalFloat("_VolExposure2", exposure);
            ActiveView.Repaint();
        }
        );
        
        ActiveView.rootVisualElement.Add(toolWindow);
        ActiveView.rootVisualElement.style.flexDirection = FlexDirection.ColumnReverse;
        SceneView.lastActiveSceneViewChanged += UpdateWindow;
    }

    public void UpdateWindow(SceneView Old, SceneView New)
    {
        if (New.hasFocus)
        {
           
            ActiveView.rootVisualElement.Remove(toolWindow);
            ActiveView = New;
            ActiveView.rootVisualElement.Add(toolWindow);
            ActiveView.rootVisualElement.style.flexDirection = FlexDirection.ColumnReverse;
            if (selectedCamera == placeholderCam)
            {
                VolumetricScript.activeCam = ActiveView.camera;
                ActiveView.Repaint();
            }
        }

    }

    public override void OnWillBeDeactivated()
    {
        toolWindow?.RemoveFromHierarchy();
        SceneView.lastActiveSceneViewChanged -= UpdateWindow;
        DestroyImmediate(placeholderGO);
    }

    public async void updateCameraList()
    {
        SceneCameras = new List<Camera>();
        SceneCameras.Add(placeholderCam);
        for (int i = 0; i < SceneView.sceneViews.Count; i++)
        {
            Camera cam1 = (SceneView.sceneViews[i] as SceneView).camera;
            cam1.name = "SceneCamera " + i.ToString();
            SceneCameras.Add(cam1);
        }
        if (Camera.main != null)
        {
            SceneCameras.Add(Camera.main);
        }
    }

    private void InitializeFogParams()
    {
        var stack = UnityEngine.Rendering.VolumeManager.instance.stack;
        var Volumetrics = stack.GetComponent<UnityEngine.Rendering.Universal.Volumetrics>();
        if (Volumetrics != null)
            Volumetrics.PushFogShaderParameters();
    }

}
