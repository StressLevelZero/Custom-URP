using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace UnityEditor.SLZMaterialUI
{
    public class MaterialToggleField : Toggle, BaseMaterialField
    {
        
        public int shaderPropertyIdx;
        public int GetShaderPropIdx() { return shaderPropertyIdx; }
        public MaterialProperty materialProperty;
        bool isIntField = false;
        string keyword;
        public delegate void BeforeChangeEvent(ChangeEvent<bool> evt);
        //public BeforeChangeEvent BeforeChange;
        public void Initialize(MaterialProperty materialProperty, int shaderPropertyIdx, string keyword, bool isIntField, bool noStyle = false)
        {
            this.materialProperty = materialProperty;
            this.shaderPropertyIdx = shaderPropertyIdx;
            this.isIntField = isIntField;
            this.keyword = keyword;
            this.RegisterValueChangedCallback(OnChangedEvent);
            bool state = false;
            if (isIntField)
            {
                state = materialProperty.intValue != 0 ? true : false;
            }
            else
            {
                state = materialProperty.floatValue > 0 ? true : false;
            }

            this.SetValueWithoutNotify(state);
            
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
            if (isIntField)
            {
                materialProperty.intValue = evt.newValue ? 1 : 0;
            }
            else
            {
                materialProperty.floatValue = evt.newValue ? 1 : 0;
            }
            
            if (keyword != null)
            {
                Object[] targets = materialProperty.targets;
                int numMats = targets.Length;
                // Setting the value through the materialProperty already recorded an undo, append to that
                Undo.RecordObjects(targets, Undo.GetCurrentGroupName());
            }
            SetKeywordOnTargets(evt.newValue);
            if (keyword != null)
            {
                Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
                Undo.IncrementCurrentGroup();
            }
            this.showMixedValue = false;
        }

        void SetKeywordOnTargets(bool value) 
        { 
        
            if (keyword != null)
            {
                Object[] materials = materialProperty.targets;
                int numMaterials = materials.Length;
                Shader s = (materials[0] as Material).shader;
                LocalKeyword kw = new LocalKeyword(s, keyword);             
                for (int i = 0; i < numMaterials; i++) 
                {
                    (materials[0] as Material).SetKeyword(kw, value);
                }
            }
        }
        public void UpdateMaterialProperty(MaterialProperty boundProp)
        {
            materialProperty = boundProp;
            bool state = false;
            if (isIntField)
            {
                state = materialProperty.intValue != 0 ? true : false;
            }
            else
            {
                state = materialProperty.floatValue > 0 ? true : false;
            }
            if (this.value != state)
            {
                this.SetValueWithoutNotify(state);

            }
            this.showMixedValue = materialProperty.hasMixedValue;
            //MarkDirtyRepaint();
        }
    }
}
