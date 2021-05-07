Shader "hidden/DebugVolumeRendering"
{
	Properties
	{
		_Color ("Color", Color) = (1, 1, 1, 1)
		_Volume ("Volume", 3D) = "" {}
		_Intensity ("Intensity", Range(1.0, 5.0)) = 1.2
		_Threshold ("Threshold", Range(0.0, 1.0)) = 0.95
		_SliceMin ("Slice min", Vector) = (0.0, 0.0, 0.0, -1.0)
		_SliceMax ("Slice max", Vector) = (1.0, 1.0, 1.0, -1.0)
	}



	SubShader {
		Blend SrcAlpha OneMinusSrcAlpha
		ZTest Always
		Tags {"RenderPipeline" = "UniversalPipeline"  "RenderType" = "Transparent" "Queue" = "Transparent" }
		Cull front

		HLSLINCLUDE
		#pragma target 3.0
		ENDHLSL

		Pass
		{
			HLSLPROGRAM
			#define REQUIRE_DEPTH_TEXTURE 1
			#define ITERATIONS 100

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"




#ifndef ITERATIONS
#define ITERATIONS 100
#endif

half4 _Color;
sampler3D _Volume;
half _Intensity, _Threshold;
half3 _SliceMin, _SliceMax;
float4x4 _AxisRotationMatrix;

uniform float4 _CameraDepthTexture_TexelSize;


struct Ray {
  float3 origin;
  float3 dir;
};

struct AABB {
  float3 min;
  float3 max;
};

bool intersect(Ray r, AABB aabb, out float t0, out float t1)
{
  float3 invR = 1.0 / r.dir;
  float3 tbot = invR * (aabb.min - r.origin);
  float3 ttop = invR * (aabb.max - r.origin);
  float3 tmin = min(ttop, tbot);
  float3 tmax = max(ttop, tbot);
  float2 t = max(tmin.xx, tmin.yz);
  t0 = max(t.x, t.y);
  t = min(tmax.xx, tmax.yz);
  t1 = min(t.x, t.y);
  return t0 <= t1;
}

float3 localize(float3 p) {
  return mul(unity_WorldToObject, float4(p, 1)).xyz;
}

float3 get_uv(float3 p) {
	// float3 local = localize(p);
	return (p + 0.5);
  }

  float4 sample_volume(float3 uv, float3 p)
  {
	float4 v = tex3D(_Volume, uv) * _Intensity;

	float3 axis = mul(_AxisRotationMatrix, float4(p, 0)).xyz;
	axis = get_uv(axis);
	float min = step(_SliceMin.x, axis.x) * step(_SliceMin.y, axis.y) * step(_SliceMin.z, axis.z);
	float max = step(axis.x, _SliceMax.x) * step(axis.y, _SliceMax.y) * step(axis.z, _SliceMax.z);

	return  min * max * v;
  }

  bool outside(float3 uv)
  {
	const float EPSILON = 0.01;
	float lower = -EPSILON;
	float upper = 1 + EPSILON;
	return (
			  uv.x < lower || uv.y < lower || uv.z < lower ||
			  uv.x > upper || uv.y > upper || uv.z > upper
		  );
  }

  struct appdata
  {
	float4 vertex : POSITION;
	float2 uv : TEXCOORD0;
  };

  struct v2f
  {
	float4 vertex : SV_POSITION;
	float2 uv : TEXCOORD0;
	float3 world : TEXCOORD1;
	float3 local : TEXCOORD2;
	float4 ase_texcoord1 : TEXCOORD3;

  };

  v2f vert(appdata v)
  {
	v2f o;
	//o.vertex = UnityObjectToClipPos(v.vertex);
	  o.vertex = TransformObjectToHClip(v.vertex);

	o.uv = v.uv;
	o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
	o.local = v.vertex.xyz;

	//Get screen pos For depth
	float4 ase_clipPos = TransformObjectToHClip((v.vertex).xyz);
	float4 screenPos = ComputeScreenPos(ase_clipPos);
	o.ase_texcoord1 = screenPos;

	return o;
  }

  float4 frag(v2f i) : SV_Target
  {

	  //For depth
	  float4 screenPos = i.ase_texcoord1;
	  float4 ase_screenPosNorm = screenPos / screenPos.w;
		float clampDepth22 = Linear01Depth(SHADERGRAPH_SAMPLE_SCENE_DEPTH(ase_screenPosNorm.xy),_ZBufferParams);


	  Ray ray;
	  // ray.origin = localize(i.world);
	  ray.origin = i.local;

	  // world space direction to object space
	  float3 dir = -(i.world - _WorldSpaceCameraPos);
	  ray.dir = normalize(mul(unity_WorldToObject, dir));

	  AABB aabb;
	  aabb.min = float3(-0.5, -0.5, -0.5);
	  aabb.max = float3(0.5, 0.5, 0.5);

	  float tnear;
	  float tfar;
	  intersect(ray, aabb, tnear, tfar);

	  tnear = max(0.0, tnear);

	  // float3 start = ray.origin + ray.dir * tnear;
	  float3 start = ray.origin;
	  float3 end = ray.origin + ray.dir * tfar;
	  float dist = abs(tfar - tnear); // float dist = distance(start, end);
	  float step_size = dist / float(ITERATIONS);
	  float3 ds = normalize(end - start) * step_size;

	  float4 dst = float4(0, 0, 0, 0);
	  float3 p = start;

	  [unroll]
	  for (int iter = 0; iter < ITERATIONS; iter++)
	  {
		float3 uv = get_uv(p);
		float4 v = sample_volume(uv, p);
		float4 src = v;
		src.a *= 0.5;
		src.rgb *= src.a;

		// blend
		dst = (1.0 - dst.a) * src + dst;
		p += ds;

		if (dst.a > _Threshold) break;
	  }

	  return saturate(dst) * _Color;
	}


			#pragma vertex vert
			#pragma fragment frag



			ENDHLSL
		}
	}
}
