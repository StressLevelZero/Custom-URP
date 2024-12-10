//#!INJECT_BEGIN UNIVERSAL_DEFINES 0
#pragma shader_feature_local_fragment _ALPHATEST_ON
//#!INJECT_END

//#!INJECT_BEGIN FRAG_POST_READ 0
#if defined(_ALPHATEST_ON)
	clip((albedo.a * _BaseColor.a) - _Cutoff);
#endif
//#!INJECT_END