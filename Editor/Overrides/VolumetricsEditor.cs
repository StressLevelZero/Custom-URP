﻿using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [VolumeComponentEditor(typeof(Volumetrics))]
    sealed class VolumetricsEditor : VolumeComponentEditor
    {
        //      SerializedDataParameter m_Type;
        SerializedDataParameter m_MipStart;
        SerializedDataParameter m_MipEnd;
        SerializedDataParameter m_MipMax;
        SerializedDataParameter m_EnableFroxelVolumetrics;
        SerializedDataParameter m_HomogeneousDensity;
        //SerializedDataParameter m_Response;
        // SerializedDataParameter m_Texture;

        SerializedDataParameter m_FogDistance;
        SerializedDataParameter m_FogBaseHeight, m_FogMaxHeight;
        SerializedDataParameter m_MaxRenderDistance;
        SerializedDataParameter m_GlobalStaticLightMultiplier;
        SerializedDataParameter m_VolumetricAlbedo;
        //{
        //    var o = new PropertyFetcher<FilmGrain>(serializedObject);

        //    m_Type = Unpack(o.Find(x => x.type));
        //    m_Intensity = Unpack(o.Find(x => x.intensity));
        //    m_Response = Unpack(o.Find(x => x.response));
        //    m_Texture = Unpack(o.Find(x => x.texture));
        //}

        //public override void OnInspectorGUI()
        //{
        //    if (UniversalRenderPipeline.asset?.postProcessingFeatureSet == PostProcessingFeatureSet.PostProcessingV2)
        //    {
        //        EditorGUILayout.HelpBox(UniversalRenderPipelineAssetEditor.Styles.postProcessingGlobalWarning, MessageType.Warning);
        //        return;
        //    }

        //    PropertyField(m_Type);

        //    if (m_Type.value.intValue == (int)FilmGrainLookup.Custom)
        //    {
        //        PropertyField(m_Texture);

        //        var texture = (target as FilmGrain).texture.value;

        //        if (texture != null)
        //        {
        //            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;

        //            // Fails when using an internal texture as you can't change import settings on
        //            // builtin resources, thus the check for null
        //            if (importer != null)
        //            {
        //                bool valid = importer.mipmapEnabled == false
        //                    && importer.alphaSource == TextureImporterAlphaSource.FromGrayScale
        //                    && importer.filterMode == FilterMode.Point
        //                    && importer.textureCompression == TextureImporterCompression.Uncompressed
        //                    && importer.textureType == TextureImporterType.SingleChannel;

        //                if (!valid)
        //                    CoreEditorUtils.DrawFixMeBox("Invalid texture import settings.", () => SetTextureImportSettings(importer));
        //            }
        //        }
        //    }

        //    PropertyField(m_Intensity);
        //    PropertyField(m_Response);
        //}

        //static void SetTextureImportSettings(TextureImporter importer)
        //{
        //    importer.textureType = TextureImporterType.SingleChannel;
        //    importer.alphaSource = TextureImporterAlphaSource.FromGrayScale;
        //    importer.mipmapEnabled = false;
        //    importer.filterMode = FilterMode.Point;
        //    importer.textureCompression = TextureImporterCompression.Uncompressed;
        //    importer.SaveAndReimport();
        //    AssetDatabase.Refresh();
        //}
    }
}

