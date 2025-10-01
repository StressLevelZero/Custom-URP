Shader "Hidden/SLZ/XR/XROcclusionMeshDistance"
{
    Properties
    {
        //_MaskTex("XR Occlusion Mesh Mask", 2DArray) = "white" {}
        //_StepCount("Step Count", Float) = 32
    }


    SubShader
    {

        Tags{ "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite On ZTest Always Blend Off Cull Off
            // SLZ MODIFIED
            ColorMask RGBA // Switched from 0 so that the camera clear color doesn't show up on the occlusion mesh
            // END SLZ MODIFIED

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment Frag
                #pragma editor_sync_compilation
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

                #pragma exclude_renderers d3d11_9x gles
                #pragma multi_compile _ XR_OCCLUSION_MESH_COMBINED

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
                    float2 screenUV :TEXCOORD0;
                #if USE_XR_OCCLUSION_MESH_COMBINED_RT_ARRAY_INDEX
                    uint rtArrayIndex : SV_RenderTargetArrayIndex;
                #else
                    uint rtArrayIndex : TEXCOORD1;
                #endif
                };

                Varyings Vert(Attributes input)
                {
                    Varyings output;
                    output.vertex = float4(input.vertex.xy * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), UNITY_NEAR_CLIP_VALUE, 1.0f);
                    output.screenUV = float2(input.vertex.x, input.vertex.y);
                #if USE_XR_OCCLUSION_MESH_COMBINED_MULTIVIEW
                    if (unity_StereoEyeIndex != uint(input.vertex.z))
                    {
                        output.vertex = float4(0.0f, 0.0f, 0.0f, 0.0f);
                    }
                #elif USE_XR_OCCLUSION_MESH_COMBINED_RT_ARRAY_INDEX
                    output.rtArrayIndex = input.vertex.z;
                #else
                    output.rtArrayIndex = unity_StereoEyeIndex;
                #endif

                    return output;
                }

                Texture2DArray<float> _MaskTex;
                float4 _MaskTex_TexelSize;
                //float _StepCount;

                float Frag(Varyings i) : SV_Target
                {
                    unity_StereoEyeIndex = i.rtArrayIndex;
                    float2 pixelCoords = i.screenUV * _MaskTex_TexelSize.zw;
                    float2 centerCoords = _MaskTex_TexelSize.zw * 0.5;
                    float stepSize = 0.5;
                    float lastX = 0;
                    float x = 0;

                    bool inside = (_MaskTex.Load(int4(pixelCoords, i.rtArrayIndex, 0)).r) != 0;
                    
                    if (inside)
                    {
                        return 0;
                    }

                    for (float step = 0.0; step < 8.0; step++)
                    {
                        x = inside ? x - stepSize : x + stepSize;
                        stepSize *= 0.5f;
                        int2 coords = round(lerp(pixelCoords, centerCoords, x));
                        float maskValue = _MaskTex.Load(int4(coords, i.rtArrayIndex, 0)).r;
                        inside = (maskValue != 0);
                        if (inside)
                        {
                            lastX = x; 
                        }
                    }

                    //float distance = length( (centerCoords - float2(lastCoords)) / _MaskTex_TexelSize.zw );

                    return lastX;
                }

            ENDHLSL
        }
    }
    Fallback Off
}
