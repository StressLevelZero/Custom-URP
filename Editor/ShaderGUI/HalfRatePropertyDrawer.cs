using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;

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

public class HalfRateSlopeDrawer : MaterialPropertyDrawer
{
    public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
    {
        return  2.0f * base.GetPropertyHeight(prop, label, editor);
    }
    public override void OnGUI(Rect position, MaterialProperty prop, string label, MaterialEditor editor)
    {
        bool halfRateValue = EndsInMagicNum(prop.floatValue);
        Rect half1 = position;
        half1.height = position.height * 0.5f;
        Rect half2 = position;
        half2.height = position.height * 0.5f;
        //half2.m
        //half2.width = position.width * 0.45f;
        half2.position = half2.position + new Vector2(0, half1.height);

        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = prop.hasMixedValue;

        halfRateValue = EditorGUI.Toggle(half1, "Half Rate Shading", halfRateValue);

        EditorGUI.showMixedValue = false;
        if (EditorGUI.EndChangeCheck())
        {
            prop.floatValue = halfRateValue ? AppendMagicNum(prop.floatValue) : RemoveMagicNum(prop.floatValue);
        }

        float SlopeValue = RemoveMagicNum(prop.floatValue);

        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = prop.hasMixedValue;
        SlopeValue = EditorGUI.FloatField(half2, label, SlopeValue);
        EditorGUI.showMixedValue = false;

        if (EditorGUI.EndChangeCheck())
        {
            prop.floatValue = halfRateValue ? AppendMagicNum(SlopeValue) : RemoveMagicNum(SlopeValue);
        }
    }

    static bool EndsInMagicNum(float val)
    {

        double v2 = Math.Abs(val) * 1000.0;
        v2 = v2 - Math.Floor(v2);
        return v2 >= 0.813 && v2 <= 0.817;
    }

    static float AppendMagicNum(float val)
    {
        int signV = Math.Sign(val);
        signV = signV == 0 ? 1 : signV;
        double v2 = val * 1000.0f;
        v2 = (Math.Floor(v2) * 0.001) + (signV * 0.0008148);
        return (float)v2;
    }
    static float RemoveMagicNum(float val)
    {
        int signV = Math.Sign(val);
        double v2 = val * 1000.0f;
        v2 = (signV == -1 ? Math.Ceiling(v2) : Math.Floor(v2)) * 0.001;
        return (float)v2;
    }
}

