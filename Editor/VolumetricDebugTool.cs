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
    
    bool isActive = false;
    float exposure = 0.05f;

    void OnEnable()
    {
        m_IconContent = new GUIContent()
        {
            image = m_ToolIcon,
            text = "Toggle Volumetrics",
            tooltip = "Toggle Volumetrics"
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
        Color backgroundColor = new Color(0.11f, 0.11f, 0.11f, 0.8f);
        toolWindow.style.backgroundColor = backgroundColor;
        toolWindow.style.marginLeft = 10f;
        toolWindow.style.marginBottom = 10f;
        toolWindow.style.paddingTop = 8f;
        toolWindow.style.paddingRight = 8f;
        toolWindow.style.paddingLeft = 8f;
        toolWindow.style.paddingBottom = 8f;
        
        Label titleLabel = new Label("Volumetric Visualization");
        titleLabel.style.fontSize = 14;
        titleLabel.style.paddingBottom = 8f;
        titleLabel.style.unityTextAlign = TextAnchor.UpperCenter;

        Toggle activeToggle = new Toggle("Show Volumetrics");
        activeToggle.value = isActive;

        UnityEditor.UIElements.FloatField exposureField = new UnityEditor.UIElements.FloatField("Exposure");
        exposureField.value = exposure;

        Slider exposureSlider = new Slider("");
        exposureSlider.value = Mathf.Sqrt(exposure);
        exposureSlider.showInputField = false;
        exposureSlider.highValue = 1.4142135624f;


        toolWindow.Add(titleLabel);
        toolWindow.Add(activeToggle);
        toolWindow.Add(exposureField);
        toolWindow.Add(exposureSlider);

        activeToggle.RegisterCallback<ChangeEvent<bool>>(e=>
        {
            isActive = activeToggle.value;
            BakedVolumetricArea.VisStateGlobal = isActive;
            exposure = exposureField.value;
            Shader.SetGlobalFloat("_VolExposure", exposure);
            EditorWindow view = EditorWindow.GetWindow<SceneView>();
            view.Repaint();
        }
        );

         exposureField.RegisterCallback<ChangeEvent<float>>(e=>
        {
            exposureField.value = Mathf.Clamp(exposureField.value, 0, 2);
            exposure = exposureField.value;
            exposureSlider.value = Mathf.Sqrt(exposure);
            Shader.SetGlobalFloat("_VolExposure", exposure);
            EditorWindow view = EditorWindow.GetWindow<SceneView>();
            view.Repaint();
        }
        );

        exposureSlider.RegisterCallback<ChangeEvent<float>>(e=>
        {
            exposure = Mathf.Pow(exposureSlider.value, 2.0f);
            exposureField.value = exposure;
            Shader.SetGlobalFloat("_VolExposure", exposure);
            EditorWindow view = EditorWindow.GetWindow<SceneView>();
            view.Repaint();
        }
        );

        SceneView sv = SceneView.lastActiveSceneView;
        sv.rootVisualElement.Add(toolWindow);
        sv.rootVisualElement.style.flexDirection = FlexDirection.ColumnReverse;
    }

    public override void OnWillBeDeactivated()
    {
        toolWindow?.RemoveFromHierarchy();
    }

    private void SetVolumesState(bool state)
    {
        BakedVolumetricArea.VisStateGlobal = state;
    }
}
