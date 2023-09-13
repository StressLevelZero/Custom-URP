using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Unity.Collections.LowLevel.Unsafe;
using System.Reflection;
using SLZ.SLZEditorTools;
using UnityEditor.SLZMaterialUI;
using System.Linq;

namespace UnityEditor // This MUST be in the base editor namespace!!!!!
{
    [CanEditMultipleObjects]
    public class LitMASGUI : UIElementsMaterialEditor
    {

        class ShaderPropertyTable
        {
            public int _BaseMap = -1;
            public int _BaseColor = -1;
            public int _Normals = -1;
            public int _MetallicGlossMap = -1;
            public int _Emission = -1;
            public int _EmissionMap = -1;
            public int _EmissionColor = -1;
            public int _EmissionFalloff = -1;
            public int _BakedMutiplier = -1;
            public int _Details = -1;
            public int _DetailMap = -1;
            public int _SSROff = -1;
            public int _SSRTemporalMul = -1;
            public List<int> unknownProperties;
            public int texturePropertyCount; 
        }


        public override VisualElement CreateInspectorGUI()
        {

            VisualElement MainWindow = new VisualElement();

            bool success = base.Initialize(MainWindow);
            if (!success)
            {
                return null;
            }

            MainWindow.styleSheets.Add(ShaderGUIUtils.shaderGUISheet);

            MaterialProperty[] props = GetMaterialProperties(this.targets);

            int[] propIdx = ShaderGUIUtils.GetMaterialPropertyShaderIdx(props, shader);
            ShaderGUIUtils.SanitizeMaterials(this.targets, props, propIdx, shader);

         
            Foldout drawProps = new Foldout();

            RenderQueueDropdown renderQueue = new RenderQueueDropdown(serializedObject, shader);
            drawProps.contentContainer.Add(renderQueue);

            Texture2D RTIcon = ShaderGUIUtils.GetClosestUnityIconMip("RenderTexture Icon", 16);
            ShaderGUIUtils.SetHeaderStyle(drawProps, "Rendering Properties", RTIcon);
            drawProps.value = false;

            Foldout baseProps = new Foldout();
            Texture2D MaterialIcon = ShaderGUIUtils.GetClosestUnityIconMip("Material Icon", 16);
            ShaderGUIUtils.SetHeaderStyle(baseProps, "Core Shading", MaterialIcon);

            Toggle emissionToggle = new Toggle();
            Foldout emissionProps = new Foldout();
            Texture2D LightIcon = ShaderGUIUtils.GetClosestUnityIconMip("Light Icon", 16);
            ShaderGUIUtils.SetHeaderStyle(emissionProps, "Emission", LightIcon, emissionToggle);

            Foldout unknownProps = new Foldout();
          
            ShaderGUIUtils.SetHeaderStyle(unknownProps, "Other");


            ShaderPropertyTable propTable = GetPropertyTable(props);
            materialFields = new BaseMaterialField[props.Length + propTable.texturePropertyCount]; // Scale/offsets are separate fields, double the number of texture properties
            int currentFieldIdx = 0;

            // Base Map ------------------------------------------------------

            TextureField baseMapField = null;
            if (propTable._BaseMap != -1)
            {
                baseMapField = new TextureField(props[propTable._BaseMap], propIdx[propTable._BaseMap], false);
                baseMapField.tooltip2 = "The albedo map is simply the color of the material. When the material is metallic, this also tints the reflections";
                baseProps.Add(baseMapField);
                materialFields[currentFieldIdx] = baseMapField;
                currentFieldIdx++;
            }

            // Base Color ----------------------------------------------------

            if (propTable._BaseColor != -1)
            {
                MaterialColorField baseColorField = new MaterialColorField();
                if (baseMapField != null)
                {
                    baseColorField.Initialize(props[propTable._BaseColor], propIdx[propTable._BaseColor], true);
                    baseMapField.rightAlignBox.Add(baseColorField);
                }
                else
                {
                    baseColorField.Initialize(props[propTable._BaseColor], propIdx[propTable._BaseColor], false);
                    baseProps.Add(baseColorField);
                }
                baseColorField.tooltip = "Base color, tints the albedo map";
                materialFields[currentFieldIdx] = baseColorField;
                currentFieldIdx++;
            }



            // MAS Map -------------------------------------------------------


            if (propTable._MetallicGlossMap != -1)
            {
                TextureField MASMap = new TextureField(props[propTable._MetallicGlossMap], propIdx[propTable._MetallicGlossMap], false);
                MASMap.tooltip2 = "Metallic, Ambient Occlusion, Smoothness map. The metallic (red channel) controls how reflective the surface is and how much the albedo tints reflections." +
                    " Ambient occlusion (green channel) is fake pre-baked shadows that darkens areas like crevices or creases which are likely to be shadowed by surface itself. " +
                    "Smoothness (blue channel) controls the sharpness of reflections, and for non-metallic surfaces the strength of reflections.";
                baseProps.Add(MASMap);
                materialFields[currentFieldIdx] = MASMap;
                currentFieldIdx++;
            }

            // Base map tiling offset ----------------------------------------

            if (propTable._BaseMap != -1)
            {
                MaterialScaleOffsetField baseScaleOffsetField = new MaterialScaleOffsetField(props[propTable._BaseMap], propIdx[propTable._BaseMap]);
                baseProps.Add(baseScaleOffsetField);
                materialFields[currentFieldIdx] = baseScaleOffsetField;
                currentFieldIdx++;
            }

            // Unknown Properties --------------------------------------------

            int numUnknown = propTable.unknownProperties.Count;
            List<int> unknownPropIdx = propTable.unknownProperties;
            for (int i = 0; i < numUnknown; i++)
            {
                MaterialProperty prop = props[unknownPropIdx[i]];
                int shaderIdx = propIdx[unknownPropIdx[i]];
                if ((prop.flags & MaterialProperty.PropFlags.HideInInspector) != 0)
                {
                    continue;
                }
                switch (prop.type) 
                {
                    case (MaterialProperty.PropType.Texture):
                        if ((prop.flags & MaterialProperty.PropFlags.NonModifiableTextureData) != 0) continue;
                        TextureField tf = new TextureField(prop, shaderIdx, (prop.flags & MaterialProperty.PropFlags.Normal) != 0);
                        unknownProps.Add(tf);
                        materialFields[currentFieldIdx] = tf;
                        currentFieldIdx++;
                        if ((prop.flags & MaterialProperty.PropFlags.NoScaleOffset) == 0)
                        {
                            MaterialScaleOffsetField msof = new MaterialScaleOffsetField(prop, shaderIdx);
                            unknownProps.Add(msof);
                            materialFields[currentFieldIdx] = msof;
                            currentFieldIdx++;
                        }

                        break;
                    case (MaterialProperty.PropType.Color):
                        MaterialColorField cf = new MaterialColorField();
                        if ((prop.flags & MaterialProperty.PropFlags.HDR) != 0)
                        {
                            cf.hdr = true;
                        }
                        cf.Initialize(prop, shaderIdx, false);
                        unknownProps.Add(cf);
                        materialFields[currentFieldIdx] = cf;
                        currentFieldIdx++;
                        break;
                    case (MaterialProperty.PropType.Vector):
                        MaterialVectorField vf = new MaterialVectorField();
                        vf.Initialize(prop, shaderIdx);
                        unknownProps.Add(vf);
                        materialFields[currentFieldIdx] = vf;
                        currentFieldIdx++;
                        break;
                    case (MaterialProperty.PropType.Range):
                        if (shader.GetPropertyAttributes(shaderIdx).Contains("IntRange"))
                        {
                            MaterialIntRangeField irf = new MaterialIntRangeField();
                            irf.Initialize(prop, shaderIdx);
                            unknownProps.Add(irf);
                            materialFields[currentFieldIdx] = irf;
                            currentFieldIdx++;
                        }
                        else
                        {
                            MaterialRangeField rf = new MaterialRangeField();
                            rf.Initialize(prop, shaderIdx);
                            unknownProps.Add(rf);
                            materialFields[currentFieldIdx] = rf;
                            currentFieldIdx++;
                        }
                        break;
                    case (MaterialProperty.PropType.Float):
                        string[] attributes = shader.GetPropertyAttributes(shaderIdx);
                        string keyword;
                        if (HasToggleAttribute(attributes, out keyword))
                        {
                            MaterialToggleField tgf = new MaterialToggleField();
                            tgf.Initialize(prop, shaderIdx, keyword, false);
                            unknownProps.Add(tgf);
                            materialFields[currentFieldIdx] = tgf;
                            currentFieldIdx++;
                        }
                        else
                        {
                            MaterialFloatField ff = new MaterialFloatField();
                            ff.Initialize(prop, shaderIdx);
                            unknownProps.Add(ff);
                            materialFields[currentFieldIdx] = ff;
                            currentFieldIdx++;
                        }
                        break;
                    case (MaterialProperty.PropType.Int):
                        MaterialIntField inf = new MaterialIntField();
                        inf.Initialize(prop, shaderIdx);
                        unknownProps.Add(inf);
                        materialFields[currentFieldIdx] = inf;
                        currentFieldIdx++;
                        break;
                }
            }

            //testBox.name = "headerRoot";
            MainWindow.Add(drawProps);
            MainWindow.Add(baseProps);
            MainWindow.Add(emissionProps);
            if (numUnknown > 0)
            {
                MainWindow.Add(unknownProps);
            }

            return MainWindow;
        }

        static char[] attributeSeparators = new char[2] { '(', ')' };
        bool HasToggleAttribute(string[] attributes, out string keyword)
        {
            int numAttr = attributes.Length;
            for (int i = 0; i < numAttr; i++) 
            {
                if (attributes[i].StartsWith("Toggle"))
                {
                    if (attributes[i].Equals("ToggleUI"))
                    {
                        keyword = null;
                    }
                    else
                    {
                        Debug.Log(attributes[i]);
                        string[] split = attributes[i].Split(attributeSeparators);
                        keyword = split[1];
                    }
                    return true;
                }
                
            }
            keyword = null;
            return false;
        }
        private ShaderPropertyTable GetPropertyTable(MaterialProperty[] props)
        {
            int numProps = props.Length;
            ShaderPropertyTable output = new ShaderPropertyTable();
            output.unknownProperties = new List<int>(numProps);
            for (int i = 0; i < numProps; i++)
            {
                if (props[i].type == MaterialProperty.PropType.Texture) output.texturePropertyCount++;

                if (output._BaseMap == -1 && (props[i].name == "_BaseMap"))
                {
                    output._BaseMap = i;
                }
                else if (output._BaseColor == -1 && (props[i].name == "_BaseColor"))
                {
                    output._BaseColor = i;
                }
                else if (output._MetallicGlossMap == -1 && (props[i].name == "_MetallicGlossMap"))
                {
                    output._MetallicGlossMap = i;
                }
                else
                {
                    output.unknownProperties.Add(i);                   
                }
            }
            return output;
        }

    }



}