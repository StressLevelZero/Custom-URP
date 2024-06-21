using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.UI;
using UnityEditor;
using UnityEditorInternal;

namespace SLZ.URPEditorBridge
{
    public static class InternalEditorUtilityBridge
    {
        public static bool BumpMapTextureNeedsFixing(MaterialProperty prop)
        {
            return InternalEditorUtility.BumpMapTextureNeedsFixing(prop);
        }

        public static void FixNormalmapTexture(MaterialProperty prop)
        {
            InternalEditorUtility.FixNormalmapTexture(prop);
        }
    }
}
