using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEditor.Rendering.Universal.ShaderGUI
{
    internal class LitDetailGUI
    {
        public static class Styles
        {
            public static readonly GUIContent detailInputs = EditorGUIUtility.TrTextContent("Detail Inputs",
                "These settings define the surface details by tiling and overlaying additional maps on the surface.");

            public static readonly GUIContent detailMaskText = EditorGUIUtility.TrTextContent("Mask",
                "Select a mask for the Detail map. The mask uses the alpha channel of the selected texture. The Tiling and Offset settings have no effect on the mask.");

            public static readonly GUIContent detailAlbedoMapText = EditorGUIUtility.TrTextContent("Detail Map",
                "(R) Desaturated albedo, (G) Normal Y, (B) Smoothness, (A) Normal X.");
            //  "Select the surface detail texture.The alpha of your texture determines surface hue and intensity.");

            public static readonly GUIContent detailNormalMapText = EditorGUIUtility.TrTextContent("Normal Map",
                "Designates a Normal Map to create the illusion of bumps and dents in the details of this Material's surface.");

            //public static readonly GUIContent detailAlbedoMapScaleInfo = EditorGUIUtility.TrTextContent("Setting the scaling factor to a value other than 1 results in a less performant shader variant.");
        }

        public struct LitProperties
        {
            // public MaterialProperty detailMask;
            public MaterialProperty detailMap;
            //public MaterialProperty detailAlbedoMapScale;
            //public MaterialProperty detailAlbedoMap;
            public MaterialProperty detailNormalMapScale;
            public MaterialProperty detailSmoothnessMapScale;
            //public MaterialProperty detailNormalMap;

            public LitProperties(MaterialProperty[] properties)
            {
                //  detailMask = BaseShaderGUI.FindProperty("_DetailMask", properties, false);
                detailMap = BaseShaderGUI.FindProperty("_DetailMap", properties, false);
                //detailAlbedoMapScale = BaseShaderGUI.FindProperty("_DetailAlbedoMapScale", properties, false);
               // detailAlbedoMap = BaseShaderGUI.FindProperty("_DetailAlbedoMap", properties, false);
                detailNormalMapScale = BaseShaderGUI.FindProperty("_DetailNormalMapScale", properties, false);
                detailSmoothnessMapScale = BaseShaderGUI.FindProperty("_DetailSmoothnessMapScale", properties, false);
                //  detailNormalMap = BaseShaderGUI.FindProperty("_DetailNormalMap", properties, false);
            }
        }

        public static void DoDetailArea(LitProperties properties, MaterialEditor materialEditor)
        {
           // materialEditor.TexturePropertySingleLine(Styles.detailMaskText, properties.detailMask);
            materialEditor.TexturePropertySingleLine(Styles.detailAlbedoMapText, properties.detailMap);
                //,properties.detailMap.textureValue != null ? properties.detailAlbedoMapScale : null);
            // if (properties.detailAlbedoMapScale.floatValue != 1.0f)
            // {
            //     EditorGUILayout.HelpBox(Styles.detailAlbedoMapScaleInfo.text, MessageType.Info, true);
            // }
            if (properties.detailMap.textureValue != null)
            {
                //   materialEditor.TexturePropertySingleLine(Styles.detailNormalMapText, properties.detailNormalMap,
                //        properties.detailNormalMap.textureValue != null ? properties.detailNormalMapScale : null);
                materialEditor.RangeProperty(properties.detailNormalMapScale, "Bump Scale");
                materialEditor.RangeProperty(properties.detailSmoothnessMapScale, "Smoothness Scale");
                materialEditor.TextureScaleOffsetProperty(properties.detailMap);
                //    materialEditor.TextureScaleOffsetProperty(properties.detailAlbedoMap);
            }
        }

        public static void SetMaterialKeywords(Material material)
        {
            if (material.HasProperty("_DetailMap") )//&& material.HasProperty("_DetailAlbedoMapScale"))
            {
                bool isScaled = false;//material.GetFloat("_DetailAlbedoMapScale") != 1.0f;
                bool hasDetailMap = material.GetTexture("_DetailMap");
                CoreUtils.SetKeyword(material, "_DETAIL_MULX2", !isScaled && hasDetailMap);
                CoreUtils.SetKeyword(material, "_NORMALMAP", hasDetailMap); //Forcing on normalmap to avoid a darkening bug with mixed directional lights :/
                //  CoreUtils.SetKeyword(material, "_DETAIL_SCALED", isScaled && hasDetailMap);
            }
        }
    }
}
