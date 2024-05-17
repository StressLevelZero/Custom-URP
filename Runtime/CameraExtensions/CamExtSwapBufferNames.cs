using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class CamExtSwapBufferNames : CameraDataExtension, IDisposable
    {
        public CamExtSwapBufferNames() { }
        public static CamExtSwapBufferNames TryGet(CameraDataExtSet dataSet)
        {
            CamExtSwapBufferNames extData = dataSet.GetExtension<CamExtSwapBufferNames>();
            if (extData == null)
            {
                extData = new CamExtSwapBufferNames(dataSet.camera);
                dataSet.AddExtension(extData);
            }

            return extData;
        }

        public string ColorBufferNameA;
        public string ColorBufferNameB;
        public string NormalBufferName;

        public CamExtSwapBufferNames(Camera cam) : base(cam)
        {
            Construct(cam);
        }

        public override void Construct(Camera cam)
        {
            base.SetCamera(cam);
            string hash = cam.GetHashCode().ToString("X8");
            ColorBufferNameA = "_CameraColorAttachmentA" + hash;
            ColorBufferNameB = "_CameraColorAttachmentB" + hash;
            NormalBufferName = "_CameraNormalsTexture" + hash;
        }

        public override void Dispose()
        {

        }
    }
}
