using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.SLZMaterialUI
{
    public class MaterialZSlopeFloatField : FloatField, BaseMaterialField
    {
        public int shaderPropertyIdx;
        public int GetShaderPropIdx() { return shaderPropertyIdx; }
        public MaterialProperty materialProperty;
        public MaterialHalfRateToggleField halfRateToggle;

        public void Initialize(MaterialProperty materialProperty, MaterialHalfRateToggleField halfRateToggle, int shaderPropertyIdx, bool fullLine = false)
        {
            this.materialProperty = materialProperty;
            this.shaderPropertyIdx = shaderPropertyIdx;
            this.RegisterValueChangedCallback(OnChangedEvent);
            
            style.marginRight = 3;
            if (materialProperty.hasMixedValue)
            {
                //this.SetValueWithoutNotify(Color.gray);
                this.showMixedValue = true;
            }
            else
            {
                this.SetValueWithoutNotify(Mathf.Floor(materialProperty.floatValue * 1000.0f) * 0.001f);
            }
            label = materialProperty.displayName;
            this.halfRateToggle = halfRateToggle;
            SetStyle(fullLine);
        }
        public void SetStyle(bool fullLine)
        {
            VisualElement label = this.ElementAt(0);
            label.AddToClassList("materialGUILeftBox");
            label.style.overflow = Overflow.Hidden;
            label.style.minWidth = 0;
            VisualElement field = this.ElementAt(1);
            field.AddToClassList("materialGUIRightBox");
            style.justifyContent = Justify.FlexStart;
            if (!fullLine)
            {
                label.style.flexBasis = 36;
                label.style.minWidth = 36;
                label.style.flexGrow = 0f;
                label.style.flexShrink = 0f;
                label.style.alignSelf = Align.FlexStart;
                field.style.flexGrow = 1;
                field.style.flexShrink = 1f;
            }
        }
        public void OnChangedEvent(ChangeEvent<float> evt)
        {
            if (halfRateToggle == null)
            {
                materialProperty.floatValue = evt.newValue;
                return;
            }

            float truncVal = Mathf.Floor(evt.newValue * 1000.0f) * 0.001f;
            this.SetValueWithoutNotify(truncVal);
            if (!halfRateToggle.materialProperty.hasMixedValue)
            {
                float sign = truncVal < 0 ? -1 : 1;
                materialProperty.floatValue = truncVal + sign * (halfRateToggle.value ? MaterialHalfRateToggleField.SLZ_COARSE_RASTER_FLAG : 0.0f);
            }
            else
            {
                Object[] materials = materialProperty.targets;
                Undo.RecordObjects(materialProperty.targets, Undo.GetCurrentGroupName());
                int numMaterials = materials.Length;
              
                for (int i = 0; i < numMaterials; i++)
                {
                    Material mat = materials[0] as Material;
                    float zSlope = mat.GetFloat(shaderPropertyIdx);
                    mat.SetFloat(shaderPropertyIdx, halfRateToggle.SetHalfRate(truncVal, halfRateToggle.IsHalfRate(zSlope)));
                    EditorUtility.SetDirty(mat);
                }
                Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
                Undo.IncrementCurrentGroup();
            }

            this.showMixedValue = false;
        }
        public void UpdateMaterialProperty(MaterialProperty boundProp)
        {
            materialProperty = boundProp;
            if (value != boundProp.floatValue)
            {
                this.SetValueWithoutNotify(boundProp.floatValue);
            }
            this.showMixedValue = boundProp.hasMixedValue;
        }
    }
}
