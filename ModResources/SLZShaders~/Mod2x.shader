Shader "SLZ/Mod2x"
{
	Properties
	{
		[HideInInspector] _AlphaCutoff("Alpha Cutoff ", Range(0, 1)) = 0.5
		[HideInInspector] _EmissionColor("Emission Color", Color) = (1,1,1,1)
		_MainTex("MainTex", 2D) = "gray" {}
		[HDR]_Color("Color", Color) = (1,1,1,0)
		_OffsetUnits("OffsetUnits", Int) = -2
		_OffsetFactor("OffsetFactor", Int) = -2
		_Multiplier("Multiplier", Float) = 1
		[Toggle(_ALPHA_ON)] _alpha("alpha", Float) = 0
		[Toggle(_VERTEXCOLORS_ON)] _VertexColors("VertexColors", Float) = 1
		// [HideInInspector] _texcoord( "", 2D ) = "white" {}

		// [HideInInspector]_QueueOffset("_QueueOffset", Float) = 0
        // [HideInInspector]_QueueControl("_QueueControl", Float) = -1
        // [HideInInspector][NoScaleOffset]unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        // [HideInInspector][NoScaleOffset]unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        // [HideInInspector][NoScaleOffset]unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
	}

	SubShader
	{
		LOD 0
		
		Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-499" "IgnoreProjector" = "True"}
		
		Cull Back
		AlphaToMask Off
		
		HLSLINCLUDE
		#pragma target 5.0
		#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/PlatformCompiler.hlsl"
		ENDHLSL
		
		Pass
		{
			
			Name "Forward"
			Tags { "LightMode"="UniversalForward" }
			
			Blend DstColor SrcColor
			ZWrite Off
			ZTest LEqual
			Offset [_OffsetFactor] , [_OffsetUnits]
			ColorMask RGBA

			HLSLPROGRAM
			
			#define _RECEIVE_SHADOWS_OFF 1
			#pragma multi_compile_instancing
			#define ASE_SRP_VERSION 999999
			#define SHADERPASS SHADERPASS_UNLIT
			#define EPSILON 2.4414e-4


			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			//#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
			//#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"
			//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/Debugging3D.hlsl"
			//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"


			#define ASE_NEEDS_FRAG_COLOR
			#define ASE_NEEDS_FRAG_WORLD_POSITION
			#pragma shader_feature _ALPHA_ON
			#pragma shader_feature _VERTEXCOLORS_ON
			#pragma multi_compile _ _VOLUMETRICS_ENABLED
			#pragma multi_compile_fog
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"


			struct VertexInput
			{
				float4 vertex : POSITION;
				float3 ase_normal : NORMAL;
				float4 ase_texcoord : TEXCOORD0;
				float4 ase_color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput
			{
				float4 clipPos : SV_POSITION;
				float3 worldPos : TEXCOORD0;
				float fogFactor : TEXCOORD2;				
				float4 ase_texcoord3 : TEXCOORD3;
				float4 ase_color : COLOR;
				float4 ase_texcoord4 : TEXCOORD4;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _MainTex_ST;
			float4 _Color;
			int _OffsetUnits;
			int _OffsetFactor;
			float _Multiplier;
			CBUFFER_END
			sampler2D _MainTex;

			shared float _StaticLightMultiplier;

						
			VertexOutput VertexFunction ( VertexInput v  )
			{
				VertexOutput o = (VertexOutput)0;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				float4 ase_clipPos = TransformObjectToHClip((v.vertex).xyz);
				float4 screenPos = ComputeScreenPos(ase_clipPos);
				o.ase_texcoord4 = screenPos;				
				o.ase_texcoord3.xy = v.ase_texcoord.xy;
				o.ase_color = v.ase_color;				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord3.zw = 0;
				v.ase_normal = v.ase_normal;

				float3 positionWS = TransformObjectToWorld( v.vertex.xyz );
				float4 positionCS = TransformWorldToHClip( positionWS );

				#if defined(ASE_NEEDS_FRAG_WORLD_POSITION)
				o.worldPos = positionWS;
				#endif
				o.fogFactor = ComputeFogFactor( positionCS.z );
				o.clipPos = positionCS;
				return o;
			}

			VertexOutput vert ( VertexInput v )
			{
				return VertexFunction( v );
			}

			half3 Mod2xFog(half3 fragColor, half fogFactor)
			{
				#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
					half fogIntensity = ComputeFogIntensity(fogFactor);
					fragColor = lerp(0.5, fragColor, fogIntensity);
				#endif
				return fragColor;
			}

			half4 frag ( VertexOutput IN  ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID( IN );
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( IN );
				#if defined(ASE_NEEDS_FRAG_WORLD_POSITION)
				float3 WorldPosition = IN.worldPos;
				#endif
				float localMyCustomExpression1_g126 = ( 0.0 );
				float2 uv_MainTex = IN.ase_texcoord3.xy * _MainTex_ST.xy + _MainTex_ST.zw;
				float4 appendResult52 = (float4(1.0 , 1.0 , 1.0 , IN.ase_color.a));
				#ifdef _VERTEXCOLORS_ON
				float4 staticSwitch38 = IN.ase_color;
				#else
				float4 staticSwitch38 = appendResult52;
				#endif
				float4 temp_output_16_0 = ( tex2D( _MainTex, uv_MainTex ) * _Color * staticSwitch38 );
				float4 temp_output_26_0 = ( ( ( temp_output_16_0 - .5 ) * _Multiplier ) + 0.5 );
				#ifdef _ALPHA_ON
				float4 lerpResult30 = lerp( .5 , temp_output_26_0 , (temp_output_16_0).a);
				float4 staticSwitch28 = lerpResult30;
				#else
				float4 staticSwitch28 = temp_output_26_0;
				#endif
				float4 color1_g126 = staticSwitch28;
				float localMyCustomExpression24_g126 = ( 0.0 );
				float4 screenPos = IN.ase_texcoord4;
				float4 ase_screenPosNorm = screenPos / screenPos.w;
				ase_screenPosNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_screenPosNorm.z : ase_screenPosNorm.z * 0.5 + 0.5;
				float2 uv24_g126 = (ase_screenPosNorm).xy;		

				float3 BakedAlbedo = 0;
				float3 BakedEmission = 0;
				float3 Color = color1_g126.xyz;
				float Alpha = 1;
				float AlphaClipThreshold = 0.5;
				float AlphaClipThresholdShadow = 0.5;

				#ifdef _ALPHATEST_ON
					clip( Alpha - AlphaClipThreshold );
				#endif

				#if defined(_ALPHAPREMULTIPLY_ON)
				Color *= Alpha;
				#endif

				#ifdef LOD_FADE_CROSSFADE
					LODDitheringTransition( IN.clipPos.xyz, unity_LODFade.x );
				#endif

				Color.rgb = Mod2xFog( Color, IN.fogFactor );

				#if defined(_VOLUMETRICS_ENABLED) 
				//works fine on the PC but not quest. Using a semi-plausible result otherwise.
					#if !defined(SHADER_API_MOBILE) 
						half3 FroxelColor = GetVolumetricColor(WorldPosition).rgb;
						Color.rgb = Color.rgb - 0.5* (2.0*Color.rgb - 1.0) * FroxelColor / max(SampleSceneColor(uv24_g126).rgb, EPSILON);
					#else
						half4 FroxelColor = GetVolumetricColor(IN.worldPos);				
						Color.rgb = Color.rgb + (saturate(FroxelColor.rgb)*(0.5-Color.rgb)); //rgb lerp	//x + s(y-x)		
						Color.rgb = lerp(0.5, Color , saturate(FroxelColor.a*FroxelColor.a) );
					#endif
				#endif

				return half4( Color, Alpha );
			}

			ENDHLSL
		}
	}	
	Fallback "Hidden/InternalErrorShader"	
}
// /*ASEBEGIN
// Version=18935
// 2028;88;1920;1019;-234.7018;841.5321;1.354427;True;True
// Node;AmplifyShaderEditor.VertexColorNode;36;-156.4016,266.9482;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
// Node;AmplifyShaderEditor.TexturePropertyNode;9;-1470,-303;Float;True;Property;_MainTex;MainTex;0;0;Create;True;0;0;0;False;0;False;None;7f2f1c5e40e723e43abc1ec20518812e;False;gray;Auto;Texture2D;-1;0;2;SAMPLER2D;0;SAMPLERSTATE;1
// Node;AmplifyShaderEditor.DynamicAppendNode;52;49.16882,383.5409;Inherit;False;FLOAT4;4;0;FLOAT;1;False;1;FLOAT;1;False;2;FLOAT;1;False;3;FLOAT;0;False;1;FLOAT4;0
// Node;AmplifyShaderEditor.StaticSwitch;38;190.3458,296.4778;Float;False;Property;_VertexColors;VertexColors;8;0;Create;True;0;0;0;False;0;False;0;1;1;True;;Toggle;2;Key0;Key1;Create;False;True;All;9;1;FLOAT4;0,0,0,0;False;0;FLOAT4;0,0,0,0;False;2;FLOAT4;0,0,0,0;False;3;FLOAT4;0,0,0,0;False;4;FLOAT4;0,0,0,0;False;5;FLOAT4;0,0,0,0;False;6;FLOAT4;0,0,0,0;False;7;FLOAT4;0,0,0,0;False;8;FLOAT4;0,0,0,0;False;1;FLOAT4;0
// Node;AmplifyShaderEditor.SamplerNode;10;-181.0179,-125.4856;Inherit;True;Property;_TextureSample1;Texture Sample 1;3;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
// Node;AmplifyShaderEditor.ColorNode;15;-91.00143,92.52959;Float;False;Property;_Color;Color;1;1;[HDR];Create;True;0;0;0;False;0;False;1,1,1,0;1,1,1,1;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
// Node;AmplifyShaderEditor.RangedFloatNode;24;303.6783,164.7443;Float;False;Constant;_Float0;Float 0;8;0;Create;True;0;0;0;False;0;False;0.5;0;0;0;0;1;FLOAT;0
// Node;AmplifyShaderEditor.SimpleMultiplyOpNode;16;278.5282,1.515851;Inherit;False;3;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT4;0,0,0,0;False;1;COLOR;0
// Node;AmplifyShaderEditor.SimpleSubtractOpNode;23;481.6185,66.22382;Inherit;False;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
// Node;AmplifyShaderEditor.RangedFloatNode;22;548.2173,349.4277;Float;False;Property;_Multiplier;Multiplier;6;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
// Node;AmplifyShaderEditor.SimpleMultiplyOpNode;25;637.6185,131.2238;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
// Node;AmplifyShaderEditor.RangedFloatNode;31;854.6781,-262.3637;Float;False;Constant;_Float1;Float 1;9;0;Create;True;0;0;0;False;0;False;0.5;0;0;0;0;1;FLOAT;0
// Node;AmplifyShaderEditor.SimpleAddOpNode;26;893.6185,74.22382;Inherit;True;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
// Node;AmplifyShaderEditor.SwizzleNode;37;685.8656,-83.42053;Inherit;False;FLOAT;3;1;2;3;1;0;COLOR;0,0,0,0;False;1;FLOAT;0
// Node;AmplifyShaderEditor.LerpOp;30;1053.464,-141.6288;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
// Node;AmplifyShaderEditor.WorldPosInputsNode;55;1250.084,68.98708;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
// Node;AmplifyShaderEditor.StaticSwitch;28;1301.032,-69.67551;Float;False;Property;_alpha;alpha;7;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Create;False;True;All;9;1;COLOR;0,0,0,0;False;0;COLOR;0,0,0,0;False;2;COLOR;0,0,0,0;False;3;COLOR;0,0,0,0;False;4;COLOR;0,0,0,0;False;5;COLOR;0,0,0,0;False;6;COLOR;0,0,0,0;False;7;COLOR;0,0,0,0;False;8;COLOR;0,0,0,0;False;1;COLOR;0
// Node;AmplifyShaderEditor.SamplerNode;1;-1208,-177;Inherit;True;Property;_TextureSample0;Texture Sample 0;1;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
// Node;AmplifyShaderEditor.ParallaxMappingNode;6;-744,16;Inherit;False;Planar;4;0;FLOAT2;0,0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT3;0,0,0;False;1;FLOAT2;0
// Node;AmplifyShaderEditor.IntNode;20;-196.3815,467.2238;Float;False;Property;_OffsetFactor;OffsetFactor;5;0;Create;True;0;0;0;True;0;False;-2;-2;False;0;1;INT;0
// Node;AmplifyShaderEditor.ColorSpaceDouble;35;146.5294,-257.5435;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
// Node;AmplifyShaderEditor.TextureCoordinatesNode;11;-1514,261;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
// Node;AmplifyShaderEditor.ToggleSwitchNode;17;-412.2653,73.80711;Float;False;Property;_Parallaxing;Parallaxing;2;0;Create;True;0;0;0;False;0;False;0;True;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
// Node;AmplifyShaderEditor.OneMinusNode;13;-994,49;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
// Node;AmplifyShaderEditor.RangedFloatNode;7;-940.5902,307.8667;Float;False;Property;_Depth;Depth;3;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
// Node;AmplifyShaderEditor.IntNode;21;-196.3815,551.2238;Float;False;Property;_OffsetUnits;OffsetUnits;4;0;Create;True;0;0;0;True;0;False;-2;-2;False;0;1;INT;0
// Node;AmplifyShaderEditor.ColorNode;162;1215.854,323.9537;Inherit;False;Property;_TargetColor;Target Color;9;0;Create;True;0;0;0;False;0;False;0,0,0,0;0.3803921,0.3803921,0.3647059,0;True;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
// Node;AmplifyShaderEditor.RangedFloatNode;39;193.349,485.6761;Float;False;Constant;_Float2;Float 2;10;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
// Node;AmplifyShaderEditor.ViewDirInputsCoordNode;8;-1203,383;Float;False;Tangent;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
// Node;AmplifyShaderEditor.FunctionNode;180;1533.19,-63.80811;Inherit;False;VolumetricsUnlitMul2x;-1;;126;04a5afc32a9fbb341bc2fd9b7f1e8895;0;2;2;FLOAT4;0,0,0,0;False;3;FLOAT3;0,0,0;False;1;FLOAT4;0
// Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;44;1532.52,9.024076;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ExtraPrePass;0;0;ExtraPrePass;5;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;-1;False;True;0;False;-1;False;False;False;False;False;False;False;False;False;True;False;255;False;-1;255;False;-1;255;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;False;False;False;False;True;3;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;True;2;True;17;d3d9;d3d11;glcore;gles;gles3;metal;vulkan;xbox360;xboxone;xboxseries;ps4;playstation;psp2;n3ds;wiiu;switch;nomrt;0;False;True;1;1;False;-1;0;False;-1;0;1;False;-1;0;False;-1;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;-1;False;True;True;True;True;True;0;False;-1;False;False;False;False;False;False;False;True;False;255;False;-1;255;False;-1;255;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;False;True;1;False;-1;True;3;False;-1;True;True;0;False;-1;0;False;-1;True;0;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
// Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;49;1532.52,59.02408;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthNormalsOnly;0;9;DepthNormalsOnly;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;-1;False;True;0;False;-1;False;False;False;False;False;False;False;False;False;True;False;255;False;-1;255;False;-1;255;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;False;False;False;False;True;3;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;True;2;True;17;d3d9;d3d11;glcore;gles;gles3;metal;vulkan;xbox360;xboxone;xboxseries;ps4;playstation;psp2;n3ds;wiiu;switch;nomrt;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;False;-1;True;3;False;-1;False;True;1;LightMode=DepthNormalsOnly;False;True;15;d3d9;d3d11_9x;d3d11;metal;vulkan;xbox360;xboxone;xboxseries;ps4;playstation;psp2;n3ds;wiiu;switch;nomrt;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
// Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;43;1532.52,9.024076;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;Meta;0;4;Meta;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;-1;False;True;0;False;-1;False;False;False;False;False;False;False;False;False;True;False;255;False;-1;255;False;-1;255;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;False;False;False;False;True;3;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;True;2;True;17;d3d9;d3d11;glcore;gles;gles3;metal;vulkan;xbox360;xboxone;xboxseries;ps4;playstation;psp2;n3ds;wiiu;switch;nomrt;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;-1;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=Meta;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
// Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;40;1917.52,-64.97592;Float;False;True;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;3;SLZ/Mod2x;2992e84f91cbeb14eab234972e07ea9d;True;Forward;0;1;Forward;8;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;-1;False;True;0;False;-1;False;False;False;False;False;False;False;False;False;True;False;255;False;-1;255;False;-1;255;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;False;False;False;False;True;3;RenderPipeline=UniversalPipeline;RenderType=Transparent=RenderType;Queue=Transparent=Queue=-499;True;2;True;17;d3d9;d3d11;glcore;gles;gles3;metal;vulkan;xbox360;xboxone;xboxseries;ps4;playstation;psp2;n3ds;wiiu;switch;nomrt;0;True;True;7;2;False;-1;3;False;-1;0;1;False;-1;10;False;-1;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;-1;False;False;False;False;False;False;False;True;False;255;False;-1;255;False;-1;255;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;False;True;2;False;-1;True;3;False;-1;True;True;0;True;20;0;True;21;True;1;LightMode=UniversalForward;False;False;2;Include;;False;;Native;Pragma;multi_compile _ _VOLUMETRICS_ENABLED;False;;Custom;Hidden/InternalErrorShader;0;0;Standard;22;Surface;1;0;  Blend;0;0;Two Sided;1;0;Cast Shadows;0;0;  Use Shadow Threshold;0;0;Receive Shadows;0;0;GPU Instancing;1;0;LOD CrossFade;0;0;Built-in Fog;0;0;DOTS Instancing;0;0;Meta Pass;0;0;Extra Pre Pass;0;0;Tessellation;0;0;  Phong;0;0;  Strength;0.5,False,-1;0;  Type;0;0;  Tess;16,False,-1;0;  Min;10,False,-1;0;  Max;25,False,-1;0;  Edge Length;16,False,-1;0;  Max Displacement;25,False,-1;0;Vertex Position,InvertActionOnDeselection;1;0;0;10;False;True;False;False;False;False;False;False;False;False;False;;False;0
// Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;41;1532.52,9.024076;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ShadowCaster;0;2;ShadowCaster;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;-1;False;True;0;False;-1;False;False;False;False;False;False;False;False;False;True;False;255;False;-1;255;False;-1;255;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;False;False;False;False;True;3;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;True;2;True;17;d3d9;d3d11;glcore;gles;gles3;metal;vulkan;xbox360;xboxone;xboxseries;ps4;playstation;psp2;n3ds;wiiu;switch;nomrt;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;-1;False;False;False;True;False;False;False;False;0;False;-1;False;False;False;False;False;False;False;False;False;True;1;False;-1;True;3;False;-1;False;True;1;LightMode=ShadowCaster;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
// Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;42;1532.52,9.024076;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthOnly;0;3;DepthOnly;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;-1;False;True;0;False;-1;False;False;False;False;False;False;False;False;False;True;False;255;False;-1;255;False;-1;255;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;False;False;False;False;True;3;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;True;2;True;17;d3d9;d3d11;glcore;gles;gles3;metal;vulkan;xbox360;xboxone;xboxseries;ps4;playstation;psp2;n3ds;wiiu;switch;nomrt;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;-1;False;False;False;True;False;False;False;False;0;False;-1;False;False;False;False;False;False;False;False;False;True;1;False;-1;False;False;True;1;LightMode=DepthOnly;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
// Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;46;1532.52,59.02408;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;SceneSelectionPass;0;6;SceneSelectionPass;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;-1;False;True;0;False;-1;False;False;False;False;False;False;False;False;False;True;False;255;False;-1;255;False;-1;255;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;False;False;False;False;True;3;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;True;2;True;17;d3d9;d3d11;glcore;gles;gles3;metal;vulkan;xbox360;xboxone;xboxseries;ps4;playstation;psp2;n3ds;wiiu;switch;nomrt;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;-1;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=SceneSelectionPass;False;True;4;d3d11;glcore;gles;gles3;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
// Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;45;1532.52,59.02408;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;Universal2D;0;5;Universal2D;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;-1;False;True;0;False;-1;False;False;False;False;False;False;False;False;False;True;False;255;False;-1;255;False;-1;255;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;False;False;False;False;True;3;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;True;2;True;17;d3d9;d3d11;glcore;gles;gles3;metal;vulkan;xbox360;xboxone;xboxseries;ps4;playstation;psp2;n3ds;wiiu;switch;nomrt;0;False;True;7;2;False;-1;3;False;-1;0;1;False;-1;10;False;-1;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;True;True;True;True;0;False;-1;False;False;False;False;False;False;False;True;False;255;False;-1;255;False;-1;255;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;False;True;2;False;-1;True;3;False;-1;True;True;0;True;20;0;True;21;True;1;LightMode=Universal2D;False;False;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
// Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;47;1532.52,59.02408;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;ScenePickingPass;0;7;ScenePickingPass;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;-1;False;True;0;False;-1;False;False;False;False;False;False;False;False;False;True;False;255;False;-1;255;False;-1;255;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;False;False;False;False;True;3;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;True;2;True;17;d3d9;d3d11;glcore;gles;gles3;metal;vulkan;xbox360;xboxone;xboxseries;ps4;playstation;psp2;n3ds;wiiu;switch;nomrt;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;LightMode=Picking;False;True;4;d3d11;glcore;gles;gles3;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
// Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;48;1532.52,59.02408;Float;False;False;-1;2;UnityEditor.ShaderGraphUnlitGUI;0;1;New Amplify Shader;2992e84f91cbeb14eab234972e07ea9d;True;DepthNormals;0;8;DepthNormals;0;False;False;False;False;False;False;False;False;False;False;False;False;True;0;False;-1;False;True;0;False;-1;False;False;False;False;False;False;False;False;False;True;False;255;False;-1;255;False;-1;255;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;7;False;-1;1;False;-1;1;False;-1;1;False;-1;False;False;False;False;True;3;RenderPipeline=UniversalPipeline;RenderType=Opaque=RenderType;Queue=Geometry=Queue=0;True;2;True;17;d3d9;d3d11;glcore;gles;gles3;metal;vulkan;xbox360;xboxone;xboxseries;ps4;playstation;psp2;n3ds;wiiu;switch;nomrt;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;1;False;-1;True;3;False;-1;False;True;1;LightMode=DepthNormalsOnly;False;True;4;d3d11;glcore;gles;gles3;0;Hidden/InternalErrorShader;0;0;Standard;0;False;0
// WireConnection;52;3;36;4
// WireConnection;38;1;52;0
// WireConnection;38;0;36;0
// WireConnection;10;0;9;0
// WireConnection;16;0;10;0
// WireConnection;16;1;15;0
// WireConnection;16;2;38;0
// WireConnection;23;0;16;0
// WireConnection;23;1;24;0
// WireConnection;25;0;23;0
// WireConnection;25;1;22;0
// WireConnection;26;0;25;0
// WireConnection;26;1;24;0
// WireConnection;37;0;16;0
// WireConnection;30;0;31;0
// WireConnection;30;1;26;0
// WireConnection;30;2;37;0
// WireConnection;28;1;26;0
// WireConnection;28;0;30;0
// WireConnection;1;0;9;0
// WireConnection;6;0;11;0
// WireConnection;6;1;13;0
// WireConnection;6;2;7;0
// WireConnection;6;3;8;0
// WireConnection;17;0;11;0
// WireConnection;17;1;6;0
// WireConnection;13;0;1;4
// WireConnection;180;2;28;0
// WireConnection;180;3;55;0
// WireConnection;40;2;180;0
// ASEEND*/
// //CHKSM=26962EDEC6603AFC435A4B8ECC3126475B025B38