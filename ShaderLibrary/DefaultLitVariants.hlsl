#pragma once

//#pragma multi_compile_fragment _ _VOLUMETRICS_ENABLED
//#pragma multi_compile_fog
#pragma skip_variants FOG_LINEAR

#if defined(SHADER_API_MOBILE)
	#pragma multi_compile _  _ADDITIONAL_LIGHTS_VERTEX	
#else
	#pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS_CASCADE
	#pragma multi_compile_fragment _ _SHADOWS_SOFT

	#if !defined(_DISABLE_ADDLIGHTS)
		#pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
		#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
	#endif

	#if !defined(_DISABLE_REFLECTIONPROBES)
		#pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
		#define _REFLECTION_PROBE_BOX_PROJECTION
	#endif
	
	#if !defined(_DISABLE_SSAO)
		#pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
	#endif

#endif

#if !defined(_DISABLE_LIGHTMAPS)
	//#pragma multi_compile_fragment _ _MIXED_LIGHTING_SUBTRACTIVE
	#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
	#pragma multi_compile _ DIRLIGHTMAP_COMBINED
	#pragma multi_compile _ LIGHTMAP_ON
	#pragma multi_compile _ DYNAMICLIGHTMAP_ON
#endif

#pragma multi_compile _ SHADOWS_SHADOWMASK
#pragma multi_compile_fragment _ _LIGHT_COOKIES

