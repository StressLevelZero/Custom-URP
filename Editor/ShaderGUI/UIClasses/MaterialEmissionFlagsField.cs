using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace UnityEditor.SLZMaterialUI
{
    public class MaterialEmissionFlagsField : VisualElement
    {

       
        static EnumFieldUtils.EnumData s_EmissionFlags;
        
        static EnumFieldUtils.EnumData emissionFlags
        {
            get
            {
               if (s_EmissionFlags == null) 
               {
                    s_EmissionFlags = new EnumFieldUtils.EnumData()
                    {
                        values = new Enum[] { MaterialGlobalIlluminationFlags.EmissiveIsBlack, MaterialGlobalIlluminationFlags.RealtimeEmissive, MaterialGlobalIlluminationFlags.BakedEmissive },
                        flagValues = new int[] { 0, 1, 2 },
                        displayNames = new string[] { "None", "Realtime", "Baked" },
                        names = new string[] { "EmissiveIsBlack", "RealtimeEmissive", "BakedEmissive" },
                        tooltip = new string[] { "Emission will not emit light during light baking", "Emission will be treated as a real-time source when baking realtime GI", "Emission will emit light when baking lighting" },
                        flags = true,
                        underlyingType = typeof(MaterialGlobalIlluminationFlags),
                        unsigned = false,
                        serializable = true,
                    };
               }
               return s_EmissionFlags;
            }
        }


        static object s_BoxedEnumData;
        static object boxedEnumData
        {
            get
            {
                if (s_BoxedEnumData == null)
                {
                    s_BoxedEnumData = EnumFieldUtils.GetBoxedEnumData(emissionFlags);
                }
                return s_BoxedEnumData;
            }
        }

        static Action<EnumField, Enum> s_UpdateValueLabelAction;
        static Action<EnumField, Enum> UpdateValueLabelAction
        { 
            get
            {
                if (s_UpdateValueLabelAction == null)
                {
                    MethodInfo updateValueInfo = typeof(EnumField).GetMethod("UpdateValueLabel", BindingFlags.Instance | BindingFlags.NonPublic);
                    s_UpdateValueLabelAction = (Action<EnumField, Enum>) updateValueInfo.CreateDelegate(typeof(Action<EnumField, Enum>));
                }
                return s_UpdateValueLabelAction;
            }
        }

        public EnumField internalField;
        public MaterialEmissionFlagsField(string label, MaterialGlobalIlluminationFlags defaultValue)
        {
            internalField = new EnumField(label, defaultValue);
            SetEnumData(internalField);
            EnumFieldUtils.UpdateValueLabelAction(internalField, defaultValue);
            VisualElement labelElement = internalField.ElementAt(0);
            labelElement.AddToClassList("materialGUILeftBox");
            labelElement.style.overflow = Overflow.Hidden;
            labelElement.style.minWidth = 0;
            VisualElement dropdownElement = internalField.ElementAt(1);
            dropdownElement.AddToClassList("materialGUIRightBox");
            style.justifyContent = Justify.FlexStart;
            Add(internalField);

        }

        public void SetEnumData(EnumField field)
        {
            EnumFieldUtils.enumDataField.SetValue(field, boxedEnumData);
        }
    }
}
