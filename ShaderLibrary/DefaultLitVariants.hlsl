#pragma once

//#pragma multi_compile_fragment _ _VOLUMETRICS_ENABLED
//#pragma multi_compile_fog
#pragma skip_variants FOG_LINEAR FOG_EXP

#if defined(SHADER_API_MOBILE)
	#define  _ADDITIONAL_LIGHTS_VERTEX	
#else
	#pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS_CASCADE
	#define _SHADOWS_SOFT 1

	#if !defined(_DISABLE_ADDLIGHTS)
		#define DYNAMIC_ADDITIONAL_LIGHTS
		#pragma multi_compile DUMMY _ADDITIONAL_LIGHTS
		#pragma skip_variants DUMMY
		#pragma dynamic_branch _ADDITIONAL_LIGHTS
		//#pragma skip_variants _ADDITIONAL_LIGHTS

		#define DYNAMIC_ADDITIONAL_LIGHT_SHADOWS
		#pragma multi_compile DUMMY _ADDITIONAL_LIGHT_SHADOWS
		#pragma skip_variants DUMMY
		#pragma dynamic_branch _ADDITIONAL_LIGHT_SHADOWS
		//#pragma skip_variants _ADDITIONAL_LIGHT_SHADOWS

	#endif

	
	#define _REFLECTION_PROBE_BLENDING
	#define _REFLECTION_PROBE_BOX_PROJECTION

	
	#if !defined(_DISABLE_SSAO)

		#define DYNAMIC_SCREEN_SPACE_OCCLUSION
		#pragma multi_compile DUMMY _SCREEN_SPACE_OCCLUSION
		#pragma skip_variants DUMMY
		#pragma dynamic_branch_fragment _SCREEN_SPACE_OCCLUSION
		//#pragma skip_variants _SCREEN_SPACE_OCCLUSION
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

