using System.Collections;
using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor;
using UnityEngine;

#if !MARROW_PROJECT

namespace SLZ.URPModResources
{
    [InitializeOnLoad]
    public class PlatformQualityListener : IActiveBuildTargetChanged
    {
        public int callbackOrder { get { return 0; } }
        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget)
        {
            //Debug.Log("Platform Switch Listener executed!");
            PlatformQualitySetter.OverrideQualitySettings(newTarget);
        }
    }
}
#endif