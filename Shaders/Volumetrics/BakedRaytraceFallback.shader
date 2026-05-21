Shader "SLZ/Volumetrics/BakedRaytraceFallback"
{
    Properties { }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        // Stub ForwardLit pass. The fallback material is only ever assigned to a renderer
        // for the brief window of AddInstance(), during which no rasterization happens —
        // so technically this pass never runs. We include it anyway because some Unity
        // editor flows (material inspector, shader stripping rules, validation passes)
        // will warn or misbehave on a material that has zero standard pipeline passes.
        // Cheaper to include the stub than to chase whatever warning shows up later.
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ColorMask 0
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            float4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "BakedRaytrace"
            Tags { "LightMode" = "BakedRaytrace" }
            HLSLPROGRAM
            #pragma raytracing surface_shader

            // Constant-output EvaluateMaterial — the whole reason this material is in the
            // BVH is that the original material's BakedRaytrace pass was broken or stripped,
            // so we deliberately ignore all material state. Returning constant matte
            // albedo + zero emission guarantees the BVH instance contributes correct
            // occlusion (the BLAS geometry is still there) without polluting the bake
            // with garbage radiance from an unbound texture sampler.
            //
            // 0.18 is the conventional middle-gray albedo for diffuse occluders. 
            // for a direct-only bake the value doesn't matter as long as emission is 0.
            #define MATERIAL_PROVIDES_EVALUATE
            void EvaluateMaterial(float2 hitUV, out float3 albedo, out float3 emission)
            {
                albedo   = float3(0.18, 0.18, 0.18);
                emission = 0;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/BakedRaytracePass.hlsl"
            ENDHLSL
        }
    }
}