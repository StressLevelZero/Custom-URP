using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    public enum CamDataExtType : int
    {
        UNKNOWN = 0,
        CAMERA_OPAQUE,
        HI_Z,
        SSR, 
        VOLUMETRICS,
        VRS,
    }

    /// <summary>
    /// Interface for per-camera data that can be stored in a CameraDataExtensions object
    /// </summary>
    public abstract class CameraDataExtension : IDisposable
    {
        public int type;
        public abstract void Dispose();
    }

    /// <summary>
    /// Class that stores a set of data objects associated with an individual camera.
    /// </summary>
    public class CameraDataExtSet : IDisposable
    {
        private bool disposing = false;

        Dictionary<int, CameraDataExtension> extensions = new Dictionary<int, CameraDataExtension>();

        public CameraDataExtension GetExtension(CamDataExtType extensionID)
        {
            return GetExtension((int) extensionID);
        }

        public CameraDataExtension GetExtension(int extensionID)
        {
            if (disposing) return null;
            CameraDataExtension ext;
            if (extensions.TryGetValue(extensionID, out ext))
            {
                return ext;
            }
            else
            {
                return null;
            }
        }

        public bool AddExtension(CameraDataExtension ext)
        {
            if (disposing) return false;
            if (ext != null && !extensions.ContainsKey(ext.type))
            {
                extensions.Add(ext.type, ext);
                return true;
            }
            return false;
        }

        public bool RemoveExtension(ref CameraDataExtension ext)
        {
            if (disposing) return false;
            if (ext != null && extensions.ContainsKey(ext.type))
            {
                extensions.Remove(ext.type);
                return true;
            }
            return false;
        }

        public void Dispose() 
        {
            if (!disposing)
            {
                int numExtensions = extensions == null ? 0 : extensions.Count;
                foreach (CameraDataExtension ext in extensions.Values)
                {
                    if (ext != null)
                    {
                        ext.Dispose();
                    }
                }
                extensions.Clear();
                disposing = true;
            }
        }
    }

    /// <summary>
    /// Singleton class that keeps track of the CameraDataExtSet object associated with each camera
    /// </summary>
    public class PerCameraExtData : IDisposable
    {
       
        private static PerCameraExtData s_ExtList;

        public static PerCameraExtData Instance
        {
            get
            {
                if (s_ExtList == null)
                    s_ExtList = new PerCameraExtData();
                return s_ExtList;
            }
        }

        private bool disposing = false;
        public Dictionary<Camera, CameraDataExtSet> extData = new Dictionary<Camera, CameraDataExtSet>();

        /// <summary>
        /// Get the set of extension data associated with a camera. Initializes the camera's set if it has none.
        /// </summary>
        /// <param name="camera">Camera to get the extension data set for</param>
        /// <returns></returns>
        public CameraDataExtSet GetCameraDataSet(Camera camera)
        {
            CameraDataExtSet output;
            if (extData.TryGetValue(camera, out output))
            {
                return output;
            }
            else
            {
                output = new CameraDataExtSet();
                extData.Add(camera, output);
                return output;
            }
        }

        /// <summary>
        /// Removes the data set associated with a camera from the dictionary, calling the necessary dispose methods
        /// </summary>
        /// <param name="camera">Camera to remove the data of</param>
        public void RemoveCamera(Camera camera) 
        {
            CameraDataExtSet output;
            if (extData.TryGetValue(camera, out output))
            {
                output.Dispose();
                extData.Remove(camera);
            }
        }

        /// <summary>
        /// Remove extension data for all destroyed cameras. This should be called infrequently as it allocates on the heap in order to turn the extData dictionary into an enumerable.
        /// </summary>
        public void RemoveAllNull()
        {
            foreach (KeyValuePair<Camera, CameraDataExtSet> pair in extData)
            {
                if (pair.Key == null)
                {
                    pair.Value.Dispose();
                }
            }
        }

        public void Dispose() 
        {
            if (!disposing && extData != null)
            {
                int numExtensions = extData.Count;
                foreach (CameraDataExtSet ext in extData.Values)
                {
                    if (ext != null)
                    {
                        ext.Dispose();
                    }
                }
                extData.Clear();
                disposing = true;
            }
        }
    }
}
