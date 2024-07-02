Shader "Universal Render Pipeline/Lit (PBR Workflow)"
{
    Properties
    {
        // Specular vs Metallic workflow
        //_WorkflowMode("WorkflowMode", Float) = 1.0 //Nope

        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)

        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        _Smoothness("Smoothness", Range(0.0, 1.0)) = 1.0 //defaulting to 1 instead of 0.5 because applying a texture will keep the default value as a scaler 
        // _SmoothnessTextureChannel("Smoothness texture channel", Float) = 0 //Unneeded variant

        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _MetallicGlossMap("Metallic", 2D) = "white" {}
        //We dont need Specular workflow. DELETED
        // _SpecColor("Specular", Color) = (0.2, 0.2, 0.2)
        // _SpecGlossMap("Specular", 2D) = "white" {}
        //Forcing this stuff off as it adds to the variant count and should rarely be disabled in a PBR world.  
        //[ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0
        //[ToggleOff] _EnvironmentReflections("Environment Reflections", Float) = 1.0

        _BumpScale("Scale", Float) = 1.0
        _BumpMap("Normal Map", 2D) = "bump" {}

        // _Parallax("Scale", Range(0.005, 0.08)) = 0.005 //too heavy for quest
        // _ParallaxMap("Height Map", 2D) = "black" {}

        //_OcclusionStrength("Strength", Range(0.0, 1.0)) = 1.0 //Meh. Not really needed. Just fix your texture
        //_OcclusionMap("Occlusion", 2D) = "white" {} //Packed together with metallic to match HDRP

        [HideInInspector] _EmissionEnabled ("Enable Emission", Float) = 0.0
        [HDR] _EmissionColor("Color", Color) = (0,0,0)
        _EmissionMap("Emission", 2D) = "white" {}

        //_DetailMask("Detail Mask", 2D) = "white" {} //Using the Blue ch in metallic map instead
        _DetailAlbedoMapScale("Scale", Range(0.0, 2.0)) = 1.0
        _DetailMap("Detail Albedo x2", 2D) = "linearGrey" {} //Renamed
        _DetailNormalMapScale("Scale", Range(0.0, 2.0)) = 1.0
        _DetailSmoothnessMapScale("_DetailSmoothnessMapScale", Range(0.0, 2.0)) = 1
        //[Normal] _DetailNormalMap("Normal Map", 2D) = "bump" {} 
        //converting to HDRP detailmaps instead https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@10.2/manual/Mask-Map-and-Detail-Map.html

        // SRP batching compatibility for Clear Coat (Not used in Lit)
        [HideInInspector] _ClearCoatMask("_ClearCoatMask", Float) = 0.0
        [HideInInspector] _ClearCoatSmoothness("_ClearCoatSmoothness", Float) = 0.0

        // Blending state
        _Surface("__surface", Float) = 0.0
        _Blend("__blend", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        [ToggleUI] _AlphaClip("__clip", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _AlphaPremultiplyEnabled("Alpha Premultiply", Float) = 0.0

         [ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0
        // Editmode props
        _QueueOffset("Queue offset", Float) = 0.0
        _OffsetUnits("OffsetUnits", Int) = 0.0
        _OffsetFactor("OffsetFactor", Int) = 0.0

        // ObsoleteProperties
        [HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
        [HideInInspector] _Color("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _GlossMapScale("Smoothness", Float) = 0.0
        [HideInInspector] _Glossiness("Smoothness", Float) = 0.0
        [HideInInspector] _GlossyReflections("EnvironmentReflections", Float) = 0.0

        [HideInInspector][NoScaleOffset]unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}

        // SLZ MODIFIED: Fix for dynamic_branch crashing lightmapper
        [HideInInspector] _EmissionMeta("WORKAROUND: Meta Pass Emission Toggle", Float) = 0
        // END SLZ MODIFIED
    }

    SubShader
    {
        // Universal Pipeline tag is required. If Universal render pipeline is not set in the graphics settings
        // this Subshader will fail. One can add a subshader below or fallback to Standard built-in to make this
        // material work with both Universal Render Pipeline and Builtin Unity Pipeline
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300


HLSLINCLUDE
#if !defined(SHADER_API_MOBILE)
#pragma use_dxc vulkan
#endif
ENDHLSL
        // ------------------------------------------------------------------
        //  Forward pass. Shades all light in a single pass. GI + emission + Fog
        Pass
        {
            // Lightmode matches the ShaderPassName set in UniversalRenderPipeline.cs. SRPDefaultUnlit and passes with
            // no LightMode tag are also rendered by Universal Render Pipeline
            Name "ForwardLit"
            Tags
            {
                    "LightMode" = "UniversalForward"
            }

            // -------------------------------------
            // Render State Commands
            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]

            ZWrite[_ZWrite]
            Cull[_Cull]
            Offset[_OffsetFactor] ,[_OffsetUnits]

            HLSLPROGRAM
            #pragma target 5.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment

            // -------------------------------------
            // Material Keywords
            #define _NORMALMAP // Just force this on
            //#pragma shader_feature_local _PARALLAXMAP
            // #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma multi_compile_local_fragment _ _DETAIL_MULX2
            //_DETAIL_SCALED
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #define DYNAMIC_ALPHAPREMULTIPLY_ON
            //#pragma multi_compile DUMMY _ALPHAPREMULTIPLY_ON
            //#pragma skip_variants DUMMY
            #pragma dynamic_branch_local _ALPHAPREMULTIPLY_ON

            #define DYNAMIC_ALPHAMODULATE_ON
            //#pragma multi_compile DUMMY _ALPHAMODULATE_ON
            //#pragma skip_variants DUMMY
            #pragma dynamic_branch_local _ALPHAMODULATE_ON
            
            #define DYNAMIC_EMISSION
            //#pragma multi_compile DUMMY _EMISSION
            //#pragma skip_variants DUMMY
            #pragma dynamic_branch_local _EMISSION
        
            
            #define DYNAMIC_METALLICSPECGLOSSMAP
            //#pragma multi_compile DUMMY _METALLICSPECGLOSSMAP
            //#pragma skip_variants DUMMY
            #pragma dynamic_branch_local  _METALLICSPECGLOSSMAP
            // #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            // #pragma shader_feature_local_fragment _OCCLUSIONMAP
            // #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            // #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
            // #pragma shader_feature_local_fragment _SPECULAR_SETUP

            // -------------------------------------
            // Universal Pipeline keywords

            //_MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _VOLUMETRICS_ENABLED
            //#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // -------------------------------------
            // Unity defined keywords
            
            #pragma multi_compile_fog
            #pragma skip_variants FOG_LINEAR FOG_EXP
            //#pragma multi_compile_fragment _ DEBUG_DISPLAY

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DefaultLitVariants.hlsl"

            //--------------------------------------
            // GPU Instancing

            #pragma multi_compile_instancing
            //#pragma instancing_options renderinglayer
            //#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags
            {
                    "LightMode" = "ShadowCaster"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM

            #pragma target 5.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            // -------------------------------------
            // Material Keywords
			
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            // #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

            //--------------------------------------
            // GPU Instancing

            #pragma multi_compile_instancing
            //#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Universal Pipeline keywords

            // This is used during shadow map generation to differentiate between directional and punctual light shadows, as they use different formulas to apply Normal Bias
            #define DYNAMIC_CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma dynamic_branch _CASTING_PUNCTUAL_LIGHT_SHADOW

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        //Pass
        //{
        //    // Lightmode matches the ShaderPassName set in UniversalRenderPipeline.cs. SRPDefaultUnlit and passes with
        //    // no LightMode tag are also rendered by Universal Render Pipeline
        //    Name "GBuffer"
        //    Tags
        //    {
        //        "LightMode" = "UniversalGBuffer"
        //    }
		//
        //    // -------------------------------------
        //    // Render State Commands
        //    ZWrite[_ZWrite]
        //    ZTest LEqual
        //    Cull[_Cull]
		//
        //    HLSLPROGRAM
        //    #pragma target 4.5
		//
        //    // Deferred Rendering Path does not support the OpenGL-based graphics API:
        //    // Desktop OpenGL, OpenGL ES 3.0, WebGL 2.0.
        //    #pragma exclude_renderers gles3 glcore
		//
        //    // -------------------------------------
        //    // Shader Stages
        //    #pragma vertex LitGBufferPassVertex
        //    #pragma fragment LitGBufferPassFragment
		//
        //    // -------------------------------------
        //    // Material Keywords
        //    #pragma shader_feature_local _NORMALMAP
        //    #pragma shader_feature_local_fragment _ALPHATEST_ON
        //    //#pragma shader_feature_local_fragment _ALPHAPREMULTIPLY_ON
        //    #pragma shader_feature_local_fragment _EMISSION
        //    #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
        //    #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
        //    #pragma shader_feature_local_fragment _OCCLUSIONMAP
        //    #pragma shader_feature_local _PARALLAXMAP
        //    #pragma shader_feature_local _ _DETAIL_MULX2 _DETAIL_SCALED
		//
        //    #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
        //    #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
        //    #pragma shader_feature_local_fragment _SPECULAR_SETUP
        //    #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
		//
        //    // -------------------------------------
        //    // Universal Pipeline keywords
        //    #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
        //    //#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
        //    //#pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
        //    #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
        //    #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
        //    #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
        //    #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
        //    #pragma multi_compile_fragment _ _RENDER_PASS_ENABLED
        //    #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
		//
        //    // -------------------------------------
        //    // Unity defined keywords
        //    #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
        //    #pragma multi_compile _ SHADOWS_SHADOWMASK
        //    #pragma multi_compile _ DIRLIGHTMAP_COMBINED
        //    #pragma multi_compile _ LIGHTMAP_ON
        //    #pragma multi_compile _ DYNAMICLIGHTMAP_ON
        //    #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
        //    #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
		//
        //    //--------------------------------------
        //    // GPU Instancing
        //    #pragma multi_compile_instancing
        //    #pragma instancing_options renderinglayer
        //    #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
		//
        //    // -------------------------------------
        //    // Includes
        //    #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
        //    #include "Packages/com.unity.render-pipelines.universal/Shaders/LitGBufferPass.hlsl"
        //    ENDHLSL
        //}
		
        Pass
        {
            Name "DepthOnly"
            Tags
            {
                    "LightMode" = "DepthOnly"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 5.0

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            //#pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            //#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        // This pass is used when drawing to a _CameraNormalsTexture texture
        Pass
        {
            Name "DepthNormals"
            Tags
            {
                    "LightMode" = "DepthNormals"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM

            #pragma exclude_renderers gles gles3 glcore
            #pragma target 5.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            // -------------------------------------
            // Material Keywords

            #define _NORMALMAP
            //#pragma shader_feature_local _PARALLAXMAP
            #pragma shader_feature_local _ _DETAIL_MULX2
            //_DETAIL_SCALED
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            // #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

            // -------------------------------------
            // Universal Pipeline keywords
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            //--------------------------------------
            // GPU Instancing

            #pragma multi_compile_instancing
            //#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
            ENDHLSL
        }

        // This pass it not used during regular rendering, only for lightmap baking.
        Pass
        {
            Name "Meta"
            Tags
            {
                    "LightMode" = "Meta"
            }

            // -------------------------------------
            // Render State Commands
            Cull Off

            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 5.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex UniversalVertexMeta
            #pragma fragment UniversalFragmentMetaLit

            #pragma multi_compile_instancing

            // -------------------------------------
            // Material Keywords
            
            // #pragma shader_feature_local_fragment _SPECULAR_SETUP
            //#define DYNAMIC_ALPHAPREMULTIPLY_ON
            //#pragma multi_compile DUMMY _ALPHAPREMULTIPLY_ON
            //#pragma skip_variants DUMMY
            //#pragma dynamic_branch_local _ALPHAPREMULTIPLY_ON

            //#define DYNAMIC_ALPHAMODULATE_ON
            //#pragma multi_compile DUMMY _ALPHAMODULATE_ON
            //#pragma skip_variants DUMMY
            //#pragma dynamic_branch_local _ALPHAMODULATE_ON
            
            
            //#pragma multi_compile_local _ _EMISSION_META

            #define DYNAMIC_EMISSION
            #define _EMISSION _EmissionMeta


            
            //#define DYNAMIC_METALLICSPECGLOSSMAP
            //#pragma multi_compile DUMMY _METALLICSPECGLOSSMAP
            //#pragma skip_variants DUMMY
            //#pragma dynamic_branch_local  _METALLICSPECGLOSSMAP

            //#pragma shader_feature_local_fragment _SPECGLOSSMAP
            #pragma shader_feature EDITOR_VISUALIZATION

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"


            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitMetaPass.hlsl"

            ENDHLSL
        }

        //Pass
        //{
        //      Name "Universal2D"
        //      Tags
        //      {
        //              "LightMode" = "Universal2D"
        //      }
        //
        //      // -------------------------------------
        //      // Render State Commands
        //      Blend[_SrcBlend][_DstBlend]
        //      ZWrite[_ZWrite]
        //      Cull[_Cull]
        //      #pragma target 2.0
        //
        //      // -------------------------------------
        //      // Shader Stages
        //      #pragma vertex vert
        //      #pragma fragment frag
        //
        //      // -------------------------------------
        //      // Material Keywords
        //      #pragma shader_feature_local_fragment _ALPHATEST_ON
        //      #pragma shader_feature_local_fragment _ALPHAPREMULTIPLY_ON
        //
        //      // -------------------------------------
        //      // Includes
        //      #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
        //      #include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/Universal2D.hlsl"
        //      ENDHLSL
        //}

        Pass
        {
            Name "BakedRaytrace"
            Tags{ "LightMode" = "BakedRaytrace" }
            HLSLPROGRAM

            #pragma target 5.0
            #pragma raytracing BakedRaytrace
            //#pragma multi_compile _EMISSION
            #define DYNAMIC_EMISSION
            #define _EMISSION _EmissionMeta
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/LitTracingPass.hlsl"

            ENDHLSL
        }        
    }
    CustomEditor "UnityEditor.Rendering.Universal.ShaderGUI.LitShader"
}
