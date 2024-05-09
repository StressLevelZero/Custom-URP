using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEditor.Rendering.Universal.ShaderGUI;
using static Unity.Rendering.Universal.ShaderUtils;

namespace UnityEditor
{
    class SLZUnlit_IMGUI : BaseShaderGUI
    {
        public MaterialProperty blendSrc;
        public MaterialProperty blendDst;

        MaterialProperty[] properties;

        public enum UnlitBlendModes
        {
            Opaque = 0,
            AlphaPremultiplied,
            AlphaBlended,
            Additive,
            Multiplicative,
        }

        static string[] unlitBlendModeNames = Enum.GetNames(typeof(UnlitBlendModes));

        // collect properties from the material properties
        public override void FindProperties(MaterialProperty[] properties)
        {
            // save off the list of all properties for shadergraph
            this.properties = properties;

            var material = materialEditor?.target as Material;
            if (material == null)
                return;

            base.FindProperties(properties);
            blendModeProp = BaseShaderGUI.FindProperty("_BlendMode", properties, false);
            blendSrc = BaseShaderGUI.FindProperty("_BlendSrc", properties, false);
            blendDst = BaseShaderGUI.FindProperty("_BlendDst", properties, false);
            zwriteProp = BaseShaderGUI.FindProperty("_ZWrite", properties, false);
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            float surface = material.GetFloat("_Surface");
            Debug.Log(oldShader.name);
            if (oldShader.name.StartsWith("Universal Render Pipeline"))
            {
                bool hasBlendmode = oldShader.FindPropertyIndex("_Blend") >= 0;


                if (hasBlendmode)
                {
                    float blend = material.GetFloat("_Blend");

                    if (surface > 0.0f && blend == (float)BaseShaderGUI.BlendMode.Alpha)
                    {
                        surface = 2.0f;
                        material.SetFloat("_Surface", surface);
                    }
                }
                bool hasEmission = material.IsKeywordEnabled("_EMISSION");
                if (hasEmission)
                {
                    material.SetFloat("_Emission", 1);
                }
            }
            bool hasTemporalAcm = newShader.FindPropertyIndex("_SSRTemporalMul") >= 0;
            switch (surface)
            {
                case 0:
                    material.SetFloat("_BlendSrc", (float)UnityEngine.Rendering.BlendMode.One);
                    material.SetFloat("_BlendDst", (float)UnityEngine.Rendering.BlendMode.Zero);
                    material.SetFloat("_ZWrite", 1);
                    material.renderQueue = -1;
                    if (hasTemporalAcm) material.SetFloat("_SSRTemporalMul", 1.0f);
                    break;
                case 1:
                    material.SetFloat("_BlendSrc", (float)UnityEngine.Rendering.BlendMode.One);
                    material.SetFloat("_BlendDst", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_ZWrite", 0);
                    if (hasTemporalAcm) material.SetFloat("_SSRTemporalMul", 0.0f);
                    material.renderQueue = 3000;
                    break;
                case 2:
                    material.SetFloat("_BlendSrc", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetFloat("_BlendDst", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_ZWrite", 0);
                    if (hasTemporalAcm) material.SetFloat("_SSRTemporalMul", 0.0f);
                    material.renderQueue = 3000;
                    break;
            }
        }

        public override void DrawSurfaceOptions(Material material)
        {
            int val = (int)blendModeProp.floatValue;


            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = blendModeProp.hasMixedValue;
            int newValue = EditorGUILayout.Popup(Styles.blendingMode, val, unlitBlendModeNames);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck() && (newValue != val || blendModeProp.hasMixedValue))
            {
                UnlitBlendModes enumVal = (UnlitBlendModes)newValue;
                materialEditor.RegisterPropertyChangeUndo(Styles.blendingMode.text);
                blendModeProp.floatValue = val = newValue;
                switch (enumVal)
                {
                    case UnlitBlendModes.Opaque:
                        blendSrc.floatValue = (float)UnityEngine.Rendering.BlendMode.One;
                        blendDst.floatValue = (float)UnityEngine.Rendering.BlendMode.Zero;
                        zwriteProp.floatValue = 1;
                        SetQueue(-1);
                        break;
                    case UnlitBlendModes.AlphaPremultiplied:
                        blendSrc.floatValue = (float)UnityEngine.Rendering.BlendMode.One;
                        blendDst.floatValue = (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
                        zwriteProp.floatValue = 0;
                        SetQueue(3000);
                        break;
                    case UnlitBlendModes.AlphaBlended:
                        blendSrc.floatValue = (float)UnityEngine.Rendering.BlendMode.SrcAlpha;
                        blendDst.floatValue = (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
                        zwriteProp.floatValue = 0;
                        SetQueue(3000);
                        break;
                    case UnlitBlendModes.Additive:
                        blendSrc.floatValue = (float)UnityEngine.Rendering.BlendMode.One;
                        blendDst.floatValue = (float)UnityEngine.Rendering.BlendMode.One;
                        zwriteProp.floatValue = 0;
                        SetQueue(3000);
                        break;
                    case UnlitBlendModes.Multiplicative:
                        blendSrc.floatValue = (float)UnityEngine.Rendering.BlendMode.DstColor;
                        blendDst.floatValue = (float)UnityEngine.Rendering.BlendMode.Zero;
                        zwriteProp.floatValue = 0;
                        SetQueue(3000);
                        break;
                }
            }
            if (val != 0)
            {
                EditorGUILayout.HelpBox("Non-opaque surfaces are EXPENSIVE on Quest and other mobile devices. Avoid when possible!", MessageType.Warning);
            }

            DoPopup(Styles.cullingText, cullingProp, Styles.renderFaceNames);
            //DoPopup(Styles.zwriteText, zwriteProp, Styles.zwriteNames);
            materialEditor.RenderQueueField();
            if (ztestProp != null)
                materialEditor.IntPopupShaderProperty(ztestProp, Styles.ztestText.text, Styles.ztestNames, Styles.ztestValues);

            DrawFloatToggleProperty(Styles.alphaClipText, alphaClipProp);

            if ((alphaClipProp != null) && (alphaCutoffProp != null) && (alphaClipProp.floatValue == 1))
                materialEditor.ShaderProperty(alphaCutoffProp, Styles.alphaClipThresholdText, 1);

            DrawFloatToggleProperty(Styles.castShadowText, castShadowsProp);
            DrawFloatToggleProperty(Styles.receiveShadowText, receiveShadowsProp);
        }

        void SetQueue(int value)
        {
            UnityEngine.Object[] array3 = materialEditor.targets;
            foreach (UnityEngine.Object @object in array3)
            {
                ((Material)@object).renderQueue = value;
            }
        }

        void ValidateQueue(Material mat)
        {

        }

        public static void UpdateMaterial(Material material, MaterialUpdateType updateType)
        {
            // newly created materials should initialize the globalIlluminationFlags (default is off)
            if (updateType == MaterialUpdateType.CreatedNewMaterial)
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;

            bool automaticRenderQueue = GetAutomaticQueueControlSetting(material);
            //BaseShaderGUI.UpdateMaterialSurfaceOptions(material, automaticRenderQueue);
            LitGUI.SetupSpecularWorkflowKeyword(material, out bool isSpecularWorkflow);
        }

        public override void ValidateMaterial(Material material)
        {
            if (material == null)
                throw new ArgumentNullException("material");

            UpdateMaterial(material, MaterialUpdateType.ModifiedMaterial);
        }


        // material main surface inputs
        public override void DrawSurfaceInputs(Material material)
        {
            DrawShaderGraphProperties(material, properties);
        }

        public override void DrawAdvancedOptions(Material material)
        {
            // Always show the queue control field.  Only show the render queue field if queue control is set to user override
            DoPopup(Styles.queueControl, queueControlProp, Styles.queueControlNames);
            //if (material.HasProperty(Property.QueueControl) && material.GetFloat(Property.QueueControl) == (float)QueueControl.UserOverride)

            base.DrawAdvancedOptions(material);

            // ignore emission color for shadergraphs, because shadergraphs don't have a hard-coded emission property, it's up to the user
            materialEditor.DoubleSidedGIField();
            materialEditor.LightmapEmissionFlagsProperty(0, enabled: true, ignoreEmissionColor: true);
        }
    }
}
