// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)

Shader "SLZ/Skybox/SLZ Procedural" {
Properties {
    //[KeywordEnum(None, Simple, High Quality)] _SunDisk ("Sun", Int) = 2
    _SunSize ("Sun Size", Range(0,1)) = 0.01
    _SunSizeConvergence("Sun Size Convergence", Range(1,10)) = 5

    _AtmosphereThickness ("Atmosphere Thickness", Range(0,5)) = 1.0
    [HDR]_SkyTint ("Sky Tint", Color) = (.5, .5, .5, 1)
    _GroundColor ("Ground", Color) = (.369, .349, .341, 1)

    _Exposure("Exposure", Range(0, 8)) = 1.3
}

SubShader {
    Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
    Cull Off ZWrite Off
    Offset 0.0008148, 0

    Pass {

        HLSLPROGRAM
        #pragma vertex vert
        #pragma fragment frag

        #pragma multi_compile _ DRAW_SKY_PROCEDURAL
        #pragma multi_compile_fragment _ _VOLUMETRICS_ENABLED
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/FullscreenSky.hlsl"

		
        #define _SUNDISK_HIGH_QUALITY

        uniform half _Exposure;     // HDR exposure
        uniform half3 _GroundColor;
        uniform half _SunSize;
        uniform half _SunSizeConvergence;
        uniform half3 _SkyTint;
        uniform half _AtmosphereThickness;
        //CBUFFER_START(UnityLighting)

        //CBUFFER_END
    #if defined(DRAW_SKY_PROCEDURAL)
        uniform half4 _WorldSpaceLightPosSun;
        uniform half4 _LightColorSun;
    #else

    CBUFFER_START(UnityLighting)


    half4 _WorldSpaceLightPos0;
    
    CBUFFER_END
        half4 _LightColor0;
        #define _WorldSpaceLightPosSun _WorldSpaceLightPos0
        #define _LightColorSun _LightColor0
    #endif

    #if defined(UNITY_COLORSPACE_GAMMA)
        #define GAMMA 2
        #define COLOR_2_GAMMA(color) color
        #define COLOR_2_LINEAR(color) color*color
        #define LINEAR_2_OUTPUT(color) sqrt(color)
    #else
        #define GAMMA 2.2
        // HACK: to get gfx-tests in Gamma mode to agree until UNITY_ACTIVE_COLORSPACE_IS_GAMMA is working properly
        #define COLOR_2_GAMMA(color) color
        #define COLOR_2_LINEAR(color) color
        #define LINEAR_2_LINEAR(color) color
    #endif

        // RGB wavelengths
        // .35 (.62=158), .43 (.68=174), .525 (.75=190)
        static const float3 kDefaultScatteringWavelength = float3(.65, .57, .475);
        static const float3 kVariableRangeForScatteringWavelength = float3(.15, .15, .15);

        #define OUTER_RADIUS 1.025
        static const float kOuterRadius = OUTER_RADIUS;
        static const float kOuterRadius2 = OUTER_RADIUS*OUTER_RADIUS;
        static const float kInnerRadius = 1.0;
        static const float kInnerRadius2 = 1.0;

        static const float kCameraHeight = 0.0001;

        #define kRAYLEIGH (lerp(0.0, 0.0025, _AtmosphereThickness*_AtmosphereThickness))      // Rayleigh constant
        #define kMIE 0.0010             // Mie constant
        #define kSUN_BRIGHTNESS 20.0    // Sun brightness

        #define kMAX_SCATTER 50.0 // Maximum scattering value, to prevent math overflows on Adrenos

        static const half kHDSundiskIntensityFactor = 15.0;
        static const half kSimpleSundiskIntensityFactor = 27.0;

        static const half kSunScale = 400.0 * kSUN_BRIGHTNESS;
        static const float kKmESun = kMIE * kSUN_BRIGHTNESS;
        static const float kKm4PI = kMIE * 4.0 * 3.14159265;
        static const float kScale = 1.0 / (OUTER_RADIUS - 1.0);
        static const float kScaleDepth = 0.25;
        static const float kScaleOverScaleDepth = (1.0 / (OUTER_RADIUS - 1.0)) / 0.25;
        static const float kSamples = 2.0; // THIS IS UNROLLED MANUALLY, DON'T TOUCH

        #define MIE_G (-0.990)
        #define MIE_G2 0.9801

        #define SKY_GROUND_THRESHOLD 0.02

        // fine tuning of performance. You can override defines here if you want some specific setup
        // or keep as is and allow later code to set it according to target api

        // if set vprog will output color in final color space (instead of linear always)
        // in case of rendering in gamma mode that means that we will do lerps in gamma mode too, so there will be tiny difference around horizon
        // #define SKYBOX_COLOR_IN_TARGET_COLOR_SPACE 0

        // sun disk rendering:
        // no sun disk - the fastest option
        #define SKYBOX_SUNDISK_NONE 0
        // simplistic sun disk - without mie phase function
        #define SKYBOX_SUNDISK_SIMPLE 1
        // full calculation - uses mie phase function
        #define SKYBOX_SUNDISK_HQ 2

        // uncomment this line and change SKYBOX_SUNDISK_SIMPLE to override material settings
        // #define SKYBOX_SUNDISK SKYBOX_SUNDISK_SIMPLE

    #ifndef SKYBOX_SUNDISK
        #if defined(_SUNDISK_NONE)
            #define SKYBOX_SUNDISK SKYBOX_SUNDISK_NONE
        #elif defined(_SUNDISK_SIMPLE)
            #define SKYBOX_SUNDISK SKYBOX_SUNDISK_SIMPLE
        #else
            #define SKYBOX_SUNDISK SKYBOX_SUNDISK_HQ
        #endif
    #endif

    #ifndef SKYBOX_COLOR_IN_TARGET_COLOR_SPACE
        #if defined(SHADER_API_MOBILE)
            #define SKYBOX_COLOR_IN_TARGET_COLOR_SPACE 1
        #else
            #define SKYBOX_COLOR_IN_TARGET_COLOR_SPACE 0
        #endif
    #endif

        // Calculates the Rayleigh phase function
        half getRayleighPhase(half eyeCos2)
        {
            return 0.75 + 0.75*eyeCos2;
        }
        half getRayleighPhase(half3 light, half3 ray)
        {
            half eyeCos = dot(light, ray);
            return getRayleighPhase(eyeCos * eyeCos);
        }


        struct appdata_t
        {
            #if DRAW_SKY_PROCEDURAL
                uint vertexID : SV_VertexID;
            #else
                float4 vertex : POSITION;
            #endif
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct v2f
        {
            float4  pos             : SV_POSITION;

       
            // for HQ sun disk, we need vertex itself to calculate ray-dir per-pixel
            float3  wPos          : TEXCOORD0;
            float3  cPos          : TEXCOORD1;
            
         #if SKYBOX_SUNDISK == SKYBOX_SUNDISK_NONE
            // as we dont need sun disk we need just rayDir.y (sky/ground threshold)
            half    skyGroundFactor : TEXCOORD2;
        #endif

            UNITY_VERTEX_OUTPUT_STEREO
        };


        float scale(float inCos)
        {
            float x = 1.0 - inCos;
            return 0.25 * exp(-0.00287 + x*(0.459 + x*(3.83 + x*(-6.80 + x*5.25))));
        }

        v2f vert (appdata_t v)
        {
            v2f OUT;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
        #if defined(DRAW_SKY_PROCEDURAL)
            GetSkyVertexPos(v.vertexID, OUT.pos, OUT.wPos);
            OUT.cPos = OUT.wPos - _WorldSpaceCameraPos;
        #else
    
            OUT.wPos = TransformObjectToWorld(v.vertex.xyz);
            OUT.pos = TransformWorldToHClip(OUT.wPos);
            OUT.cPos = OUT.wPos;
        #endif

        #if defined(UNITY_COLORSPACE_GAMMA) && SKYBOX_COLOR_IN_TARGET_COLOR_SPACE
            OUT.groundColor = sqrt(OUT.groundColor);
            OUT.skyColor    = sqrt(OUT.skyColor);
            #if SKYBOX_SUNDISK != SKYBOX_SUNDISK_NONE
                OUT.sunColor= sqrt(OUT.sunColor);
            #endif
        #endif

            return OUT;
        }


        // Calculates the Mie phase function
        half getMiePhase(float eyeCos, float eyeCos2)
        {
            half temp = 1.0 + MIE_G2 - 2.0 * MIE_G * eyeCos;
            temp = pow(temp, sqrt(_SunSize) * 10);
            temp = max(temp,1.0e-4); // prevent division by zero, esp. in half precision
            temp = 1.5 * ((1.0 - MIE_G2) / (2.0 + MIE_G2)) * (1.0 + eyeCos2) / temp;
            #if defined(UNITY_COLORSPACE_GAMMA) && SKYBOX_COLOR_IN_TARGET_COLOR_SPACE
                temp = pow(temp, .454545);
            #endif
            return temp;
        }

        // Calculates the sun shape
        half calcSunAttenuation(float3 lightPos, float3 ray)
        {
        #if SKYBOX_SUNDISK == SKYBOX_SUNDISK_SIMPLE
            half3 delta = lightPos - ray;
            half dist = length(delta);
            half spot = 1.0 - smoothstep(0.0, _SunSize, dist);
            return spot * spot;
        #else // SKYBOX_SUNDISK_HQ
            float focusedEyeCos = pow(saturate(dot(lightPos, ray)), _SunSizeConvergence);
            return getMiePhase(-focusedEyeCos, focusedEyeCos * focusedEyeCos);
        #endif
        }



        half4 frag (v2f IN) : SV_Target
        {
    
            half3 kSkyTintInGammaSpace = COLOR_2_GAMMA(_SkyTint); // convert tint from Linear back to Gamma
            half3 kScatteringWavelength = lerp (
                kDefaultScatteringWavelength-kVariableRangeForScatteringWavelength,
                kDefaultScatteringWavelength+kVariableRangeForScatteringWavelength,
                half3(1,1,1) - kSkyTintInGammaSpace); // using Tint in sRGB gamma allows for more visually linear interpolation and to keep (.5) at (128, gray in sRGB) point
            half3 kInvWavelength = 1.0 / pow(kScatteringWavelength, 4);

            half kKrESun = kRAYLEIGH * kSUN_BRIGHTNESS;
            half kKr4PI = kRAYLEIGH * 4.0 * 3.14159265;

            half3 cameraPos = half3(0,kInnerRadius + kCameraHeight,0);    // The camera's current position

            // Get the ray from the camera to the vertex and its length (which is the far point of the ray passing through the atmosphere)
            half3 eyeRay = normalize(IN.cPos);

            half far = 0.0;
            half3 cIn, cOut;
            
            bool aboveHorizon = eyeRay.y >= 0.0;
    
            far = aboveHorizon ? sqrt(kOuterRadius2 + kInnerRadius2 * eyeRay.y * eyeRay.y - kInnerRadius2) - kInnerRadius * eyeRay.y :
                                 (-kCameraHeight) / (min(-0.001, eyeRay.y));

            float3 pos = cameraPos + far * eyeRay;
            // Initialize the scattering loop variables
            float sampleLength = far / kSamples;
            float scaledLength = sampleLength * kScale;
            float3 sampleRay = eyeRay * sampleLength;
            float3 samplePoint = cameraPos + sampleRay * 0.5;
            half depth = exp((-kCameraHeight) * ( aboveHorizon ? kScaleOverScaleDepth : 1.0/kScaleDepth));
            if(eyeRay.y >= 0.0)
            {

                // Sky
                // Calculate the length of the "atmosphere"


                // Calculate the ray's starting position, then calculate its scattering offset
                half height = kInnerRadius + kCameraHeight;
                half startAngle = dot(eyeRay, cameraPos) / height;
                half startOffset = depth*scale(startAngle);
                // Now loop through the sample rays
                half3 frontColor = half3(0.0, 0.0, 0.0);
                // Weird workaround: WP8 and desktop FL_9_3 do not like the for loop here
                // (but an almost identical loop is perfectly fine in the ground calculations below)
                // Just unrolling this manually seems to make everything fine again.
                [unroll] for(int i=0; i<int(kSamples); i++)
                {
                    half height = length(samplePoint);
                    half depth = exp(kScaleOverScaleDepth * (kInnerRadius - height));
                    half lightAngle = dot(_WorldSpaceLightPosSun.xyz, samplePoint) / height;
                    half cameraAngle = dot(eyeRay, samplePoint) / height;
                    half scatter = (startOffset + depth*(scale(lightAngle) - scale(cameraAngle)));
                    half3 attenuate = exp(-clamp(scatter, 0.0, kMAX_SCATTER) * (kInvWavelength * kKr4PI + kKm4PI));

                    frontColor += attenuate * (depth * scaledLength);
                    samplePoint += sampleRay;
                }



                // Finally, scale the Mie and Rayleigh colors and set up the varying variables for the pixel shader
                cIn = frontColor * (kInvWavelength * kKrESun);
                cOut = frontColor * kKmESun;
            }
            else
            {
                // Ground
                // Calculate the ray's starting position, then calculate its scattering offset
                
                half cameraAngle = dot(-eyeRay, pos);
                half lightAngle = dot(_WorldSpaceLightPosSun.xyz, pos);
                half cameraScale = scale(cameraAngle);
                half lightScale = scale(lightAngle);
                half cameraOffset = depth*cameraScale;
                half temp = (lightScale + cameraScale);



                // Now loop through the sample rays
                half3 frontColor = half3(0.0, 0.0, 0.0);
                half3 attenuate;
                [unroll] for(int i=0; i<int(kSamples); i++) // Loop removed because we kept hitting SM2.0 temp variable limits. Doesn't affect the image too much.
                {
                    half height = length(samplePoint);
                    half depth = exp(kScaleOverScaleDepth * (kInnerRadius - height));
                    half scatter = depth*temp - cameraOffset;
                    attenuate = exp(-clamp(scatter, 0.0, kMAX_SCATTER) * (kInvWavelength * kKr4PI + kKm4PI));
                    frontColor += attenuate * (depth * scaledLength);
                    samplePoint += sampleRay;
                }

                cIn = frontColor * (kInvWavelength * kKrESun + kKmESun);
                cOut = clamp(attenuate, 0.0, 1.0);
            }


            // if we want to calculate color in vprog:
            // 1. in case of linear: multiply by _Exposure in here (even in case of lerp it will be common multiplier, so we can skip mul in fshader)
            // 2. in case of gamma and SKYBOX_COLOR_IN_TARGET_COLOR_SPACE: do sqrt right away instead of doing that in fshader

            half3 groundColor = _Exposure * (cIn + COLOR_2_LINEAR(_GroundColor) * cOut);
            half3 skyColor    = _Exposure * (cIn * getRayleighPhase(_WorldSpaceLightPosSun.xyz, -eyeRay));

        #if SKYBOX_SUNDISK != SKYBOX_SUNDISK_NONE
            // The sun should have a stable intensity in its course in the sky. Moreover it should match the highlight of a purely specular material.
            // This matching was done using the standard shader BRDF1 on the 5/31/2017
            // Finally we want the sun to be always bright even in LDR thus the normalization of the lightColor for low intensity.
            half lightColorIntensity = clamp(length(_LightColorSun.xyz), 0.25, 1);
           
            half3 sunColor    = kHDSundiskIntensityFactor * saturate(cOut) * _LightColorSun.xyz / lightColorIntensity;

        #endif
    
    
            half3 col = half3(0.0, 0.0, 0.0);

    
    
        // if y > 1 [eyeRay.y < -SKY_GROUND_THRESHOLD] - ground
        // if y >= 0 and < 1 [eyeRay.y <= 0 and > -SKY_GROUND_THRESHOLD] - horizon
        // if y < 0 [eyeRay.y > 0] - sky
        #if SKYBOX_SUNDISK == SKYBOX_SUNDISK_HQ
            half3 ray = -eyeRay;
            half y = ray.y / SKY_GROUND_THRESHOLD;
        #elif SKYBOX_SUNDISK == SKYBOX_SUNDISK_SIMPLE
            half3 ray = IN.rayDir.xyz;
            half y = ray.y / SKY_GROUND_THRESHOLD;
        #else
            half y = skyGroundFactor;
        #endif

            // if we did precalculate color in vprog: just do lerp between them
            col = lerp(skyColor, groundColor, saturate(y));

        #if SKYBOX_SUNDISK != SKYBOX_SUNDISK_NONE
            if(y < 0.0)
            {
                col += sunColor * calcSunAttenuation(_WorldSpaceLightPosSun.xyz, -ray);
            }
        #endif

        #if defined(UNITY_COLORSPACE_GAMMA) && !SKYBOX_COLOR_IN_TARGET_COLOR_SPACE
            col = LINEAR_2_OUTPUT(col);
        #endif
            half4 finalColor = Volumetrics( half4(col,1.0), IN.wPos.xyz);
            //finalColor.rg = fmod(IN.pos.xy, 32.0) / 32.0;
    
            // Dither the sky so we don't see banding.
            const half ditherMatrix[4][4] =
            {
            {   -0.5,       0,  -0.375,  0.125},
            {   0.25,   -0.25,   0.375, -0.125},
            {-0.3125,  0.1875, -0.4375, 0.0625},
            { 0.4375, -0.0625,  0.3125,-0.1875}
            };
            
            int2 ditherCoords = int2(fmod(IN.pos.xy, 4.0));
            half dither = ditherMatrix[ditherCoords.y][ditherCoords.x];
            // Derivative of the Linear->SRGB conversion function at the final color. We want to offset by +/- 1/255 in SRGB space
            //float3 offset = finalColor.rgb > 0.0031308 ? (12.92).xxx : 1.055 * ( (1.0 / 2.4) * pow(finalColor.rgb, -0.5833333333333));
            //finalColor.rgb += 1/(32 * offset) * dither;
            finalColor.rgb += (1.0/255.0) * dither;
            return finalColor;

        }
        ENDHLSL
    }
}


Fallback Off
//CustomEditor "SkyboxProceduralShaderGUI"
}
