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
        const string keyword_DETAILS_UV_ON = "_DETAILS_UV_ON";
        const string keyword_BRDF = "_BRDFMAP";
        const string keyword_EXPENSIVE_TP = "_EXPENSIVE_TP";

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
        enum DetailsMode
        {
            Off = 0,
            Details = 1,
            DetailsUV = 2,
        }

        static readonly List<DetailsMode> k_DetailsModes = new()
        {
            DetailsMode.Off,
            DetailsMode.Details,
            DetailsMode.DetailsUV
        };

        static string FormatDetailsMode(DetailsMode m) => m switch
        {
            DetailsMode.Off => "Off",
            DetailsMode.Details => "Details (Fractal)",
            DetailsMode.DetailsUV => "Details (UV)",
            _ => m.ToString()
        };

        static DetailsMode GetDetailsMode(Material mat)
        {
            // If both are on (old/bad state), prefer UV.
            if (mat.IsKeywordEnabled(keyword_DETAILS_UV_ON)) return DetailsMode.DetailsUV;
            if (mat.IsKeywordEnabled(keyword_DETAILS_ON)) return DetailsMode.Details;
            return DetailsMode.Off;
        }

        static void ApplyDetailsMode(Material mat, DetailsMode mode)
        {
            switch (mode)
            {
                case DetailsMode.Off:
                    CoreUtils.SetKeyword(mat, keyword_DETAILS_ON, false);
                    CoreUtils.SetKeyword(mat, keyword_DETAILS_UV_ON, false);
                    mat.SetFloat("_Details", 0f); // keep your existing enable flag in sync (optional but handy)
                    break;

                case DetailsMode.Details:
                    CoreUtils.SetKeyword(mat, keyword_DETAILS_ON, true);
                    CoreUtils.SetKeyword(mat, keyword_DETAILS_UV_ON, false);
                    mat.SetFloat("_Details", 1f);
                    break;

                case DetailsMode.DetailsUV:
                    CoreUtils.SetKeyword(mat, keyword_DETAILS_ON, false);
                    CoreUtils.SetKeyword(mat, keyword_DETAILS_UV_ON, true);
                    mat.SetFloat("_Details", 1f);
                    break;
            }
        }

        HelpBox TransparentWarning;
        HelpBox AlphaClipWarning;
        int AlphaClipWarningCount;
        HelpBox ZOffsetWarning;
        int ZOffsetWarningCount;
        SurfaceTypeField surfaceTypeField;
        RenderQueueDropdown renderQueue;
        MaterialToggleField alphaClipToggle;

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();
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

            //----------------------------------------------------------------
            // Warning Messages ----------------------------------------------
            //----------------------------------------------------------------

            TransparentWarning = new HelpBox("Transparent materials are expensive on Quest, use sparingly!", HelpBoxMessageType.Warning);
            TransparentWarning.style.display = DisplayStyle.None;
            ZOffsetWarning = new HelpBox("Non-zero Z Offset slope/units. This will prevent this material from SRP batching with any other material that does not have precisely the same ZOffset values.", HelpBoxMessageType.Warning);
            ZOffsetWarning.style.display = DisplayStyle.None;
            AlphaClipWarning = new HelpBox("Opaque alpha clip materials are very expensive on Quest, prefer transparency if possible!", HelpBoxMessageType.Warning);
            AlphaClipWarning.style.display = DisplayStyle.None;

            MainWindow.Add(TransparentWarning);
            MainWindow.Add(AlphaClipWarning);
            MainWindow.Add(ZOffsetWarning);

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
                List<int> cullChoices = new List<int>() { (int)CullMode.Back, (int)CullMode.Front, (int)CullMode.Off};
                Dictionary<int, string> cullLabels = new Dictionary<int, string>() { { (int)CullMode.Back, "Front" }, { (int)CullMode.Front, "Back" }, { (int)CullMode.Off, "Both (EXPENSIVE)" } };

                MaterialIntPopup cullPopup = new MaterialIntPopup();
                cullPopup.label = "Rendered Side";
                cullPopup.Initialize(props[cullIdx], propIdx[cullIdx], cullChoices, cullLabels);
               
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

            // Triplanar options ---------------------------------------------

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

            // Base map tiling offset ----------------------------------------

            if (baseMapIdx != -1 && (propertyFlags(props[baseMapIdx]) & MaterialPropertyFlags.NoScaleOffset) == 0)
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

            //----------------------------------------------------------------
            // Detail Properties ---------------------------------------------
            //----------------------------------------------------------------

            Toggle detailToggle = null;
            Foldout detailProps = new Foldout();
           
            bool hasDetails = false;
            
            var detailsBody = new VisualElement();
            detailProps.tooltip = "Fractal texture sampling is effectively infinite textile density. UV is legacy behavior and should only be used if one fixed resolution or tiling is needed ";
            detailProps.Add(detailsBody); // everything that should be disabled goes in here
            int detailMapIdx = PropertyIdx(ref propTable, PName._DetailMap);
            if (detailMapIdx != -1)
            {
                TextureField detailsMapField = new TextureField(props[detailMapIdx], propIdx[detailMapIdx], false, shaderImporter?.GetDefaultTexture(props[detailMapIdx].name));
                detailsMapField.tooltip2 = LitMASGui_Tooltips.DetailMap.ToString();
                detailsBody.Add(detailsMapField);
                materialFields.Add(detailsMapField);
                hasDetails = true;

                MaterialScaleOffsetField detailScaleOffset = new MaterialScaleOffsetField(props[detailMapIdx], propIdx[detailMapIdx]);
                detailsBody.Add(detailScaleOffset);
                materialFields.Add(detailScaleOffset);
            }

            int detailToggleIdx = PropertyIdx(ref propTable, PName._Details);
            if (detailToggleIdx != -1 && hasDetails)
            {
                var mats = props[detailToggleIdx].targets.Cast<Material>().ToArray();

                DetailsMode first = GetDetailsMode(mats[0]);
                bool mixed = mats.Skip(1).Any(m => GetDetailsMode(m) != first);

                var detailsModePopup = new PopupField<DetailsMode>(
                    "Details Mode",
                    k_DetailsModes,
                    first,
                    FormatDetailsMode,
                    FormatDetailsMode
                );

                detailsModePopup.showMixedValue = mixed;

                // IMPORTANT: don't disable the foldout contentContainer (it would disable the popup)
                detailsBody.SetEnabled(mixed || first != DetailsMode.Off);

                detailsModePopup.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObjects(mats, "Change Details Mode");

                    foreach (var m in mats)
                    {
                        ApplyDetailsMode(m, evt.newValue);
                        EditorUtility.SetDirty(m);
                    }

                    detailsBody.SetEnabled(evt.newValue != DetailsMode.Off);
                });

                // Put popup above the disabled body
                detailProps.Insert(0, detailsModePopup);
            }


            if (hasDetails)
            {
                Texture2D detailIcon = ShaderGUIUtils.GetClosestUnityIconMip("Grid Icon", 16);
                ShaderGUIUtils.SetHeaderStyle(detailProps, "Details", detailIcon, detailToggle);
                MainWindow.Add(detailProps);
            }


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

            //----------------------------------------------------------------
            // Unknown Properties --------------------------------------------
            //----------------------------------------------------------------
            Foldout unknownProps = new Foldout();
            Texture2D otherIcon = ShaderGUIUtils.GetClosestUnityIconMip("Settings Icon", 16);
            ShaderGUIUtils.SetHeaderStyle(unknownProps, "Other", otherIcon);

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
            
            if (numUnknown > 0)
            {
                MainWindow.Add(unknownProps);
            }

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

    }



}