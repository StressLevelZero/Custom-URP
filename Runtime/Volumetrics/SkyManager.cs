using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

[InitializeOnLoad] 
#endif
public static class SkyManager
{
    public static Texture skytexture;

    static readonly int ID_SkyTexture = Shader.PropertyToID("_SkyTexture");
    static readonly int ID_SkyMipCount = Shader.PropertyToID("_SkyMipCount");
    static readonly int ID_MipFogParam = Shader.PropertyToID("_MipFogParameters");
    
    static SkyManager()
    {
        SetSkyMips(new Vector4(0, 1, 1, 0));
#if UNITY_EDITOR
        EditorApplication.delayCall += DelayedCheckSky; //Delaying first call when loaded
        EditorSceneManager.sceneOpened -= SceneOpenedCallback;
        EditorSceneManager.sceneOpened += SceneOpenedCallback;
#endif
        //Double checking that this doesn't exist. We purposely don't unregister it because we need it constantly called whenever there's a change.
        SceneManager.sceneLoaded -= OnSceneLoaded; 
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        CheckSky();
    }
    
    
#if UNITY_EDITOR
    static void SceneOpenedCallback(Scene scene, OpenSceneMode mode)
    {
        Debug.Log(mode + " : " +scene);
        if (!EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            CheckSky();
        }
        else
        {
            EditorApplication.delayCall += DelayedCheckSky;
        }
    }
    
    static void DelayedCheckSky()
    {
        CheckSky();
        EditorApplication.delayCall -= DelayedCheckSky;
    }
#endif


    static void GenerateSkyTexture()
    {
        //Generate Skybox
        RenderTexture cubetex = new RenderTexture(256, 256, 1, RenderTextureFormat.DefaultHDR);
        cubetex.enableRandomWrite = true;
        cubetex.useMipMap = true;
        cubetex.dimension = UnityEngine.Rendering.TextureDimension.Cube;
        cubetex.autoGenerateMips = true;
        cubetex.name = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "_MipSky";
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
        Debug.Log("Generated sky: " + cubetex.name );

    }
    static public void CheckSky()
    {
        //Debug.Log("Running CheckSky");
        if (skytexture == null)
        {
            GenerateSkyTexture();
        }

        if (RenderSettings.defaultReflectionMode == DefaultReflectionMode.Custom)
        {
            if (RenderSettings.customReflectionTexture != null && RenderSettings.customReflectionTexture.GetType() == typeof(Cubemap)) SetSkyTexture(RenderSettings.customReflectionTexture);
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

    // void ClearRenderTexture(RenderTexture rt, Color color)
    // {
    //     RenderTexture activeRT = RenderTexture.active;
    //     RenderTexture.active = rt;
    //     GL.sRGBWrite = rt.sRGB;
    //     if (rt.dimension == TextureDimension.Tex3D)
    //     {
    //         for (int i = 0; i < rt.depth; i++)
    //         {
    //             Graphics.SetRenderTarget(rt, 0, CubemapFace.Unknown, i);
    //             GL.Clear(false, true, color);
    //         }
    //
    //     }
    //     else if (rt.dimension == TextureDimension.Cube)
    //     {
    //         Graphics.SetRenderTarget(rt, 0, CubemapFace.PositiveX, 0);
    //         GL.Clear(false, true, color);
    //         Graphics.SetRenderTarget(rt, 0, CubemapFace.PositiveY, 0);
    //         GL.Clear(false, true, color);
    //         Graphics.SetRenderTarget(rt, 0, CubemapFace.PositiveZ, 0);
    //         GL.Clear(false, true, color);
    //         Graphics.SetRenderTarget(rt, 0, CubemapFace.NegativeX, 0);
    //         GL.Clear(false, true, color);
    //         Graphics.SetRenderTarget(rt, 0, CubemapFace.NegativeY, 0);
    //         GL.Clear(false, true, color);
    //         Graphics.SetRenderTarget(rt, 0, CubemapFace.NegativeZ, 0);
    //         GL.Clear(false, true, color);
    //     }
    //
    //     RenderTexture.active = activeRT;
    // }
}
