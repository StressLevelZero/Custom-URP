using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Unity.Mathematics;

namespace UnityEditor.SLZMaterialUI
{
    public class MaterialHalfRateToggleField : Toggle, BaseMaterialField
    {
        public const float SLZ_COARSE_RASTER_FLAG = 0.0008148f;
        public const float SLZ_COARSE_RASTER_FLAG_G = 0.000817f;
        public const float SLZ_COARSE_RASTER_FLAG_L = 0.000813f;
        public int shaderPropertyIdx;

        public int GetShaderPropIdx() { return shaderPropertyIdx; }
        public MaterialProperty materialProperty;
        public MaterialZSlopeFloatField zSlopeField;
        MaterialProperty zSlopeProp { get => zSlopeField != null ? zSlopeField.materialProperty : this.materialProperty; }
        bool isIntField = false;
        string keyword;
        public delegate void BeforeChangeEvent(ChangeEvent<bool> evt);
        //public BeforeChangeEvent BeforeChange;
        public void Initialize(MaterialProperty materialProperty, MaterialZSlopeFloatField zSlopeField, int shaderPropertyIdx, string keyword, bool isIntField, bool noStyle = false)
        {
            this.materialProperty = materialProperty;
            this.shaderPropertyIdx = shaderPropertyIdx;
            this.isIntField = isIntField;
            this.keyword = keyword;
            this.zSlopeField = zSlopeField;
            this.RegisterValueChangedCallback(OnChangedEvent);
            bool state = materialProperty.floatValue > 0.0;
            this.SetValueWithoutNotify(state);
            if (zSlopeField != null && !materialProperty.hasMixedValue && !zSlopeField.materialProperty.hasMixedValue)
            {
                // if the digits starting at the ten thousands place approximately equals 8148, then half rate shading is enabled
                bool halfRateFlag = IsHalfRate(zSlopeField.materialProperty.floatValue);
                //Debug.Log($"IsHalfRate: {halfRateFlag}, state: {state}");
                // Failsafe to ensure the z-slope is properly flagged 
                if (state != halfRateFlag)
                {
                    zSlopeField.materialProperty.floatValue = SetHalfRate(zSlopeField.value, state);
                }
            }

            
            style.marginRight = 3;
            if (materialProperty.hasMixedValue)
            {
                this.showMixedValue = true;
            }
            
            if (!noStyle)
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
        public void OnChangedEvent(ChangeEvent<bool> evt)
        {
            //BeforeChange.Invoke(evt);
            float onVal = zSlopeField != null ? 1f : SLZ_COARSE_RASTER_FLAG;
            materialProperty.floatValue = evt.newValue ? onVal : 0.0f;
            

            Object[] targets = materialProperty.targets;
            int numMats = targets.Length;
            // Setting the value through the materialProperty already recorded an undo, append to that
            Undo.RecordObjects(targets, Undo.GetCurrentGroupName());
            if (keyword != null)
            {
                SetKeywordOnTargets(evt.newValue);
            }
            if (zSlopeField != null)
            {
                SetHRSlopeOnTargets(evt.newValue);
            }

            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            Undo.IncrementCurrentGroup();
            this.showMixedValue = false;
        }

        public bool IsHalfRate(float zSlope)
        {
            float zSlopeTerminator = math.frac(math.abs(zSlope) * 1000.0f) * 0.001f;
            //Debug.Log($"zSlopeTerminator: {zSlopeTerminator}");
            return zSlopeTerminator >= SLZ_COARSE_RASTER_FLAG_L && zSlopeTerminator <= SLZ_COARSE_RASTER_FLAG_G ? true : false;
        }

        public float SetHalfRate(float zSlope, bool halfRate)
        {
            float zSlopeTrunc = math.floor(zSlope * 1000.0f) * 0.001f;
            float sign = zSlope < 0 ? -1 : 1;
            return zSlopeTrunc + sign * (halfRate ? SLZ_COARSE_RASTER_FLAG : 0.0f);
        }

        void SetKeywordOnTargets(bool value) 
        { 
        
            if (!string.IsNullOrEmpty(keyword))
            {
                Object[] materials = materialProperty.targets;
                int numMaterials = materials.Length;
                Shader s = (materials[0] as Material).shader;
                LocalKeyword kw = new LocalKeyword(s, keyword);             
                for (int i = 0; i < numMaterials; i++) 
                {
                    Material m = materials[i] as Material;
                    m.SetKeyword(kw, value);
                    EditorUtility.SetDirty(m);
                }
            }
        }

        void SetHRSlopeOnTargets(bool value)
        {
            Object[] materials = materialProperty.targets;
            int numMaterials = materials.Length;
            int zSlopePropIdx = zSlopeField.GetShaderPropIdx();
     
            for (int i = 0; i < numMaterials; i++)
            {
                Material mat = (Material)materials[i];
                float zSlope = mat.GetFloat(zSlopeField.materialProperty.name);
                float halfRate = SetHalfRate(zSlope, value);
                mat.SetFloat(zSlopeField.materialProperty.name, halfRate);
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssetIfDirty(mat);
            }
        }
        public void UpdateMaterialProperty(MaterialProperty boundProp)
        {
            materialProperty = boundProp;
            bool state = materialProperty.floatValue != 0.0f;

            //Debug.Log($"Update toggle {boundProp.name}, value: {state}");
            this.SetValueWithoutNotify(state);
            this.showMixedValue = materialProperty.hasMixedValue;
            this.style.color = Color.red;

            //MarkDirtyRepaint();
        }
    }
}
