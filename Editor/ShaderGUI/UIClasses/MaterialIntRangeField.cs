using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace UnityEditor.SLZMaterialUI
{
    public class MaterialIntRangeField : SliderInt, BaseMaterialField
    {
        public int shaderPropertyIdx;
        public int GetShaderPropIdx() { return shaderPropertyIdx; }
        public MaterialProperty materialProperty;

        public void Initialize(MaterialProperty materialProperty, int shaderPropertyIdx)
        {
            this.materialProperty = materialProperty;
            this.shaderPropertyIdx = shaderPropertyIdx;
            this.RegisterValueChangedCallback(OnChangedEvent);
            this.SetValueWithoutNotify((int)materialProperty.floatValue);
            this.lowValue = (int)materialProperty.rangeLimits.x;
            this.highValue = (int)materialProperty.rangeLimits.y;
            this.showInputField = true;
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
            materialProperty.floatValue = evt.newValue; // Int ranges are still floats...
            this.showMixedValue = false;
        }
        public void UpdateMaterialProperty(MaterialProperty boundProp)
        {
            materialProperty = boundProp;
            if (value != boundProp.floatValue)
            {
                this.SetValueWithoutNotify((int)boundProp.floatValue);
                this.lowValue = (int)boundProp.rangeLimits.x;
                this.highValue = (int)boundProp.rangeLimits.y;
            }
            this.showMixedValue = boundProp.hasMixedValue;
        }
    }
}
