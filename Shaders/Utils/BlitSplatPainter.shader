Shader "Hidden/BlitSplatPainter"
{
    SubShader
    {
        Tags {"RenderPipeline" = "UniversalPipeline"  "RenderType" = "Opaque" "Queue" = "Overlay" }
        LOD 100

        HLSLINCLUDE
        #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags {"Lightmode"="UniversalForward"}
            Cull Off
            ZTest Off
            Conservative True
            HLSLPROGRAM
            
            #pragma vertex ProgVert
            #pragma fragment ProgFrag

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"


            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"



            struct VertexData
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv1          : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Interpolators
            {
                float4 positionCS      : SV_POSITION;
                float3 positionWS      : TEXCOORD0;
                float3 normalWS        : NORMAL;
                float2 uv1             : TEXCOORD1;
                float3 positionStencil : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Texture2D<half4> _SwapChain;
            SamplerState     sampler_SwapChain;

            Texture2D<half> _StencilMap;
            SamplerState    sampler_StencilMap;

            CBUFFER_START(BlitSplatCBuf)
            float4x4 _ObjectToWorld;
            float4x4 _WorldToObject;
            float4x4 _WorldToStencil;
            float _TargetChannel;
            float _TargetValue;
            float _FlowRate;
            float _Pad0;
            CBUFFER_END

            Interpolators ProgVert (VertexData v)
            {
                Interpolators o;
                UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.positionCS = float4(2.0*float2(v.uv1.x, 1.0 - v.uv1.y) - 1, 0.5, 1);

                o.positionWS = mul(_ObjectToWorld, float4(v.positionOS.xyz,1)).xyz;
                o.normalWS = SafeNormalize(mul(v.normalOS, (float3x3)_WorldToObject));

                o.positionStencil = mul(_WorldToStencil, float4(o.positionWS, 1.0));

                o.uv1 = v.uv1;

                return o;
            }

            half4 ProgFrag (Interpolators i) : SV_Target
            {
                //if (any(i.positionStencil.xy < 0) || any(i.positionStencil.xy > 1)) discard; // Discard/clip causes bug where the pixels right on the boundary that are supposed to be discarded write 0

                float2 stencilUV = i.positionStencil.xy;
                half stencil = _StencilMap.Sample(sampler_StencilMap, stencilUV).r;
                half strength = _FlowRate * stencil;
                if (any(i.positionStencil.xyz < 0) || any(i.positionStencil.xyz > 1)) strength = 0;
                half normalFalloff = sqrt(saturate(dot(-normalize(i.normalWS), normalize(_WorldToStencil._m20_m21_m22))) );
                strength *= normalFalloff;
                half zFalloff = i.positionStencil.z > 0.5 ? 2 * (i.positionStencil.z - 0.5) : 2 * (0.5 - i.positionStencil.z);
                zFalloff = half(1.0) - (zFalloff * zFalloff * zFalloff);
                strength *= zFalloff;
                half4 colorIn = _SwapChain.Load(int3(i.positionCS.xy, 0));
                int targetChannel = clamp((int)_TargetChannel, 0, 4);

                half inChannelWeights[5] = {colorIn.x, colorIn.y, colorIn.z, colorIn.w, max(0, half(1.0) - colorIn.x - colorIn.y - colorIn.z - colorIn.w)};
                half inTgtChannelWeight = inChannelWeights[targetChannel];
                
                if (targetChannel == 4)
                {
                   
                    [unroll] for (int cIdx = 0; cIdx < 4; cIdx++)
                    {
                        half targetValue = _TargetValue > 0.5 ? 0 : 1.0;
                        if (inChannelWeights[cIdx] > 0)
                        {
                            half targetValue = _TargetValue > 0.5 ? 0 : 2.0 * inChannelWeights[cIdx];
                            half outTgtChannelWeight = lerp(inChannelWeights[cIdx], targetValue, strength);
                            inChannelWeights[cIdx] = outTgtChannelWeight;
                        }
                    }
                } 
                else
                {
                    half outTgtChannelWeight = lerp(inTgtChannelWeight, _TargetValue, strength);
                    inChannelWeights[targetChannel] = outTgtChannelWeight;
                }
            
                half4 colorOut = half4(inChannelWeights[0],inChannelWeights[1],inChannelWeights[2],inChannelWeights[3]);
                colorOut = select(colorOut < (4.0/255), float4(0,0,0,0), colorOut);
                return colorOut;
            }
            ENDHLSL
        }
    }
}
