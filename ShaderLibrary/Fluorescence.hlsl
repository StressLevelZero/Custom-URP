#if !defined(SLZ_FLUORESCENCE_INCLUDED)
#define SLZ_FLUORESCENCE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

// complete bullshit estimation of fluorescent intensity based on the hue of the light
// Translate hue and saturation into a spherical gausian (ok, actually a circular gaussian in this case)
// where hue is the angle and saturation 0 to 1 is remapped into a sharpness factor from 0 to infinity
// to 
half GetFluorescenceIntensityFromDiffuse(half3 diffuseLight, half excitationHue)
{
	half3 hsv = RgbToHsv(diffuseLight);
	half hue = hsv.x;
	half saturation = clamp(hsv.y, 0.01, 0.975);
	half value = hsv.z;
	
	half angle = hue - excitationHue;
	half cosA = cos(2 * PI * angle);
	half sharpness = saturation / (1 - saturation);

	half SG = exp(sharpness * (cosA - 1));
	
	return value * SG;
}

#endif