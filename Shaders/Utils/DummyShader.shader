Shader "Hidden/DUMMY_SHADER"
{
	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Geometry+100"}
		LOD 100

		HLSLINCLUDE
		#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

		
		ENDHLSL

		Pass
		{
			Name "Forward"
			Tags {"Lightmode" = "UniversalForward"}

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			// make fog work
			//#pragma multi_compile_fog
			//#pragma multi_compile_fragment _ _VOLUMETRICS_ENABLED   
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
				o.wNormal = normalize(TransformObjectToWorldNormal(v.normal, false));

				return o;
			}

			half4 frag(v2f i) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
			///	i.wNormal = normalize(i.wNormal);
				half fresnel = 1.0 - saturate(abs(dot(i.wNormal, UNITY_MATRIX_V._m20_m21_m22)));
				return half4(0.7,0.2,0.0,1) * fresnel * fresnel;
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
			#pragma multi_compile_instancing
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
	
				o.vertex = TransformObjectToHClip(v.vertex.xyz);
				
				return o;
			}

			half4 frag(v2f i) : SV_Target
			{
				
				

				return half4(1,0,1,1);
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
			#pragma multi_compile_instancing
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
	}
}