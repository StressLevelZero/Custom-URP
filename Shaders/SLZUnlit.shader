Shader "SLZ/SLZ Unlit"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        [HDR]_BaseColor ("Color Tint", Color) = (1,1,1,1)
        
        [HideInInspector]_BlendMode ("Blend Mode", float) = 0
        [HideInInspector]_BlendSrc ("Blend Source", float) = 1
        [HideInInspector]_BlendDst ("Blend Destination", float) = 0
        [HideInInspector][ToggleUI] _ZWrite ("ZWrite", float) = 1
        [HideInInspector]_Cull ("Cull Side", float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry"}
        LOD 100

        Pass
        {
            Name "Forward"
            Tags {"Lightmode" = "UniversalForward"}

            ZWrite [_ZWrite]
            Blend [_BlendSrc] [_BlendDst]
            Cull [_Cull]

            HLSLPROGRAM

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #pragma target 5.0

            #pragma vertex vert
            #pragma fragment frag
           
            //#pragma multi_compile_instancing
            #pragma multi_compile _ FOG_EXP2
            #pragma multi_compile_fragment _ _VOLUMETRICS_ENABLED   

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float3 uv_fogFactor : TEXCOORD0;
                float3 wPos : TEXCOORD1;
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _BlendMode;
            CBUFFER_END

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.wPos = TransformObjectToWorld(v.vertex.xyz);
                o.vertex = TransformWorldToHClip(o.wPos);
                o.uv_fogFactor.xy = TRANSFORM_TEX(v.uv, _BaseMap);
                half clipZ_0Far = UNITY_Z_0_FAR_FROM_CLIPSPACE(o.vertex.z); // normalize the clipspace z-coordinate to 1 near 0 far for platforms that have a different range of clip coordinates
                o.uv_fogFactor.z = unity_FogParams.x * clipZ_0Far;
                return o;
            }

            /* @brief Blend volumetrics with control for the surface type.
             *
             * @param color       Final surface color
             * @param positionWS  World-space position of the fragment
             * @param surfaceType Enum of the surface type, where 0: opaque, 1: transparent (alpha premultiplied), 2: fade (alpha blend) 
             * @return color blended towards the volumetric color if the surface is opaque, or blended towards transparency otherwise
             */
            half4 VolumetricsBlend(half4 color, float3 positionWS, int blendMode) {
            
            #if defined(_VOLUMETRICS_ENABLED)
            
                half4 FroxelColor = GetVolumetricColor(positionWS);
            	
                FroxelColor.rgb = blendMode == 1 ? FroxelColor.rgb * color.a : FroxelColor.rgb;
                half3 fadeOutVal = blendMode == 4 ? 1.0.xxx : 0.0.xxx;
            	color.rgb = lerp(fadeOutVal,color.rgb, FroxelColor.a);
                if (blendMode < 3)
                {
            	    color.rgb += FroxelColor.rgb;
                }
            #endif
                return color;
            }

            half4 MixFogColorBlend(real4 fragColor, float3 viewDirectionWS, float fogFactor, int blendMode)
            {
            #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                half fogIntensity = ComputeFogIntensity(fogFactor);
            	
                half3 mipFog = MipFog(viewDirectionWS, fogFactor, 7 );
                if (blendMode == 1) // 1 = Transparent, which is actually alpha premultiplied.
                {
                    mipFog *= fragColor.a;
                }
            	
            	switch (blendMode)
            	{
            		case (3): mipFog = (0.0).xxx; break; // Additive - lerp to 0
            		case (4): mipFog = (1.0).xxx; break; // Multiplicative - lerp to 1
            		default: break;
            	}
            	
                fragColor.rgb = lerp(mipFog, fragColor.rgb, fogIntensity);
            #endif
                return fragColor;
            }

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv_fogFactor.xy);
                col *= _BaseColor;
                // apply fog
                float3 viewDir = i.wPos - _WorldSpaceCameraPos;

                col.rgb = MixFogColorBlend(col, viewDir, i.uv_fogFactor.z, _BlendMode);
                col = VolumetricsBlend(col, i.wPos, _BlendMode);

                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags {"Lightmode" = "DepthOnly"}

            ColorMask 0
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #pragma target 5.0
            //#pragma multi_compile_instancing
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wPos = TransformObjectToWorld(v.vertex.xyz);
                o.vertex = TransformWorldToHClip(wPos);

                return o;
            }

            void frag(v2f i)
            {

            }

            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags {"Lightmode" = "DepthNormals"}

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag 
            //#pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 wNormal : NORMAL;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 wPos = TransformObjectToWorld(v.vertex.xyz);
                o.vertex = TransformWorldToHClip(wPos);
                o.wNormal = TransformObjectToWorldNormal(v.normal, false);

                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                i.wNormal = normalize(i.wNormal);
                return half4(EncodeWSNormalForNormalsTex(i.wNormal), 0.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Shadowcaster"
            Tags {"Lightmode" = "Shadowcaster"}

            ColorMask 0
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #pragma target 5.0

            #pragma vertex vert
            #pragma fragment frag
            //#pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 _LightDirection;
            float3 _LightPosition;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wPos = TransformObjectToWorld(v.vertex.xyz);
                half3 wNorm = TransformObjectToWorldNormal(v.normal, true);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 wLightDir = normalize(_LightPosition - wPos);
                #else
                float3 wLightDir = _LightDirection;
                #endif
                o.vertex = ApplySLZShadowBias(wPos, wNorm, wLightDir);
                return o;
            }

            void frag(v2f i)
            {
              
            }

            ENDHLSL
        }

        Pass
        {
            Name "Meta"
            Tags {"Lightmode" = "Meta"}

            Blend [_BlendSrc] [_BlendDst]
			ZWrite [_ZWrite]
			Cull Off

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
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _BlendMode;
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
                half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                col *= _BaseColor;

                MetaInput metaInput = (MetaInput)0;
                metaInput.Albedo = 0;
                metaInput.Emission = col.rgb; 

                #ifdef EDITOR_VISUALIZATION
                    metaInput.VizUV = i.VizUV.xy;
                    metaInput.LightCoord = i.LightCoord;
                #endif

                return MetaFragment(metaInput);
            }
            ENDHLSL
        }

        Pass{
            Name "BakedRaytrace"
            Tags{ "LightMode" = "BakedRaytrace" }
            HLSLPROGRAM
          
            #pragma raytracing BakedRaytrace
            
            #include "UnityRaytracingMeshUtils.cginc" //Yes, this is in fact a BiRP include. Found in Unity\(Version Number)\Editor\Data\CGIncludes 
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _BlendMode;
            CBUFFER_END

            struct RayPayload
            {
                float4 color;
                float3 dir;
            };

            struct AttributeData
            {
                float2 barycentrics;
            };

            struct Vertex
            {
                float2 texcoord;
                // Other per-vertex properties would go here
            };

            #define UNLIT_IS_EMISSIVE // uncomment if you want your unlit shader to emit light

            [shader("closesthit")]
            void BakedRaytrace(inout RayPayload payload, AttributeData attributes : SV_IntersectionAttributes)
            {
                //Intialize payload
                payload.color = float4(0,0,0,1); 
                payload.dir = float3(1,0,0); // Volumetrics do 0 bounces, output ray direction meaningless

                #if defined(UNLIT_IS_EMISSIVE) 
                    uint2 launchIdx = DispatchRaysIndex();
                    uint primitiveIndex = PrimitiveIndex();

                    uint3 triangleIndicies = UnityRayTracingFetchTriangleIndices(primitiveIndex);
                    Vertex v0, v1, v2;

                    // Fetch the uv for each vertex
                    v0.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.x, kVertexAttributeTexCoord0);
                    v1.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.y, kVertexAttributeTexCoord0);
                    v2.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.z, kVertexAttributeTexCoord0);
                    
                    // Interpolate the UV's at the ray hit's barycentric coordinates
                    float3 barycentrics = float3(1.0 - attributes.barycentrics.x - attributes.barycentrics.y, attributes.barycentrics.x, attributes.barycentrics.y);
                    Vertex vInterpolated;
                    vInterpolated.texcoord = v0.texcoord * barycentrics.x + v1.texcoord * barycentrics.y + v2.texcoord * barycentrics.z;

                    float4 emission = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, vInterpolated.texcoord * _BaseMap_ST.xy + _BaseMap_ST.zw, 0);
                    emission *= _BaseColor;
                    payload.color = emission;
                #endif
            }
            ENDHLSL
        }
    }
    CustomEditor "UnityEditor.SLZUnlit_IMGUI"
}