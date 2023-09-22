using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    public class PersistentRT : CameraDataExtension, IDisposable
    {
        public static PersistentRT TryGet(CameraDataExtSet dataSet, int type)
        {
            CameraDataExtension extData = dataSet.GetExtension(type);
            if (extData == null) 
            {
                PersistentRT newRt = new PersistentRT(type);
                dataSet.AddExtension(newRt);
                return newRt;
            }
#if UNITY_EDITOR
            if (extData.GetType() != typeof(PersistentRT)) 
            {
                Debug.LogError("Per-Camera data extensions: Tried to get PersistentRT with type ID " + type + ", but found type " + extData.GetType().Name + " for that ID!");
                return null;
            }
#endif

            return extData as PersistentRT;
        }


        public RenderTexture renderTexture;
        public RTHandle handle;

        public PersistentRT(int type)
        {
            this.type = type;
            
        }

        public PersistentRT(CamDataExtType type)
        {
            this.type = (int)type;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateRT(in RenderTextureDescriptor desc, in string name = "", in string name2 = "")
        {

            if (renderTexture != null)
            {
               
                if (desc.width == renderTexture.width &&
                    desc.height == renderTexture.height &&
                    desc.volumeDepth == renderTexture.volumeDepth &&
                    desc.graphicsFormat == renderTexture.graphicsFormat &&
                    desc.depthStencilFormat == renderTexture.depthStencilFormat &&
                    desc.msaaSamples == renderTexture.antiAliasing &&
                    desc.enableRandomWrite == renderTexture.enableRandomWrite
                    )
                {
                    return;
                }
                else
                {
                    clearRT();
                }
            }

            //Debug.Log("New RenderTexture?");
            renderTexture = new RenderTexture(desc);
            renderTexture.name = string.Format("PersistentRT {0} {1}", name, name2);
            handle = RTHandles.Alloc(renderTexture);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateRT(in RenderTextureDescriptor desc, Camera camera, in string name2 = "")
        {

            if (renderTexture != null)
            {

                if (desc.width == renderTexture.width &&
                    desc.height == renderTexture.height &&
                    desc.volumeDepth == renderTexture.volumeDepth &&
                    desc.graphicsFormat == renderTexture.graphicsFormat &&
                    desc.depthStencilFormat == renderTexture.depthStencilFormat &&
                    desc.msaaSamples == renderTexture.antiAliasing &&
                    desc.enableRandomWrite == renderTexture.enableRandomWrite
                    )
                {
                    return;
                }
                else
                {
                    clearRT();
                }
            }

            //Debug.Log("New RenderTexture?");
            renderTexture = new RenderTexture(desc);
            renderTexture.name = string.Format("PersistentRT {0} {1}", camera.name, name2);
            handle = RTHandles.Alloc(renderTexture);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateRT(Span<RenderTextureDescriptor> desc, in string name = "", in string name2 = "")
        {

            if (renderTexture != null)
            {

                if (desc[0].width == renderTexture.width &&
                    desc[0].height == renderTexture.height &&
                    desc[0].volumeDepth == renderTexture.volumeDepth &&
                    desc[0].graphicsFormat == renderTexture.graphicsFormat &&
                    desc[0].depthStencilFormat == renderTexture.depthStencilFormat &&
                    desc[0].msaaSamples == renderTexture.antiAliasing &&
                    desc[0].enableRandomWrite == renderTexture.enableRandomWrite)
                {
                    return;
                }
                else
                {
                    clearRT();
                }
            }

            Debug.Log("New RenderTexture?");
            renderTexture = new RenderTexture(desc[0]);
            renderTexture.name = string.Format("PersistentRT {0} {1}", name, name2);
            handle = RTHandles.Alloc(renderTexture);
        }


        public RenderTexture GetRenderTexture(in RenderTextureDescriptor desc, string name = "", string name2 = "")
        {
            UpdateRT(in desc, "", "");
            return renderTexture;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RTHandle GetRTHandle(in RenderTextureDescriptor desc, string name = "", string name2 = "")
        {
            UpdateRT(in desc, name, name2);
            return handle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RTHandle GetRTHandle(in RenderTextureDescriptor desc, Camera camera, string name2 = "")
        {
            UpdateRT(in desc, camera, name2);
            return handle;
        }

        public RenderTexture GetRenderTexture(Span<RenderTextureDescriptor> desc, string name = "", string name2 = "")
        {
            UpdateRT(desc, name, name2);
            return renderTexture;
        }

        public RTHandle GetRTHandle(Span<RenderTextureDescriptor> desc, string name = "", string name2 = "")
        {
            UpdateRT(desc, name, name2);
            return handle;
        }

        public override void Dispose()
        {
            clearRT();
        }

        public void clearRT()
        {
            if (renderTexture != null)
            {
                //RTHandles.Release(handle);
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
}
