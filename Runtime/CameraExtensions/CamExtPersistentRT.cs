using System;
using System.Collections;
using System.Collections.Generic;
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

        public void UpdateRT(RenderTextureDescriptor desc, string name = "", string name2 = "")
        {

            if (renderTexture != null)
            {
               
                if (desc.width == renderTexture.width &&
                    desc.height == renderTexture.height &&
                    desc.volumeDepth == renderTexture.volumeDepth &&
                    desc.colorFormat == renderTexture.format)
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

        public RenderTexture GetRenderTexture(RenderTextureDescriptor desc, string name = "", string name2 = "")
        {
            UpdateRT(desc, name, name2);
            return renderTexture;
        }

        public RTHandle GetRTHandle(RenderTextureDescriptor desc, string name = "", string name2 = "")
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
