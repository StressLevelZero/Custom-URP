Shader "SLZ/Transmissive Lightmap"
{
    Properties
    {
        [MainTexture] _MainTex ("Main Texture", 2D) = "white" {}
        // _BaseMap ("Main Texture 2", 2D) = "white" {}
        _TransparencyLM ("Lightmapper Transmissive Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Transparent"}
        LOD 100

        Pass
        {
            Name "Forward"
            Tags {"Lightmode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
        // make fog work
        #pragma multi_compile_fog
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

        TEXTURE2D(_MainTex);
        TEXTURE2D(_TransparencyLM);
        SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _Color;
        CBUFFER_END

        v2f vert(appdata v)
        {
            v2f o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.wPos = TransformObjectToWorld(v.vertex.xyz);
            o.vertex = TransformWorldToHClip(o.wPos);
            o.uv_fogFactor.xy = TRANSFORM_TEX(v.uv, _MainTex);
            half clipZ_0Far = UNITY_Z_0_FAR_FROM_CLIPSPACE(o.vertex.z); // normalize the clipspace z-coordinate to 1 near 0 far for platforms that have a different range of clip coordinates
            o.uv_fogFactor.z = unity_FogParams.x * clipZ_0Far;
            return o;
        }

        half4 frag(v2f i) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            half4 col = 1e-9 * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv_fogFactor.xy) + 
                SAMPLE_TEXTURE2D(_TransparencyLM, sampler_MainTex, i.uv_fogFactor.xy)
                ;

            int2 checkerUV = (int2)floor(i.uv_fogFactor.xy * 4);
            int checkerIdx = (checkerUV.x + checkerUV.y) & 1;
            if (checkerIdx == 1)
            {
                col = 0.5 * col + float4(0.75, 0, 0.75, 0.5); 
            }
            else
            {
                col = 0.5 * col;
            }
            // apply fog
            //float3 viewDir = i.wPos - _WorldSpaceCameraPos;
            //col.rgb = MixFog(col.rgb, viewDir, i.uv_fogFactor.z);
            //col = Volumetrics(col, i.wPos);

            return col;
        }
        ENDHLSL
    }

    Pass
    {
        Name "DepthOnly"
        Tags {"Lightmode" = "DepthOnly"}

        HLSLPROGRAM
        #pragma vertex vert
        #pragma fragment frag
            // Depth-only doesn't use fog
            //#pragma multi_compile_fog
            //#pragma multi_compile_fragment _ _VOLUMETRICS_ENABLED   

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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

            /* Don't need textures
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            */

            /* Don't need anything in the cbuffer
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END
            */

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wPos = TransformObjectToWorld(v.vertex.xyz);
                o.vertex = TransformWorldToHClip(wPos);

                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            // If we aren't outputting SV_Depth, just return 0
            return 0;
        }
        ENDHLSL
    }

    Pass
    {
        Name "DepthNormals"
        Tags {"Lightmode" = "DepthNormals"}

        HLSLPROGRAM
        #pragma vertex vert
        #pragma fragment frag 

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

        HLSLPROGRAM
        #pragma vertex vert
        #pragma fragment frag

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

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return 0;
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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityMetaVertexPosition(v.vertex.xyz, v.uv1, v.uv2);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                #if defined(EDITOR_VISUALIZATION)
                    UnityEditorVizData(v.vertex.xyz, v.uv, v.uv1, v.uv2, o.VizUV, o.LightCoord);
                #endif
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                MetaInput metaInput = (MetaInput)0; //Initialize the struct's memory to 0 so the compiler doesn't complain
                metaInput.Albedo = 0;
                metaInput.Albedo = col.rgb;
                metaInput.Emission = 0;
                //metaInput.Emission = col.rgb; // use this instead if you want the shader to not just block light

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
            //#pragma target 5.0 //Doesn't do anything, Raytracing shaders are always compiled with DXC, which sets it to at least 6.0 
            #pragma raytracing BakedRaytrace
            
            #include "UnityRaytracingMeshUtils.cginc" //Yes, this is in fact a BiRP include. Found in Unity\(Version Number)\Editor\Data\CGIncludes 
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END

            #include "Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/BakedRaytraceData.hlsl"


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

                    float4 emission = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, vInterpolated.texcoord * _MainTex_ST.xy + _MainTex_ST.zw, 0);

                    payload.color = emission;
                #endif
            }
            ENDHLSL
        }
    }
}