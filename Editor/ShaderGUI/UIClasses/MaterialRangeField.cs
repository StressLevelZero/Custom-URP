using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.SLZMaterialUI
{
    public class MaterialRangeField : Slider, BaseMaterialField
    {
        public int shaderPropertyIdx;
        public int GetShaderPropIdx() { return shaderPropertyIdx; }
        public MaterialProperty materialProperty;

        public void Initialize(MaterialProperty materialProperty, int shaderPropertyIdx, bool noStyle = false)
        {
            this.materialProperty = materialProperty;
            this.shaderPropertyIdx = shaderPropertyIdx;
            this.RegisterValueChangedCallback(OnChangedEvent);
            this.SetValueWithoutNotify(materialProperty.floatValue);

            if (materialProperty.rangeLimits.x != 0 || materialProperty.rangeLimits.y != 0)
            {
                this.lowValue = materialProperty.rangeLimits.x;
                this.highValue = materialProperty.rangeLimits.y;
            }
            this.showInputField = true;
            if (this.showInputField && (materialProperty.floatValue < this.lowValue || materialProperty.floatValue > this.highValue) )
            {
                TextField inputField = (TextField) this.Children().Last().Children().Last();
                inputField.SetValueWithoutNotify(materialProperty.floatValue.ToString());
            }
            
            style.marginRight = 3;
            if (materialProperty.hasMixedValue)
            {
                //this.SetValueWithoutNotify(Color.gray);
                this.showMixedValue = true;
            }
            label = materialProperty.displayName;
            if (!noStyle) SetFullLineStyle();
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
        public void OnChangedEvent(ChangeEvent<float> evt)
        {
            materialProperty.floatValue = evt.newValue;
            this.showMixedValue = materialProperty.hasMixedValue;
        }
        public void UpdateMaterialProperty(MaterialProperty boundProp)
        {
            materialProperty = boundProp;
            if (value != boundProp.floatValue)
            {
                this.SetValueWithoutNotify(boundProp.floatValue);
                this.lowValue = boundProp.rangeLimits.x;
                this.highValue = boundProp.rangeLimits.y;

            }
            this.showMixedValue = boundProp.hasMixedValue;
        }
    }
}
