using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;

[System.Serializable]
public struct PackingLayout
{
    [Tooltip("Which property will this texture be applied to")]
    public string PropertyName;
    [Header("Input textures")]
    [Header("Red Channel") ]
    public string RedInputProperty;
    public ChannelOptions RedOptions;

    [Header("Green Channel")]
    public string GreenInputProperty;
    public ChannelOptions GreenOptions;

    [Header("Blue Channel")]
    public string BlueInputProperty;
    public ChannelOptions BlueOptions;

    [Header("Alpha Channel")]
    public string AlphaInputProperty;
    public ChannelOptions AlphaOptions;

    public PackingOptions packingOptions;
}

[System.Serializable]
public struct PackingOptions
{
    [Header("Advanced Options")]
    [Tooltip("Overrides the texture in the target property slot on the material. New doesn't override anything")]
    public OverrideSlot OverrideTexture;
    [Tooltip("Is the outputted texture sRGB")]
    public bool sRGB;
    [Tooltip("Disable the alpha Channel if unused by shader. Halves the file size.")]
    public bool EnableAlphaChannel;
    [Tooltip("Uncompresses and unclamps the resolution of the source textures before transfering data")]
    public bool UncompressBeforeTask;
    [Tooltip("Adds suffix identifier data to the file name. Does nothing if Override Texture is turned on.")]
    public string Suffix;
    [Tooltip("Deletes source textures. Will keep the overrided texture if it's the same")]
    public bool DeleteSource;
    [Tooltip("Extension and encoding of the saved file. Overriding textures ignores this.")]
    public TextureFileExtension textureFileExtension;
    [Tooltip("Compression Level of saved texture")]
    public TextureImporterCompression textureCompression;
}

[System.Serializable]
public enum OverrideSlot
{
    New, Red, Green, Blue, Alpha
}

[System.Serializable]
public enum DefaultColor
{ 
    Black,White,Gray,LinearGray
}

    [System.Serializable]
public struct ChannelOptions
{
    [Tooltip("Picks which channel pull from")]
    public RGBA InputChannel;
    [Tooltip("Inverts the input map")]
    public bool invert;
    [Tooltip("What's the default value if there's no map")]
    public DefaultColor defaultColor;
}



//Used to parse out textures
[System.Serializable]
public struct PackingTargetLayout
{
    public string PropertyName;
    [Header("Red Channel")]
    public Texture2D RedInputTexture;
    public ChannelOptions RedOptions;

    [Header("Green Channel")]
    public Texture2D GreenInputTexture;
    public ChannelOptions GreenOptions;

    [Header("Blue Channel")]
    public Texture2D BlueInputTexture;
    public ChannelOptions BlueOptions;

    [Header("Alpha Channel")]
    public Texture2D AlphaInputTexture;
    public ChannelOptions AlphaOptions;

    public PackingOptions packingOptions;
    }



[CreateAssetMenu(fileName = "PackingTemplate", menuName = "Rendering/Texture Packing Template", order = 5)]
public class TexturePackingTemplate : ScriptableObject
{
    public Shader TargetShader;
    public PackingLayout[] Packing;
}