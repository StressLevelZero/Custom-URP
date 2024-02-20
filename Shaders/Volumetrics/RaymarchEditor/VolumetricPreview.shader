Shader "Hidden/VolumetricPreview"
{
    Properties
    {
        _Volume ("Volume", 3D) = "" {}
        //_GlobalExtinction("Extinction", float) = 0.05
        //_GlobalScattering("Scattering", color) = (0.05, 0.05, 0.05, 0.05)
        _Intensity ("Intensity", Range(0.0, 5.0)) = 1.2
		_Threshold ("Threshold", Range(0.0, 1.0)) = 0.95
        _StepDist ("Step Distance", float) = 0.5
        //_VolExposure2 ("Volume Exposure", float) = 0.05
    }
    SubShader
    {
        Blend One One
		ZTest Always
		Tags {"RenderPipeline" = "UniversalPipeline"  "RenderType" = "Transparent" "Queue" = "Transparent" }
		Cull front
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
          
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

          
            
            #ifndef MAX_ITERATIONS
            #define MAX_ITERATIONS 100
            #define INV_MAX_ITERATIONS 0.01
            #endif

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 screenPos : TEXCOORD1;
                float4 wPos : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };



            TEXTURE3D(_Volume);
            SamplerState sampler_Volume;
            float _GlobalExtinction;
            float4 _GlobalScattering;

            CBUFFER_START(UnityPerMaterial)
            float _Threshold;
            float _Intensity;
            float _StepDist;
            float _VolExposure2;
            CBUFFER_END

            /** Moves the given ray to the surface of the bounding box via AABB ray intersection
             */
            void MoveRayToBoxSurface(inout float3 rayPos, float3 rayDir, float3 boxCenter, float3 boxSize, out float2 intersectLen)
            {
                float3 rayPosCentered = rayPos - boxCenter;
                float3 boxBottom = -boxSize;
                float3 boxTop = boxSize;
                float3 rcpRayDir = rcp(rayDir);
                float3 tBottom = rcpRayDir * (boxBottom - rayPosCentered);
                float3 tTop = rcpRayDir * (boxTop - rayPosCentered);
                float3 tMin = min(tBottom, tTop);
                float3 tMax = max(tBottom, tTop);
                float2 temp = max(tMin.xx, tMin.yz);
                temp = max(temp.x, temp.y);
                intersectLen.x = temp;
                temp = min(tMax.xx, tMax.yz);
                temp = min(temp.x, temp.y);
                intersectLen.y = temp;

                if (intersectLen.x > 0)
                {
                    rayPos += intersectLen.x * rayDir;
                }
            }

            v2f vert (appdata v)
            {
                v2f o;
                
                UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = TransformObjectToHClip(v.vertex);
                o.uv = v.uv;
                o.screenPos = ComputeScreenPos(o.vertex);
                o.wPos = float4(TransformObjectToWorld(v.vertex),1);

                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
			    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                // Get the screen depth
                float2 screenUV = i.screenPos.xy/i.screenPos.w;
                float depth = SampleSceneDepth(screenUV);
                depth = Linear01Depth(depth, _ZBufferParams);

                // calculate the length of the ray from the camera to the depth value sampled at the pixel
                float3 camToPix = i.wPos.xyz - _WorldSpaceCameraPos;
                float pixDepth = dot(camToPix, -UNITY_MATRIX_I_V._m02_m12_m22); // distance from the camera along the camera's -z
                float3 depthPos = camToPix * (_ProjectionParams.z / pixDepth) * depth; //normalize camToPix such that the vector's length along the camera's Z is 1, then multiply by the depth times the far plane distance to get the ray from the camera to the pixel in the depth texture
                float rayLenDepth = length(depthPos);

                float3 rayDir = normalize(camToPix);
                float3 rayPos = _WorldSpaceCameraPos;

                //Axes of the box, equal to half the length of the box on each axis. Assumes that the object is axis aligned!
                float3 boxSize = UNITY_MATRIX_M._m00_m11_m22 * 0.5;
                float3 boxCenter = UNITY_MATRIX_M._m03_m13_m23;
                float3 boxMin = boxCenter-boxSize;

                // move the ray to the surface of the bounding box if the camera is outside the box
                float2 intersectLen;
                MoveRayToBoxSurface(rayPos, rayDir, boxCenter, boxSize, intersectLen);

                //Max distance the ray can travel is either to the far side of the bounding box or to the position specified by the depth in the camera depth texture
                float rayMaxDist = min(intersectLen.y, rayLenDepth);

                //Ensures the rays will always be able to reach the farthest corners of the bounding box
                float rayStepSize = 2*sqrt(boxSize.x*boxSize.x + boxSize.y*boxSize.y + boxSize.z*boxSize.z) * INV_MAX_ITERATIONS;
                

                float3 rayUVW = (rayPos - boxMin) / (2.0 * boxSize); 
                float4 totalColor = 0;
                float4 volumeColor = float4(0,0,0,1);
                
                float totalDist = length(rayPos - _WorldSpaceCameraPos);
                float transmittance = exp(-_GlobalExtinction * rayStepSize);
                float exposure = _VolExposure2*0.01;
                [branch] if (totalDist < rayMaxDist)
                {
                    [loop] for (int iter = 0; iter < MAX_ITERATIONS; iter++)
                    {
                        rayUVW = (rayPos - boxMin) / (2.0 * boxSize);
                        
                        
                        volumeColor = SAMPLE_TEXTURE3D_LOD(_Volume, sampler_Volume, rayUVW, 0)  * _Intensity;
                        float3 stepColor = exposure * volumeColor.rgb * volumeColor.a * ((1-transmittance)/_GlobalExtinction) * _GlobalScattering;

                        rayPos += rayStepSize * rayDir;
                        totalDist += rayStepSize;
                        
                        [branch] if (totalDist > rayMaxDist)
                        {
                            float stepFrac = max(0, (rayStepSize - (totalDist - rayMaxDist)) / rayStepSize);
                            
                            totalColor.rgb += stepFrac * stepColor;
                            break;
                        }
                        
                        totalColor.rgb += stepColor;
                        totalColor.a *= transmittance;
                    }
                    //totalColor.rgb = float3(0,0,1);
                }
                float4 col = float4( totalColor.rgb, 1);

                return col;
            }
            ENDHLSL
        }
    }
}
