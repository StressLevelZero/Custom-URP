using System.Collections;
using System.Collections.Generic;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Linq;
using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Class which stores a single rendertexture, and handles (re)initializing it with a given rendertexture descriptor
    /// </summary>
    public class RTPermanentHandle : IDisposable
    {
        public RenderTexture renderTexture;

        public RenderTexture GetRenderTexture(RenderTextureDescriptor desc)
        {

            if (renderTexture != null)
            {

                if (desc.width == renderTexture.width &&
                    desc.height == renderTexture.height &&
                    desc.colorFormat == renderTexture.format)
                {
                    return renderTexture;
                }
                else
                {
                    clearRT();
                }
            }
            //Debug.Log("New RenderTexture?");
            renderTexture = new RenderTexture(desc);
            renderTexture.name = "PersistentRT";

            return renderTexture;
        }

        public void Dispose()
        {

            clearRT();

        }

        public void clearRT()
        {
            if (renderTexture != null)
            {
                renderTexture.DiscardContents();
                renderTexture.Release();
#if UNITY_EDITOR
                if (Application.isPlaying)
                {
                    Object.Destroy(renderTexture);
                }
                else
                {
                    Object.DestroyImmediate(renderTexture);
                }
#else
                Object.Destroy(renderTexture);
#endif
            }
        }
    }


    /// <summary>
    /// Class that stores a dictionary pairing a camera with a permanent rendertexture. 
    /// </summary>
    public class SLZPerCameraRTStorage : IDisposable
    {
        public Dictionary<Camera, RTPermanentHandle> perCameraRTHandle;

        public SLZPerCameraRTStorage()
        {
            perCameraRTHandle = new Dictionary<Camera, RTPermanentHandle>();
        }

        public void RemoveCamera(Camera cam)
        {
            RTPermanentHandle handle;
            if (perCameraRTHandle.TryGetValue(cam, out handle))
            {
                handle.clearRT();
                perCameraRTHandle.Remove(cam);
            }

        }
        public RTPermanentHandle GetHandle(Camera cam)
        {
            RTPermanentHandle handle;
            if (perCameraRTHandle.TryGetValue(cam, out handle))
            {
                return handle;
            }
            else
            {
                handle = new RTPermanentHandle();
                perCameraRTHandle.Add(cam, handle);
                return handle;
            }
        }

        public void Dispose()
        {
            if (perCameraRTHandle != null)
            {
                //Debug.Log("Clearing RenderTextures");
                foreach (RTPermanentHandle r in perCameraRTHandle.Values)
                {
                    r.clearRT();
                }
            }
        }

        public void RemoveAllNull()
        {
            List<Camera> removeList = new List<Camera>();
            //Debug.Log("Count: " + perCameraRTHandle.Count);
            foreach (var cam in perCameraRTHandle)
            {
#if !UNITY_EDITOR
                if (OpqPair.Key == null || OpqPair.Key.isActiveAndEnabled == false)
#else
                if (cam.Key == null)
#endif
                {
                    cam.Value.clearRT();
                    removeList.Add(cam.Key);
                    //Debug.Log("Removed RenderTexture");
                }
            }
            for (int i = 0; i < removeList.Count; i++)
            {
                perCameraRTHandle.Remove(removeList[i]);
            }
        }
    }

}