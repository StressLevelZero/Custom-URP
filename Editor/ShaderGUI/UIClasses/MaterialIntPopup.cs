using System;
using System.Linq;
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

        public struct Choice
        {
            public int value;
            public string label;
            public string[] enabledKws;
            public string[] disabledKws;

        }

        List<Choice> choiceValues;
        int numChoices = 0;


        public void Initialize(MaterialProperty materialProperty, int shaderPropertyIdx, List<Choice> choiceValues, List<int> visibleChoiceIdxs = null)
        {
          
            this.numChoices = choiceValues.Count;
            this.choiceValues = choiceValues;

            this.materialProperty = materialProperty;
            this.shaderPropertyIdx = shaderPropertyIdx;

            if (visibleChoiceIdxs == null)
            {
                this.choices = Enumerable.Range(0, choiceValues.Count).ToList<int>();
            }
            else
            {
                this.choices = visibleChoiceIdxs;
            }

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

            RegisterCallback<ChangeEvent<int>>(OnValueChanged);

            this.SetValueWithoutNotify(-1);
            for (int valueIdx = 0; valueIdx < numChoices; valueIdx++)
            {
                if (choiceValues[valueIdx].value == (int)materialProperty.floatValue)
                {
                    this.SetValueWithoutNotify(valueIdx);
                }       
            }
        }

        public void OnValueChanged(ChangeEvent<int> evt)
        {
            bool validSelection = evt.newValue >= 0 && evt.newValue < numChoices;
            int selection = Mathf.Clamp(evt.newValue, 0, numChoices);
            string[] enabledKeywords = choiceValues[selection].enabledKws;
            string[] disabledKeywords = choiceValues[selection].disabledKws;
            bool hasKws = validSelection && (enabledKeywords != null || disabledKeywords != null);
            int undoGroupIdx = 0;
            if (hasKws)
            {
                Undo.IncrementCurrentGroup();
                undoGroupIdx = Undo.GetCurrentGroup();
            }

            
            materialProperty.floatValue = (float) choiceValues[selection].value;

            if (!hasKws)
            {
                return;
            }

            UnityEngine.Object[] materials = materialProperty.targets;
            Undo.RecordObjects(materials, "Set Keywords");
            int numMaterials = materials.Length;
            for (int i = 0; i < numMaterials; i++) 
            {
                Material mat = materials[i] as Material;
                if (enabledKeywords != null)
                {
                    foreach (string kw in enabledKeywords)
                    {
                        CoreUtils.SetKeyword(mat, kw, true);
                    }
                }
                if (disabledKeywords != null)
                {
                    foreach (string kw in disabledKeywords)
                    {
                        CoreUtils.SetKeyword(mat, kw, false);
                    }
                }
                EditorUtility.SetDirty(mat);
                //AssetDatabase.SaveAssetIfDirty(mat);
            }

            Undo.CollapseUndoOperations(undoGroupIdx);
        }

        public void UpdateMaterialProperty(MaterialProperty boundProp)
        {
            materialProperty = boundProp;
            int newVal = (int)boundProp.floatValue;
            this.SetValueWithoutNotify(-1);
            for (int valueIdx = 0; valueIdx < numChoices; valueIdx++)
            {
                if (choiceValues[valueIdx].value == newVal)
                {
                    this.SetValueWithoutNotify(newVal);
                }
            }
            this.showMixedValue = boundProp.hasMixedValue;
        }

        string GetCurrentFlagName(int index)
        {
            if (!materialProperty.hasMixedValue && index >= 0 && index < numChoices)
            {
                return choiceValues[index].label;
            }
            else
            {
                return "-";
            }
        }

        string GetValidFlagName(int index)
        {
            index.ToString();
            if (index >= 0 && index < numChoices)
            {
                return choiceValues[index].label;
            }
            else
            {
                return "-";
            }
        }

        public int GetValueIndex(int value)
        {
            for (int valueIdx = 0; valueIdx < numChoices; valueIdx++)
            {
                if (choiceValues[valueIdx].value == (int)materialProperty.floatValue)
                {
                    return valueIdx;
                }       
            }
            return -1;
        }
    }
}
