using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SLZMaterialUI;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.SLZMaterialUI
{
    public class MaterialColorField : ColorField, BaseMaterialField
    {
        public int shaderPropertyIdx;
        public int GetShaderPropIdx() { return shaderPropertyIdx; }
        public MaterialProperty materialProperty;

        public void Initialize(MaterialProperty materialProperty, int shaderPropertyIdx, bool isPartOfTexField)
        {
            this.materialProperty = materialProperty;
            this.shaderPropertyIdx = shaderPropertyIdx;
            this.RegisterValueChangedCallback(OnChangedEvent);
            this.SetValueWithoutNotify(materialProperty.colorValue);
            style.marginRight = 3;
            if (materialProperty.hasMixedValue)
            {
                //this.SetValueWithoutNotify(Color.gray);
                this.showMixedValue = true;
            }
            if (isPartOfTexField)
            {
                style.alignSelf = Align.Center;
                style.flexGrow = 1;
                style.flexShrink = 1;
            }
            else
            {
                label = materialProperty.displayName;
                SetFullLineStyle();
            }
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
        public void OnChangedEvent(ChangeEvent<Color> evt)
        {
            materialProperty.colorValue = evt.newValue;
            this.showMixedValue = false;
        }
        public void UpdateMaterialProperty(MaterialProperty boundProp)
        {
            if (value != boundProp.colorValue)
            {
                this.SetValueWithoutNotify(boundProp.colorValue);
            }
            this.showMixedValue = boundProp.hasMixedValue;
        }
    }
}
