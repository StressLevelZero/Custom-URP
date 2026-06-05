Shader "SLZ/Debug/Show Micro Triangles"
{
    Properties
    {
        //[Toggle(_FIXED_SCREEN_DISTANCE)] _UseFixedDistance ("Use fixed distance", float) = 0
        //_MeshDistance ("Mesh Distance", float) = 10
        //_FovDegrees ("Vertical FOV", Range(0,180)) = 110
        //_Resolution ("Vertical Resolution", float) = 1700
        //_MaxThreshold ("Max Triangle Thickness", float) = 16
        //_MinThreshold ("Min Triangle Thickness", float) = 4
        _Conservative ("Conservative Raster", float) = 1
        _Gradient ("Gradient Texture", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent"}
        LOD 100
        
        Pass
        {
            Name "Forward"
            Tags {"Lightmode" = "UniversalForward"}
            //Offset -0.05, -2
            //ZTest Always
            //ZWrite Off
            //Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
           // Blend One One
            Conservative [_Conservative]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma geometry geo
            #pragma fragment frag
            #pragma target 5.0

            #pragma multi_compile _ _FIXED_SCREEN_DISTANCE
            #pragma multi_compile _ _MICRO_TRI_DISPLAY_AS_WIREFRAME

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct appdata
            {
                float4 vertex : POSITION0;
                #if defined(STEREO_MULTIVIEW_ON)
                uint stereoTargetEyeIndexAsBlendIdx0 : TEXCOORD0;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2g
            {
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct g2f
            {
                float4 vertex : SV_POSITION;
                float triSize : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
                #if defined(STEREO_MULTIVIEW_ON)
                uint eyeIndex : SV_RenderTargetArrayIndex;
                #endif
            };

           CBUFFER_START(MicroTriVisParams)
               float4 _MTParams1;
               float4 _MTParams2;
           CBUFFER_END

           #define _MeshDistance _MTParams1.x
           #define _Resolution   _MTParams1.y
           #define _FovDegrees   _MTParams1.z
           #define _MaxThreshold _MTParams1.w
           #define _MinThreshold _MTParams2.x

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_Gradient);


            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END

            v2g vert(appdata v)
            {
                v2g o = (v2g)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v,o);
                #if !defined(STEREO_MULTIVIEW_ON)
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                #endif
                o.vertex = v.vertex;
                #if defined(UNITY_COMPILER_DXC)
                o.vertex.y *= -1;
                #endif
                return o;
            }

            void swap(inout float4 a, inout float4 b)
            {
                 float4 temp = b;
                    b = a;
                    a = temp;
            }


            #if defined(_MICRO_TRI_DISPLAY_AS_WIREFRAME)
                #define OUTPUT_COUNT 4
                #define OUTPUT_STREAM LineStream<g2f>
            #else
                #define OUTPUT_COUNT 3
                #define OUTPUT_STREAM TriangleStream<g2f>
            #endif

            #if defined(STEREO_MULTIVIEW_ON)
                #define DECLARE_GS_INSTANCE_ID , uint GSInstanceID : SV_GSInstanceID
                #define APPLY_STEREO_EYE_INDEX(g) g.eyeIndex = unity_StereoEyeIndex
                #define INSTANCE_COUNT 2
            #else
                #define DECLARE_GS_INSTANCE_ID , uint GSInstanceID : SV_GSInstanceID
                #define APPLY_STEREO_EYE_INDEX(g)
                #define INSTANCE_COUNT 1
            #endif

            [instance(INSTANCE_COUNT)]
            [maxvertexcount(OUTPUT_COUNT)]
            void geo(triangle v2g input[3], 
                inout OUTPUT_STREAM outputStream
                DECLARE_GS_INSTANCE_ID
                )
            {
                #if defined(STEREO_MULTIVIEW_ON)
                   unity_StereoEyeIndex = GSInstanceID;
                #else
                   UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input[0]);
                #endif

                float3 wPos[3];
                [unroll] for (int i = 0; i < 3; i++)
                {
                    wPos[i] = TransformObjectToWorld(input[i].vertex.xyz);
                }

                float4 edge0 = float4(wPos[1] - wPos[0], 0);
                float4 edge1 = float4(wPos[2] - wPos[1], 0);
                float4 edge2 = float4(wPos[0] - wPos[2], 0);
                
                edge0.w = length(edge0.xyz);
                edge1.w = length(edge1.xyz);
                edge2.w = length(edge2.xyz);
                
                if (edge0.w > edge1.w)
                {
                    swap(edge0, edge1);
                }

                if (edge1.w > edge2.w)
                {
                    swap(edge1, edge2);
                }

                if (edge0.w > edge1.w)
                {
                    swap(edge0, edge1);
                }

                float3 proj = edge1.xyz - (dot(edge1.xyz, edge2.xyz) / dot(edge2.xyz, edge2.xyz)) * edge2.xyz;

                float triThickness = length(proj);

                
                float dist;
                #if defined(_FIXED_SCREEN_DISTANCE)
                    dist = _MeshDistance;
                #else
                    float3 baryCenter = 0.3333 * wPos[0] + 0.3333 * wPos[1] + 0.3333 * wPos[2];
                    dist = distance(baryCenter, _WorldSpaceCameraPos); 
                #endif

                float totalScreenHeight = tan(0.5 * PI * (_FovDegrees / 180.0)) * dist;
                float triHeight = triThickness * (_Resolution / (0.5 * totalScreenHeight));
                

                bool isMinMicro = triHeight < _MinThreshold;
                bool isMaxMicro = triHeight < _MaxThreshold;
                

                if (isMaxMicro)
                {
                g2f output[3];
                [unroll] for (int k = 0; k < 3; k++)
                {
                    float3 vtx2cam = wPos[k].xyz - _WorldSpaceCameraPos;
                    float zOffset = isMinMicro ? -0.001f : -0.0005f;
                    wPos[k].xyz = wPos[k].xyz + zOffset * vtx2cam;
                    output[k] = (g2f)0;
                    output[k].vertex = TransformWorldToHClip(wPos[k].xyz);
                    output[k].vertex.xyz = isMaxMicro ? output[k].vertex.xyz : asfloat(0x7fffffff).xxx;
                    output[k].triSize = triHeight;
                    UNITY_TRANSFER_INSTANCE_ID(input[k], output[k]);
                    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output[k]);
                    APPLY_STEREO_EYE_INDEX(output[k]);
                }
                outputStream.Append(output[0]);
                outputStream.Append(output[1]);
                outputStream.Append(output[2]);
                #if defined(_MICRO_TRI_DISPLAY_AS_WIREFRAME)
                outputStream.Append(output[0]);
                #endif
                }
                //outputStream.RestartStrip();
            }


            half4 frag(g2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float lerpFactor = saturate((i.triSize - _MaxThreshold) / (_MinThreshold - _MaxThreshold));
                /*
                float hue = lerp(0.5,0,(lerpFactor * lerpFactor));
                float3 color = HsvToRgb(float3(hue, 1, 1));
                float alpha = (lerpFactor * lerpFactor * lerpFactor);
                */
                float4 lut = SAMPLE_TEXTURE2D_LOD(_Gradient, sampler_LinearClamp, float2(lerpFactor, 0), 0);
                float3 color = lut.rgb;
                float alpha = lut.a;
                float blink = i.triSize < _MinThreshold ? (0.75 + 0.3 * sin(6 * _Time[2])) : 1;
                    //lerp(1, (0.75 + 0.3 * sin(6 * _Time[2])), lerpFactor * lerpFactor); 
                //color *= 0.5*alpha;
                return  float4( blink * color, alpha);
            }
            ENDHLSL
        }
    }
}