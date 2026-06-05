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
using UnityEditor.Search;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine.Experimental.GlobalIllumination;
using UnityEngine.Experimental.Rendering;





#if UNITY_6000_1_OR_NEWER
using MaterialPropertyFlags = UnityEngine.Rendering.ShaderPropertyFlags;
#else
using MaterialPropertyFlags = UnityEditor.MaterialProperty.PropFlags;
#endif

namespace UnityEditor // This MUST be in the base editor namespace!!!!!
{


    [CanEditMultipleObjects]
    public class LitMASGUI : UIElementsMaterialEditor
    {
        // Temp patch to allow this to work in 2022, 6.0, and 6.1+ simultaneously 
#if UNITY_6000_1_OR_NEWER
        private MaterialPropertyFlags propertyFlags(MaterialProperty prop) => prop.propertyFlags;
#else
        private MaterialPropertyFlags propertyFlags(MaterialProperty prop) => prop.flags;

#endif
        const string keyword_DETAILS_ON = "_DETAILS_ON";
        const string keyword_FRACTAL_DETAILS_OFF = "_FRACTAL_DETAILS_OFF";
        const string obsolete_keyword_DETAILS_UV_ON = "_DETAILS_UV_ON";
        const string keyword_BRDF = "_BRDFMAP";
        const string keyword_EXPENSIVE_TP = "_EXPENSIVE_TP";
        const string keyword_FLUORESCENCE = "_FLUORESCENCE";

        const string defaultMASGUID = "75f1fbacfa73385419ec8d7700a107ea";
        static string s_defaultMASPath;
        static string defaultMASPath
        {
            get
            {
                if (s_defaultMASPath == null)
                {
                    s_defaultMASPath = AssetDatabase.GUIDToAssetPath(defaultMASGUID);
                }
                return s_defaultMASPath;
            }
        }

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
            _DetailNormalScale,
            g_tBRDFMap,
            BRDFMAP,
            _HitRamp,
            _HitColor,

            // Rendering properties
            _Surface,
            _BlendSrc,
            _BlendDst,
            _ZWrite,
            _Cull,
            _HalfShade,
            _Slope,
            _Offset,
            _Alphatest,
            _Cutoff,

            // Triplanar properties
            _Expensive,
            _RotateUVs,
            _DetailsuseLocalUVs,
            _UVScaler,

            // Fluorescence
            _Fluorescent,
            _FluorMap,
            _FluorColor,
            _FluorAbsorbance,
            _FluorAlbedoTint,

            // Layered
            _BaseMap1,
            _BaseMap2,
            _BaseMap3,
            _BaseMap4,

            _AYSXMap,
            _AYSXMap1,
            _AYSXMap2,
            _AYSXMap3,
            _AYSXMap4,

            _HeightMap,
            _HeightMap1,
            _HeightMap2,
            _HeightMap3,
            _HeightMap4,

            _SplatMap,
            _UseGRID,
        };

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
            "_DetailNormalScale",
            "g_tBRDFMap",
            "BRDFMAP",
            "_HitRamp",
            "_HitColor",

            // Rendering properties
            "_Surface",
            "_BlendSrc",
            "_BlendDst",
            "_ZWrite",
            "_Cull",
            "_HalfShade",
            "_Slope",
            "_Offset",
            "_Alphatest",
            "_Cutoff",

             // Triplanar properties
            "_Expensive",
            "_RotateUVs",
            "_DetailsuseLocalUVs",
            "_UVScaler",

            // Fluorescence
            "_Fluorescent",
            "_FluorMap",
            "_FluorColor",
            "_FluorAbsorbance",
            "_FluorAlbedoTint",

            // Layered
            "_BaseMap1",
            "_BaseMap2",
            "_BaseMap3",
            "_BaseMap4",

            "_AYSXMap",
            "_AYSXMap1",
            "_AYSXMap2",
            "_AYSXMap3",
            "_AYSXMap4",

            "_HeightMap",
            "_HeightMap1",
            "_HeightMap2",
            "_HeightMap3",
            "_HeightMap4",

            "_SplatMap",
            "_UseGRID",
        };

        static readonly Dictionary<string, PName> upgradeTextures = new Dictionary<string, PName>()
        {
            {"_Fluorescence", PName._FluorMap},
        };

        static readonly Dictionary<string, PName> upgradeVectors = new Dictionary<string, PName>()
        {
           {"_FluorescenceTint", PName._FluorColor},
           {"_Absorbance",       PName._FluorAbsorbance},
        };

        static readonly Dictionary<string, PName> upgradeFloats = new Dictionary<string, PName>()
        {
           {"_AlbedotoFluorescence", PName._FluorAlbedoTint},
        };

        class ShaderPropertyTable
        {
            public int[] nameToPropIdx;
            public List<int> unknownProperties;
            public int texturePropertyCount; 
        }

        enum DetailsMode
        {
            None = 0,
            Details = 1,
            DetailsFractal = 2,
        }

        /*
        static readonly List<DetailsMode> k_DetailsModes = new()
        {
            DetailsMode.Details,
            DetailsMode.DetailsFractal
        };
        */

        static string FormatDetailsMode(DetailsMode m)
        {
            switch (m)
            {
                case DetailsMode.Details : return "Details";
                case DetailsMode.DetailsFractal : return "Fractal Details";
                default: return "None";
            }
        }

        static DetailsMode GetDetailsMode(Material mat)
        {
            // If both are on (old/bad state), prefer UV.
            if (mat.IsKeywordEnabled(keyword_DETAILS_ON))
            {
                if (mat.IsKeywordEnabled(keyword_FRACTAL_DETAILS_OFF))
                {
                    return DetailsMode.Details;
                }
                else
                {
                    return DetailsMode.DetailsFractal;
                }
            }
            return DetailsMode.Details;
        }

        static void ApplyDetailsMode(Material mat, DetailsMode mode)
        {
            switch (mode)
            {
                case DetailsMode.None:
                    CoreUtils.SetKeyword(mat, keyword_DETAILS_ON, false);
                    CoreUtils.SetKeyword(mat, keyword_FRACTAL_DETAILS_OFF, false);
                    mat.SetFloat("_Details", 0.0f);
                    break;
                case DetailsMode.Details:
                    CoreUtils.SetKeyword(mat, keyword_DETAILS_ON, true);
                    CoreUtils.SetKeyword(mat, keyword_FRACTAL_DETAILS_OFF, true);
                    mat.SetFloat("_Details", 1f);
                    break;
                case DetailsMode.DetailsFractal:
                    CoreUtils.SetKeyword(mat, keyword_DETAILS_ON, true);
                    CoreUtils.SetKeyword(mat, keyword_FRACTAL_DETAILS_OFF, false);
                    mat.SetFloat("_Details", 2f);
                    break;
            }
        }

        HelpBox TransparentWarning;
        HelpBox AlphaClipWarning;
        int AlphaClipWarningCount;
        HelpBox ZOffsetWarning;
        int ZOffsetWarningCount;
        HelpBox DetailScaleWarning;
        int DetailScaleWarningCount;
     
        HelpBox AdvancedModeWarning;
        bool hasHiddenProperties;

        HelpBox basemapAlphaWarning;
        bool hasBasemapAlpha;
        
        SurfaceTypeField surfaceTypeField;
        RenderQueueDropdown renderQueue;
        MaterialToggleField alphaClipToggle;

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();
        
            #if MARROW_INTERNAL
                bool advancedMode = AdvancedMaterialProps.GetVisibility();
                AdvancedMaterialProps.stateChangeCallback += RebuildOnAdvVisChange;
                root.RegisterCallback<DetachFromPanelEvent>(UnregisterVisChangeOnDetach);
            #else
                bool advancedMode = true;
            #endif

            VisualElement MainWindow = new VisualElement();
            root.Add(MainWindow);
            bool success = base.Initialize(root,MainWindow);
            if (!success)
            {
                return null;
            }

            MainWindow.styleSheets.Add(ShaderGUIUtils.shaderGUISheet);

            MaterialProperty[] props = materialProperties;

            int[] propIdx = ShaderGUIUtils.GetMaterialPropertyShaderIdx(props, base.shader);

            ShaderImporter shaderImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(base.shader)) as ShaderImporter;
            
            //ShaderGUIUtils.SanitizeMaterials(this.targets, props, propIdx, shader);

            ShaderPropertyTable propTable = GetPropertyTable(props);
            materialFields = new List<BaseMaterialField>(props.Length + propTable.texturePropertyCount + 5); // Scale/offsets are separate fields, double the number of texture properties
            //int currentFieldIdx = 0;
            
            if (targets.Length < 2)
            {
                bool upgraded = UpgradeObsoleteMaterialProps(serializedObject, props, propTable, upgradeTextures, upgradeVectors, upgradeFloats, null);
                if (upgraded) 
                {
                    serializedObject.ApplyModifiedProperties();
                    materialProperties = MaterialEditor.GetMaterialProperties(this.targets);
                }
                if ((this.target.hideFlags & HideFlags.NotEditable) != 0)
                {
                    MainWindow.SetEnabled(false);
                }
            }

            //----------------------------------------------------------------
            // Warning Messages ----------------------------------------------
            //----------------------------------------------------------------

            TransparentWarning = new HelpBox("Transparent materials are expensive on Quest, use sparingly!", HelpBoxMessageType.Warning);
            TransparentWarning.style.display = DisplayStyle.None;
            ZOffsetWarning = new HelpBox("Non-zero Z Offset slope/units. This will prevent this material from SRP batching with any other material that does not have precisely the same ZOffset values.", HelpBoxMessageType.Warning);
            ZOffsetWarning.style.display = DisplayStyle.None;
            AlphaClipWarning = new HelpBox("Opaque alpha clip materials are very expensive on Quest, prefer transparency if possible!", HelpBoxMessageType.Warning);
            AlphaClipWarning.style.display = DisplayStyle.None;
            DetailScaleWarning = new HelpBox("Detail Normal Scale is not 1", HelpBoxMessageType.Warning);
            DetailScaleWarning.style.display = DisplayStyle.None;
            basemapAlphaWarning = new HelpBox("Layer 0 Base Map has an alpha channel. Height is no longer stored in the alpha channel, this should be removed from the texture to reduce memory usage and increase compression quality", HelpBoxMessageType.Error);
            basemapAlphaWarning.style.display = DisplayStyle.None;

            #if MARROW_INTERNAL
            if (advancedMode)
            {
                AdvancedModeWarning = new HelpBox("Showing Advanced Properties", HelpBoxMessageType.Info);
                MainWindow.Add(AdvancedModeWarning);
            }
            #endif


            MainWindow.Add(TransparentWarning);
            MainWindow.Add(AlphaClipWarning);
            MainWindow.Add(ZOffsetWarning);
            MainWindow.Add(DetailScaleWarning);
            MainWindow.Add(basemapAlphaWarning);

            //----------------------------------------------------------------
            // Rendering Properties ------------------------------------------
            //----------------------------------------------------------------

            Foldout drawProps = new Foldout();


            //{
                //drawProps.value = false;

            renderQueue = new RenderQueueDropdown(serializedObject, shader);

            int surfaceIdx = PropertyIdx(ref propTable, PName._Surface);
            int blendSrcIdx = PropertyIdx(ref propTable, PName._BlendSrc);
            int blendDstIdx = PropertyIdx(ref propTable, PName._BlendDst);
            int zWriteIdx = PropertyIdx(ref propTable, PName._ZWrite);
            if (surfaceIdx != -1 && blendSrcIdx != -1 && blendDstIdx != -1 && zWriteIdx != -1)
            {
                MaterialDummyField blendSrcField = new MaterialDummyField(props[blendSrcIdx], propIdx[blendSrcIdx]);
                MaterialDummyField blendDstField = new MaterialDummyField(props[blendDstIdx], propIdx[blendDstIdx]);
                MaterialDummyField zWriteField = new MaterialDummyField(props[zWriteIdx], propIdx[zWriteIdx]);
                materialFields.Add(blendSrcField);
                materialFields.Add(blendDstField);
                materialFields.Add(zWriteField);

                surfaceTypeField = new SurfaceTypeField();
                surfaceTypeField.Initialize(
                    props[surfaceIdx], 
                    propIdx[surfaceIdx],
                    blendSrcField,
                    blendDstField,
                    zWriteField,
                    renderQueue
                    );

                if (!surfaceTypeField.materialProperty.hasMixedValue && surfaceTypeField.value > 0) TransparentWarning.style.display = DisplayStyle.Flex;

                surfaceTypeField.RegisterValueChangedCallback((ChangeEvent<int> evt) => {
                    TransparentWarning.style.display = evt.newValue > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                    AlphaClipWarning.style.display = alphaClipToggle != null && alphaClipToggle.value && surfaceTypeField.value == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                });
                AlphaClipWarningCount += surfaceTypeField.value > 0 ? -1 : 0;
                surfaceTypeField.tooltip = LitMASGui_Tooltips.Surface.ToString();
                materialFields.Add(surfaceTypeField);
                drawProps.contentContainer.Add(surfaceTypeField);
            }

            int cullIdx = PropertyIdx(ref propTable, PName._Cull);
            if (cullIdx != -1)
            {
                List<MaterialIntPopup.Choice> cullChoices = new List<MaterialIntPopup.Choice>() 
                { 
                    new MaterialIntPopup.Choice {value = (int)CullMode.Back,  label = "Front",  enabledKws = null, disabledKws = null}, 
                    new MaterialIntPopup.Choice {value = (int)CullMode.Front, label = "Back",   enabledKws = null, disabledKws = null},
                    new MaterialIntPopup.Choice {value = (int)CullMode.Off,   label = "Both",   enabledKws = null, disabledKws = null}
                };
                
                MaterialIntPopup cullPopup = new MaterialIntPopup();
                cullPopup.label = "Rendered Side";
                cullPopup.Initialize(props[cullIdx], propIdx[cullIdx], cullChoices);
               
                materialFields.Add(cullPopup);
                drawProps.contentContainer.Add(cullPopup);
            }

            int alphaClipIdx = PropertyIdx(ref propTable, PName._Alphatest);
            int alphaClipThresholdIdx = PropertyIdx(ref propTable, PName._Cutoff);
            if (alphaClipThresholdIdx != -1 && alphaClipIdx != -1)
            {
                VisualElement alphaClipping = new VisualElement();
                alphaClipping.style.justifyContent = Justify.FlexStart;
                alphaClipping.style.alignItems = Align.Center;
                alphaClipping.style.flexDirection = FlexDirection.Row;
                alphaClipping.style.marginLeft = alphaClipping.style.marginRight = 3;

                Label alphaClipLabel = new Label("Alpha Clip");
                alphaClipLabel.AddToClassList("materialGUILeftBox");
                alphaClipLabel.style.overflow = Overflow.Hidden;
                alphaClipLabel.style.minWidth = 0;
                alphaClipping.Add(alphaClipLabel);

                VisualElement alphaClipFields = new VisualElement();
                alphaClipFields.AddToClassList("materialGUIRightBox");
                alphaClipFields.style.flexGrow = 1;
                alphaClipping.Add(alphaClipFields);

                MaterialRangeField alphaClipThreshold = new MaterialRangeField();
                alphaClipThreshold.Initialize(props[alphaClipThresholdIdx], propIdx[alphaClipThresholdIdx], true);
                alphaClipThreshold.style.flexGrow = 1f;
                alphaClipThreshold.style.flexShrink = 1f;
                //alphaClipThreshold.style.flexBasis = 24;
                alphaClipThreshold.label = null;
                materialFields.Add(alphaClipThreshold);


                alphaClipToggle = new MaterialToggleField();
                alphaClipToggle.Initialize(props[alphaClipIdx], propIdx[alphaClipIdx], "_ALPHATEST_ON", false);
                alphaClipToggle.label = null;
                alphaClipToggle.style.flexGrow = 0f;
                alphaClipToggle.style.flexShrink = 0f;
                alphaClipToggle.style.flexBasis = 24;
                alphaClipToggle.style.minWidth = 24;
                alphaClipToggle.style.marginLeft = 1;
                alphaClipToggle.ExtraOnChangeEvent = (ChangeEvent<bool> evt) =>
                {
                    surfaceTypeField.alphaClip = evt.newValue;
                    if (surfaceTypeField == null || surfaceTypeField.materialProperty.hasMixedValue || surfaceTypeField.value == 0)
                    {
                        UnityEngine.Object[] targets = alphaClipToggle.materialProperty.targets;
                        int numTargets = targets.Length;

                        if (evt.newValue == true)
                        {
                            alphaClipThreshold.SetEnabled(true);
                            for (int i = 0; i < numTargets; i++)
                            {
                                Material mat = (Material)targets[i];
                                if (mat.renderQueue < 2400)
                                {
                                    mat.renderQueue = 2450;
                                    EditorUtility.SetDirty(mat);
                                }
                            }
                        }
                        else
                        {
                            alphaClipThreshold.SetEnabled(false);
                            for (int i = 0; i < numTargets; i++)
                            {
                                Material mat = (Material)targets[i];
                                if (mat.renderQueue == 2450)
                                {
                                    mat.renderQueue = -1;
                                }
                                EditorUtility.SetDirty(mat);
                            }
                        }

                        AlphaClipWarning.style.display = alphaClipToggle.value && (surfaceTypeField == null || surfaceTypeField.value == 0) ? DisplayStyle.Flex : DisplayStyle.None;

                    }
                };
                surfaceTypeField.alphaClip = alphaClipToggle.value;
                AlphaClipWarning.style.display = alphaClipToggle.value && surfaceTypeField.value == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                alphaClipThreshold.SetEnabled(alphaClipToggle.value || alphaClipToggle.materialProperty.hasMixedValue);

                materialFields.Add(alphaClipToggle);
                alphaClipFields.Add(alphaClipToggle);
                alphaClipFields.Add(alphaClipThreshold);

                drawProps.Add(alphaClipping);
            }

            int zSlopeIdx = PropertyIdx(ref propTable, PName._Slope);
            int zOffsetIdx = PropertyIdx(ref propTable, PName._Offset);
            int halfShadeIdx = PropertyIdx(ref propTable, PName._HalfShade);
            MaterialZSlopeFloatField zSlopeFloatField = zSlopeIdx != -1 ? new MaterialZSlopeFloatField() : null;
            MaterialHalfRateToggleField halfShadeToggle = halfShadeIdx != -1 ? new MaterialHalfRateToggleField() : null;

            // "Advanced properties" ie properties you don't want artists changing at random. Category gets added dead last and starts collapsed.
            // Set it up here since the Z-Offset and half-rate are intertwined, and we want to hide only the z-offset
            Foldout advancedProps = new Foldout();
            bool hasAdvancedProps = false;


            if (zSlopeIdx != -1 || zOffsetIdx != -1)
            {
                hasAdvancedProps = true;
                VisualElement zOffset = new VisualElement();
                zOffset.style.justifyContent = Justify.FlexStart;
                zOffset.style.alignItems = Align.Center;
                zOffset.style.flexDirection = FlexDirection.Row;
                zOffset.style.marginLeft = zOffset.style.marginRight = 3;

                Label zOffsetLabel = new Label("Z Offset");
                zOffsetLabel.AddToClassList("materialGUILeftBox");
                zOffsetLabel.style.overflow = Overflow.Hidden;
                zOffsetLabel.style.minWidth = 0;
                zOffset.Add(zOffsetLabel);

                VisualElement zOffsetFields = new VisualElement();
                zOffsetFields.AddToClassList("materialGUIRightBox");

                if (zSlopeIdx != -1)
                {
                    zSlopeFloatField.Initialize(props[zSlopeIdx], halfShadeToggle, propIdx[zSlopeIdx], false);
                    zSlopeFloatField.label = "Slope";
                    zSlopeFloatField.AddToClassList("materialGUIRightBox");
                    if (zSlopeFloatField.value != 0) ZOffsetWarningCount += 1;
                    zSlopeFloatField.RegisterValueChangedCallback(SetZOffsetWarningVisibility<float>);
                    materialFields.Add(zSlopeFloatField);
                    zOffsetFields.Add(zSlopeFloatField);
                }

                if (zOffsetIdx != -1)
                {
                    MaterialIntField zOffsetUnits = new MaterialIntField();
                    zOffsetUnits.Initialize(props[zOffsetIdx], propIdx[zOffsetIdx], true);
                    zOffsetUnits.label = "Units";
                    VisualElement zOffsetUnitsLabel = zOffsetUnits.ElementAt(0);
                    VisualElement zOffsetUnitsField = zOffsetUnits.ElementAt(1);
                    zOffsetUnits.AddToClassList("materialGUIRightBox");
                    zOffsetUnitsLabel.style.flexBasis = 36;
                    zOffsetUnitsLabel.style.minWidth = 36;
                    zOffsetUnitsLabel.style.flexGrow = 0f;
                    zOffsetUnitsLabel.style.flexShrink = 0f;
                    zOffsetUnitsLabel.style.alignSelf = Align.FlexStart;
                    zOffsetUnitsField.style.flexGrow = 1;
                    zOffsetUnitsField.style.flexShrink = 1f;

                    if (zOffsetUnits.value != 0) ZOffsetWarningCount += 1;
                    zOffsetUnits.RegisterValueChangedCallback(SetZOffsetWarningVisibility<int>);

                    materialFields.Add(zOffsetUnits);
                    zOffsetFields.Add(zOffsetUnits);
                }

                ZOffsetWarning.style.display = ZOffsetWarningCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;

                zOffset.Add(zOffsetFields);
                advancedProps.contentContainer.Add(zOffset);
            }

            if (halfShadeIdx != -1)
            {
                halfShadeToggle.Initialize(props[halfShadeIdx], zSlopeFloatField, propIdx[halfShadeIdx], string.Empty, false);
                drawProps.contentContainer.Add(halfShadeToggle);
                materialFields.Add(halfShadeToggle);
            }



            drawProps.contentContainer.Add(renderQueue);
            //}
            MainWindow.Add(drawProps);

#region Core Properties
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
            int baseMap1Idx = PropertyIdx(ref propTable, PName._BaseMap1); // Don't render the basemap here if this is layered
            if (baseMapIdx != -1 && baseMap1Idx == -1)
            {
                baseMapField = new TextureField(props[baseMapIdx], propIdx[baseMapIdx], false);
                baseMapField.tooltip2 = LitMASGui_Tooltips.BaseMap.ToString();
                baseProps.Add(baseMapField);
                materialFields.Add(baseMapField);
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
                baseColorField.tooltip = LitMASGui_Tooltips.BaseColor.ToString();
                materialFields.Add(baseColorField);
                hasCoreProperty = true;
            }

            // MAS Map -------------------------------------------------------
            int MASMapIdx = PropertyIdx(ref propTable, PName._MetallicGlossMap);
            if (MASMapIdx != -1)
            {
                Texture2D defaultMAS = AssetDatabase.LoadAssetAtPath<Texture2D>(defaultMASPath);
                TextureField MASMap = new TextureField(props[MASMapIdx], propIdx[MASMapIdx], false, shaderImporter?.GetDefaultTexture(props[MASMapIdx].name));
                MASMap.tooltip2 = LitMASGui_Tooltips.MASMap.ToString();
                baseProps.Add(MASMap);
                materialFields.Add(MASMap);
                hasCoreProperty = true;

                MAS_defaultSlider defaultSlider = new MAS_defaultSlider(MASMap);
                MASMap.rightAlignBox.Add(defaultSlider);
            }

            // Normal Map ----------------------------------------------------
            int NormalMapIdx = PropertyIdx(ref propTable, PName._BumpMap);
            if (NormalMapIdx != -1)
            {
                TextureField NormalMap = new TextureField(props[NormalMapIdx], propIdx[NormalMapIdx], true);
                NormalMap.thisEditor = this;
                NormalMap.tooltip2 = LitMASGui_Tooltips.NormalMap.ToString();
                baseProps.Add(NormalMap);
                materialFields.Add(NormalMap);

                int NormalsIdx = PropertyIdx(ref propTable, PName._Normals);
                if (NormalsIdx != -1) 
                {
                    NormalMap.leftAlignBox.SetEnabled(props[NormalsIdx].floatValue > 0.0);
                    MaterialToggleField normalToggle = new MaterialToggleField();
                    normalToggle.Initialize(props[NormalsIdx], propIdx[NormalsIdx], null, false, true);
                    normalToggle.RegisterValueChangedCallback(evt => NormalMap.leftAlignBox.SetEnabled(evt.newValue));
                    NormalMap.rightAlignBox.Add(normalToggle);
                    materialFields.Add(normalToggle);
                }
                hasCoreProperty = true;
            }

            int BRDFRampIdx = PropertyIdx(ref propTable, PName.g_tBRDFMap);
            if(BRDFRampIdx != -1)
            {
                TextureField BRDFRamp = new TextureField(props[BRDFRampIdx], propIdx[BRDFRampIdx], false);
                //NormalMap.tooltip2 = LitMASGui_Tooltips.NormalMap.ToString();
                baseProps.Add(BRDFRamp);
                materialFields.Add(BRDFRamp);

                int BRDFRampToggleIdx = PropertyIdx(ref propTable, PName.BRDFMAP);
                if (BRDFRampToggleIdx != -1)
                {
                    BRDFRamp.leftAlignBox.SetEnabled(props[BRDFRampToggleIdx].floatValue > 0.0);
                    MaterialToggleField BRDFRampToggle = new MaterialToggleField();
                    BRDFRampToggle.Initialize(props[BRDFRampToggleIdx], propIdx[BRDFRampToggleIdx], keyword_BRDF, false, true);
                    BRDFRampToggle.RegisterValueChangedCallback(evt => BRDFRamp.leftAlignBox.SetEnabled(evt.newValue));
                    BRDFRamp.rightAlignBox.Add(BRDFRampToggle);
                    materialFields.Add(BRDFRampToggle);
                }
                hasCoreProperty = true;
            }
#endregion // Core Properties

#region Layered
           
            if (baseMap1Idx != -1)
            {
                int splatMapIdx = PropertyIdx(ref propTable, PName._SplatMap);
                TextureField splatMapField = new TextureField(props[splatMapIdx], propIdx[splatMapIdx], false);
                splatMapField.tooltip2 = LitMASGui_Tooltips.SplatMap.ToString();
                baseProps.Add(splatMapField);
                materialFields.Add(splatMapField);

                int useGRIDIdx = PropertyIdx(ref propTable, PName._UseGRID);
                MaterialToggleField useGridField = new MaterialToggleField();
                useGridField.Initialize(props[useGRIDIdx], propIdx[useGRIDIdx], null);
                useGridField.label = "World Projected UVs";
                baseProps.Add(useGridField);
                materialFields.Add(useGridField);

                ScrollView scrollView = new ScrollView(ScrollViewMode.Horizontal);
                scrollView.contentContainer.style.minWidth = 580;
                scrollView.contentContainer.style.flexDirection = FlexDirection.Column;
                scrollView.style.marginTop = 8;
                //scrollView.style.flexGrow = 1;

                Texture2D layer0Basemap =  props[baseMapIdx].textureValue as Texture2D;

                #if !UNITY_ANDROID && MARROW_INTERNAL
                if (layer0Basemap && GraphicsFormatUtility.HasAlphaChannel(layer0Basemap.graphicsFormat))
                {
                    basemapAlphaWarning.style.display = DisplayStyle.Flex;
                }
                #endif

                scrollView.Add(LayeredHeader());
                hasCoreProperty = true;
                Span<int> layerMapIdxs = stackalloc int[5];
                layerMapIdxs[0] = baseMapIdx;
                layerMapIdxs[1] = baseMap1Idx;
                layerMapIdxs[2] = PropertyIdx(ref propTable, PName._BaseMap2);
                layerMapIdxs[3] = PropertyIdx(ref propTable, PName._BaseMap3);
                layerMapIdxs[4] = PropertyIdx(ref propTable, PName._BaseMap4);
                VisualElement baseMaps = LayeredTextureField(ref layerMapIdxs, props, propIdx, "Base Maps", LitMASGui_Tooltips.BaseMap);
                scrollView.Add(baseMaps);

                #if !UNITY_ANDROID && MARROW_INTERNAL
                TextureField baseMapField0 = (TextureField)materialFields[materialFields.Count - 5];
                baseMapField0.texObjField.RegisterValueChangedCallback((ChangeEvent<Object> evt) => 
                {
                    Texture2D newTex = evt.newValue as Texture2D;
                    if (newTex && GraphicsFormatUtility.HasAlphaChannel(newTex.graphicsFormat))
                    {
                        basemapAlphaWarning.style.display = DisplayStyle.Flex;
                    }
                    else
                    {
                        basemapAlphaWarning.style.display = DisplayStyle.None;
                    }
                } 
                );
                #endif

                VisualElement ScaleOffsets = LayeredScaleOffsetField(ref layerMapIdxs, props, propIdx, "Scale Offsets", "");

                layerMapIdxs[0] = PropertyIdx(ref propTable, PName._AYSXMap);
                layerMapIdxs[1] = PropertyIdx(ref propTable, PName._AYSXMap1);
                layerMapIdxs[2] = PropertyIdx(ref propTable, PName._AYSXMap2);
                layerMapIdxs[3] = PropertyIdx(ref propTable, PName._AYSXMap3);
                layerMapIdxs[4] = PropertyIdx(ref propTable, PName._AYSXMap4);
                VisualElement aysxMaps = LayeredTextureField(ref layerMapIdxs, props, propIdx, "AYSX Maps", LitMASGui_Tooltips.AYSXMap);
                scrollView.Add(aysxMaps);
                
                layerMapIdxs[0] = PropertyIdx(ref propTable, PName._HeightMap);
                layerMapIdxs[1] = PropertyIdx(ref propTable, PName._HeightMap1);
                layerMapIdxs[2] = PropertyIdx(ref propTable, PName._HeightMap2);
                layerMapIdxs[3] = PropertyIdx(ref propTable, PName._HeightMap3);
                layerMapIdxs[4] = PropertyIdx(ref propTable, PName._HeightMap4);
                VisualElement heightMaps = LayeredTextureField(ref layerMapIdxs, props, propIdx, "Height Maps", LitMASGui_Tooltips.HeightMap);
                scrollView.Add(heightMaps);

                scrollView.Add(ScaleOffsets);

                baseProps.Add(scrollView);
            }
#endregion

#region Triplanar
            //----------------------------------------------------------------
            // Triplanar options ---------------------------------------------
            //----------------------------------------------------------------

            int fixSeamsIdx = PropertyIdx(ref propTable, PName._Expensive);
            if (fixSeamsIdx != -1)
            {
                MaterialToggleField seamToggle = new MaterialToggleField();
                seamToggle.Initialize(props[fixSeamsIdx], propIdx[fixSeamsIdx], keyword_EXPENSIVE_TP, false);
                materialFields.Add(seamToggle);
                baseProps.Add(seamToggle);
            }

            int rotateUVsIdx = PropertyIdx(ref propTable, PName._RotateUVs);
            if (rotateUVsIdx != -1)
            {
                MaterialToggleField rotateUVsToggle = new MaterialToggleField();
                rotateUVsToggle.Initialize(props[rotateUVsIdx], propIdx[rotateUVsIdx], null, false);
                materialFields.Add(rotateUVsToggle);
                baseProps.Add(rotateUVsToggle);
            }
            int triplanarScaleIdx = PropertyIdx(ref propTable, PName._UVScaler);
            if (triplanarScaleIdx != -1)
            {
                MaterialFloatField triplanarScaleField = new MaterialFloatField();
                triplanarScaleField.Initialize(props[triplanarScaleIdx], propIdx[triplanarScaleIdx]);
                materialFields.Add(triplanarScaleField);
                baseProps.Add(triplanarScaleField);
            }
#endregion

            // Base map tiling offset ----------------------------------------

            if (baseMapIdx != -1 && (propertyFlags(props[baseMapIdx]) & MaterialPropertyFlags.NoScaleOffset) == 0 && baseMap1Idx == -1)
            {
                MaterialScaleOffsetField baseScaleOffsetField = new MaterialScaleOffsetField(props[baseMapIdx], propIdx[baseMapIdx]);
                baseProps.Add(baseScaleOffsetField);
                materialFields.Add(baseScaleOffsetField);
            }



            if (hasCoreProperty)
            {
                Texture2D RTIcon = ShaderGUIUtils.GetClosestUnityIconMip("RenderTexture Icon", 16);
                ShaderGUIUtils.SetHeaderStyle(drawProps, "Rendering Properties", RTIcon);
                MainWindow.Add(baseProps);
            }

#region Emission
            //----------------------------------------------------------------
            // Emission Properties -------------------------------------------
            //----------------------------------------------------------------

            Toggle emissionToggle = null;
            Foldout emissionProps = new Foldout();
            
            
            bool hasEmissionProperty = false;
            // Emission Map --------------------------------------------------

            TextureField emissionMapField = null;
            int emissionMapIdx = PropertyIdx(ref propTable, PName._EmissionMap);
            if (emissionMapIdx != -1)
            {
                emissionMapField = new TextureField(props[emissionMapIdx], propIdx[emissionMapIdx], false);
                emissionMapField.tooltip2 = LitMASGui_Tooltips.EmissionMap.ToString();
                emissionProps.Add(emissionMapField);
                materialFields.Add(emissionMapField);
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
                emissionColorField.tooltip = LitMASGui_Tooltips.EmissionColor.ToString();
                materialFields.Add(emissionColorField);
                hasEmissionProperty = true;
            }

            int emissionFalloffIdx = PropertyIdx(ref propTable, PName._EmissionFalloff);
            if (emissionFalloffIdx != -1)
            {
                MaterialFloatField emissionFalloffField = new MaterialFloatField();

                emissionFalloffField.Initialize(props[emissionFalloffIdx], propIdx[emissionFalloffIdx]);
                emissionProps.Add(emissionFalloffField);

                emissionFalloffField.tooltip = LitMASGui_Tooltips.EmissionFalloff.ToString();
                materialFields.Add(emissionFalloffField);
                hasEmissionProperty = true;
            }

            int emissionMultiplierIdx = PropertyIdx(ref propTable, PName._BakedMutiplier);
            if (emissionMultiplierIdx != -1)
            {
                MaterialFloatField emissionMultiplierField = new MaterialFloatField();

                emissionMultiplierField.Initialize(props[emissionMultiplierIdx], propIdx[emissionMultiplierIdx]);
                emissionProps.Add(emissionMultiplierField);

                emissionMultiplierField.tooltip = LitMASGui_Tooltips.EmissionFalloff.ToString();
                materialFields.Add(emissionMultiplierField);
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
                materialFields.Add(emissionMatToggle);
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

                Texture2D LightIcon = ShaderGUIUtils.GetClosestUnityIconMip("Light Icon", 16);
                ShaderGUIUtils.SetHeaderStyle(emissionProps, "Emission", LightIcon, emissionToggle);
                MainWindow.Add(emissionProps);
            }
#endregion // Emission

#region Details
            //----------------------------------------------------------------
            // Detail Properties ---------------------------------------------
            //----------------------------------------------------------------

            Toggle detailToggle = null;
            Foldout detailProps = new Foldout();
           
            bool hasDetails = false;
            
            //var detailsBody = new VisualElement();
            // detailProps.tooltip = "Fractal texture sampling is effectively infinite textile density. UV is legacy behavior and should only be used if one fixed resolution or tiling is needed ";
            // detailProps.Add(detailsBody); // everything that should be disabled goes in here
            int detailMapIdx = PropertyIdx(ref propTable, PName._DetailMap);
            if (detailMapIdx != -1)
            {
                TextureField detailsMapField = new TextureField(props[detailMapIdx], propIdx[detailMapIdx], false, shaderImporter?.GetDefaultTexture(props[detailMapIdx].name));
                detailsMapField.tooltip2 = LitMASGui_Tooltips.DetailMap.ToString();
                detailProps.Add(detailsMapField);
                materialFields.Add(detailsMapField);
                hasDetails = true;

                MaterialScaleOffsetField detailScaleOffset = new MaterialScaleOffsetField(props[detailMapIdx], propIdx[detailMapIdx]);
                detailProps.Add(detailScaleOffset);
                materialFields.Add(detailScaleOffset);
            }

            

            MaterialIntPopup detailPopup = new MaterialIntPopup();
            int detailToggleIdx = PropertyIdx(ref propTable, PName._Details);

            bool hasFractalDetails = false;
            if (detailToggleIdx != -1)
            {
                LocalKeyword fractalKw = base.shader.keywordSpace.FindKeyword(keyword_FRACTAL_DETAILS_OFF);
                hasFractalDetails = fractalKw.isValid;
            }

            if (hasFractalDetails && hasDetails)
            {
                List<MaterialIntPopup.Choice> detailChoices = new List<MaterialIntPopup.Choice>() 
                { 
                    new MaterialIntPopup.Choice {value = (int)DetailsMode.None,           label = "Disabled",enabledKws = null, disabledKws = new string[] {keyword_FRACTAL_DETAILS_OFF, keyword_DETAILS_ON}}, 
                    new MaterialIntPopup.Choice {value = (int)DetailsMode.Details,        label = "Simple",  enabledKws = new string[] {keyword_FRACTAL_DETAILS_OFF, keyword_DETAILS_ON}, disabledKws = null}, 
                    new MaterialIntPopup.Choice {value = (int)DetailsMode.DetailsFractal, label = "Fractal", enabledKws = new string[] {keyword_DETAILS_ON}, disabledKws = new string[] {keyword_FRACTAL_DETAILS_OFF}}
                };
                List<int> visibleChoices = new List<int>() {1, 2};
                
                detailPopup = new MaterialIntPopup();
                detailPopup.label = "Detail Mode";

                detailPopup.Initialize(props[detailToggleIdx], propIdx[detailToggleIdx], detailChoices, visibleChoices);

                materialFields.Add(detailPopup);
                detailProps.contentContainer.Insert(0,detailPopup);
            }

            
            if (detailToggleIdx != -1 && hasDetails)
            {
                MaterialToggleField detailMatToggle = new MaterialToggleField();
                string detailToggleKw = hasFractalDetails ? null : "_DETAILS_ON";
                detailMatToggle.Initialize(props[detailToggleIdx], propIdx[detailToggleIdx], detailToggleKw, false, true);

                if (hasFractalDetails)
                {
                    detailMatToggle.RegisterCallback<ChangeEvent<bool>>(evt => 
                    { 
                        // Remember the old detail value
                        if (!evt.newValue && !detailMatToggle.materialProperty.hasMixedValue && !detailPopup.showMixedValue) 
                        {
                            detailMatToggle.onFloatValue = detailPopup.value;
                        }
                        detailProps.contentContainer.SetEnabled(evt.newValue); 
                        detailPopup.value = evt.newValue ? detailPopup.GetValueIndex((int)detailMatToggle.onFloatValue) : (int)detailMatToggle.offFloatValue;
                    }
                    );
                }
                else
                {
                    detailMatToggle.RegisterCallback<ChangeEvent<bool>>(evt => 
                    {
                        detailProps.contentContainer.SetEnabled(evt.newValue); 
                    }
                    );
                }
                bool detailEnabled = props[detailToggleIdx].floatValue > 0.0f;
                detailProps.contentContainer.SetEnabled(detailEnabled);
                materialFields.Add(detailMatToggle);                
                detailToggle = detailMatToggle;
            }

            int detailNrmScaleIdx = PropertyIdx(ref propTable, PName._DetailNormalScale);
            if (detailNrmScaleIdx != -1)
            {
                hasHiddenProperties = true;
                VisualElement detailNrmScaleField;
                if (advancedMode)
                {
                    MaterialFloatField detailNrmScaleRawField = new MaterialFloatField();
                    detailNrmScaleRawField.Initialize(props[detailNrmScaleIdx], propIdx[detailNrmScaleIdx]);
                    materialFields.Add(detailNrmScaleRawField);
                    detailNrmScaleField = detailNrmScaleRawField;
                    detailProps.contentContainer.Add(detailNrmScaleField);
                }
                else
                {
                    MaterialRangeField detailScaleRangeField = new MaterialRangeField();
                    detailScaleRangeField.lowValue  = 0.1f;
                    detailScaleRangeField.highValue = 1.0f;
                    detailScaleRangeField.Initialize(props[detailNrmScaleIdx], propIdx[detailNrmScaleIdx]);
                    materialFields.Add(detailScaleRangeField);
                    detailNrmScaleField = detailScaleRangeField;
                    advancedProps.contentContainer.Add(detailNrmScaleField);
                    hasAdvancedProps = true;
                    detailScaleRangeField.RegisterValueChangedCallback(SetDetailScaleWarningVisibility);
                }
                #if MARROW_INTERNAL
                if (props[detailNrmScaleIdx].floatValue != 1.0f) EnableDetailScaleWarning();
                #endif
            }

            if (hasDetails)
            {
                Texture2D detailIcon = ShaderGUIUtils.GetClosestUnityIconMip("Grid Icon", 16);
                ShaderGUIUtils.SetHeaderStyle(detailProps, "Details", detailIcon, detailToggle);
                MainWindow.Add(detailProps);
            }
#endregion // Details

#region Impacts
            //----------------------------------------------------------------
            // Impact Properties --------------------------------------------
            //----------------------------------------------------------------
            Foldout ImpactProps = new Foldout();
            bool hasImpacts = false;

            int hitRampIdx = PropertyIdx(ref propTable, PName._HitRamp);
            TextureField hitRamp = null;
            if (hitRampIdx != -1)
            {
                hitRamp = new TextureField(props[hitRampIdx], propIdx[hitRampIdx], false);
                ImpactProps.Add(hitRamp);
                materialFields.Add(hitRamp);
                hasImpacts = true;
            }

            int hitColorIdx = PropertyIdx(ref propTable, PName._HitColor);
            if (hitRampIdx != -1)
            {
                MaterialColorField hitColorField = new MaterialColorField();
                hitColorField.hdr = true;
                if (hitColorIdx != -1)
                {
                    hitColorField.Initialize(props[hitColorIdx], propIdx[hitColorIdx], true);
                    hitRamp.rightAlignBox.Add(hitColorField);
                }
                else
                {
                    hitColorField.Initialize(props[emissionColorIdx], propIdx[emissionColorIdx], false);
                    ImpactProps.Add(hitColorField);
                }
                hitColorField.tooltip = LitMASGui_Tooltips.EmissionColor.ToString();
                materialFields.Add(hitColorField);
                hasImpacts = true;
            }

            if (hasImpacts)
            {
                Texture2D impactIcon = ShaderGUIUtils.GetClosestUnityIconMip("RaycastCollider Icon", 16);
                ShaderGUIUtils.SetHeaderStyle(ImpactProps, "Impacts", impactIcon);
                MainWindow.Add(ImpactProps);
            }
#endregion // Impacts

#region Fluorescence
            //----------------------------------------------------------------
            // Fluorescence Properties ---------------------------------------
            //----------------------------------------------------------------

            Toggle fluorToggle = null;
            Foldout fluorProps = new Foldout();
            bool hasFluorescence = false;

            TextureField fluorMapField = null;
            int fluorMapIdx = PropertyIdx(ref propTable, PName._FluorMap);
            if (fluorMapIdx != -1)
            {
                fluorMapField = new TextureField(props[fluorMapIdx], propIdx[fluorMapIdx], false);
                fluorMapField.tooltip2 = LitMASGui_Tooltips.FluorMap.ToString();
                fluorProps.Add(fluorMapField);
                materialFields.Add(fluorMapField);
                hasFluorescence = true;
            }


            int fluorColorIdx = PropertyIdx(ref propTable, PName._FluorColor);
            if (fluorColorIdx != -1)
            {
                MaterialColorField fluorColorField = new MaterialColorField();
                if (fluorMapField != null)
                {
                    fluorColorField.Initialize(props[fluorColorIdx], propIdx[fluorColorIdx], true);
                    fluorMapField.rightAlignBox.Add(fluorColorField);
                }
                else
                {
                    fluorColorField.Initialize(props[fluorColorIdx], propIdx[fluorColorIdx], false);
                    fluorProps.Add(fluorColorField);
                }
                fluorColorField.tooltip = LitMASGui_Tooltips.FluorColor.ToString();
                materialFields.Add(fluorColorField);
                hasFluorescence = true;
            }

            int fluorAbsorbIdx = PropertyIdx(ref propTable, PName._FluorAbsorbance);
            if (fluorAbsorbIdx != -1)
            {
                MaterialColorField fluorAbsorbField = new MaterialColorField();
                fluorAbsorbField.Initialize(props[fluorAbsorbIdx], propIdx[fluorAbsorbIdx], false);
                fluorProps.Add(fluorAbsorbField);
                fluorAbsorbField.tooltip = LitMASGui_Tooltips.FluorAbsorbance.ToString();
                if (fluorAbsorbField.label.StartsWith("Fluorescence")) fluorAbsorbField.label = fluorAbsorbField.label.Substring("Fluorescence".Length);
                materialFields.Add(fluorAbsorbField);
                hasFluorescence = true;
            }

            int fluorAlbedoTintIdx = PropertyIdx(ref propTable, PName._FluorAlbedoTint);
            if (fluorAbsorbIdx != -1)
            {
                MaterialRangeField fluorAlbedoTintField = new MaterialRangeField();
                fluorAlbedoTintField.Initialize(props[fluorAlbedoTintIdx], propIdx[fluorAlbedoTintIdx], false);
                if (fluorAlbedoTintField.label.StartsWith("Fluorescence")) fluorAlbedoTintField.label = fluorAlbedoTintField.label.Substring("Fluorescence".Length);
                fluorProps.Add(fluorAlbedoTintField);
                fluorAlbedoTintField.tooltip = LitMASGui_Tooltips.FluorAlbedoTint.ToString();
                materialFields.Add(fluorAlbedoTintField);
                hasFluorescence = true;
            }


            int fluorToggleIdx = PropertyIdx(ref propTable, PName._Fluorescent);
            if (hasFluorescence && fluorToggleIdx != -1)
            {
                MaterialToggleField fluorMatToggle = new MaterialToggleField();
                fluorMatToggle.Initialize(props[fluorToggleIdx], propIdx[fluorToggleIdx], keyword_FLUORESCENCE, false, true);
                materialFields.Add(fluorMatToggle);
                fluorToggle = fluorMatToggle;

                fluorMatToggle.RegisterCallback<ChangeEvent<bool>>(evt => { fluorProps.contentContainer.SetEnabled(evt.newValue); });
                bool fluorEnabled = props[fluorToggleIdx].floatValue > 0.0f;
                fluorProps.contentContainer.SetEnabled(fluorEnabled);
            }

            if (hasFluorescence)
            {
                Texture2D FluorIcon = ShaderGUIUtils.GetClosestUnityIconMip("AreaLight Icon", 16);
                ShaderGUIUtils.SetHeaderStyle(fluorProps, "Fluorescence", FluorIcon, fluorToggle);
                MainWindow.Add(fluorProps);
            }
#endregion // Fluorescence

#region Unknown Properties
            //----------------------------------------------------------------
            // Unknown Properties --------------------------------------------
            //----------------------------------------------------------------
            Foldout unknownProps = new Foldout();
            Texture2D otherIcon = ShaderGUIUtils.GetClosestUnityIconMip("Settings Icon", 16);
            ShaderGUIUtils.SetHeaderStyle(unknownProps, "Other", otherIcon);
            bool hasUnknown = false;
            int numUnknown = propTable.unknownProperties.Count;
            List<int> unknownPropIdx = propTable.unknownProperties;
            for (int i = 0; i < numUnknown; i++)
            {
                MaterialProperty prop = props[unknownPropIdx[i]];
                int shaderIdx = propIdx[unknownPropIdx[i]];
                if ((propertyFlags(prop) & MaterialPropertyFlags.HideInInspector) != 0)
                {
                    continue;
                }
                hasUnknown = true;
                switch (prop.type) 
                {
                    case (MaterialProperty.PropType.Texture):
                        if ((propertyFlags(prop) & MaterialPropertyFlags.NonModifiableTextureData) != 0) continue;
                        TextureField tf = new TextureField(prop, shaderIdx, (propertyFlags(prop) & MaterialPropertyFlags.Normal) != 0, shaderImporter?.GetDefaultTexture(prop.name));
                        unknownProps.Add(tf);
                        materialFields.Add(tf);

                        if ((propertyFlags(prop) & MaterialPropertyFlags.NoScaleOffset) == 0)
                        {
                            MaterialScaleOffsetField msof = new MaterialScaleOffsetField(prop, shaderIdx);
                            unknownProps.Add(msof);
                            materialFields.Add(msof);
                        }

                        break;
                    case (MaterialProperty.PropType.Color):
                        MaterialColorField cf = new MaterialColorField();
                        if ((propertyFlags(prop) & MaterialPropertyFlags.HDR) != 0)
                        {
                            cf.hdr = true;
                        }
                        cf.Initialize(prop, shaderIdx, false);
                        unknownProps.Add(cf);
                        materialFields.Add(cf);
                        break;
                    case (MaterialProperty.PropType.Vector):
                        MaterialVectorField vf = new MaterialVectorField();
                        vf.Initialize(prop, shaderIdx);
                        unknownProps.Add(vf);
                        materialFields.Add(vf);
                        break;
                    case (MaterialProperty.PropType.Range):
                        if (shader.GetPropertyAttributes(shaderIdx).Contains("IntRange"))
                        {
                            MaterialIntRangeField irf = new MaterialIntRangeField();
                            irf.Initialize(prop, shaderIdx);
                            unknownProps.Add(irf);
                            materialFields.Add(irf);
                        }
                        else
                        {
                            MaterialRangeField rf = new MaterialRangeField();
                            rf.Initialize(prop, shaderIdx);
                            unknownProps.Add(rf);
                            materialFields.Add(rf);
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
                            materialFields.Add(tgf);
                        }
                        else
                        {
                            MaterialFloatField ff = new MaterialFloatField();
                            ff.Initialize(prop, shaderIdx);
                            unknownProps.Add(ff);
                            materialFields.Add(ff);
                        }
                        break;
                    case (MaterialProperty.PropType.Int):
                        MaterialIntField inf = new MaterialIntField();
                        inf.Initialize(prop, shaderIdx);
                        unknownProps.Add(inf);
                        materialFields.Add(inf);
                        break;
                }
            }
            
            if (hasUnknown)
            {
                MainWindow.Add(unknownProps);
            }
#endregion // Unknown Properties

            if (hasAdvancedProps)
            {
                Texture2D advancedIcon = ShaderGUIUtils.GetClosestUnityIconMip("console.warnicon.sml", 16);
                ShaderGUIUtils.SetHeaderStyle(advancedProps, "Advanced", advancedIcon);
                advancedProps.value = false;
                MainWindow.Add(advancedProps);
            }

            return root;
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

        void SetZOffsetWarningVisibility<T>(ChangeEvent<T> evt) where T : struct, IEquatable<T>
        {
            //Debug.Log($"Change Event? {evt.previousValue}, {evt.newValue}, {default(T)}");

            // Unity's .NET isn't new enough to have INumber<T> so abuse the fact that we only need to compare against 0, which is the default value
            if (!evt.previousValue.Equals(default) && !evt.newValue.Equals(default)) return;
            if (evt.previousValue.Equals(default) && !evt.newValue.Equals(default))
            {
                ZOffsetWarningCount += 1;
                ZOffsetWarning.style.display = DisplayStyle.Flex;
            }
            if (!evt.previousValue.Equals(default) && evt.newValue.Equals(default))
            {
                ZOffsetWarningCount -= 1;
                ZOffsetWarning.style.display = ZOffsetWarningCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        void SetDetailScaleWarningVisibility(ChangeEvent<float> evt)
        {
            //Debug.Log($"Change Event? {evt.previousValue}, {evt.newValue}, {default(T)}");

            // Unity's .NET isn't new enough to have INumber<T> so abuse the fact that we only need to compare against 0, which is the default value
            if (evt.previousValue != 1.0f && evt.newValue != 1.0f) return;
            if (evt.previousValue == 1.0f && evt.newValue != 1.0f)
            {
                DetailScaleWarning.style.display = DisplayStyle.Flex;
            }
            if (evt.previousValue != 1.0f && evt.newValue == 1.0f)
            {
                DetailScaleWarning.style.display = DisplayStyle.None;
            }
        }

        void EnableDetailScaleWarning()
        {
            DetailScaleWarning.style.display = DisplayStyle.Flex;
        }

#region PropertyUpgrades

        interface IMigrateProp
        {
            public PName Binding();
            public void Populate(SerializedProperty array, int index, PName binding);
            public void Migrate(MaterialProperty newProp);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct TexEnv : IMigrateProp
        {
            public PName binding;
            public Texture texture;
            public float4 scaleOffset;

            public readonly PName Binding() => binding;

            public void Populate(SerializedProperty array, int index, PName binding)
            {
                SerializedProperty textureProp = array.FindPropertyRelative($"Array.data[{index}].second.m_Texture");
                SerializedProperty scaleProp   = array.FindPropertyRelative($"Array.data[{index}].second.m_Scale");
                SerializedProperty offsetProp  = array.FindPropertyRelative($"Array.data[{index}].second.m_Offset");
                this.binding = binding;
                this.texture = (Texture) textureProp.objectReferenceValue;
                this.scaleOffset = math.float4(scaleProp.vector2Value, offsetProp.vector2Value);
            }

            public void Migrate(MaterialProperty newProp)
            {
                newProp.textureValue = texture;
                newProp.textureScaleAndOffset = scaleOffset;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct FloatEnv : IMigrateProp
        {
            public PName binding;
            public float value;

            public readonly PName Binding() => binding;

            public void Populate(SerializedProperty array, int index, PName binding)
            {
                SerializedProperty prop = array.FindPropertyRelative($"Array.data[{index}].second");
                this.binding = binding;
                this.value = prop.floatValue;
            }

            public void Migrate(MaterialProperty newProp)
            {
                newProp.floatValue = value;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IntEnv : IMigrateProp
        {
            public PName binding;
            public int value;

            public readonly PName Binding() => binding;

            public void Populate(SerializedProperty array, int index, PName binding)
            {
                SerializedProperty prop = array.FindPropertyRelative($"Array.data[{index}].second");
                this.binding = binding;
                this.value = prop.intValue;
            }

            public void Migrate(MaterialProperty newProp)
            {
                newProp.intValue = value;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct VectorEnv : IMigrateProp
        {
            public PName binding;
            public Vector4 value;

            public readonly PName Binding() => binding;

            public void Populate(SerializedProperty array, int index, PName binding)
            {
                SerializedProperty prop = array.FindPropertyRelative($"Array.data[{index}].second");
                this.binding = binding;
                this.value = prop.colorValue; // Vectors saved as colors!
            }

            public void Migrate(MaterialProperty newProp)
            {
                if (newProp.type == MaterialProperty.PropType.Vector)
                {
                    newProp.vectorValue = value;
                }
                else
                {
                    newProp.colorValue = value;
                }
            }
        }

        static bool UpgradeObsoleteMaterialProps(SerializedObject target, MaterialProperty[] props, ShaderPropertyTable propTable,
            Dictionary<string, PName> upgradeTextures,
            Dictionary<string, PName> upgradeVectors,
            Dictionary<string, PName> upgradeFloats,
            Dictionary<string, PName> upgradeInts
        )
        {
            bool needsReload = false;
            if (upgradeTextures != null) needsReload |= UpgradePropertySingle<TexEnv>   (target, props, propTable, upgradeTextures, "m_SavedProperties.m_TexEnvs");
            if (upgradeVectors != null)  needsReload |= UpgradePropertySingle<VectorEnv>(target, props, propTable, upgradeVectors,  "m_SavedProperties.m_Colors");
            if (upgradeFloats != null)   needsReload |= UpgradePropertySingle<FloatEnv> (target, props, propTable, upgradeFloats,   "m_SavedProperties.m_Floats");
            if (upgradeInts != null)     needsReload |= UpgradePropertySingle<IntEnv>   (target, props, propTable, upgradeInts,     "m_SavedProperties.m_Ints");

            return needsReload;
        }

        static bool UpgradePropertySingle<TProp>(
            SerializedObject target, 
            MaterialProperty[] props, 
            ShaderPropertyTable propTable, 
            Dictionary<string, PName> upgradeMap,
            string propertyArrayPath
            ) where TProp : IMigrateProp
        {
            int upgradableCount = 0;
            int obsoletePropCount = upgradeMap.Count;

            foreach (KeyValuePair<string,PName> pair in upgradeMap)
            {
                if (PropertyIdx(ref propTable, pair.Value) != -1)
                {
                    upgradableCount++;
                }
            }

            bool removedElements = false;
            if (upgradableCount > 0)
            {
                List<TProp> oldValues = new List<TProp>(upgradableCount);
                List<int> removeElements = new List<int>(upgradableCount);
                SerializedProperty propertyArray = target.FindProperty(propertyArrayPath);

                int arrayLength = propertyArray.arraySize;
                for (int i = 0; i < arrayLength; i++)
                {
                    SerializedProperty subProp = propertyArray.FindPropertyRelative($"Array.data[{i}].first");
                    string name = subProp.stringValue;
                    if (upgradeMap.TryGetValue(name, out PName pName))
                    {
                        
                        SerializedProperty texture = propertyArray.FindPropertyRelative($"Array.data[{i}].second.m_Texture");
                        SerializedProperty scale = propertyArray.FindPropertyRelative($"Array.data[{i}].second.m_Scale");
                        SerializedProperty offset = propertyArray.FindPropertyRelative($"Array.data[{i}].second.m_Offset");
                        TProp migrateProp = default;
                        migrateProp.Populate(propertyArray, i, pName);
                        oldValues.Add(migrateProp);
                        removeElements.Add(i);
                    }
                }

                foreach (TProp migrateProp in oldValues)
                {
                    int propIdx = PropertyIdx(ref propTable, migrateProp.Binding());
                    migrateProp.Migrate(props[propIdx]);
                }
                target.Update();
                int numRemove = removeElements.Count();
                removedElements = numRemove > 0;
                for (int rIdx = numRemove - 1; rIdx >= 0; rIdx--)
                {
                    propertyArray.DeleteArrayElementAtIndex(removeElements[rIdx]);
                }
                target.ApplyModifiedProperties();
            }
            return removedElements;
        }

        void RebuildOnAdvVisChange(bool b)
        {
            this.RebuildUI();
        }

        void UnregisterVisChangeOnDetach(DetachFromPanelEvent evt)
        {
            AdvancedMaterialProps.stateChangeCallback -= RebuildOnAdvVisChange;
        }


        VisualElement LayeredTextureField(ref Span<int> baseMapIdxs, MaterialProperty[] props, int[] propIdx, string label, ReadOnlySpan<char> tooltip)
        {
            VisualElement baseMaps = new VisualElement();
            baseMaps.style.flexDirection = FlexDirection.Row;
            baseMaps.style.alignContent = Align.Center;
            baseMaps.style.alignItems = Align.Center;
            baseMaps.style.marginBottom = 0;
            baseMaps.style.marginTop = 0;
            //baseMaps.style.height = 36;
            baseMaps.AddToClassList("unity-base-field");
            baseMaps.AddToClassList("unity-base-field__inspector-field");
            VisualElement leftAlignBox = new VisualElement();
            leftAlignBox.AddToClassList("layeredMaterialGUILeftBox");
            baseMaps.Add(leftAlignBox);
            Label baseMapLabel = new Label(label);
            baseMapLabel.AddToClassList("unity-base-field__label");
            baseMapLabel.AddToClassList("unity-base-text-field__label");
            leftAlignBox.Add(baseMapLabel);
            VisualElement rightAlignBox = new VisualElement();
            rightAlignBox.AddToClassList("layeredMaterialGUIRightBox");
            rightAlignBox.style.flexDirection = FlexDirection.Row;
            rightAlignBox.style.justifyContent = Justify.SpaceBetween;
            baseMaps.Add(rightAlignBox);
            for (int i = 0; i < 5; i++)
            {
                TextureField baseMapXField = new TextureField(props[baseMapIdxs[i]], propIdx[baseMapIdxs[i]], false, null, 48);
                if ((i & 1) != 1)
                {
                    baseMapXField.AddToClassList("layeredMaterialAltBackground");
                }
                baseMapXField.tooltip2 = tooltip.ToString();
                baseMapXField.label.style.display = DisplayStyle.None;
                baseMapXField.rightAlignBox.style.display = DisplayStyle.None;
                baseMapXField.style.flexBasis  = 1.0f / 5.0f;
                baseMapXField.style.flexGrow   = 1.0f / 5.0f;
                baseMapXField.style.flexShrink = 1.0f / 5.0f;
                baseMapXField.ElementAt(0).style.alignContent = Align.Center;
                baseMapXField.ElementAt(0).style.justifyContent = Justify.Center;
                baseMapXField.leftAlignBox.style.flexGrow = 1.0f;
                rightAlignBox.Add(baseMapXField);
                materialFields.Add(baseMapXField);
            }
            return baseMaps;
        }

        VisualElement LayeredHeader()
        {
            VisualElement root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignContent = Align.Center;
            root.style.alignItems = Align.Center;
            root.style.marginBottom = 0;
            root.style.marginTop = 0;            
            //baseMaps.style.height = 36;
            root.AddToClassList("unity-base-field");
            root.AddToClassList("unity-base-field__inspector-field");
            VisualElement leftAlignBox = new VisualElement();
            leftAlignBox.AddToClassList("layeredMaterialGUILeftBox");
            root.Add(leftAlignBox);

            VisualElement rightAlignBox = new VisualElement();
            rightAlignBox.AddToClassList("layeredMaterialGUIRightBox");
            rightAlignBox.style.flexDirection = FlexDirection.Row;
            rightAlignBox.style.justifyContent = Justify.SpaceBetween;
            root.Add(rightAlignBox);
            for (int i = 0; i < 5; i++)
            {
                Label labelX = new Label("Layer " + i);
                if ((i & 1) != 1)
                {
                    labelX.AddToClassList("layeredMaterialAltBackground");
                }
                labelX.style.unityFontStyleAndWeight = FontStyle.Bold;
                labelX.style.flexBasis  = 1.0f / 5.0f;
                labelX.style.flexGrow   = 1.0f / 5.0f;
                labelX.style.flexShrink = 1.0f / 5.0f;
                labelX.style.unityTextAlign = TextAnchor.MiddleCenter;
                rightAlignBox.Add(labelX);
            }
            return root;
        }
        
        VisualElement LayeredScaleOffsetField(ref Span<int> baseMapIdxs, MaterialProperty[] props, int[] propIdx, string label, ReadOnlySpan<char> tooltip)
        {
            VisualElement baseMaps = new VisualElement();
            baseMaps.style.flexDirection = FlexDirection.Row;
            baseMaps.style.alignContent = Align.Center;
            baseMaps.style.alignItems = Align.Center;
            baseMaps.style.marginBottom = 0;
            baseMaps.style.marginTop = 0;
            //baseMaps.style.height = 36;
            baseMaps.AddToClassList("unity-base-field");
            baseMaps.AddToClassList("unity-base-field__inspector-field");
            VisualElement leftAlignBox = new VisualElement();
            leftAlignBox.AddToClassList("layeredMaterialGUILeftBox");
            baseMaps.Add(leftAlignBox);
            Label baseMapLabel = new Label(label);
            baseMapLabel.AddToClassList("unity-base-field__label");
            baseMapLabel.AddToClassList("unity-base-text-field__label");
            leftAlignBox.Add(baseMapLabel);
            VisualElement rightAlignBox = new VisualElement();
            rightAlignBox.AddToClassList("layeredMaterialGUIRightBox");
            rightAlignBox.style.flexDirection = FlexDirection.Row;
            rightAlignBox.style.justifyContent = Justify.SpaceBetween;
            baseMaps.Add(rightAlignBox);
            for (int i = 0; i < 5; i++)
            {

                MaterialScaleOffsetField baseMapXField = new MaterialScaleOffsetField(props[baseMapIdxs[i]], propIdx[baseMapIdxs[i]], true);
                if ((i & 1) != 1)
                {
                    baseMapXField.AddToClassList("layeredMaterialAltBackground");
                }
                baseMapXField.style.flexBasis  = 1.0f / 5.0f;
                baseMapXField.style.flexGrow   = 1.0f / 5.0f;
                baseMapXField.style.flexShrink = 1.0f / 5.0f;
                FloatField offsetXInput = (FloatField) baseMapXField.offsetInput.ElementAt(0);
                offsetXInput.style.flexGrow   = 0.5f;
                offsetXInput.style.flexShrink = 0.5f;
                offsetXInput.style.flexBasis  = 0.5f;
                //offsetXInput.style.paddingRight = 4;
                //offsetXInput.RemoveAt(0);
                int grabWidth = 4;
                float labelFlexSize = 0.05f;
                offsetXInput.label = " ";
                //offsetXInput.labelElement.style.maxWidth = grabWidth;
                offsetXInput.labelElement.style.minWidth = grabWidth;
                offsetXInput.labelElement.style.flexGrow  = labelFlexSize;
                offsetXInput.labelElement.style.flexShrink= labelFlexSize;
                offsetXInput.labelElement.style.flexBasis = labelFlexSize;
                offsetXInput.labelElement.style.textOverflow = TextOverflow.Clip;

                FloatField offsetYInput = (FloatField) baseMapXField.offsetInput.ElementAt(1);
                offsetYInput.style.flexGrow   = 0.5f;
                offsetYInput.style.flexShrink = 0.5f;
                offsetYInput.style.flexBasis  = 0.5f;
                //offsetYInput.RemoveAt(0);
                offsetYInput.label = " ";
                //offsetYInput.labelElement.style.maxWidth = grabWidth;
                offsetYInput.labelElement.style.minWidth = grabWidth;
                offsetYInput.labelElement.style.flexGrow  = labelFlexSize;
                offsetYInput.labelElement.style.flexShrink= labelFlexSize;
                offsetYInput.labelElement.style.flexBasis = labelFlexSize;
                offsetYInput.labelElement.style.textOverflow = TextOverflow.Clip;

                FloatField tilingXInput = (FloatField) baseMapXField.tilingInput.ElementAt(0);
                tilingXInput.style.flexGrow   = 0.5f;
                tilingXInput.style.flexShrink = 0.5f;
                tilingXInput.style.flexBasis  = 0.5f;
                //tilingXInput.RemoveAt(0);
                tilingXInput.label = " ";
                //tilingXInput.labelElement.style.maxWidth = grabWidth;
                tilingXInput.labelElement.style.minWidth = grabWidth;
                tilingXInput.labelElement.style.flexGrow  = labelFlexSize;
                tilingXInput.labelElement.style.flexShrink= labelFlexSize;
                tilingXInput.labelElement.style.flexBasis = labelFlexSize;
                tilingXInput.labelElement.style.textOverflow = TextOverflow.Clip;

                FloatField tilingYInput = (FloatField) baseMapXField.tilingInput.ElementAt(1);
                tilingYInput.style.flexGrow   = 0.5f;
                tilingYInput.style.flexShrink = 0.5f;
                tilingYInput.style.flexBasis  = 0.5f;

                //tilingYInput.RemoveAt(0);
                tilingYInput.label = " ";
                //tilingYInput.labelElement.style.maxWidth = grabWidth;
                tilingYInput.labelElement.style.minWidth = grabWidth;
                tilingYInput.labelElement.style.flexGrow  = labelFlexSize;
                tilingYInput.labelElement.style.flexShrink= labelFlexSize;
                tilingYInput.labelElement.style.flexBasis = labelFlexSize;
                tilingYInput.labelElement.style.textOverflow = TextOverflow.Clip;

                rightAlignBox.Add(baseMapXField);
                materialFields.Add(baseMapXField);
            }
            return baseMaps;
        }

#endregion
    
    } // LitMASGui
} // namespace