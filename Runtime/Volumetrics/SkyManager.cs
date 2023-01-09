using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;

public class SkyManager
{
    public static Texture skytexture;

    static readonly int ID_SkyTexture = Shader.PropertyToID("_SkyTexture");
    static readonly int ID_SkyMipCount = Shader.PropertyToID("_SkyMipCount");
    static readonly int ID_MipFogParam = Shader.PropertyToID("_MipFogParameters");


    static void GenerateSkyTexture()
    {
        //Generate Skybox
        RenderTexture cubetex = new RenderTexture(256, 256, 1, RenderTextureFormat.DefaultHDR);
        cubetex.enableRandomWrite = true;
        cubetex.useMipMap = true;
        cubetex.dimension = UnityEngine.Rendering.TextureDimension.Cube;
        cubetex.autoGenerateMips = true;
        //cubetex.
        //    cubetex.Create();

        Camera renderCam = new GameObject().AddComponent<Camera>();
        renderCam.gameObject.hideFlags = HideFlags.DontSave;
        renderCam.enabled = false;
        renderCam.cullingMask = 0;
        renderCam.backgroundColor = Color.black;
        renderCam.clearFlags = CameraClearFlags.Skybox;
        renderCam.RenderToCubemap(cubetex);
        //cubetex.GenerateMips();
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            Object.Destroy(renderCam.gameObject);
        }
        else
        {
            Object.DestroyImmediate(renderCam.gameObject);
        }
#else
        Object.Destroy(renderCam.gameObject);
#endif
        skytexture = cubetex;
    }
    static public void CheckSky()
    {
        if (skytexture == null) GenerateSkyTexture();

        if (RenderSettings.defaultReflectionMode == DefaultReflectionMode.Custom)
        {
            if (RenderSettings.customReflection != null) SetSkyTexture(RenderSettings.customReflection);
            else SetSkyTexture(CoreUtils.blackCubeTexture);
        }
        else //DefaultReflectionMode.Skybox
        {
            if (skytexture != null)
            {
                SetSkyTexture(skytexture);
            }
            else SetSkyTexture(CoreUtils.blackCubeTexture);
        }
     //   if (Shader.GetGlobalTexture(ID_SkyTexture) != null) return;              
    }

    static public void SetSkyMips(Vector4 MipFogParam)
    {
        Shader.SetGlobalVector(ID_MipFogParam, MipFogParam);
    }

    static public void SetSkyTexture(Texture SkyTex)
    {
        Shader.SetGlobalTexture(ID_SkyTexture, SkyTex);
        Shader.SetGlobalInt(ID_SkyMipCount, SkyTex.mipmapCount);
    }

    void ClearRenderTexture(RenderTexture rt, Color color)
    {
        RenderTexture activeRT = RenderTexture.active;
        RenderTexture.active = rt;
        GL.sRGBWrite = rt.sRGB;
        if (rt.dimension == TextureDimension.Tex3D)
        {
            for (int i = 0; i < rt.depth; i++)
            {
                Graphics.SetRenderTarget(rt, 0, CubemapFace.Unknown, i);
                GL.Clear(false, true, color);
            }

        }
        else if (rt.dimension == TextureDimension.Cube)
        {
            Graphics.SetRenderTarget(rt, 0, CubemapFace.PositiveX, 0);
            GL.Clear(false, true, color);
            Graphics.SetRenderTarget(rt, 0, CubemapFace.PositiveY, 0);
            GL.Clear(false, true, color);
            Graphics.SetRenderTarget(rt, 0, CubemapFace.PositiveZ, 0);
            GL.Clear(false, true, color);
            Graphics.SetRenderTarget(rt, 0, CubemapFace.NegativeX, 0);
            GL.Clear(false, true, color);
            Graphics.SetRenderTarget(rt, 0, CubemapFace.NegativeY, 0);
            GL.Clear(false, true, color);
            Graphics.SetRenderTarget(rt, 0, CubemapFace.NegativeZ, 0);
            GL.Clear(false, true, color);
        }

        RenderTexture.active = activeRT;
    }
}
