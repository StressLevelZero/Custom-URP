using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class HalfRateDrawer : MaterialPropertyDrawer
{
    public override void OnGUI(Rect position, MaterialProperty prop, string label, MaterialEditor editor)
    {
        bool value = (prop.floatValue >= 0.0008147f && prop.floatValue <= 0.0008149f);

        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = prop.hasMixedValue;

        value = EditorGUI.Toggle(position, label, value);

        EditorGUI.showMixedValue = false;
        if (EditorGUI.EndChangeCheck())
        {
            prop.floatValue = value ? 0.0008148f : 0.0f;
        }
    }
}
