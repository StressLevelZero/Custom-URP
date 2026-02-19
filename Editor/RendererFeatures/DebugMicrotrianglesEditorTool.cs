using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;
using UnityEditor.EditorTools;

using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor.Overlays;
using static SLZ.SLZEditorTools.DebugMircotriangles;
using UnityEngine.Rendering.Universal;
using UnityEditor.Rendering.Universal;
using System.Reflection;
using UnityEditor.Experimental;

namespace SLZ.SLZEditorTools
{
    [Overlay(typeof(SceneView), "DebugMicrotrianglesOverlay", "Microtriangle Visualization", defaultDisplay = true)]
    [Icon("d_SpriteShapeRenderer Icon")]
    public class DebugMicrotrianglesOverlay : Overlay
    {
        const string sessionstate_active  = "SLZMicroT.Active";
        const string sessionstate_wire    = "SLZMicroT.Wire";
        const string sessionstate_useDist = "SLZMicroT.UseDist";
        const string sessionstate_distVal = "SLZMicroT.DistVal";
        const string sessionstate_conRast = "SLZMicroT.conRast";


        [SerializeField]
        Texture2D m_ToolIcon;
        VisualElement m_RootPanel;


        public override void OnCreated()
        {
            base.OnCreated();
        }

        VisualElement m_ActiveBox;
        VisualElement m_DistanceHorizBox;
        FloatField m_DistanceValueField;
        Button m_GetCamDistButton;

        Toggle m_WireframeToggle;


        public override VisualElement CreatePanelContent()
        {
            DebugMircotriangles.active = true;

            VisualElement m_RootPanel = new VisualElement();
            m_RootPanel.style.alignContent = Align.Stretch;
            m_RootPanel.style.width = 200;
            /*
            toolWindow.style.width = 200;
            toolWindow.style.height = 64;
            Color backgroundColor = new Color(0.11f, 0.11f, 0.11f, 0.8f);
            toolWindow.style.backgroundColor = backgroundColor;
            toolWindow.style.marginLeft = 10f;
            toolWindow.style.marginBottom = 10f;
            toolWindow.style.paddingTop = 8f;
            toolWindow.style.paddingRight = 8f;
            toolWindow.style.paddingLeft = 8f;
            toolWindow.style.paddingBottom = 8f;
            */

            Toggle activeToggle = new Toggle("Enable");
            SetFieldStyle(activeToggle);
            bool active = SessionState.GetBool(sessionstate_active, false);
            activeToggle.SetValueWithoutNotify(active);
            activeToggle.RegisterValueChangedCallback(SetActiveEvent);

            DebugMircotriangles.active = active;

            m_ActiveBox = new VisualElement();
            m_ActiveBox.SetEnabled(active);

            MicroTriVisParams globals = DebugMircotriangles.GetDefaultParameters();

            m_WireframeToggle = new Toggle("  Wireframe");
            SetFieldStyle(m_WireframeToggle);
            bool wireframe = SessionState.GetBool(sessionstate_wire, false);
            m_WireframeToggle.SetValueWithoutNotify(wireframe);
            SetWireframe(wireframe);
            m_WireframeToggle.RegisterValueChangedCallback(SetWireframeEvent);
            m_ActiveBox.Add(m_WireframeToggle);

            Toggle conRastToggle = new Toggle("  Conservative Raster");
            conRastToggle.tooltip = "Adds a 1px border to triangles. Prevents triangles smaller than 1px from disappearing.";
            SetFieldStyle(conRastToggle);
            bool conRast = SessionState.GetBool(sessionstate_conRast, true);
            conRastToggle.SetValueWithoutNotify(conRast);
            SetConservative(conRast);
            conRastToggle.RegisterValueChangedCallback(SetConservativeEvent);
            m_ActiveBox.Add(conRastToggle);

            Toggle useDistanceToggle = new Toggle("  Fixed Distance");
            useDistanceToggle.tooltip = "Calculate triangle screen size based on a fixed distance rather than the current distance to the camera";
            SetFieldStyle(useDistanceToggle);
            bool useDistance = SessionState.GetBool(sessionstate_useDist, false);
            useDistanceToggle.SetValueWithoutNotify(useDistance);
            SetUseDistance(useDistance);
            useDistanceToggle.RegisterValueChangedCallback(SetUseDistanceEvent);
            m_ActiveBox.Add(useDistanceToggle);


            m_DistanceHorizBox = new VisualElement();
            m_DistanceHorizBox.style.flexDirection = FlexDirection.Row;
            m_DistanceHorizBox.style.justifyContent = Justify.SpaceBetween;
            m_DistanceHorizBox.SetEnabled(useDistanceToggle.value);


            m_DistanceValueField = new FloatField("  Distance");
            SetFieldStyle(m_DistanceValueField);
            m_DistanceValueField.style.flexGrow = 1;
            m_DistanceValueField.style.flexShrink = 1;

            float distanceValue = SessionState.GetFloat(sessionstate_distVal, 10.0f);
            m_DistanceValueField.SetValueWithoutNotify(distanceValue);

            SetDistance(distanceValue);
            Debug.Log($"Distance: {distanceValue}");
            m_DistanceValueField.RegisterValueChangedCallback(SetDistanceEvent);
            m_DistanceHorizBox.Add(m_DistanceValueField);

            m_GetCamDistButton = new Button(SetDistanceFromCamEvent);
            m_GetCamDistButton.style.backgroundImage = ShaderGUIUtils.GetClosestUnityIconMip("d_SceneViewCamera", 16);// EditorResources.Load<Texture2D>("d_SceneViewCamera");
            m_GetCamDistButton.style.width = 16;
            m_GetCamDistButton.tooltip = "Get Distance to Selected";
            m_DistanceHorizBox.Add(m_GetCamDistButton);

            m_ActiveBox.Add(m_DistanceHorizBox);

            m_RootPanel.Add(activeToggle);
            m_RootPanel.Add(m_ActiveBox);

            return m_RootPanel;
        }

        void SetActiveEvent(ChangeEvent<bool> e)
        {
            if (e.newValue)
            {
                CheckAndAddDebugRenderFeature();
            }
            SessionState.SetBool(sessionstate_active, e.newValue);
            m_ActiveBox.SetEnabled(e.newValue);
            DebugMircotriangles.active = e.newValue;
        }

        void SetUseDistance(bool useDistance)
        {
            Shader.SetKeyword(DebugMircotriangles._FIXED_SCREEN_DISTANCE, useDistance);
        }

        static HashSet<UniversalRenderPipelineAsset> hasDebugMircotriangles = new HashSet<UniversalRenderPipelineAsset>();
        void CheckAndAddDebugRenderFeature()
        {

            UniversalRenderPipelineAsset rpAsset = (UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
            if (hasDebugMircotriangles.Contains(rpAsset)) return;

            UniversalRendererData urd = (UniversalRendererData)rpAsset.m_RendererDataList[0];
            List<ScriptableRendererFeature> features = urd.rendererFeatures;
            foreach (ScriptableRendererFeature feature in features)
            {
                if (feature.GetType() == typeof(DebugMircotriangles))
                {
                    hasDebugMircotriangles.Add(rpAsset);
                    return;
                }
            }

            UniversalRendererDataEditor editor = Editor.CreateEditor(urd, typeof(UniversalRendererDataEditor)) as UniversalRendererDataEditor;
            MethodInfo onEnableMi = typeof(ScriptableRendererDataEditor).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo addComponentMi = typeof(ScriptableRendererDataEditor).GetMethod("AddComponent", BindingFlags.NonPublic | BindingFlags.Instance);
            onEnableMi.Invoke(editor, new object[] { });
            addComponentMi.Invoke((ScriptableRendererDataEditor)editor, new object[] { typeof(DebugMircotriangles).FullName });
            CoreUtils.Destroy(editor);

            foreach (ScriptableRendererFeature feature in features)
            {
                if (feature.GetType() == typeof(DebugMircotriangles))
                {
                    DebugMircotriangles dm = (DebugMircotriangles)feature;
                    dm.name = ObjectNames.NicifyVariableName(nameof(DebugMircotriangles));
                    dm.MicrotriangleShader = Shader.Find(DebugMircotriangles.shaderName);
                    return;
                }
            }
        }

        void SetUseDistanceEvent(ChangeEvent<bool> e)
        {
            SessionState.SetBool(sessionstate_useDist, e.newValue);
            m_DistanceHorizBox.SetEnabled(e.newValue);
            SetDistance(m_DistanceValueField.value);
            SetUseDistance(e.newValue);
        }

        void SetDistanceFromCamEvent()
        {
            if (SceneView.lastActiveSceneView != null && Selection.activeGameObject != null)
            {
                float dist = Vector3.Distance(SceneView.lastActiveSceneView.camera.transform.position, Selection.activeGameObject.transform.position);
                dist = Mathf.Round(dist * 100.0f) * 0.01f;
                m_DistanceValueField.value = dist;
            }
        }

        void SetWireframe(bool useWireframe)
        {
            Shader.SetKeyword(DebugMircotriangles._MICRO_TRI_DISPLAY_AS_WIREFRAME, useWireframe);
        }

        void SetWireframeEvent(ChangeEvent<bool> e)
        {
            SessionState.SetBool(sessionstate_wire, e.newValue);
            SetWireframe(e.newValue);
        }

        void SetDistance(float dist)
        {
            DebugMircotriangles.SetMeshDistance(dist);
        }

        void SetDistanceEvent(ChangeEvent<float> e)
        {
            SessionState.SetFloat(sessionstate_distVal, e.newValue);
            DebugMircotriangles.SetMeshDistance(e.newValue);
        }

        void SetConservative(bool val)
        {
            DebugMircotriangles.SetConservativeRaster(val);
        }

        void SetConservativeEvent(ChangeEvent<bool> e)
        {
            SessionState.SetBool(sessionstate_conRast, e.newValue);
            SetConservative(e.newValue);
        }

        static void SetFieldStyle<T>(BaseField<T> field)
        {
            field.labelElement.style.paddingRight = 20;
            foreach (VisualElement child in field.Children())
            {
                if (child != field.labelElement)
                {
                    child.style.justifyContent = Justify.FlexEnd;
                    break;
                }
            }
        }

    }
}