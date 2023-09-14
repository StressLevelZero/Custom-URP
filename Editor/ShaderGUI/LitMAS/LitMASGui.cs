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
using System.Runtime.CompilerServices;
using static UnityEngine.Rendering.DebugUI.MessageBox;

namespace UnityEditor // This MUST be in the base editor namespace!!!!!
{
    [CanEditMultipleObjects]
    public class LitMASGUI : UIElementsMaterialEditor
    {
        const string keyword_DETAILS_ON = "_DETAILS_ON";

        enum PName
        {
            _BaseMap = 0,
            _BaseColor,
            _MetallicGlossMap,
            _Normals,
            _BumpMap,
            _Emission,
            _EmissionMap,
            _EmissionColor,
            _EmissionFalloff,
            _BakedMutiplier,
            _Details,
            _DetailMap,
        }
        static ReadOnlySpan<string> propertyNames => new string[] {
            "_BaseMap",
            "_BaseColor",
            "_MetallicGlossMap",
            "_Normals",
            "_BumpMap",
            "_Emission",
            "_EmissionMap",
            "_EmissionColor",
            "_EmissionFalloff",
            "_BakedMutiplier",
            "_Details",
            "_DetailMap",
        };
        class ShaderPropertyTable
        {
            public int[] nameToPropIdx;
            //public int _BaseMap = -1;
            //public int _BaseColor = -1;
            //public int _Normals = -1;
            //public int _MetallicGlossMap = -1;
            //public int _Emission = -1;
            //public int _EmissionMap = -1;
            //public int _EmissionColor = -1;
            //public int _EmissionFalloff = -1;
            //public int _BakedMutiplier = -1;
            //public int _Details = -1;
            //public int _DetailMap = -1;
            //public int _SSROff = -1;
            //public int _SSRTemporalMul = -1;
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
            //ShaderGUIUtils.SanitizeMaterials(this.targets, props, propIdx, shader);

            ShaderPropertyTable propTable = GetPropertyTable(props);
            materialFields = new BaseMaterialField[props.Length + propTable.texturePropertyCount]; // Scale/offsets are separate fields, double the number of texture properties
            int currentFieldIdx = 0;

            //----------------------------------------------------------------
            // Rendering Properties ------------------------------------------
            //----------------------------------------------------------------

            Foldout drawProps = new Foldout();

            Texture2D RTIcon = ShaderGUIUtils.GetClosestUnityIconMip("RenderTexture Icon", 16);
            ShaderGUIUtils.SetHeaderStyle(drawProps, "Rendering Properties", RTIcon);
            //drawProps.value = false;

            RenderQueueDropdown renderQueue = new RenderQueueDropdown(serializedObject, shader);
            drawProps.contentContainer.Add(renderQueue);

            MainWindow.Add(drawProps);

            //----------------------------------------------------------------
            // Core Properties -----------------------------------------------
            //----------------------------------------------------------------

            Foldout baseProps = new Foldout();
            Texture2D MaterialIcon = ShaderGUIUtils.GetClosestUnityIconMip("Material Icon", 16);
            ShaderGUIUtils.SetHeaderStyle(baseProps, "Core Shading", MaterialIcon);
            bool hasCoreProperty = false;

            // Base Map ------------------------------------------------------


            TextureField baseMapField = null;
            int baseMapIdx = PropertyIdx(ref propTable, PName._BaseMap);
            if (baseMapIdx != -1)
            {
                baseMapField = new TextureField(props[baseMapIdx], propIdx[baseMapIdx], false);
                baseMapField.tooltip2 = "The albedo map is simply the color of the material. When the material is metallic, this also tints the reflections";
                baseProps.Add(baseMapField);
                materialFields[currentFieldIdx] = baseMapField;
                currentFieldIdx++;
                hasCoreProperty = true;
            }

            // Base Color ----------------------------------------------------
            int baseColorIdx = PropertyIdx(ref propTable, PName._BaseColor);
            if (baseColorIdx != -1)
            {
                MaterialColorField baseColorField = new MaterialColorField();
                if (baseMapField != null)
                {
                    baseColorField.Initialize(props[baseColorIdx], propIdx[baseColorIdx], true);
                    baseMapField.rightAlignBox.Add(baseColorField);
                }
                else
                {
                    baseColorField.Initialize(props[baseColorIdx], propIdx[baseColorIdx], false);
                    baseProps.Add(baseColorField);
                }
                baseColorField.tooltip = "Base color, tints the albedo map";
                materialFields[currentFieldIdx] = baseColorField;
                currentFieldIdx++;
                hasCoreProperty = true;
            }

            // MAS Map -------------------------------------------------------
            int MASMapIdx = PropertyIdx(ref propTable, PName._MetallicGlossMap);
            if (MASMapIdx != -1)
            {
                TextureField MASMap = new TextureField(props[MASMapIdx], propIdx[MASMapIdx], false);
                MASMap.tooltip2 = "Metallic, Ambient Occlusion, Smoothness map. The metallic (red channel) controls how reflective the surface is and how much the albedo tints reflections." +
                    " Ambient occlusion (green channel) is fake pre-baked shadows that darkens areas like crevices or creases which are likely to be shadowed by surface itself. " +
                    "Smoothness (blue channel) controls the sharpness of reflections, and for non-metallic surfaces the strength of reflections.";
                baseProps.Add(MASMap);
                materialFields[currentFieldIdx] = MASMap;
                currentFieldIdx++;
                hasCoreProperty = true;
            }

            // Normal Map ----------------------------------------------------
            int NormalMapIdx = PropertyIdx(ref propTable, PName._BumpMap);
            if (NormalMapIdx != -1)
            {
                TextureField NormalMap = new TextureField(props[NormalMapIdx], propIdx[NormalMapIdx], true);
                NormalMap.tooltip2 = "Vector map that offsets the normal (direction the surface is facing) when calculating lighting. Used to add high-resolution detail to otherwise simple mesh geometry.";
                baseProps.Add(NormalMap);
                materialFields[currentFieldIdx] = NormalMap;
                currentFieldIdx++;

                int NormalsIdx = PropertyIdx(ref propTable, PName._Normals);
                if (NormalsIdx != -1) 
                {
                    NormalMap.leftAlignBox.SetEnabled(props[NormalsIdx].floatValue > 0.0);
                    MaterialToggleField normalToggle = new MaterialToggleField();
                    normalToggle.Initialize(props[NormalsIdx], propIdx[NormalsIdx], null, false, true);
                    normalToggle.RegisterValueChangedCallback(evt => NormalMap.leftAlignBox.SetEnabled(evt.newValue));
                    NormalMap.rightAlignBox.Add(normalToggle);
                }
                hasCoreProperty = true;
            }

            // Base map tiling offset ----------------------------------------

            if (baseMapIdx != -1)
            {
                MaterialScaleOffsetField baseScaleOffsetField = new MaterialScaleOffsetField(props[baseMapIdx], propIdx[baseMapIdx]);
                baseProps.Add(baseScaleOffsetField);
                materialFields[currentFieldIdx] = baseScaleOffsetField;
                currentFieldIdx++;
            }

            if (hasCoreProperty) MainWindow.Add(baseProps);

            //----------------------------------------------------------------
            // Emission Properties -------------------------------------------
            //----------------------------------------------------------------

            Toggle emissionToggle = null;
            Foldout emissionProps = new Foldout();
            Texture2D LightIcon = ShaderGUIUtils.GetClosestUnityIconMip("Light Icon", 16);
            
            bool hasEmissionProperty = false;
            // Emission Map --------------------------------------------------

            TextureField emissionMapField = null;
            int emissionMapIdx = PropertyIdx(ref propTable, PName._EmissionMap);
            if (emissionMapIdx != -1)
            {
                emissionMapField = new TextureField(props[emissionMapIdx], propIdx[emissionMapIdx], false);
                emissionMapField.tooltip2 = "The emission map controls the intensity and color of light being emitted by the material";
                emissionProps.Add(emissionMapField);
                materialFields[currentFieldIdx] = emissionMapField;
                currentFieldIdx++;
                hasEmissionProperty = true;
            }

            int emissionColorIdx = PropertyIdx(ref propTable, PName._EmissionColor);
            if (emissionColorIdx != -1)
            {
                MaterialColorField emissionColorField = new MaterialColorField();
                emissionColorField.hdr = true;
                if (emissionMapIdx != -1)
                {
                    emissionColorField.Initialize(props[emissionColorIdx], propIdx[emissionColorIdx], true);
                    emissionMapField.rightAlignBox.Add(emissionColorField);
                }
                else
                {
                    emissionColorField.Initialize(props[emissionColorIdx], propIdx[emissionColorIdx], false);
                    emissionProps.Add(emissionColorField);
                }
                emissionColorField.tooltip = "Emission color, tints the emission from the emission map. Additionally, the alpha channel controls" +
                    " how much the albedo map tints the emission. As the alpha goes to 0, the emission is increasingly tinted by the albedo";
                materialFields[currentFieldIdx] = emissionColorField;
                currentFieldIdx++;
                hasEmissionProperty = true;
            }

            int emissionFalloffIdx = PropertyIdx(ref propTable, PName._EmissionFalloff);
            if (emissionFalloffIdx != -1)
            {
                MaterialFloatField emissionFalloffField = new MaterialFloatField();

                emissionFalloffField.Initialize(props[emissionFalloffIdx], propIdx[emissionFalloffIdx]);
                emissionProps.Add(emissionFalloffField);

                emissionFalloffField.tooltip = "Controls the strength of the emission fresenel. This is an effect where the strength of the emission decreases as the surface points farther away from the camera.";
                materialFields[currentFieldIdx] = emissionFalloffField;
                currentFieldIdx++;
                hasEmissionProperty = true;
            }

            int emissionMultiplierIdx = PropertyIdx(ref propTable, PName._BakedMutiplier);
            if (emissionMultiplierIdx != -1)
            {
                MaterialFloatField emissionMultiplierField = new MaterialFloatField();

                emissionMultiplierField.Initialize(props[emissionMultiplierIdx], propIdx[emissionMultiplierIdx]);
                emissionProps.Add(emissionMultiplierField);

                emissionMultiplierField.tooltip = "Artificially increases the strength of the emission when baking lights. WARNING: this will make specular lighting too " +
                    "dark when <i><b>not</b></i> using shaders that guestimate the specular from light probes. Normally specular lighting comes entirely from the reflection probe, and this " +
                    "multiplier does not effect the brightness seen by reflection probes";
                materialFields[currentFieldIdx] = emissionMultiplierField;
                currentFieldIdx++;
                hasEmissionProperty = true;
            }

            int emissionToggleIdx = PropertyIdx(ref propTable, PName._Emission);
            EmissionToggleField emissionMatToggle = null;
            if (emissionToggleIdx != -1)
            {
                emissionMatToggle = new EmissionToggleField();
                emissionMatToggle.Initialize(props[emissionToggleIdx], propIdx[emissionToggleIdx], null, false, true);

                emissionMatToggle.RegisterCallback<ChangeEvent<bool>>(evt => { emissionProps.contentContainer.SetEnabled(evt.newValue); });
                emissionToggle = emissionMatToggle;
                materialFields[currentFieldIdx] = emissionMatToggle;
                currentFieldIdx++;
                hasEmissionProperty = true;

                bool emissionEnabled = props[emissionToggleIdx].floatValue > 0.0f;
                emissionProps.contentContainer.SetEnabled(emissionEnabled);
            }


            if (hasEmissionProperty)
            {
                GIFlagsPopup emissionFlags = new GIFlagsPopup(serializedObject);
                emissionProps.Add(emissionFlags);

                Toggle doubleSidedGIToggle = new Toggle("Double Sided GI");
                SetAlignStyle(doubleSidedGIToggle);
                doubleSidedGIToggle.bindingPath = "m_DoubleSidedGI";
                emissionProps.Add(doubleSidedGIToggle);

                ShaderGUIUtils.SetHeaderStyle(emissionProps, "Emission", LightIcon, emissionToggle);
                MainWindow.Add(emissionProps);

            }

            //----------------------------------------------------------------
            // Detail Properties ---------------------------------------------
            //----------------------------------------------------------------

            Toggle detailToggle = null;
            Foldout detailProps = new Foldout();
            Texture2D detailIcon = ShaderGUIUtils.GetClosestUnityIconMip("Grid Icon", 16);
            bool hasDetails = false;

            int detailMapIdx = PropertyIdx(ref propTable, PName._DetailMap);
            if (detailMapIdx != -1)
            {
                TextureField detailsMapField = new TextureField(props[detailMapIdx], propIdx[detailMapIdx], false);
                detailsMapField.tooltip2 = "REPLACE THIS";
                detailProps.Add(detailsMapField);
                materialFields[currentFieldIdx] = detailsMapField;
                currentFieldIdx++;
                hasDetails = true;

                MaterialScaleOffsetField detailScaleOffset = new MaterialScaleOffsetField(props[detailMapIdx], propIdx[detailMapIdx]);
                detailProps.Add(detailScaleOffset);
                materialFields[currentFieldIdx] = detailScaleOffset;
                currentFieldIdx++;
            }

            int detailToggleIdx = PropertyIdx(ref propTable, PName._Details);
            if (detailToggleIdx != -1 && hasDetails)
            {
                MaterialToggleField detailMatToggle = new MaterialToggleField();
                detailMatToggle.Initialize(props[detailToggleIdx], propIdx[detailToggleIdx], keyword_DETAILS_ON, false, true);
                detailMatToggle.RegisterCallback<ChangeEvent<bool>>(evt => { detailProps.contentContainer.SetEnabled(evt.newValue); });
                bool detailEnabled = props[detailToggleIdx].floatValue > 0.0f;
                detailProps.contentContainer.SetEnabled(detailEnabled);
                //detailProps.Add(detailMatToggle);
                materialFields[currentFieldIdx] = detailMatToggle;
                currentFieldIdx++;

                detailToggle = detailMatToggle;
            }


            if (hasDetails)
            {
                ShaderGUIUtils.SetHeaderStyle(detailProps, "Details", detailIcon, detailToggle);
                MainWindow.Add(detailProps);
            }
            //----------------------------------------------------------------
            // Unknown Properties --------------------------------------------
            //----------------------------------------------------------------
            Foldout unknownProps = new Foldout();
            ShaderGUIUtils.SetHeaderStyle(unknownProps, "Other");

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
                       // Debug.Log(attributes[i]);
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
            int numNames = propertyNames.Length;
            ShaderPropertyTable output = new ShaderPropertyTable();

            int[] nameToPropIdx = new int[numNames];
            output.nameToPropIdx = nameToPropIdx;
            for (int i = 0; i < numNames; i++) nameToPropIdx[i] = -1;

            output.unknownProperties = new List<int>(numProps);
            for (int propIdx = 0; propIdx < numProps; propIdx++)
            {
                if (props[propIdx].type == MaterialProperty.PropType.Texture) output.texturePropertyCount++;
                bool unknown = true;
                string propName = props[propIdx].name;
                for (int nameIdx = 0; nameIdx < numNames; nameIdx++)
                {
                    if (nameToPropIdx[nameIdx] == -1 && string.Equals(propertyNames[nameIdx], propName))
                    {
                        nameToPropIdx[nameIdx] = propIdx;
                        unknown = false;
                        break;
                    }
                }
                if (unknown)
                {
                    output.unknownProperties.Add(propIdx);                   
                }
            }
            return output;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int PropertyIdx(ref ShaderPropertyTable table, PName name)
        {
            return table.nameToPropIdx[(int)name];
        }

        static void SetAlignStyle(VisualElement vi)
        {
            VisualElement left = vi.ElementAt(0);
            left.AddToClassList("materialGUILeftBox");
            left.style.overflow = Overflow.Hidden;
            left.style.minWidth = 0;
            VisualElement right = vi.ElementAt(1);
            right.AddToClassList("materialGUIRightBox");
            vi.style.justifyContent = Justify.FlexStart;
            vi.style.marginRight = 3;
        }

    }



}