using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace UnityEditor.SLZMaterialUI
{
    public class MaterialIntPopup : PopupField<int>, BaseMaterialField
    {
        public int shaderPropertyIdx;
        public int GetShaderPropIdx() { return shaderPropertyIdx; }
        public MaterialProperty materialProperty;

        Dictionary<int, string> choiceLabels;

        public void Initialize(MaterialProperty materialProperty, int shaderPropertyIdx, List<int> choices, Dictionary<int, string> choiceLabels)
        {
          

            this.materialProperty = materialProperty;
            this.shaderPropertyIdx = shaderPropertyIdx;

            this.choices = choices;
            this.choiceLabels = choiceLabels;

            this.formatSelectedValueCallback = GetCurrentFlagName;
            this.formatListItemCallback = GetValidFlagName;

            VisualElement label = ElementAt(0);
            label.AddToClassList("materialGUILeftBox");
            label.style.overflow = Overflow.Hidden;
            label.style.minWidth = 0;
            VisualElement dropdown = ElementAt(1);
            dropdown.AddToClassList("materialGUIRightBox");
            style.justifyContent = Justify.FlexStart;
            style.marginRight = 3;

            RegisterCallback<ChangeEvent<int>>(evt =>
            {
                materialProperty.floatValue = (float)evt.newValue;
            }
            );

            this.SetValueWithoutNotify((int)materialProperty.floatValue);
        }



        public void UpdateMaterialProperty(MaterialProperty boundProp)
        {
            materialProperty = boundProp;
            int newVal = (int)boundProp.floatValue;
            if (this.value != newVal)
            {
                this.SetValueWithoutNotify(newVal);
            }
            this.showMixedValue = boundProp.hasMixedValue;
        }

        string GetCurrentFlagName(int type)
        {
            string label;
            if (choiceLabels.TryGetValue(type, out label))
            {
                return label;
            }
            else
            {
                return "-";
            }
        }

        string GetValidFlagName(int type)
        {
            return choiceLabels[type];
        }
    }
}
