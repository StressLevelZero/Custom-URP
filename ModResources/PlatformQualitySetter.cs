using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;


namespace SLZ.URPModResources
{
    internal static class PlatformQualitySetter
    {
        const string ProjectQSPath = "ProjectSettings/QualitySettings.asset";
        const string TemplateQSPath = "Packages/com.unity.render-pipelines.universal/ModResources/QualitySettings/QualitySettings.asset";
        static readonly int[] questSettingsIndex = { 3 };
        static readonly int[] standaloneSettingsIndex = { 2, 1, 0 };
        internal static void OverrideQualitySettings(BuildTarget target)
        {
            Object oldQS = AssetDatabase.LoadAllAssetsAtPath(ProjectQSPath)[0];
            Object newQS = AssetDatabase.LoadAllAssetsAtPath(TemplateQSPath)[0];
            EditorUtility.CopySerialized(newQS, oldQS);
            SerializedObject qs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(ProjectQSPath)[0]);
            SerializedProperty qsArray = qs.FindProperty("m_QualitySettings");
            SerializedProperty qsCurrent = qs.FindProperty("m_CurrentQuality");
            switch (target)
            {
                case BuildTarget.Android:
                    RemoveSPArrayItems(ref qsArray, standaloneSettingsIndex);
                    qsCurrent.intValue = 0; // Quest Performant
                    break;
                default:
                    RemoveSPArrayItems(ref qsArray, questSettingsIndex);
                    qsCurrent.intValue = 2; // PC High
                    break;
            }
            qs.ApplyModifiedProperties();
        }

        static void RemoveSPArrayItems(ref SerializedProperty prop, int[] items)
        {
            Array.Sort(items);
            int max = items.Length - 1;
            for (int i = max; i >= 0; i--) // go in reverse order so removing an item doesn't change the index of the next item
            {
                prop.DeleteArrayElementAtIndex(items[i]);
            }
        }
    }
}
