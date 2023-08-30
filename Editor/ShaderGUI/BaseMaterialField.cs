using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.SLZMaterialUI
{
    public abstract class BaseMaterialField : VisualElement
    {
        public int shaderPropIdx;
        public abstract void UpdateMaterialProperty(MaterialProperty boundProp);
    }
}
