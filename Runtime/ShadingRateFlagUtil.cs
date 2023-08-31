using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class ShadingRateFlagUtil
{
    private static FieldInfo s_rtDescFlags;
    private static FieldInfo rtDescFlags
    {
        get
        {
            if (s_rtDescFlags == null)
            {
                s_rtDescFlags = typeof(RenderTextureDescriptor).GetField("_flags", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            if (s_rtDescFlags == null)
            {
                Debug.LogError("Could not find flags");
            }
            return s_rtDescFlags;
        }
    }

    public static RenderTextureDescriptor AddShadingRateFlag(RenderTextureDescriptor rtDesc)
    {
        object boxed = rtDesc;
        Int32 oldflags = (Int32)rtDescFlags.GetValue(boxed);
        rtDescFlags.SetValue(boxed, oldflags | (1 << 14)); // 1<<14 is the value of the undocumented/unsupported internal shading rate flag
        rtDesc = (RenderTextureDescriptor)boxed;
        return rtDesc;
    }
}
