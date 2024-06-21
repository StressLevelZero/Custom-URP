Shader"Hidden/ShaderGUITextureIconBlit"
{
    Properties
    {
            _Blit2D("2D Tex", 2D) = "gray" {}
            _Blit3D("3D Tex", 3D) = "gray" {}
            _Blit2DArray("2D Array Tex", 2DArray) = "gray" {}
            _BlitCube("Cube Tex", CUBE) = "gray" {}
            _BlitCubeArray("Cube Tex", CubeArray) = "gray" {}
            _BlitDim("Texture Dimensions", Vector) = (0,0,0,0)
            _BlitScaleBias("_BlitScaleBias", Vector) = (1,1,0,0)
    }  
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
            #pragma editor_sync_compilation

            #pragma multi_compile_local DIM_2D DIM_2DARRAY DIM_CUBE DIM_CUBEARRAY DIM_3D
            #pragma multi_compile_local _ NORMAL_MAP_AG NORMAL_MAP_RG

            // Core.hlsl for XR dependencies
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingFullscreen.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_Blit2D);
            TEXTURE3D(_Blit3D);
            TEXTURE2D_ARRAY(_Blit2DArray);
            TEXTURECUBE(_BlitCube);
            TEXTURECUBE_ARRAY(_BlitCubeArray);
            float4 _BlitDim;

            
            float getManualLOD(float2 uv, float2 texDims)
            {
                float2 dx = ddx(uv * texDims);
                float2 dy = ddy(uv * texDims);
                float maxDiv = max(length(dx),length(dy));
                return log2(maxDiv);
            }
            SAMPLER(blitsampler_TrilinearClamp);


            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 col = half4(0,0,0,0);
                #if defined(DIM_2D) 
                col = SAMPLE_TEXTURE2D(_Blit2D, blitsampler_TrilinearClamp, uv);
                #elif defined(DIM_2DARRAY)
                col = SAMPLE_TEXTURE2D_ARRAY(_Blit2DArray, blitsampler_TrilinearClamp, uv, 0);
                #elif defined(DIM_CUBE)
                col = SAMPLE_TEXTURECUBE_LOD(_BlitCube, blitsampler_TrilinearClamp, float3(uv.xy - 0.5, 0.5), 0);//getManualLOD(uv, _BlitDim.xy));
                #elif defined(DIM_CUBEARRAY)
                col = SAMPLE_TEXTURECUBE_ARRAY_LOD(_BlitCubeArray, blitsampler_TrilinearClamp, float3(uv.xy - 0.5, 0.5), 0, 0);// getManualLOD(uv, _BlitDim.xy));
                #elif defined(DIM_3D)
                col = SAMPLE_TEXTURE3D_LOD(_Blit3D, blitsampler_TrilinearClamp, float3(uv, 0.5), getManualLOD(uv, _BlitDim.xy));
                #endif
    
                #if defined(NORMAL_MAP_AG) || defined(NORMAL_MAP_RG)
                col.rgb = UnpackNormalmapRGorAG(col);
                col.g = -col.g;
                col.rgb = 0.5 * col.rgb + 0.5;
                col.rgb = pow(col.rgb, 2.2);
                #endif
                return float4(col.rgb, 1);
            }
            ENDHLSL
        }
    }
}
