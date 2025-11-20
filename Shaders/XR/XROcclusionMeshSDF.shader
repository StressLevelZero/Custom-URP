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
                #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"


                #pragma exclude_renderers d3d11_9x gles
                /*
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
                */
                Texture2DArray<float> _MaskTex;
                float4 _MaskTex_TexelSize;
                //float _StepCount;

                float2 Frag(Varyings i) : SV_Target
                {
                    //unity_StereoEyeIndex = i.rtArrayIndex;
                       UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                    float2 uv = i.texcoord;
                    float2 pixelCoords = uv;// * _MaskTex_TexelSize.zw;
                    float2 centerCoords = float2(0.5, 0.5);//_MaskTex_TexelSize.zw * 0.5;
                    float stepSize = 0.5;
                    float lastX = 0;
                    float2 lastCoords = pixelCoords;
                    float x = 0;

                    bool startsInside = (_MaskTex.SampleLevel(sampler_LinearClamp, float3(pixelCoords, unity_StereoEyeIndex), 0).r) > 0;
                    bool inside = startsInside;
                    if (startsInside)
                    {
                        pixelCoords = 2 * pixelCoords - 1.0;
                        float maxDim = max(abs(pixelCoords.x), abs(pixelCoords.y));
                        pixelCoords /= maxDim;
                        pixelCoords = 0.5 * pixelCoords + 0.5;
                        inside = (_MaskTex.SampleLevel(sampler_LinearClamp, float3(pixelCoords, unity_StereoEyeIndex), 0).r) > 0;
                    }
                    
                    for (float step = 0.0; step < 16.0; step++)
                    {
                        x = inside ? x - stepSize : x + stepSize;
                        //stepSize *= 0.5f;
                        float2 coords = (lerp(pixelCoords, centerCoords, x));
                        float maskValue = _MaskTex.SampleLevel(sampler_LinearClamp, float3(coords, unity_StereoEyeIndex), 0).r;
                        bool nowInside = (maskValue > 0) && (all(coords > 0) && all(coords < 1));
                        if (nowInside != inside) stepSize *= 0.5f;
                        inside = nowInside;
                        if (inside)
                        {
                            lastX = x;
                        }
                        else
                        {
                            lastCoords = coords;
                        }
                    }
                    
                    float distance = 1.0 - saturate(8 * length(lastCoords - uv));
                    //distance *= distance;
                    //distance = saturate(2.0 * distance - 1.0);

                    return startsInside ? float2(0, distance) : float2(lastX, 1);
                }

            ENDHLSL
        }
    }
    Fallback Off
}
