Shader "Hidden/SLZURP/CopySubpassInput"
{
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Background" "PreviewType"="Skybox"}
        //Blend One Zero
        ZWrite Off
        ZTest Off
        Cull Off


        Pass
        {
           
            HLSLPROGRAM
            
            #pragma vertex vert
            #pragma fragment frag
            #pragma exclude_renderers d3d11 
            #define SHADERPASS SHADERPASS_FORWARD

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            FRAMEBUFFER_INPUT_FLOAT(0);

            struct appdata
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
               
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURECUBE(_Tex);
            SamplerState sampler_Tex;
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
    
                float4 clipQuad = GetQuadVertexPosition(v.vertexID, UNITY_RAW_FAR_CLIP_VALUE);
                clipQuad.xy = 4.0f * clipQuad.xy - 1.0f;
                o.vertex = clipQuad;         
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                return LOAD_FRAMEBUFFER_INPUT(0, i);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
