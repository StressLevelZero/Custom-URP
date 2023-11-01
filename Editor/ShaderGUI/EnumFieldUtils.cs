using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace UnityEditor
{
    public class EnumFieldUtils : MonoBehaviour
    {
        public class EnumData
        {
            public Enum[] values;
            public int[] flagValues;
            public string[] displayNames;
            public string[] names;
            public string[] tooltip;
            public bool flags;
            public Type underlyingType;
            public bool unsigned;
            public bool serializable;
        }

        static FieldInfo s_EnumDataField;

        public static FieldInfo enumDataField
        {
            get
            {
                if (s_EnumDataField == null)
                {
                    s_EnumDataField = typeof(EnumField).GetField("m_EnumData", BindingFlags.Instance | BindingFlags.NonPublic);
                }
                return s_EnumDataField;
            }
        }

        static FieldInfo s_values;
        static FieldInfo s_flagValues;
        static FieldInfo s_displayNames;
        static FieldInfo s_names;
        static FieldInfo s_tooltip;

        static void InitializeFields()
        {
            Type enumType = enumDataField.FieldType;
            s_values = enumType.GetField("values", BindingFlags.Instance | BindingFlags.Public);
            s_flagValues = enumType.GetField("flagValues", BindingFlags.Instance | BindingFlags.Public);
            s_displayNames = enumType.GetField("displayNames", BindingFlags.Instance | BindingFlags.Public);
            s_names = enumType.GetField("names", BindingFlags.Instance | BindingFlags.Public);
            s_tooltip = enumType.GetField("tooltip", BindingFlags.Instance | BindingFlags.Public);
        }

        public static object GetBoxedEnumData(EnumData enumData)
        {
            if (s_values == null)
            {
                InitializeFields();
            }
            Type enumType = enumDataField.FieldType;
            object s_BoxedEnumData = Activator.CreateInstance(enumType);
            s_values.SetValue(s_BoxedEnumData, enumData.values);
            s_flagValues.SetValue(s_BoxedEnumData, enumData.flagValues);
            s_displayNames.SetValue(s_BoxedEnumData, enumData.displayNames);
            s_names.SetValue(s_BoxedEnumData, enumData.names);
            s_tooltip.SetValue(s_BoxedEnumData, enumData.tooltip);
            return s_BoxedEnumData;
        }

        static Action<EnumField, Enum> s_UpdateValueLabelAction;
        public static Action<EnumField, Enum> UpdateValueLabelAction
        {
            get
            {
                if (s_UpdateValueLabelAction == null)
                {
                    MethodInfo updateValueInfo = typeof(EnumField).GetMethod("UpdateValueLabel", BindingFlags.Instance | BindingFlags.NonPublic);
                    s_UpdateValueLabelAction = (Action<EnumField, Enum>)updateValueInfo.CreateDelegate(typeof(Action<EnumField, Enum>));
                }
                return s_UpdateValueLabelAction;
            }
        }
    }
}
