using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Rendering.Universal;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using SLZ.SLZEditorTools;
using UnityEditor.Graphing.Util;
using System.Reflection;
using Unity.Profiling;

namespace UnityEditor.SLZMaterialUI
{
    public abstract class UIElementsMaterialEditor : MaterialEditor
    {
        static ProfilerMarker UpdateProfiler = new ProfilerMarker("UIElementsUpdateUI");
        static ProfilerMarker PrepareForAnim = new ProfilerMarker("UIElementsUpdateUI_PrepareForAnim");
        public Shader shader;
        public List<BaseMaterialField> materialFields;
        public MaterialProperty[] materialProperties;
        public virtual void UpdateUI()
        {
            UpdateProfiler.Begin();
            //Debug.Log("Called Update UI");
            materialProperties = MaterialEditor.GetMaterialProperties(this.targets);
            PrepareMaterialPropertiesForAnimationMode(materialProperties, true);
            int[] shaderProp2MatProp = ShaderGUIUtils.GetShaderIdxToMaterialProp(materialProperties, shader);
            if (materialFields == null)
            {
                return;
            }
            int numFields = materialFields.Count;
            for (int fIdx = 0; fIdx < numFields; fIdx++)
            {
                    int propIndex = shaderProp2MatProp[materialFields[fIdx].GetShaderPropIdx()];
                    //Debug.Log("Updating with indices: " + materialFields[fIdx].GetShaderPropIdx() + " " + propIndex);
                    materialFields[fIdx].UpdateMaterialProperty(materialProperties[propIndex]);
            }
            UpdateProfiler.End();
        }

        public override bool UseDefaultMargins()
        {
            return false;
        }

        /// <summary>
        /// Initialize the material inspector, getting the shader used by the materials and assigning it to the shader field.
        /// In a sane world, unity would only attempt to create the material editor when all materials share the same shader.
        /// But I don't trust unity, so check to make sure that they're all the same and not null.
        /// </summary>
        /// <returns>true on success, false on failure</returns>
        public bool Initialize(VisualElement root, VisualElement window)
        {
            materialProperties = MaterialEditor.GetMaterialProperties(this.targets);
            PrepareMaterialPropertiesForAnimationMode(materialProperties, true);

            SerializedProperty serializedShader = serializedObject.FindProperty("m_Shader");
            if (serializedShader.hasMultipleDifferentValues || serializedShader.objectReferenceValue == null)
            {
                Debug.LogError("SLZ Material Inspector: attempted to draw custom inspector for materials with different shaders");
                return false;
            }
            this.shader = (Shader)serializedShader.objectReferenceValue;
            if (shader == null)
            {
                Debug.LogError("SLZ Material Inspector: attempted to draw custom inspector for material with null shader");
                return false;
            }


            IMGUIContainer OnUpdate = new IMGUIContainer(() => {
                bool isDisplay = window.style.display != DisplayStyle.None;
                if (base.isVisible != isDisplay)
                {
                    window.style.display = base.isVisible ? DisplayStyle.Flex : DisplayStyle.None;
                    root.MarkDirtyRepaint();
                }

                OnIMGUITick();
             
            });
            root.Add(OnUpdate);
            root.RegisterCallback<AttachToPanelEvent>(evt => Undo.undoRedoPerformed += UpdateUI);
            root.RegisterCallback<DetachFromPanelEvent>(evt => Undo.undoRedoPerformed -= UpdateUI);

            return true;
        }

        static MethodInfo s_RefreshInspectors;
        

        protected override void OnShaderChanged()
        {
            ShaderGUIUtils.ForceRebuild(this);
        }

        void OnIMGUITick()
        {
            if ((target as Material).shader != shader)
            {
                ShaderGUIUtils.ForceRebuild(this);
                return;
            }
            if (base.isVisible && AnimationMode.InAnimationMode())
            {
                PrepareMaterialPropertiesForAnimationMode(materialProperties, true);
                UpdateUI();
            }
        }

    }
}