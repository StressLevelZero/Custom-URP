using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.SLZMaterialUI
{
    public interface BaseMaterialField
    {
        public int GetShaderPropIdx();
        public abstract void UpdateMaterialProperty(MaterialProperty boundProp);
    }
}
