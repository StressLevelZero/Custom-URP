using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class CamExtSwapBufferNames : CameraDataExtension, IDisposable
    {
        public static CamExtSwapBufferNames TryGet(CameraDataExtSet dataSet, int type)
        {
            CameraDataExtension extData = dataSet.GetExtension(type);
            if (extData == null)
            {
                CamExtSwapBufferNames newNames = new CamExtSwapBufferNames(type, dataSet.camera);
                dataSet.AddExtension(newNames);
                return newNames;
            }
#if UNITY_EDITOR
            if (extData.GetType() != typeof(CamExtSwapBufferNames))
            {
                Debug.LogError("Per-Camera data extensions: Tried to get CamExtSwapBufferNames with type ID " + type + ", but found type " + extData.GetType().Name + " for that ID!");
                return null;
            }
#endif
            return extData as CamExtSwapBufferNames;
        }

        public string ColorBufferNameA;
        public string ColorBufferNameB;
        public string NormalBufferName;

        public CamExtSwapBufferNames(int type, Camera cam)
        {
            this.type = type;
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
