Shader "Hidden/Universal Render Pipeline/Blit"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100

        Pass
        {
            Name "Blit"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Fragment
            #pragma multi_compile_fragment _ _LINEAR_TO_SRGB_CONVERSION
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #define  _RECONSTRUCT_VRS_TILES
            #pragma multi_compile_fragment _ _VRS_MASK_MODE
            #pragma multi_compile_fragment _ _BLEED_OCCLUSION_MASK

            // Core.hlsl for XR dependencies
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingFullscreen.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/RadialDensityMask.hlsl"

            SAMPLER(sampler_BlitTexture);

            float4 _BlitTexture_TexelSize;

            #ifdef _BLEED_OCCLUSION_MASK
            TEXTURE2D_X(_VrOccMeshDistance);
            #endif

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                #ifdef _BLEED_OCCLUSION_MASK
                    float distance = 2 * SAMPLE_TEXTURE2D_X_LOD(_VrOccMeshDistance, sampler_LinearClamp, uv, 0);
                    uv = lerp(uv, float2(0.5,0.5), distance);
                #endif
    
                half4 col = SampleScreenRDMCorrection(_BlitTexture, sampler_BlitTexture, _BlitTexture_TexelSize, uv);
                #ifdef _BLEED_OCCLUSION_MASK
                    if (distance > 0)
                    {
                        col.rgb = select(col.rgb > 1, sqrt(col.rgb), col.rgb);
                    }
                #endif

                #ifdef _LINEAR_TO_SRGB_CONVERSION
                col = LinearToSRGB(col);
                #endif

                #if defined(DEBUG_DISPLAY)
                half4 debugColor = 0;

                if(CanDebugOverrideOutputColor(col, uv, debugColor))
                {
                    return debugColor;
                }
                #endif

                return col;
            }
            ENDHLSL
        }
    }
}
