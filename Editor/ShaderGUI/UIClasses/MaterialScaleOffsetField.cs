using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SLZMaterialUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.SLZMaterialUI
{
    public class MaterialScaleOffsetField : VisualElement, BaseMaterialField
    {
        public int shaderPropertyIdx;
        public int GetShaderPropIdx() { return shaderPropertyIdx; }
        public MaterialProperty materialProperty;
        Vector2Field tilingField;
        Vector2Field offsetField;

        public MaterialScaleOffsetField(MaterialProperty boundProp, int shaderPropertyIdx)
        {
            this.materialProperty = boundProp;
            this.shaderPropertyIdx = shaderPropertyIdx;
            Vector4 scaleOffset = boundProp.textureScaleAndOffset;
            tilingField = new Vector2Field();
            tilingField.style.marginRight = 4;
            tilingField.label = "Tiling";
            VisualElement tilingLabel = tilingField.ElementAt(0);
            VisualElement tilingInput = tilingField.ElementAt(1);
            tilingInput.RemoveAt(2);
            tilingLabel.AddToClassList("materialGUILeftBox");
            tilingInput.AddToClassList("materialGUIRightBox");
            tilingField.SetValueWithoutNotify(new Vector2(scaleOffset.x, scaleOffset.y));
            tilingField.RegisterValueChangedCallback(OnChangedEventTiling);

            offsetField = new Vector2Field();
            offsetField.style.marginRight = 4;
            offsetField.label = "Offset";
            VisualElement offsetLabel = offsetField.ElementAt(0);
            VisualElement offsetInput = offsetField.ElementAt(1);
            
            offsetInput.RemoveAt(2);
            offsetLabel.AddToClassList("materialGUILeftBox");
            offsetInput.AddToClassList("materialGUIRightBox");
            offsetField.SetValueWithoutNotify(new Vector2(scaleOffset.z, scaleOffset.w));
            offsetField.RegisterValueChangedCallback(OnChangedEventOffset);

            Add(tilingField);
            Add(offsetField);
        }

        public void OnChangedEventTiling(ChangeEvent<Vector2> evt)
        {
            Vector4 scaleOffset = new Vector4(evt.newValue.x, evt.newValue.y, offsetField.value.x, offsetField.value.y);
            materialProperty.textureScaleAndOffset = scaleOffset;
            tilingField.showMixedValue = false;
        }

        public void OnChangedEventOffset(ChangeEvent<Vector2> evt)
        {
            Vector4 scaleOffset = new Vector4(tilingField.value.x, tilingField.value.y, evt.newValue.x, evt.newValue.y);
            materialProperty.textureScaleAndOffset = scaleOffset;
            tilingField.showMixedValue = false;
        }
        public void UpdateMaterialProperty(MaterialProperty boundProp)
        {
            materialProperty = boundProp;
            Vector2 scale = new Vector2(boundProp.textureScaleAndOffset.x, boundProp.textureScaleAndOffset.y);
            Vector2 offset = new Vector2(boundProp.textureScaleAndOffset.z, boundProp.textureScaleAndOffset.w);
            tilingField.SetValueWithoutNotify(scale);
            offsetField.SetValueWithoutNotify(offset);
            if (materialProperty.hasMixedValue)
            {
                tilingField.showMixedValue = true;
                offsetField.showMixedValue = true;
            }
            MarkDirtyRepaint();
        }

    }
}