Shader "Hidden/Universal Render Pipeline/XR/XROcclusionMeshDepthOnly"
{
    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        #pragma exclude_renderers d3d11_9x gles
        #pragma multi_compile _ XR_OCCLUSION_MESH_COMBINED
        #pragma multi_compile _ Y_FLIP

        // Not all platforms properly support SV_RenderTargetArrayIndex
        #if defined(SHADER_API_D3D11) || defined(SHADER_API_VULKAN) || defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES3) || defined(SHADER_API_PSSL)
            #if defined (UNITY_STEREO_MULTIVIEW_ENABLED)
                #define USE_XR_OCCLUSION_MESH_COMBINED_MULTIVIEW XR_OCCLUSION_MESH_COMBINED
            #else
                #define USE_XR_OCCLUSION_MESH_COMBINED_RT_ARRAY_INDEX XR_OCCLUSION_MESH_COMBINED
            #endif
        #endif

        struct Attributes
        {
            float4 vertex : POSITION;
        };

        struct Varyings
        {
            float4 vertex : SV_POSITION;

        #if USE_XR_OCCLUSION_MESH_COMBINED_RT_ARRAY_INDEX
            uint rtArrayIndex : SV_RenderTargetArrayIndex;
        #endif
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;

            float3 scaledVtx = TransformObjectToWorld(input.vertex);
            if (UNITY_MATRIX_M._m11 < 0)
            {
                scaledVtx.y += 1.0;
            }

            output.vertex = float4(scaledVtx.xy * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), UNITY_NEAR_CLIP_VALUE, 1.0f);            

        #if USE_XR_OCCLUSION_MESH_COMBINED_MULTIVIEW
            if (unity_StereoEyeIndex != uint(input.vertex.z))
            {
                output.vertex = float4(0.0f, 0.0f, 0.0f, 0.0f);
            }
        #elif USE_XR_OCCLUSION_MESH_COMBINED_RT_ARRAY_INDEX
            output.rtArrayIndex = input.vertex.z;
        #endif

            return output;
        }

        void Frag()
        {

        }

    ENDHLSL

    SubShader
    {
        Tags{ "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite On ZTest LEqual Blend Off Cull Off

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment Frag
            ENDHLSL
        }
    }
    Fallback Off
}
