using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// DO NOT USE: This is a hack to fix using the 2022 pipeline in unity 6
    /// </summary>
    public class RenderGraphSettings
#if UNITY_6000_3_OR_NEWER
        : IRenderPipelineGraphicsSettings
#endif
    {
        public int version => -1;
        public bool enableRenderCompatibilityMode => true;
    }
}
