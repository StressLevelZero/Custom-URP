using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.SLZMaterialUI
{
    public class MaterialDummyField : INotifyValueChanged<float>, BaseMaterialField
    {
        public int shaderPropertyIdx;
        public int GetShaderPropIdx() { return shaderPropertyIdx; }
        public MaterialProperty materialProperty;
        float m_value;
        public float value
        {
            get { return m_value; }
            set {
                if (m_value != value)
                {
                    m_value = value;
                    materialProperty.floatValue = m_value;
                }
            }
        }

        public MaterialDummyField(MaterialProperty materialProperty, int shaderPropertyIdx)
        {
            this.materialProperty = materialProperty;
            this.shaderPropertyIdx = shaderPropertyIdx;
            SetValueWithoutNotify(materialProperty.floatValue);
        }
        public void UpdateMaterialProperty(MaterialProperty boundProp)
        {
            materialProperty = boundProp;
            if (value != boundProp.floatValue)
            {
                this.SetValueWithoutNotify(boundProp.floatValue);
            }
        }

        public void SetValueWithoutNotify(float value)
        {
            m_value = value;
        }
    }
}
