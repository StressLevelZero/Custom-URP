using System;
using UnityEditor.Rendering.Universal;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using static Unity.Rendering.Universal.ShaderUtils;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Rendering;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Drawing;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using BlendMode = UnityEngine.Rendering.BlendMode;
using RenderQueue = UnityEngine.Rendering.RenderQueue;


namespace UnityEditor
{
    // Used for ShaderGraph Lit shaders
    class LitMASIMGUI : BaseShaderGUI
    {
        public MaterialProperty workflowMode;
        public MaterialProperty blendSrc;
        public MaterialProperty blendDst;


        MaterialProperty[] properties;

        // collect properties from the material properties
        public override void FindProperties(MaterialProperty[] properties)
        {
            // save off the list of all properties for shadergraph
            this.properties = properties;

            var material = materialEditor?.target as Material;
            if (material == null)
                return;

            base.FindProperties(properties);
            workflowMode = BaseShaderGUI.FindProperty(Property.SpecularWorkflowMode, properties, false);
            blendSrc = BaseShaderGUI.FindProperty("_BlendSrc", properties, false);
            blendDst = BaseShaderGUI.FindProperty("_BlendDst", properties, false);
            zwriteProp = BaseShaderGUI.FindProperty("_ZWrite", properties, false);
        }

        static string[] surfaceNames = new string[]
        {
            "Opaque",
            "Transparent",
            "Fade",
        };
        public override void DrawSurfaceOptions(Material material)
        {
            int val = (int)surfaceTypeProp.floatValue;

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = surfaceTypeProp.hasMixedValue;
            int newValue = EditorGUILayout.Popup(Styles.surfaceType, val, surfaceNames);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck() && (newValue != val || surfaceTypeProp.hasMixedValue))
            {
                materialEditor.RegisterPropertyChangeUndo(Styles.surfaceType.text);
                surfaceTypeProp.floatValue = val = newValue;
                switch (newValue)
                {
                    case 0:
                        blendSrc.floatValue = (float)UnityEngine.Rendering.BlendMode.One;
                        blendDst.floatValue = (float)UnityEngine.Rendering.BlendMode.Zero;
                        zwriteProp.floatValue = 1;
                        SetQueue(-1);
                        break;
                    case 1:
                        blendSrc.floatValue = (float)UnityEngine.Rendering.BlendMode.One;
                        blendDst.floatValue = (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
                        zwriteProp.floatValue = 0;
                        SetQueue(3000);
                        break;
                    case 2:
                        blendSrc.floatValue = (float)UnityEngine.Rendering.BlendMode.SrcAlpha;
                        blendDst.floatValue = (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
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
} // namespace UnityEditor
