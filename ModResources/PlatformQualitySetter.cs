using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;
using Object = UnityEngine.Object;


namespace SLZ.URPModResources
{
    internal static class PlatformQualitySetter
    {
        const string ProjectQSPath = "ProjectSettings/QualitySettings.asset";
        const string PcPresetPath = "Packages/com.unity.render-pipelines.universal/Presets/QualitySettings_PC.preset";
        const string QuestPresetPath = "Packages/com.unity.render-pipelines.universal/Presets/QualitySettings_Quest.preset";
        static readonly int[] questSettingsIndex = { 3 };
        static readonly int[] standaloneSettingsIndex = { 2, 1, 0 };
        internal static void OverrideQualitySettings(BuildTarget target)
        {

            Object oldQS = AssetDatabase.LoadAllAssetsAtPath(ProjectQSPath)[0];
            Preset newQS;
            if (target == BuildTarget.Android)
            {
                newQS =  AssetDatabase.LoadAssetAtPath<Preset>(QuestPresetPath);
            }
            else
            {
                newQS = AssetDatabase.LoadAssetAtPath<Preset>(PcPresetPath);
            }
            newQS.ApplyTo(oldQS);
        }
    }
}
