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
    /// Singleton class that keeps track of the CameraDataExtSet object associated with each camera.
    /// 
    /// Why not make the data components on the camera? Unity doesn't allow adding components to the scene view camera lmao.
    /// Also calling GetComponent is probably more expensive than a dictionary lookup, and Unity doesn't always call OnDestroy 
    /// so we don't have a reliable way to dispose of rendertextures/computebuffers/nativearrays.
    /// </summary>
    public class CameraExtDataPool : IDisposable
    {
       
        private static CameraExtDataPool s_ExtList;

        public static CameraExtDataPool Instance
        {
            get
            {
                if (s_ExtList == null)
                    s_ExtList = new CameraExtDataPool();
                return s_ExtList;
            }
        }

        private bool disposing = false;
        public Dictionary<Camera, CameraDataExtSet> extData = new Dictionary<Camera, CameraDataExtSet>();
        private List<CameraDataExtSet> extDataList = new List<CameraDataExtSet>();
        private List<Camera> cameraList = new List<Camera>();

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
                cameraList.Add(camera);
                extDataList.Add(output);
                return output;
            }
        }

        /// <summary>
        /// Removes the data set associated with a camera from the dictionary and lists, calling the necessary dispose methods
        /// </summary>
        /// <param name="camera">Camera to remove the data of</param>
        public void RemoveCamera(Camera camera) 
        {
            CameraDataExtSet output;
            if (extData.TryGetValue(camera, out output))
            {
                int numCameras = cameraList.Count;
                int i = 0;
                for (; i < numCameras; i++)
                {
                    if (camera == cameraList[i])
                    {
                        cameraList.RemoveAt(i);
                        extDataList.RemoveAt(i);
                        break;
                    }
                }

                output.Dispose();
                extData.Remove(camera);
            }
        }

        /// <summary>
        /// Remove extension data for all destroyed cameras.
        /// </summary>
        public void RemoveAllNull()
        {
            int numCameras = cameraList.Count;
            for (int i = numCameras-1; i >= 0; i--)
            {
                if (cameraList[i] == null)
                {
                    extData.Remove(cameraList[i]);
                    cameraList.RemoveAt(i);
                    extDataList[i].Dispose();
                    extDataList.RemoveAt(i);
                }
            }
        }

        int purgeCounter = 0;
        const int maxCount = 900;

        /// <summary>
        /// Removes all null cameras and associated data every 900th call to the function
        /// </summary>
        public void PeriodicPurge()
        {
            purgeCounter++;
            if (purgeCounter > maxCount)
            {
                RemoveAllNull();
                purgeCounter = 0;
            }
        }

        public void Dispose() 
        {
            if (!disposing && extData != null)
            {
                int numExtensions = extDataList.Count;
                for (int i = 0; i < numExtensions; i++)
                {
                    extDataList[i]?.Dispose();
                }
                extData.Clear();
                cameraList.Clear();
                extDataList.Clear();
                disposing = true;
            }
        }
    }
}
