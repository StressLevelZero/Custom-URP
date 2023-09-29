// Force Re-import: 1
Shader "SLZ/LitMAS/LitMAS Triplanar"
{
    Properties
    {
        [ForceReload][Toggle(_EXPENSIVE_TP)] _Expensive("Fix Derivative Seams (expensive)", float) = 0
        [NoScaleOffset][MainTexture] _BaseMap("Texture", 2D) = "white" {}
        _UVScaler("Texture Scale", Float) = 1
        [MainColor] _BaseColor("BaseColor", Color) = (1,1,1,1)
        [ToggleUI] _RotateUVs("Rotate UVs", Float) = 0
        [ToggleUI] _Normals("Normal Map enabled", Float) = 0
        [NoScaleOffset][Normal] _BumpMap ("Normal map", 2D) = "bump" {}
        [NoScaleOffset]_MetallicGlossMap("MAS", 2D) = "white" {}
        [Space(30)][Header(Emissions)][Space(10)][ToggleUI] _Emission("Emission Enable", Float) = 0
        [NoScaleOffset]_EmissionMap("Emission Map", 2D) = "white" {}
        [HDR]_EmissionColor("Emission Color", Color) = (1,1,1,1)
        _EmissionFalloff("Emission Falloff", Float) = 1
        _BakedMutiplier("Emission Baked Mutiplier", Float) = 1
        [Space(30)][Header(Details)][Space(10)][Toggle(_DETAILS_ON)] _Details("Details enabled", Float) = 0
            [ToggleUI] _DetailsuseLocalUVs("Details use Local UVs", Float) = 0
        _DetailMap("DetailMap", 2D) = "gray" {}
                [Space(30)][Header(Screen Space Reflections)][Space(10)][Toggle(_NO_SSR)] _SSROff("Disable SSR", Float) = 0
        [Header(This should be 0 for skinned meshes)]
        _SSRTemporalMul("Temporal Accumulation Factor", Range(0, 2)) = 1.0

		_Surface ("Surface Type", float) = 0
		_BlendSrc ("Blend Source", float) = 1
		_BlendDst ("Blend Destination", float) = 0
		[ToggleUI] _ZWrite ("ZWrite", float) = 1
		_Cull ("Cull Side", float) = 2
      
    }
    SubShader
    {
        Tags {"RenderPipeline" = "UniversalPipeline"  "RenderType" = "Opaque" "Queue" = "Geometry" }
        //Blend One Zero
		//ZWrite On
		ZTest LEqual
		Offset 0 , 0
		ColorMask RGBA
        LOD 100

        Pass
        {
            Name "Forward"
            Tags {"Lightmode"="UniversalForward"}
            Blend [_BlendSrc] [_BlendDst]
			ZWrite [_ZWrite]
			Cull [_Cull]
            HLSLPROGRAM
            //#pragma use_dxc
            #pragma only_renderers vulkan
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0

            #define LITMAS_FEATURE_LIGHTMAPPING
            #define LITMAS_FEATURE_TP
            #define LITMAS_FEATURE_EMISSION
			#if defined(SHADER_API_DESKTOP)
			#pragma require WaveVote
			#pragma require QuadShuffle
			#define _SM6_QUAD 1
			#endif
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include_with_pragmas "LitMASInclude/ShaderInjector/TriplanarForward.hlsl"
            ENDHLSL
        }

		Pass
        {
            Name "DepthOnly"
            Tags {"Lightmode"="DepthOnly"}
			ZWrite [_ZWrite]
			Cull [_Cull]
			//ZTest Off
			ColorMask 0

            HLSLPROGRAM
            #pragma only_renderers vulkan
            
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include "LitMASInclude/DepthOnly.hlsl" 

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

            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include "LitMASInclude/ShaderInjector/TriplanarDepthNormals.hlsl" 

            ENDHLSL
        }

 		Pass
		{
			
			Name "ShadowCaster"
			Tags { "LightMode"="ShadowCaster" }

			ZWrite [_ZWrite]
			Cull Off
			ZTest LEqual
			AlphaToMask Off
			ColorMask 0

			HLSLPROGRAM
            #pragma only_renderers vulkan
			
			#pragma vertex vert
			#pragma fragment frag
            #pragma multi_compile _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include "LitMASInclude/ShadowCaster.hlsl" 

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
            #pragma shader_feature _ EDITOR_VISUALIZATION

            #define SHADERPASS SHADERPASS_META
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include "LitMASInclude/ShaderInjector/TriplanarMeta.hlsl" 
            ENDHLSL
        }

        Pass
		{
			
            Name "BakedRaytrace"
            Tags{ "LightMode" = "BakedRaytrace" }
			HLSLPROGRAM
            #pragma only_renderers vulkan

            #include "LitMASInclude/BakedRayTrace.hlsl"

            ENDHLSL
        }
    }

     // Duplicate subshader for DX11, since using '#pragma require' automatically marks the whole subshader as invalid for dx11 even if its guarded by an API define
    SubShader
    {
        Tags {"RenderPipeline" = "UniversalPipeline"  "RenderType" = "Opaque" "Queue" = "Geometry" }
        //Blend One Zero
		//ZWrite On
		ZTest LEqual
		Offset 0 , 0
		ColorMask RGBA
        LOD 100

        Pass
        {
            Name "Forward"
            Tags {"Lightmode"="UniversalForward"}
            Blend [_BlendSrc] [_BlendDst]
			ZWrite [_ZWrite]
			Cull [_Cull]
            HLSLPROGRAM
            #pragma exclude_renderers vulkan

            //#pragma use_dxc
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0

            #define LITMAS_FEATURE_LIGHTMAPPING
            #define LITMAS_FEATURE_TP
            #define LITMAS_FEATURE_EMISSION
			//#if defined(SHADER_API_DESKTOP) && defined(SHADER_API_VULKAN)
			//#pragma require QuadShuffle
			//#define _SM6_QUAD 1
			//#endif
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include_with_pragmas "LitMASInclude/ShaderInjector/TriplanarForward.hlsl"
            ENDHLSL
        }

		Pass
        {
            Name "DepthOnly"
            Tags {"Lightmode"="DepthOnly"}
			ZWrite [_ZWrite]
			Cull [_Cull]
			//ZTest Off
			ColorMask 0

            HLSLPROGRAM
            #pragma exclude_renderers vulkan            
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include "LitMASInclude/DepthOnly.hlsl" 

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
            #include "LitMASInclude/ShaderInjector/TriplanarDepthNormals.hlsl" 

            ENDHLSL
        }

 		Pass
		{
			
			Name "ShadowCaster"
			Tags { "LightMode"="ShadowCaster" }

			ZWrite [_ZWrite]
			Cull Off
			ZTest LEqual
			AlphaToMask Off
			ColorMask 0

			HLSLPROGRAM
            #pragma exclude_renderers vulkan			
			#pragma vertex vert
			#pragma fragment frag
            #pragma multi_compile _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include "LitMASInclude/ShadowCaster.hlsl" 

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
            #pragma shader_feature _ EDITOR_VISUALIZATION

            #define SHADERPASS SHADERPASS_META
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include "LitMASInclude/ShaderInjector/TriplanarMeta.hlsl" 
            ENDHLSL
        }

        Pass
		{
			
            Name "BakedRaytrace"
            Tags{ "LightMode" = "BakedRaytrace" }
			HLSLPROGRAM
            #pragma exclude_renderers vulkan
            #include "LitMASInclude/BakedRayTrace.hlsl"

            ENDHLSL
        }
    }

    CustomEditor "LitMASGUI"
    //CustomEditor "UnityEditor.ShaderGraphLitGUI"
    Fallback "Hidden/InternalErrorShader"
}
