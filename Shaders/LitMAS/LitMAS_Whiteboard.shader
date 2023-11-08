Shader "SLZ/LitMAS/LitMAS Whiteboard"
{
    Properties
    {
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("BaseColor", Color) = (1,1,1,1)
        _PenMap("Pen Texture", 2D) = "black" {}
        [ToggleUI] _PenMono("Pen Monochrome", float) = 0
        [HideInInspector]_PenMonoColor("Pen Monochrome Color", Color) = (0,0,0,1)
        [ToggleUI] _Normals("Normal Map enabled", Float) = 0
        [NoScaleOffset][Normal] _BumpMap ("Normal map", 2D) = "bump" {}
        [NoScaleOffset]_MetallicGlossMap("MAS", 2D) = "white" {}
        [Space(30)][Header(Details)][Space(10)][Toggle(_DETAILS_ON)] _Details("Details enabled", Float) = 0
        _DetailMap("DetailMap", 2D) = "gray" {}
       
        [HideInInspector]_Surface ("Surface Type", float) = 0
        [HideInInspector]_BlendSrc ("Blend Source", float) = 1
        [HideInInspector]_BlendDst ("Blend Destination", float) = 0
        [HideInInspector][ToggleUI] _ZWrite ("ZWrite", float) = 1
        [HideInInspector]_Cull ("Cull Side", float) = 2
    }
    SubShader
    {
        Tags {"RenderPipeline" = "UniversalPipeline"  "RenderType" = "Opaque" "Queue" = "Geometry" "DisableBatching"="True"}
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
            
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            
            #define LITMAS_FEATURE_LIGHTMAPPING
            #define LITMAS_FEATURE_TS_NORMALS
            #define LITMAS_FEATURE_WHITEBOARD

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include_with_pragmas "LitMASInclude/ShaderInjector/WhiteBoardForward.hlsl"

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
            #include "LitMASInclude/ShaderInjector/WhiteBoardDepthNormals.hlsl"
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
            #define _NORMAL_DROPOFF_TS 1
            #define _EMISSION
            #define _NORMALMAP 1

            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature _ EDITOR_VISUALIZATION

            #define SHADERPASS SHADERPASS_META
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include "LitMASInclude/ShaderInjector/WhiteBoardMeta.hlsl" 
            ENDHLSL
        }

        Pass
        {

            Name "BakedRaytrace"
            Tags{ "LightMode" = "BakedRaytrace" }
            HLSLPROGRAM

            #include "LitMASInclude/BakedRayTrace.hlsl"

            ENDHLSL
        }
    }


    //CustomEditor "LitMASGUI"
    CustomEditor "UnityEditor.LitMASIMGUI"
    //CustomEditor "UnityEditor.ShaderGraphLitGUI"
    Fallback "Hidden/InternalErrorShader"
}
