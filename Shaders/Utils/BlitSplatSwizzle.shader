Shader "Hidden/BlitSplatSwizzle"
{
    Properties
    {
        _BlitScaleBias ("Blit Scale Bias", Vector) = (1,1,0,0)
        _BlitTexture ("Blit Texture", 2D) = "" {}
    }
    SubShader
    {
        Tags {"RenderPipeline" = "UniversalPipeline"  "RenderType" = "Opaque" "Queue" = "Overlay" }
        LOD 100

        HLSLINCLUDE
        #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
        ENDHLSL

        Pass
        {
            Name "Blit"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma editor_sync_compilation
            #pragma vertex Vert
            #pragma fragment ProgFrag

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"


            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingFullscreen.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            float4 _BlitTexture_TexelSize;

            float4x4 _BlitSplatSwizzle;

            float4 ProgFrag (Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 dim;
                _BlitTexture.GetDimensions(dim.x,dim.y);
                float4 colorIn = _BlitTexture.Sample(sampler_LinearClamp, uv);
                float4 colorOut = mul(_BlitSplatSwizzle, colorIn);
                return colorOut;
            }
            ENDHLSL
        }
    }
}
