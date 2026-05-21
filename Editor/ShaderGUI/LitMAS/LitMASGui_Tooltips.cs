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
            "<color=#e45252>(R) = Metallic</color>    \n" +
            "<color=#30d137>(G) = Ambient Occlusion</color>    \n" +
            "<color=#2f82ff>(B) = Smoothness</color>    \n";

        public static ReadOnlySpan<char> AYSXMap =>
            "<color=#e45252>(R) = Ambient Occlusion</color>    \n" +
            "<color=#30d137>(G) = Hemi-octahedral Normal X</color>    \n" +
            "<color=#2f82ff>(B) = Smoothness</color>    \n" +
            "<color=#ffffff>(A) = Hemi-octahedral Normal Y</color>    ";

        public static ReadOnlySpan<char> NormalMap =>
            "<color=#e45252>(R) = Unused</color>    \n" +
            "<color=#30d137>(G) = Hemi-octahedral Normal X</color>    \n" +
            "<color=#2f82ff>(B) = Geometric Roughness</color>    \n" +
            "<color=#ffffff>(A) = Hemi-octahedral Normal Y</color>    ";

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
            "<color=#e45252>(R) = Overlay Brighten/Darken Albedo</color>  \n<color=#30d137>(G) = Normal X</color>  \n<color=#2f82ff>(B) = Overlay Roughen/Polish Smoothness</color>  \n<color=white>(A) = Normal Y</color>" + 
            "\n \nRed channel brightens or darkens the base material, with 0.5 being neutral. " +
            "Normals are blended with the base normal map. " + "Blue channel Roughen or Polishes the base material, with 0.5 being neutral";

        public static ReadOnlySpan<char> Surface =>
            "How the material is blended with the background. Opaque does what you'd expect and does not blend with what's behind it. Transparent is physically transparent, ie " +
            "only the diffuse lighting component is alpha blended with the background. Specular reflections off the surface are unaffected by alpha and are added " +
            "to the background. Thus you can still see reflections off a 0 alpha transparent object. Addtionally, as the metallic goes to 1, the material becomes opaque. Fade is " +
            "simple alpha blending; as the alpha goes to 0 the material fully disappears";

        public static ReadOnlySpan<char> FluorMap =>
            "Map of the fluorescent strength and tint";

        public static ReadOnlySpan<char> FluorColor =>
            "Color multiplier on top of the fluorescence map";
        
        public static ReadOnlySpan<char> FluorAbsorbance =>
            "For each of the red, green, blue, and alpha channels of incoming light, how much is converted into light of the color determined by the fluorescence map and tint."+
            " The alpha channel represents UV, but is only available from realtime light sources.";

        public static ReadOnlySpan<char> FluorAlbedoTint =>
            "How much the base map and color tint the fluorescence";

       public static ReadOnlySpan<char> SplatMap =>
            "Contains the weights layers 1 through 4 encoded into the R,G,B, and A channels. The weight of layer 0 is implicitly stored as 1 minus the sum of the other layer weights";
        
        public static ReadOnlySpan<char> HeightMap =>
            "<color=#e45252>(R) = Splat Weight Multiplier</color>\nScales the weight of the layer in the Splatmap to give more natural looking blending between layers.";
    }
}
