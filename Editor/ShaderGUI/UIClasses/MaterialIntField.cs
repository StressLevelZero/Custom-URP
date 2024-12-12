using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.SLZMaterialUI
{
    public class MaterialIntField : IntegerField, BaseMaterialField
    {
        bool propertyIsFloat;
        public int shaderPropertyIdx;
        public int GetShaderPropIdx() { return shaderPropertyIdx; }
        public MaterialProperty materialProperty;

        public void Initialize(MaterialProperty materialProperty, int shaderPropertyIdx, bool propertyIsFloat = false)
        {
            this.propertyIsFloat = propertyIsFloat;
            this.materialProperty = materialProperty;
            this.shaderPropertyIdx = shaderPropertyIdx;
            this.RegisterValueChangedCallback(OnChangedEvent);
            this.SetValueWithoutNotify(propertyIsFloat ? (int)materialProperty.floatValue : materialProperty.intValue);
            style.marginRight = 3;
            if (materialProperty.hasMixedValue)
            {
                //this.SetValueWithoutNotify(Color.gray);
                this.showMixedValue = true;
            }
            label = materialProperty.displayName;
            SetFullLineStyle();
        }
        public void SetFullLineStyle()
        {
            VisualElement label = this.ElementAt(0);
            label.AddToClassList("materialGUILeftBox");
            label.style.overflow = Overflow.Hidden;
            label.style.minWidth = 0;
            VisualElement color = this.ElementAt(1);
            color.AddToClassList("materialGUIRightBox");
            style.justifyContent = Justify.FlexStart;
        }
        public void OnChangedEvent(ChangeEvent<int> evt)
        {
            if (propertyIsFloat)
            {
                materialProperty.floatValue = evt.newValue;
            }
            else
            {
                materialProperty.intValue = evt.newValue;
            }
            this.showMixedValue = false;
        }
        public void UpdateMaterialProperty(MaterialProperty boundProp)
        {
            materialProperty = boundProp;
            int newVal = propertyIsFloat ? (int)boundProp.floatValue : boundProp.intValue;
            if (value != newVal)
            {
                this.SetValueWithoutNotify(newVal);
            }
            this.showMixedValue = boundProp.hasMixedValue;
        }
    }
}
