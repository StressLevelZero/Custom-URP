using System;
using System.Reflection;
using UnityEditor.Overlays;
using System.Collections.Generic;
using UnityEditorInternal;
using UnityEditor.Snap;
using UnityEngine;
using UnityEditor.Toolbars;
using UnityEditor;
using UnityEngine.UIElements;
using FrameCapture = UnityEngine.Apple.FrameCapture;
using FrameCaptureDestination = UnityEngine.Apple.FrameCaptureDestination;
using UnityEngine.Rendering.Universal;

namespace SLZ.Editor
{
    
    [EditorToolbarElement(elementID, typeof(SceneView))]
    public class SceneViewVolumetricToggle : EditorToolbarToggle, IAccessContainerWindow
    {
        public const string elementID = VolumetricEditorToolbar.overlayID + "/Volumetrics";
        public EditorWindow containerWindow { get; set; }
        SceneView sceneView => containerWindow as SceneView;

        public SceneViewVolumetricToggle()
        {
            var content = EditorGUIUtility.TrTextContentWithIcon("", "Unknown", "d_preAudioAutoPlayOff");
            name = elementID;
            text = content.text;
            icon = content.image as Texture2D;
            tooltip = L10n.Tr("When toggled on, the Scene is in 2D view. When toggled off, the Scene is in 3D view.");
            this.RegisterValueChangedCallback(OnValueChanged);
            RegisterCallback<AttachToPanelEvent>(OnAttachedToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
           
            //Type reflType = Type.GetType("UnityEditor.Toolbars.SceneViewToolbarElements, UnityEditor.UIServiceModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
            //if (reflType != null)
            //{
            //    MethodInfo reflMethod = reflType.GetMethod("AddStyleSheets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            //    if (reflMethod != null)
            //    {
            //        reflMethod.Invoke(null, new object[1] { this });
            //    }
            //    else
            //    {
            //        Debug.Log("Could not find method");
            //    }
            //}
            //else
            //{
            //    Debug.Log("Could not find class");
            //}
            
        }

        void OnValueChanged(ChangeEvent<bool> evt)
        {
            Camera sceneCam = sceneView.camera;
            VolumetricRendering vR = VolumetricPool.Instance.GetSceneVol(sceneCam);
            vR.enableEditorPreview = evt.newValue;
            if (evt.newValue)
            {
                vR.enabled = true;
                vR.enable();
                vR.StartSceneViewRendering();
            }
            else
            {
                vR.disable();
                vR.enabled = false;
            }
        }
        void OnAttachedToPanel(AttachToPanelEvent evt)
        {
            value = sceneView.in2DMode;
            //sceneView.modeChanged2D += OnModeChanged;
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            //sceneView.modeChanged2D -= OnModeChanged;
        }

        void OnModeChanged(bool enabled)
        {
            value = enabled;
        }
    }



    // IconAttribute provides a way to define an icon for when an Overlay is in collapsed form. If not provided, the name initials are used.

    //[Icon("Assets/unity.png")]

    // Toolbar Overlays must inherit `ToolbarOverlay` and implement a parameter-less constructor. The contents of a toolbar are populated with string IDs, which are passed to the base constructor. IDs are defined by EditorToolbarElementAttribute.
    [Overlay(typeof(SceneView), overlayID, "Volumetric Toggle", true)]
    public class VolumetricEditorToolbar : ToolbarOverlay
    {
        public const string overlayID = "VolumetricsToolbar";
        // ToolbarOverlay implements a parameterless constructor, passing the EditorToolbarElementAttribute ID.
        // This is the only code required to implement a toolbar Overlay. Unlike panel Overlays, the contents are defined
        // as standalone pieces that will be collected to form a strip of elements.

        VolumetricEditorToolbar() : base(
            SceneViewVolumetricToggle.elementID
            )
        { }
    }

    public class VolumetricPool : MonoBehaviour
    {
        static VolumetricPool s_Instance;
        public static VolumetricPool Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    GameObject volPool = new GameObject();
                    volPool.hideFlags = HideFlags.DontSave;
                    volPool.name = "SceneViewVolumetricRendererPool";
                    s_Instance = volPool.AddComponent<VolumetricPool>();
                }
                return s_Instance;
            }
        }

        public Dictionary<Camera, VolumetricRendering> SceneToVol;

        [ExecuteInEditMode]
        private void Awake()
        {
            SceneToVol = new Dictionary<Camera, VolumetricRendering>();
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<Camera, VolumetricRendering> kvp in SceneToVol)
            {
                DestroyImmediate(kvp.Value.gameObject);
            }
        }

        public VolumetricRendering GetSceneVol(Camera sceneCam)
        {
            if (sceneCam != null)
            {
                VolumetricRendering vol;
                if (SceneToVol == null)
                {
                    SceneToVol = new Dictionary<Camera, VolumetricRendering>();
                }
                bool hasCam = SceneToVol.TryGetValue(sceneCam, out vol);
                if (!hasCam)
                {
                    GameObject sceneVol = new GameObject();
                    sceneVol.hideFlags = HideFlags.DontSave;
                    sceneVol.transform.parent = this.transform;
                    sceneVol.name = "Scene Camera Volume " + SceneToVol.Count;
                    vol = sceneVol.AddComponent<VolumetricRendering>();
                    VolumetricRendering mainVol = Camera.main?.GetComponent<VolumetricRendering>();
                    VolumetricData volData = mainVol?.volumetricData;
                    if (mainVol == null || volData == null)
                    {
                        volData = AssetDatabase.LoadAssetAtPath<VolumetricData>("Packages/com.unity.render-pipelines.universal/Runtime/Volumetrics/PlaceholderVolumetricSettings.asset");
                    }
                    if (mainVol != null)
                    {
                        vol.tempOffset = mainVol.tempOffset;
                        vol.volumetricData = mainVol.volumetricData;
                        vol.reprojectionAmount = mainVol.reprojectionAmount;
                        vol.FroxelBlur = mainVol.FroxelBlur;
                        vol.SliceDistributionUniformity = mainVol.SliceDistributionUniformity;
                        vol.albedo = mainVol.albedo;
                        vol.meanFreePath = mainVol.meanFreePath;
                        vol.StaticLightMultiplier = mainVol.StaticLightMultiplier;
                    }

                    vol.volumetricData = volData;
                    vol.cam = sceneCam;
                    SceneToVol.Add(sceneCam, vol);
                }
                return vol;
            }
            else
            {
                Debug.Log("NULL CAMERA");
                return null;
            }
        }
    }
}