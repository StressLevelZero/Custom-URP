using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class MaterialPropertyPatchRuntime
{
    public delegate bool IsPersistentDelegate(Renderer r);
    static IsPersistentDelegate s_IsPersistent;
    static IsPersistentDelegate IsPersistent
    {
        get
        {
            if (s_IsPersistent == null)
            {
                MethodInfo IsPersistentMI = typeof(Renderer).GetMethod("IsPersistent", BindingFlags.Instance | BindingFlags.NonPublic);
                s_IsPersistent = (IsPersistentDelegate)IsPersistentMI.CreateDelegate(typeof(IsPersistentDelegate));
            }
            return s_IsPersistent;
        }
    }

    public delegate Material GetMaterialDelegate(Renderer r);
    static GetMaterialDelegate s_GetMaterialDelegate;
    static GetMaterialDelegate GetMaterial
    {
        get
        {
            if (s_GetMaterialDelegate == null)
            {
                MethodInfo getMaterialMI = typeof(Renderer).GetMethod("GetMaterial", BindingFlags.Instance | BindingFlags.NonPublic);
                s_GetMaterialDelegate = (GetMaterialDelegate)getMaterialMI.CreateDelegate(typeof(GetMaterialDelegate));
            }
            return s_GetMaterialDelegate;
        }
    }

    public delegate Material[] GetMaterialArrayDelegate(Renderer r);
    static GetMaterialArrayDelegate s_GetMaterialArrayDelegate;
    static GetMaterialArrayDelegate GetMaterialArray
    {
        get
        {
            if (s_GetMaterialArrayDelegate == null)
            {
                MethodInfo getMaterialArrayMI = typeof(Renderer).GetMethod("GetMaterialArray", BindingFlags.Instance | BindingFlags.NonPublic);
                s_GetMaterialArrayDelegate = (GetMaterialArrayDelegate)getMaterialArrayMI.CreateDelegate(typeof(GetMaterialArrayDelegate));
            }
            return s_GetMaterialArrayDelegate;
        }
    }

    public static Material CreateMaterialInstanceExt(this Renderer renderer)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Debug.LogError("Not allowed to access Renderer.CreateMaterialInstanceExt in edit mode. Creating material instances in edit mode is wrong.", renderer);
            return null;
        }
        if (IsPersistent.Invoke(renderer))
        {
            Debug.LogError("Not allowed to access Renderer.CreateMaterialInstanceExt on prefab object. Use Renderer.sharedMaterial instead", renderer);
            return null;
        }
        return GetMaterial.Invoke(renderer);
#else
        return renderer.material;
#endif
    }

    public static Material[] CreateMaterialArrayInstanceExt(this Renderer renderer)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Debug.LogError("Not allowed to access Renderer.CreateMaterialArrayInstanceExt in edit mode. Creating material instances in edit mode is wrong.", renderer);
            return null;
        }
        if (IsPersistent.Invoke(renderer))
        {
            Debug.LogError("Not allowed to access Renderer.CreateMaterialArrayInstanceExt on prefab object. Use Renderer.sharedMaterials instead", renderer);
            return null;
        }
        return GetMaterialArray.Invoke(renderer);
#else
        return renderer.materials;
#endif
    }
}
