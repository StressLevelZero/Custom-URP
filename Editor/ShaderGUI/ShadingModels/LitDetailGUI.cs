using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace UnityEditor.Rendering.Universal.ShaderGUI
{
    internal class LitDetailGUI
    {
        internal static class Styles
        {
            public static readonly GUIContent detailInputs = EditorGUIUtility.TrTextContent("Detail Inputs",
                "These settings define the surface details by tiling and overlaying additional maps on the surface.");

            
            public static readonly GUIContent detailMaskText = EditorGUIUtility.TrTextContent("Mask",
                "Select a mask for the Detail map. The mask uses the alpha channel of the selected texture. The Tiling and Offset settings have no effect on the mask.");

            // SLZ MODIFIED
            //public static readonly GUIContent detailAlbedoMapText = EditorGUIUtility.TrTextContent("Base Map",
            //    "Select the surface detail texture.The alpha of your texture determines surface hue and intensity.");

            public static readonly GUIContent detailAlbedoMapText = EditorGUIUtility.TrTextContent("Detail Map",
                "(R) Desaturated albedo, (G) Normal Y, (B) Smoothness, (A) Normal X.");

            // END SLZ MODIFIED

            public static readonly GUIContent detailNormalMapText = EditorGUIUtility.TrTextContent("Normal Map",
                "Designates a Normal Map to create the illusion of bumps and dents in the details of this Material's surface.");

            // SLZ MODIFIED
            //public static readonly GUIContent detailAlbedoMapScaleInfo = EditorGUIUtility.TrTextContent("Setting the scaling factor to a value other than 1 results in a less performant shader variant.");
            // END SLZ MODIFIED

            public static readonly GUIContent detailAlbedoMapFormatError = EditorGUIUtility.TrTextContent("This texture is not in linear space.");
        }

        public struct LitProperties
        {
            // SLZ MODIFIED

            //public MaterialProperty detailMask;
            //public MaterialProperty detailAlbedoMapScale;
            //public MaterialProperty detailAlbedoMap;
            public MaterialProperty detailNormalMapScale;
            //public MaterialProperty detailNormalMap;

            public MaterialProperty detailMap;
            public MaterialProperty detailSmoothnessMapScale;

            // END SLZ MODIFIED


            public LitProperties(MaterialProperty[] properties)
            {
                // SLZ MODIFIED

                //detailMask = BaseShaderGUI.FindProperty("_DetailMask", properties, false);
                //detailAlbedoMapScale = BaseShaderGUI.FindProperty("_DetailAlbedoMapScale", properties, false);
                //detailAlbedoMap = BaseShaderGUI.FindProperty("_DetailAlbedoMap", properties, false);
                detailNormalMapScale = BaseShaderGUI.FindProperty("_DetailNormalMapScale", properties, false);
                //detailNormalMap = BaseShaderGUI.FindProperty("_DetailNormalMap", properties, false);

                detailMap = BaseShaderGUI.FindProperty("_DetailMap", properties, false);
                detailSmoothnessMapScale = BaseShaderGUI.FindProperty("_DetailSmoothnessMapScale", properties, false);

                // END SLZ MODIFIED
            }
        }

        public static void DoDetailArea(LitProperties properties, MaterialEditor materialEditor)
        {
            // SLZ MODIFIED

            //materialEditor.TexturePropertySingleLine(Styles.detailMaskText, properties.detailMask);
            materialEditor.TexturePropertySingleLine(Styles.detailAlbedoMapText, properties.detailMap);
            //if (properties.detailAlbedoMapScale.floatValue != 1.0f)
            //{
            //    EditorGUILayout.HelpBox(Styles.detailAlbedoMapScaleInfo.text, MessageType.Info, true);
            //}
            //materialEditor.TexturePropertySingleLine(Styles.detailNormalMapText, properties.detailNormalMap,
            //    properties.detailNormalMap.textureValue != null ? properties.detailNormalMapScale : null);
            //materialEditor.TextureScaleOffsetProperty(properties.detailAlbedoMap);

            if (properties.detailMap.textureValue != null)
            {
                materialEditor.RangeProperty(properties.detailNormalMapScale, "Bump Scale");
                materialEditor.RangeProperty(properties.detailSmoothnessMapScale, "Smoothness Scale");
                materialEditor.TextureScaleOffsetProperty(properties.detailMap);
            }

            // END SLZ MODIFIED

            var detailAlbedoTexture = properties.detailMap.textureValue as Texture2D;
            if (detailAlbedoTexture != null && GraphicsFormatUtility.IsSRGBFormat(detailAlbedoTexture.graphicsFormat))
            {
                EditorGUILayout.HelpBox(Styles.detailAlbedoMapFormatError.text, MessageType.Warning, true);
            }

        }

        public static void SetMaterialKeywords(Material material)
        {
            // SLZ MODIFIED
            //if (material.HasProperty("_DetailAlbedoMap") && material.HasProperty("_DetailNormalMap") && material.HasProperty("_DetailAlbedoMapScale"))
            if (material.HasProperty("_DetailMap"))
            {
                bool isScaled = false; //material.GetFloat("_DetailAlbedoMapScale") != 1.0f;
                bool hasDetailMap = material.GetTexture("_DetailMap"); //material.GetTexture("_DetailAlbedoMap") || material.GetTexture("_DetailNormalMap");
                CoreUtils.SetKeyword(material, "_DETAIL_MULX2", !isScaled && hasDetailMap);
                if (hasDetailMap) CoreUtils.SetKeyword(material, "_NORMALMAP", true); //Forcing on normalmap to avoid a darkening bug with mixed directional lights :/
                //CoreUtils.SetKeyword(material, "_DETAIL_SCALED", isScaled && hasDetailMap);
            }
            // END SLZ MODIFIED
        }
    }
}
