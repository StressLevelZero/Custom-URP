using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SLZ.SLZEditorTools
{
    internal static class LitMASGui_Tooltips
    {
        public static ReadOnlySpan<char> BaseMap => 
            "Base Color (RGB).\nThe color of the material. When the material is metallic, this also tints the reflections";

        public static ReadOnlySpan<char> BaseColor =>
            "Base color, tints the albedo map";

        public static ReadOnlySpan<char> MASMap =>
            "Metallic (R), Ambient Occlusion (G), Smoothness (B).\nThe metallic controls how reflective the surface is and how much the albedo tints reflections. " +
            "Ambient occlusion is fake pre-baked shadows that darkens areas like crevices or creases which are likely to be shadowed by surface itself. " +
            "Smoothness controls the sharpness of reflections, and for non-metallic surfaces the strength of reflections.";

        public static ReadOnlySpan<char> NormalMap =>
            "Unused (R), Normal Oct Y (G), Geometric Roughness (B), Normal Oct X (A).\nVector map that offsets the normal (direction the surface is facing) when calculating lighting. Used to add high-resolution detail to otherwise simple mesh geometry. " +
            "Also contains roughness calculated from the normal variance for the mips in the blue channel. Assumes normals are encoded into hemi-octahedral format and stored in the G and A channels. " +
            "The texture importer will by default automatically convert textures marked as normal maps to hemi-octahedral format and calculate geometric roughness";

        public static ReadOnlySpan<char> EmissionMap =>
            "Emission Color (RGB).\nControls the intensity and color of light being emitted by the material";

        public static ReadOnlySpan<char> EmissionColor =>
             "Tints the emission from the emission map. Additionally, the alpha channel controls " +
             "how much the albedo map tints the emission. As the alpha goes to 0, the emission is increasingly tinted by the albedo";

        public static ReadOnlySpan<char> EmissionFalloff =>
            "Controls the strength of the emission fresenel. This is an effect where the strength of the emission decreases as the surface points farther away from the camera.";

        public static ReadOnlySpan<char> EmissionBakedMultiplier =>
            "Artificially increases the strength of the emission when baking lights. WARNING: this will make specular lighting too " +
            "dark for other shaders that don't guestimate the specular from diffuse light probes. Shaders that do not use Bonelab's " +
            "custom lighting model will most likely only get specular lighting from reflection probes, and this " +
            "multiplier does not effect the brightness of this material as seen by reflection probes.";

        public static ReadOnlySpan<char> DetailMap =>
            "Overlay Brighten/Darken (R), Normal X (G), Geometric Roughness (B), Normal Y (A).\nRed channel brightens or darkens the material, with 0.5 being neutral. " +
            "The rest of the channels are exactly like the normal map, and are blended with the normal map to allow having two different scales of normal details";


    }
}
