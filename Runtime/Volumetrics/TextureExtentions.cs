using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Reflection;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

//[System.Serializable]
//public struct int2
//{
//    public int x;
//    public int y;
//}
//[System.Serializable]
//public struct uint2
//{
//    public uint x;
//    public uint y;
//}
[System.Serializable]
public enum RGBA
{
    Red, Green, Blue, Alpha
}

[System.Serializable]
public enum TextureFileExtension
{
    PNG, EXR, JPG, TGA
}
public static class TextureExtentions
{  

    public static void SetComputeBuffer(this ComputeShader shader, string name,  int kernel, ComputeBuffer buffer)
    {
        //   Debug.Log("Setting buffer");
        if (buffer != null)
        {
            shader.SetBuffer(kernel, name, buffer);

            //     Debug.Log(name + " set");
        }
    }

    public static RenderTexture Copy3DSliceToRenderTexture(RenderTexture source, int layer)
    {
        
        RenderTexture render = new RenderTexture((int)source.width, (int)source.height, 0, RenderTextureFormat.ARGB32);
        render.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;
        render.enableRandomWrite = true;
        render.wrapMode = TextureWrapMode.Clamp;
        render.Create();
#if UNITY_EDITOR
        ComputeShader slicer = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/Slicer.compute"); //Todo: Fix build error. Find better way to load?

        int kernelIndex = slicer.FindKernel("CSMain");
        slicer.SetTexture(kernelIndex, "voxels", source);
        slicer.SetInt("layer", layer);
        slicer.SetTexture(kernelIndex, "Result", render);
        slicer.Dispatch(kernelIndex, (int)source.width, (int)source.height, 1);
#endif
        return render;
    }

    public static Texture2D ConvertFromRenderTexture(RenderTexture rt)
    {
        Texture2D output = new Texture2D(rt.width, rt.height);
        RenderTexture.active = rt;
        output.ReadPixels(new Rect(0, 0, rt.width, rt.height ), 0, 0);
        output.Apply();
        return output;
    }

    public static Texture2D ConvertToTexture2D(this RenderTexture rt)
    {
        Texture2D output = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, false);
        RenderTexture.active = rt;
        output.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        output.Apply();
        return output;
    }

    public static Vector2Int GetImageSize(this Texture2D asset)
    {
#if UNITY_EDITOR

        if (asset != null)
        {
            string assetPath = AssetDatabase.GetAssetPath(asset);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

            if (importer != null)
            {
                object[] args = new object[2] { 0, 0 };
                MethodInfo mi = typeof(TextureImporter).GetMethod("GetWidthAndHeight", BindingFlags.NonPublic | BindingFlags.Instance);
                mi.Invoke(importer, args);
                return new Vector2Int((int)args[0], (int)args[1]);
            }
        }
#endif
        return   new Vector2Int(0,0);
    }
    /// <summary>
    /// Releases and destroys the rendertexture
    /// </summary>
    /// <param name="renderTexture"></param>
    public static void Clear(this RenderTexture renderTexture)
    {
        if (renderTexture == null) return;
        renderTexture.Release();
        UnityEngine.Rendering.CoreUtils.Destroy(renderTexture);
    }


    public static void SaveToTexture3D(this RenderTexture Rtex3d, string NameAtPath)
    {
        //Texture3D export = new Texture3D((int)Rtex3d.width, (int)Rtex3d.height, (int)Rtex3d.depth, TextureFormat.ARGB32, false);
        //       RenderTexture selectedRenderTexture;  

        RenderTexture[] layers = new RenderTexture[(int)Rtex3d.volumeDepth];
        for (int i = 0; i < (int)Rtex3d.volumeDepth; i++)
            layers[i] = Copy3DSliceToRenderTexture(Rtex3d, i);

        Texture2D[] finalSlices = new Texture2D[Rtex3d.volumeDepth];
        for (int i = 0; i < Rtex3d.volumeDepth; i++)
            finalSlices[i] = ConvertFromRenderTexture(layers[i]);


        Texture3D output = new Texture3D((int)Rtex3d.width, (int)Rtex3d.height, (int)Rtex3d.volumeDepth, TextureFormat.RGB24, false);
        output.filterMode = FilterMode.Trilinear;
        output.wrapMode = TextureWrapMode.Clamp;
        Color[] outputPixels = output.GetPixels();

        //Temp slice export for debugging
        //for (int z = 0; z < Rtex3d.volumeDepth; z++)
        //{
        //    AssetDatabase.CreateAsset(finalSlices[z], NameAtPath + "_" + z + ".asset");

        //}

        for (int z = 0; z < Rtex3d.volumeDepth; z++)
        {
            Color[] layerPixels = finalSlices[z].GetPixels();
            for (int y = 0; y < Rtex3d.height; y++)

                for (int x = 0; x < Rtex3d.width; x++)
                {
                    // outputPixels[x + y * Rtex3d.width + z * Rtex3d.width * Rtex3d.height] = layerPixels[x + y * Rtex3d.width * Rtex3d.depth + z + Rtex3d.width];
                    outputPixels[x + y * Rtex3d.width + z * Rtex3d.width * Rtex3d.height] = layerPixels[x + y * Rtex3d.width];
                    // outputPixels[x + y * Rtex3d.width + z * Rtex3d.width * Rtex3d.height] = new Color((float)x / (float)Rtex3d.width, 0,0,0);
                }
        }

        output.SetPixels(outputPixels);
        //output.wrapMode = TextureWrapMode.Clamp; //Clamp texture in sampler too
        output.Apply();

        //     AssetDatabase.CreateAsset(output, "Assets/" + nameOfTheAsset + ".asset");
#if UNITY_EDITOR
        AssetDatabase.CreateAsset(output, NameAtPath + ".asset"); // Todo: Either disable during build or make a build version
        Debug.Log("Saved " + NameAtPath);
#endif
    }

    public static TextureFileExtension GetTextureExtension(this string path)
    {        
        return (TextureFileExtension)System.Enum.Parse(typeof(TextureFileExtension), Path.GetExtension(path) );
    }


    public static byte[] EncodeTexture(this Texture2D tex, TextureFileExtension textureFileExtension)
    {
        switch (textureFileExtension) {
            case TextureFileExtension.PNG:
        return tex.EncodeToPNG();
            case TextureFileExtension.JPG:
        return tex.EncodeToJPG();
            case TextureFileExtension.EXR:
        return tex.EncodeToEXR();
            case TextureFileExtension.TGA:
        return tex.EncodeToTGA();
        }
        return null;
    }
}
