// Force reimport: 2
Shader "SLZ/LitMAS/LitMAS Standard"
{
    Properties
    {
        [ForceReload][MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("BaseColor", Color) = (1,1,1,1)
        [ToggleUI] _Normals("Normal Map enabled", Float) = 0
        [NoScaleOffset][Normal] _BumpMap ("Normal map", 2D) = "bump" {}
        [NoScaleOffset]_MetallicGlossMap("MAS", 2D) = "white" {}
        [Space(30)][Header(Emissions)][Space(10)][ToggleUI] _Emission("Emission Enable", Float) = 0
        [NoScaleOffset]_EmissionMap("Emission Map", 2D) = "white" {}
        [HDR]_EmissionColor("Emission Color", Color) = (1,1,1,1)
        _EmissionFalloff("Emission Falloff", Float) = 1
        _BakedMutiplier("Emission Baked Mutiplier", Float) = 1
        [Space(30)][Header(Details)][Space(10)][Toggle(_DETAILS_ON)] _Details("Details enabled", Float) = 0
        _DetailMap("Detail Map", 2D) = "gray" {}
        _DetailNormalScale("Detail Normal Scale", Float) = 1.0
        [Space(30)][Header(Screen Space Reflections)][Space(10)][Toggle(_SLZ_SSR_DISABLED)] _SSROff("Disable SSR", Float) = 0
        // SSR temporal accumulation, no longer used
        // [Header(This should be 0 for skinned meshes)]
        [HideInInspector]_SSRTemporalMul("Temporal Accumulation Factor", Range(0, 2)) = 1.0
        [Toggle(_ALPHATEST_ON)]_Alphatest("Alpha Clipping", float) = 0
        _Cutoff("Alpha Clip Threshold", Range(0,1)) = 0.5
        //[Toggle(_SM6_QUAD)] _SM6_Quad("Quad-avg SSR", Float) = 0

        _Surface ("Surface Type", float) = 0
        _BlendSrc ("Blend Source", float) = 1
        _BlendDst ("Blend Destination", float) = 0
        [ToggleUI] _ZWrite ("ZWrite", float) = 1

        _Cull ("Cull Side", float) = 2
        _HalfShade("Enable Vulkan Per-Draw Shading Rate Hack", float) = 0
        _Slope("Offset Slope Factor", float) = 0
        _Offset("Offset Units", float) = 0
        _SSRSmoothnessRange ("SSR Smoothness Range", Vector) = (0.4, 0.7, 0, 0)
        //_TransparencyLM("Base Map", 2D) = "white" {}

        [Toggle(_FLUORESCENCE)] _Fluorescent("Fluorescence", float) = 0
        _FluorMap("Fluorescence Map", 2D) = "white" {}
        _FluorColor("Fluorescence Color", Color) = (1,1,1,1)
        _FluorAbsorbance("Fluorescence Absorbance", Color) = (0,0.1875,0.929,1)
        _FluorAlbedoTint("Fluorescence Albedo Influence", Range( 0 , 1)) = 1
    }
    SubShader
    {
        Tags {"RenderPipeline" = "UniversalPipeline"  "RenderType" = "Opaque" "Queue" = "Geometry" }
        
        ZTest LEqual
        Offset [_Slope], [_Offset]
        LOD 100

        HLSLINCLUDE
        #define LITMAS_FORCE_REIMPORT 1
        #define CBUFFER_PATH "Packages/com.unity.render-pipelines.universal/Shaders/LitMAS/LitMASInclude/ShaderInjector/StandardCBuffer.hlsl"
        ENDHLSL

        Pass
        {
            Blend [_BlendSrc] [_BlendDst]
            ZWrite [_ZWrite]
            Cull [_Cull]
            Name "Forward"
            Tags {"Lightmode"="UniversalForward"}
            HLSLPROGRAM
            #define SLZ_ENABLE_PROFILING
            #pragma only_renderers vulkan
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0

            #define LITMAS_FEATURE_LIGHTMAPPING
            #define LITMAS_FEATURE_TS_NORMALS
            #define LITMAS_FEATURE_EMISSION
            #define LITMAS_FEATURE_SSR
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"

            //#pragma use_dxc vulkan

            #if defined(SHADER_API_DESKTOP) && !defined(_SLZ_SSR_DISABLED)
            //#pragma require WaveVote
            //#define _SM6_WAVE_VOTE 1

            // Do quad-averaging of the SSR results. 
            #pragma require QuadShuffle
            #define _SM6_QUAD 1
            #endif
            //#define _ADDITIONAL_LIGHTS
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/LitMAS/LitMASInclude/ShaderInjector/StandardForward.hlsl"

            ENDHLSL
        }

        Pass
        {

            Name "DepthOnly"
            Tags {"Lightmode"="DepthOnly"}
            ZWrite [_ZWrite]
            Cull [_Cull]
            ColorMask 0

            HLSLPROGRAM
            #pragma only_renderers vulkan
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/LitMAS/LitMASInclude/ShaderInjector/StandardDepthOnly.hlsl" 

            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags {"Lightmode" = "DepthNormals"}
            ZWrite [_ZWrite]
            Cull [_Cull]
            //ZTest Off
            //ColorMask 0

            HLSLPROGRAM
            #pragma only_renderers vulkan
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/LitMAS/LitMASInclude/ShaderInjector/StandardDepthNormals.hlsl" 
            ENDHLSL
        }

        Pass
        {
            
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite [_ZWrite]
            ZTest LEqual
            
            Cull [_Cull]
            ColorMask 0

            HLSLPROGRAM
            #pragma only_renderers vulkan
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/LitMAS/LitMASInclude/ShaderInjector/StandardShadowCaster.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }
            Blend [_BlendSrc] [_BlendDst]
            ZWrite [_ZWrite]
            Cull Off

            HLSLPROGRAM
            #pragma only_renderers vulkan
            #define _NORMAL_DROPOFF_TS 1
            #define _EMISSION
            #define _NORMALMAP 1

            #pragma vertex vert
            #pragma fragment frag

            #define SHADERPASS SHADERPASS_META
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/LitMAS/LitMASInclude/ShaderInjector/StandardMeta.hlsl" 
            ENDHLSL
        }

        Pass
        {
            
            Name "BakedRaytrace"
            Tags{ "LightMode" = "BakedRaytrace" }
            HLSLPROGRAM
            #pragma only_renderers vulkan
            #pragma multi_compile _ _EMISSION_ON
            #pragma use_dxc
            #if !defined(SHADER_API_D3D11)
            //#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/HLSL2021Support.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define SLZ_HLSL2021

            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature _DUMMY_VERT_FRAG

            #if _DUMMY_VERT_FRAG
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitMAS/LitMASInclude/ShaderInjector/StandardBakedRT.hlsl"
            #endif

            void vert()
            {
                
            }

            void frag()
            {
                
            }
            #endif


            ENDHLSL
        }
    }

 // Duplicate subshader for DX11, since using '#pragma require' automatically marks the whole subshader as invalid for dx11 even if its guarded by an API define
    SubShader
    {

        HLSLINCLUDE
        #define LITMAS_FORCE_REIMPORT 1
        #define CBUFFER_PATH "Packages/com.unity.render-pipelines.universal/Shaders/LitMAS/LitMASInclude/ShaderInjector/StandardCBuffer.hlsl"
        ENDHLSL

        Tags {"RenderPipeline" = "UniversalPipeline"  "RenderType" = "Opaque" "Queue" = "Geometry" }
        
        ZTest LEqual
        Offset 0 , 0
        ColorMask RGBA
        LOD 100

        HLSLINCLUDE
        //
        ENDHLSL

        Pass
        {
            Blend [_BlendSrc] [_BlendDst]
            ZWrite [_ZWrite]
            Cull [_Cull]
            Name "Forward"
            Tags {"Lightmode"="UniversalForward"}

            HLSLPROGRAM
            #pragma exclude_renderers vulkan
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0

            #define LITMAS_FEATURE_LIGHTMAPPING
            #define LITMAS_FEATURE_TS_NORMALS
            #define LITMAS_FEATURE_EMISSION
            #define LITMAS_FEATURE_SSR
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
           
            //#if defined(SHADER_API_DESKTOP)
            //#pragma require QuadShuffle
            //#define _SM6_QUAD 1
            //#endif

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/LitMAS/LitMASInclude/ShaderInjector/StandardForward.hlsl"

            ENDHLSL
        }

        Pass
        {

            Name "DepthOnly"
            Tags {"Lightmode"="DepthOnly"}
            ZWrite [_ZWrite]
            Cull [_Cull]
            ColorMask 0

            HLSLPROGRAM
            #pragma exclude_renderers vulkan
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/LitMAS/LitMASInclude/ShaderInjector/StandardDepthOnly.hlsl" 
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags {"Lightmode" = "DepthNormals"}
            ZWrite [_ZWrite]
            Cull [_Cull]
            //ZTest Off
            //ColorMask 0

            HLSLPROGRAM
            #pragma exclude_renderers vulkan
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/LitMAS/LitMASInclude/ShaderInjector/StandardDepthNormals.hlsl" 
            ENDHLSL
        }

        Pass
        {
            
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite [_ZWrite]
            ZTest LEqual
            
            Cull [_Cull]
            ColorMask 0

            HLSLPROGRAM
            #pragma exclude_renderers vulkan
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/LitMAS/LitMASInclude/ShaderInjector/StandardShadowCaster.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }
            Blend [_BlendSrc] [_BlendDst]
            ZWrite [_ZWrite]
            Cull Off

            HLSLPROGRAM
            #pragma exclude_renderers vulkan
            #define _NORMAL_DROPOFF_TS 1
            #define _EMISSION
            #define _NORMALMAP 1

            #pragma vertex vert
            #pragma fragment frag

            #define SHADERPASS SHADERPASS_META
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/LitMAS/LitMASInclude/ShaderInjector/StandardMeta.hlsl" 
            ENDHLSL
        }

        Pass
        {
            
            Name "BakedRaytrace"
            Tags{ "LightMode" = "BakedRaytrace" }
            HLSLPROGRAM
            #pragma exclude_renderers vulkan
            #pragma multi_compile _ _EMISSION_ON
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/LitMAS/LitMASInclude/ShaderInjector/StandardBakedRT.hlsl"

            ENDHLSL
        }
    }

    CustomEditor "LitMASGUI"
    //CustomEditor "UnityEditor.ShaderGraphLitGUI"
    //Fallback "Hidden/InternalErrorShader"
}
