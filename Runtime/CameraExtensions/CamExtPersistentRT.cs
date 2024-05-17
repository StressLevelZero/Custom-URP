using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    public partial class PersistentRT : CameraDataExtension, IDisposable
    {

        public RenderTexture renderTexture;
        public RTHandle handle;

        public PersistentRT()
        {

        }

        public PersistentRT(Camera cam) : base(cam)
        {

        }

        public PersistentRT(in RenderTextureDescriptor desc, Camera cam) : base(cam)
        {
            renderTexture = new RenderTexture(desc);
        }

        public override void Construct(Camera cam)
        {
            base.SetCamera(cam);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateRT(ref RenderTextureDescriptor desc)
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
                renderTexture.Release();
                renderTexture.descriptor = desc;
                renderTexture.Create();
            }
            else
            {
                renderTexture = new RenderTexture(desc);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                renderTexture.name = this.name;
#endif
                handle = RTHandles.Alloc(renderTexture);

            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateRT(ReadOnlySpan<RenderTextureDescriptor> desc)
        {

            if (renderTexture)
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

                renderTexture.Release();
                renderTexture.descriptor = desc[0];
                renderTexture.Create();
            }
            else
            {
                renderTexture = new RenderTexture(desc[0]);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                renderTexture.name = this.name;
#endif
                handle = RTHandles.Alloc(renderTexture);
            }
        }


        public RenderTexture GetRenderTexture(ref RenderTextureDescriptor desc)
        {
            UpdateRT(ref desc);
            return renderTexture;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RTHandle GetRTHandle(ref RenderTextureDescriptor desc)
        {
            UpdateRT(ref desc);
            return handle;
        }

        public RenderTexture GetRenderTexture(ReadOnlySpan<RenderTextureDescriptor> desc)
        {
            UpdateRT(desc);
            return renderTexture;
        }

        public override void Dispose()
        {
            if (renderTexture != null)
            {
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

    public class PrevOpaqueRT : PersistentRT
    {
        public PrevOpaqueRT() { }
        public PrevOpaqueRT(Camera cam) : base(cam) { }
    }

    public class PrevHiZRT : PersistentRT
    {
        public PrevHiZRT() { }
        public PrevHiZRT(Camera cam) : base(cam) { }
    }
}
