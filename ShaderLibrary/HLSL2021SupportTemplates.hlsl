#if !defined(HLSL2021_TYPE)

    #define HLSL2021_TYPE float
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/HLSL2021SupportTemplates.hlsl"
    #undef HLSL2021_TYPE

    #define HLSL2021_TYPE min16float
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/HLSL2021SupportTemplates.hlsl"
    #undef HLSL2021_TYPE

    #define HLSL2021_TYPE int
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/HLSL2021SupportTemplates.hlsl"
    #undef HLSL2021_TYPE

    #define HLSL2021_TYPE bool
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/HLSL2021SupportTemplates.hlsl"
    #undef HLSL2021_TYPE

#elif !defined(HLSL2021_VEC)

    #define HLSL2021_VEC 2
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/HLSL2021SupportTemplates.hlsl"
    #undef HLSL2021_VEC

    #define HLSL2021_VEC 3
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/HLSL2021SupportTemplates.hlsl"
    #undef HLSL2021_VEC

    #define HLSL2021_VEC 4
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/HLSL2021SupportTemplates.hlsl"
    #undef HLSL2021_VEC

#else

    #define CCAT2(a,b) a##b

    CCAT2(HLSL2021_TYPE, HLSL2021_VEC) select(CCAT2(bool, HLSL2021_VEC) a, CCAT2(HLSL2021_TYPE, HLSL2021_VEC) b, CCAT2(HLSL2021_TYPE, HLSL2021_VEC) c) 
    { 
        return a ? b : c; 
    }

    #undef CCAT2

#endif