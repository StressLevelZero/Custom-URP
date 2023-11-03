Shader "Skybox/SLZ Cubemap"
{
    Properties
    {
        _SkyTex ("Sky Texture", CUBE) = "black" {}
        [HDR] _SkyColor ("Sky Color", Color) = (1,1,1,1)
        _Rotation ("Rotation", Range(0,360)) = 0
        [Toggle(USE_DIST_FOG)]_UseFog ("Apply Distance Fog", Int) = 0
        _FogDist ("Fog Max Distance (0 to camera far clip)", Range(0,1)) = 1.0
       
    }
    SubShader
    {
        Tags {"Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox"}
        Blend One Zero
		ZWrite Off
        ZClip Off
        Cull Off


        Pass
        {
           
            HLSLPROGRAM
            
            #pragma vertex vert
            #pragma fragment frag
            #pragma exclude_renderers gles
            #pragma multi_compile_fragment _ _VOLUMETRICS_ENABLED
            #pragma multi_compile_fragment _ FOG_LINEAR FOG_EXP2
            #pragma multi_compile _ DRAW_SKY_PROCEDURAL // Declared when actually drawing to a camera. Unity's spherical harmonic baking assumes old icosphere mesh drawing.
            #pragma shader_feature USE_DIST_FOG
            #define SHADERPASS SHADERPASS_FORWARD

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"


            struct appdata
            {
                #if defined(DRAW_SKY_PROCEDURAL)
                half2 uv0 : TEXCOORD0;
                uint vertexID : SV_VertexID;
               
                #else
                float4 vertex : POSITION;
                #endif
               
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                #if defined(DRAW_SKY_PROCEDURAL)
               
                float2 uv0 : TEXCOORD0;
                float4 wPos_xyz_fog_x : TEXCOORD1;
                nointerpolation float4 rotMatrix : TEXCOORD2;
                #else
                float3 uv0 : TEXCOORD0;
                nointerpolation float4 rotMatrix : TEXCOORD1;
                #endif
                
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURECUBE(_SkyTex);
            SamplerState sampler_SkyTex;
            //CBUFFER_START(UnityPerMaterial)
            half4 _SkyColor;
            float _FogDist;
            float _Rotation;
            //CBUFFER_END


            /* Gets the position of a vertex as a part of a right triangle that completely covers the screen
             * Assumes a single triangle mesh, with the positions based on the vertex's ID. 
             * CCW order 
             * 0 : 0,1     0
             * 1 : 0,0     | \
             * 2 : 1,0     1--2
             */
            float4 GetQuadVertexPosition2(uint vertexID, float z = UNITY_NEAR_CLIP_VALUE)
            {
                uint topBit = vertexID >> 1u;
                uint botBit = (vertexID & 1u);
                float y = 1.0f - ((vertexID & 2u) >> 1);
                float x = (vertexID & 1u);//1 - (topBit + botBit) & 1; // produces 1 for indices 0,3 and 0 for 1,2
                return float4(x, y, z, 1.0);
            }
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    
                #if defined(DRAW_SKY_PROCEDURAL) // Generate fullscreen tri from procedural vertices 
    
                float4 clipQuad = GetQuadVertexPosition(v.vertexID, UNITY_RAW_FAR_CLIP_VALUE);
                clipQuad.xy = 4.0f * clipQuad.xy - 1.0f;
                float4 wPos = mul(UNITY_MATRIX_I_VP, clipQuad);
                o.wPos_xyz_fog_x.xyz = wPos.xyz / wPos.w;
                o.vertex = clipQuad;
                half clipZ_0Far = lerp(_ProjectionParams.y, _ProjectionParams.z, _FogDist);
                o.wPos_xyz_fog_x.w = unity_FogParams.x * clipZ_0Far;
    
                #else // Handle drawing normal icosphere mesh
    
                //o.wPos_xyz_fog_x = float4(TransformObjectToWorld(v.vertex.xyz), 0);
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv0 = v.vertex.xyz;
                #endif
                float sin1;
                float cos1;
                float angle = (_Rotation / 180.0) * PI;
                sincos(angle, sin1, cos1);
                o.rotMatrix = float4(cos1, -sin1, sin1, cos1);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
               
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
               
             
    
                #if defined(DRAW_SKY_PROCEDURAL) 
                float3 viewDir = normalize(float3(i.wPos_xyz_fog_x.xyz - _WorldSpaceCameraPos));
                float3 viewDir2 = float3(
                    viewDir.x * i.rotMatrix.x + viewDir.z * i.rotMatrix.y,
                    viewDir.y, 
                    viewDir.x * i.rotMatrix.z + viewDir.z * i.rotMatrix.w);
                half4 col = SAMPLE_TEXTURECUBE_LOD(_SkyTex, sampler_SkyTex, viewDir2, 0);
                col *= _SkyColor;
                #if defined(USE_DIST_FOG)
                    col.rgb = MixFog(col.rgb, viewDir, i.wPos_xyz_fog_x.w);
                #endif
                col = Volumetrics(col, i.wPos_xyz_fog_x.xyz);
                #else
                half4 col = SAMPLE_TEXTURECUBE_LOD(_SkyTex, sampler_SkyTex, i.uv0, 0);
                col *= _SkyColor;
                #endif

                return col;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
