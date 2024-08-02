Shader "SLZ/Decal Simple Lit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [HDR] _EmissionColor("Emission Color (RGB), Albedo Multiplier (A)", Color) = (0,0,0,1)
        _BakedMultiplier("Emission Baked Multiplier", Float) = 1.0
        [MainColor] _Color ("Baking Transparency Multiplier (A) (RGB unused)", Color) = (1, 1, 1, 0)
        [HalfRateSlope]_Slope("Z Slope Factor", Float) = -1.0008148
        _Offset("Z Offset", Float) = -1
        //[HideInInspector]_TransparencyLM ("Lightmapping Transmission Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "ForceNoShadowCasting"="True" "PreviewType"="Plane"}
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Offset [_Slope], [_Offset]

        Pass
        {
            Name "Forward"
            Tags {"Lightmode" = "UniversalForward"}
           

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _VOLUMETRICS_ENABLED

            #define SLZ_NO_SPECULAR
            #define SLZ_DISABLE_BAKED_SPEC

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DefaultLitVariants.hlsl"

            #pragma skip_variants _REFLECTION_PROBE_BOX_PROJECTION
            #pragma skip_variants DYNAMICLIGHTMAP_ON
            #pragma skip_variants DIRLIGHTMAP_COMBINED

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZLighting.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float4 uv01 : TEXCOORD0;
                float4 wPos_fog : TEXCOORD1;
                float3 wNormal : TEXCOORD2;
                float3 vtxLighting : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _EmissionColor;
                float4 _BaseMap_ST;
                float _BakedMultiplier;
            CBUFFER_END

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.wPos_fog.xyz = TransformObjectToWorld(v.vertex.xyz);
                o.positionCS = TransformWorldToHClip(o.wPos_fog.xyz);

                o.uv01.xy = TRANSFORM_TEX(v.uv, _BaseMap);
                OUTPUT_LIGHTMAP_UV(v.uv1.xy, unity_LightmapST, o.uv01.zw);
                half clipZ_0Far = UNITY_Z_0_FAR_FROM_CLIPSPACE(o.positionCS.z);
                o.wPos_fog.w = unity_FogParams.x * clipZ_0Far;

                o.wNormal = TransformObjectToWorldNormal(v.normal);
                o.vtxLighting = VertexLighting(o.wPos_fog.xyz, o.wNormal);

                #if !defined(LIGHTMAP_ON) && defined(SHADER_API_MOBILE)
	            o.vtxLighting += SampleSHVertex(o.wNormal.xyz);
                #endif

                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                half4 albedoAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv01.xy);
                half3 emission = _EmissionColor.rgb * lerp(1, albedoAlpha.rgb, _EmissionColor.a);
                albedoAlpha *= _BaseColor;

                SLZFragData fragData = SLZGetFragData(i.positionCS, i.wPos_fog, i.wNormal, i.uv01.zw, half2(0,0), i.vtxLighting);
                SLZSurfData surfData = SLZGetSurfDataMetallicGloss(albedoAlpha.rgb, 0, 0, 1, emission, albedoAlpha.a);

                half4 color = SLZPBRFragment(fragData, surfData, 2);
              
                // apply fog
                float3 viewDir = i.wPos_fog.xyz - _WorldSpaceCameraPos;

                color = MixFogSurf(color, -fragData.viewDir, i.wPos_fog.w, 2);
		        color = VolumetricsSurf(color, i.wPos_fog.xyz, 2);

                return color;
            }
            ENDHLSL
        }


        Pass
        {
            Name "Meta"
            Tags {"Lightmode" = "Meta"}

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature EDITOR_VISUALIZATION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                #if defined(EDITOR_VISUALIZATION)
                float2 VizUV        : TEXCOORD1;
                float4 LightCoord   : TEXCOORD2;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _EmissionColor;
                float4 _BaseMap_ST;
                float _BakedMultiplier;
            CBUFFER_END

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityMetaVertexPosition(v.vertex.xyz, v.uv1, v.uv2);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                #if defined(EDITOR_VISUALIZATION)
                    UnityEditorVizData(v.vertex.xyz, v.uv, v.uv1, v.uv2, o.VizUV, o.LightCoord);
                #endif
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) ;
                

                MetaInput metaInput = (MetaInput)0; //Initialize the struct's memory to 0 so the compiler doesn't complain
                metaInput.Albedo = col.rgb * _BaseColor;
                metaInput.Emission = _EmissionColor.rgb * lerp(1, col.rgb, _EmissionColor.a) * col.a * _BakedMultiplier;
                //metaInput.Emission = col.rgb; // use this instead if you want the shader to not just block light
                
                #ifdef EDITOR_VISUALIZATION
                    metaInput.VizUV = i.VizUV.xy;
                    metaInput.LightCoord = i.LightCoord;
                #endif

                return MetaFragment(metaInput);
            }
            ENDHLSL
        }
    }
    CustomEditor "UnityEditor.ShaderGraphLitGUI"
}