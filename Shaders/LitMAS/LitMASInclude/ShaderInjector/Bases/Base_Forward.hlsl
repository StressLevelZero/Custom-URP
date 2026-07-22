
#define SHADERPASS SHADERPASS_FORWARD
#define _NORMAL_DROPOFF_TS 1
#define _EMISSION
#define _NORMALMAP 1

#if defined(SHADER_API_MOBILE)
    //#!INJECT_POINT MOBILE_DEFINES
#else              

    //#!INJECT_POINT STANDALONE_DEFINES

#endif

#if !defined(LITMAS_FEATURE_LIGHTMAPPING)
#define _DISABLE_LIGHTMAPS
#endif

#define UNITY_UNIFIED_SHADER_PRECISION_MODEL

#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DefaultLitVariants.hlsl"

//#!INJECT_POINT UNIVERSAL_DEFINES



#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZVkExtensions.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZLighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SLZBlueNoise.hlsl"

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MobileAntibanding.hlsl"

//#!INJECT_POINT INCLUDES


struct VertIn
{
    float4 vertex   : POSITION;
    float3 normal    : NORMAL;
    float4 tangent   : TANGENT;
    //#!TEXCOORD float4 uv0 0
    //#!TEXCOORD float4 uv1 0
    //#!TEXCOORD float4 uv2 0
    //#!INJECT_POINT VERTEX_IN
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertOut
{
    float4 vertex       : SV_POSITION;
    //#!TEXCOORD float4 uv0XY_tanXY 1
#if defined(LIGHTMAP_ON) || defined(DYNAMICLIGHTMAP_ON)
    //#!TEXCOORD float4 uv1 1
#endif
    //#!TEXCOORD half4 SHVertLights_btSign 1
    //#!TEXCOORD half4 normXYZ_tanZ 1
    //#!TEXCOORD float4 wPos_fog 1

    //#!INJECT_POINT INTERPOLATORS

    UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
};

#define UNPACK_UV0(i) i.uv0XY_tanXY.xy
#define UNPACK_NORMAL(i) i.normXYZ_tanZ.xyz
#define UNPACK_TANGENT(i) half3(i.uv0XY_tanXY.zw, i.normXYZ_tanZ.w)
#define UNPACK_BITANGENT_SIGN(i) i.SHVertLights_btSign.w
#define UNPACK_WPOS(i) i.wPos_fog.xyz
//#define UNPACK_FOG(i) i.wPos_fog.w
#define UNPACK_VERTLIGHTS(i) i.SHVertLights_btSign.xyz

//#!INJECT_POINT DEFAULT_TEXTURES
//#!INJECT_DEFAULT
TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

TEXTURE2D(_BumpMap);
TEXTURE2D(_MetallicGlossMap);
//#!INJECT_END



//#!INJECT_POINT UNIFORMS

#if defined(CBUFFER_PATH)
#include CBUFFER_PATH
#endif

//#!INJECT_POINT FUNCTIONS

VertOut vert(VertIn v)
{
    VertOut o = (VertOut)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

    o.wPos_fog.xyz = TransformObjectToWorld(v.vertex.xyz);
    o.vertex = TransformWorldToHClip(o.wPos_fog.xyz);
    o.uv0XY_tanXY.xy = v.uv0.xy;

#if defined(LIGHTMAP_ON) || defined(DIRLIGHTMAP_COMBINED)
    OUTPUT_LIGHTMAP_UV(v.uv1.xy, unity_LightmapST, o.uv1.xy);
#endif

#ifdef DYNAMICLIGHTMAP_ON
    OUTPUT_LIGHTMAP_UV(v.uv2.xy, unity_DynamicLightmapST, o.uv1.zw);
#endif

    // Exp2 fog
    // half clipZ_0Far = UNITY_Z_0_FAR_FROM_CLIPSPACE(o.vertex.z);
    // o.wPos_fog.w = unity_FogParams.x * clipZ_0Far;

    //#!INJECT_POINT VERTEX_NORMALS
    //#!INJECT_DEFAULT
    o.normXYZ_tanZ.xyz = normalize(TransformObjectToWorldDir(v.normal));
    o.normXYZ_tanZ.w = v.tangent.z; // avoid having the tangent optimized out to prevent issues with the depth-prepass
    //#!INJECT_END


    // Calculate vertex lights and L2 probe lighting on quest 
    o.SHVertLights_btSign.xyz = VertexLighting(UNPACK_WPOS(o), UNPACK_NORMAL(o));
#if !defined(LIGHTMAP_ON) && !defined(DYNAMICLIGHTMAP_ON) && defined(SHADER_API_MOBILE)
    o.SHVertLights_btSign.xyz += SampleSHVertex(o.normXYZ_tanZ.xyz);
#endif

    //#!INJECT_POINT VERTEX_END
    return o;
}

struct FragOut
{
    float4 color : SV_Target;
    //#!INJECT_POINT FRAG_OUT_STRUCT
};

FragOut frag(VertOut i 
    , bool frontFace : SV_IsFrontFace
    SLZ_DECLARE_FRAG_SIZE
    //#!INJECT_POINT FRAG_PARAMETERS
) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

    RequestFragmentDensityEXT();
    SLZ_SETUP_FRAG_SIZE(i.vertex.xy);

    if (!frontFace)
    {
        UNPACK_NORMAL(i) = -UNPACK_NORMAL(i);
    }
/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Read Input Data---------------------------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/

    //#!INJECT_POINT FRAG_READ_INPUTS
    //#!INJECT_DEFAULT
    float2 uv0 = UNPACK_UV0(i);
    float2 uv_main = mad(uv0, _BaseMap_ST.xy, _BaseMap_ST.zw);
    half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv_main);
    half4 mas = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_BaseMap, uv_main);
    //#!INJECT_END

    //#!INJECT_POINT FRAG_POST_READ

    //#!INJECT_POINT PBR_VALUES
    //#!INJECT_DEFAULT
    albedo *= _BaseColor;
    albedo.a = _Surface == 0 ? half(1.0) : albedo.a;
    half metallic = mas.r;
    half ao = mas.g;
    half smoothness = mas.b;
    //#!INJECT_END

    //#!INJECT_POINT FRAG_POST_INPUTS

/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Sample Normal Map-------------------------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/

    half3 normalTS = half3(0, 0, 1);
    half  geoSmooth = 1;
    half4 normalMap = half4(0, 0, 1, 0);

    //#!INJECT_POINT NORMAL_MAP

/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Read Detail Map---------------------------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/

    //#!INJECT_POINT DETAIL_MAP
    


    //#!INJECT_POINT PRE_NORMAL_TS_TO_WS

/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Transform Normals To Worldspace-----------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/

    //#!INJECT_POINT NORMAL_TRANSFORM    


/*---------------------------------------------------------------------------------------------------------------------------*/
/*---Lighting Calculations---------------------------------------------------------------------------------------------------*/
/*---------------------------------------------------------------------------------------------------------------------------*/
    
    //#!INJECT_POINT SPEC_AA
    //#!INJECT_DEFAULT
    #if !defined(SHADER_API_MOBILE) && !defined(LITMAS_FEATURE_TP) // Specular antialiasing based on normal derivatives. Only on PC to avoid cost of derivatives on Quest
        //smoothness = min(smoothness, SLZGeometricSpecularAA(UNPACK_NORMAL(i)));
        smoothness = SLZGeometricNormalFiltering(smoothness, UNPACK_NORMAL(i), /*variance*/ 0.075, /*threshold*/ 0.2);
    #endif
    //#!INJECT_END

    //#!INJECT_POINT PRE_FRAGDATA

    #if defined(LIGHTMAP_ON)
        SLZFragData fragData = SLZGetFragData(i.vertex, UNPACK_WPOS(i), normalWS, i.uv1.xy, i.uv1.zw, UNPACK_VERTLIGHTS(i));
    #else
        SLZFragData fragData = SLZGetFragData(i.vertex, UNPACK_WPOS(i), normalWS, float2(0, 0), float2(0, 0), UNPACK_VERTLIGHTS(i));
    #endif
    #if defined(SHADER_API_MOBILE)
        half antibandingNoise = AntibandingNoise(i.vertex.xy);
    #endif

    half4 emission = half4(0,0,0,0);

    //#!INJECT_POINT EMISSION

    //#!INJECT_POINT PRE_SURFDATA

    SLZSurfData surfData = SLZGetSurfDataMetallicGloss(albedo.rgb, saturate(metallic), saturate(smoothness), ao, emission.rgb, albedo.a);
    half4 color = half4(1, 1, 1, 1);

    //#!INJECT_POINT PRE_LIGHTING_CALC

    //#!INJECT_POINT LIGHTING_CALC
    //#!INJECT_DEFAULT
        color = SLZPBRFragment(fragData, surfData, _Surface);
    //#!INJECT_END


    //#!INJECT_POINT VOLUMETRIC_FOG
    //#!INJECT_DEFAULT
    //color = MixFogSurf(color, -fragData.viewDir, UNPACK_FOG(i), _Surface);
    color = VolumetricsSurf(color, fragData.position, _Surface);
    //#!INJECT_END
    
    FragOut output = (FragOut) 0;
    output.color = color;
    
    //#!INJECT_POINT MOBILE_ANTIBANDING
    //#!INJECT_DEFAULT
    #if defined(SHADER_API_MOBILE)
        // Don't do this for now, holding on to fragData.screenUV or i.vertex.xy occupies a full-precision register for the entire shader 
        ApplyInterleavedAntibanding(output.color.rgb, antibandingNoise);
    #endif
    //#!INJECT_END
    
    //#!INJECT_POINT FRAG_OUT_POPULATE

    return output;
}