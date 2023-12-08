using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Mathematics;


namespace UnityEngine.Rendering.Universal
{
    [CreateAssetMenu(fileName="volumetricSettings.asset",menuName="Rendering/Volumetric Settings")]
    public class VolumetricQualitySettings : ScriptableObject
    {
        [Serializable]
        public struct VolSettings
        {
            public float near;// = 0.01f;
            public float far;// = 80f;
            public int3 froxelResolution;// = 32;
            public int clipMapResolution;// = 64;
            public float clipmapScale; //= 20;
            public float clipmapScale2; //= 200;
            public float clipmapResampleThreshold;// = 3;
        }

        
        public static VolSettings DefaultSettings()
        {
            return new VolSettings
            {
                near = 0.01f,
                far = 80f,
                froxelResolution = new int3(32,32,24),
                clipMapResolution = 64,
                clipmapScale = 20,
                clipmapScale2 = 200,
                clipmapResampleThreshold = 3
            };
        }

        [SerializeField] VolSettings[] SettingsLevels;

        public VolSettings Low { get => SettingsLevels[0]; }
        public VolSettings Medium { get => SettingsLevels[1]; }
        public VolSettings High { get => SettingsLevels[2]; }
        public VolSettings Ultra { get => SettingsLevels[3]; }

        public VolumetricQualitySettings()
        {
            SettingsLevels = new VolSettings[4]
            {
            DefaultSettings(),
            DefaultSettings(),
            DefaultSettings(),
            DefaultSettings(),
            };
        }

        public void OnValidate()
        {
            if (SettingsLevels == null)
            {
                SettingsLevels = new VolSettings[4]
                {
                DefaultSettings(),
                DefaultSettings(),
                DefaultSettings(),
                DefaultSettings(),
                };
            }
            if (SettingsLevels.Length < 4)
            {
                VolSettings[] newSettings = new VolSettings[4];
                for (int i = 0; i < SettingsLevels.Length; i++)
                {
                    newSettings[i] = SettingsLevels[i];
                }
                for (int i = SettingsLevels.Length; i < 4; i++)
                {
                    newSettings[i] = DefaultSettings();
                }
            }
            else if (SettingsLevels.Length > 4)
            {
                VolSettings[] newSettings = new VolSettings[4];
                for (int i = 0; i < 4; i++)
                {
                    newSettings[i] = SettingsLevels[i];
                }
            }
        }
    }

    [CustomEditor(typeof(VolumetricQualitySettings))]
    public class VolumetricSettingsUI : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement window = new VisualElement();
            VisualElement titleBar = CreateTitle();
            window.Add(titleBar);
            VisualElement nearClip = CreateRow<FloatField, float>("Near Clip", "near");
            window.Add(nearClip);
            VisualElement farClip = CreateRow<FloatField, float>("Far Clip", "far");
            window.Add(farClip);
            VisualElement froxelWidth = CreateRow<FloatField, float>("Froxel Width", "froxelResolution.x");
            window.Add(froxelWidth);
            VisualElement froxelHeight = CreateRow<FloatField, float>("Froxel Height", "froxelResolution.y");
            window.Add(froxelHeight);
            VisualElement froxelDepth = CreateRow<FloatField, float>("Froxel Depth", "froxelResolution.z");
            window.Add(froxelDepth);
            VisualElement clipMapRes = CreateRow<FloatField, float>("Clipmap Resolution", "clipMapResolution");
            window.Add(clipMapRes);
            VisualElement clipScale = CreateRow<FloatField, float>("Near Clipmap Size", "clipScale");
            window.Add(clipScale);
            VisualElement clipScale2 = CreateRow<FloatField, float>("Far Clipmap Size", "clipScale2");
            window.Add(clipScale2);
            VisualElement clipResample = CreateRow<FloatField, float>("Clipmap Resample Dist", "clipmapResampleThreshold");
            window.Add(clipResample);
            //Foldout lowSettings = LevelSettings("Low", 0);
            //window.Add(lowSettings);
            //Foldout medSettings = LevelSettings("Medium", 0);
            //window.Add(medSettings);
            //Foldout highSettings = LevelSettings("High", 0);
            //window.Add(highSettings);
            //Foldout ultraSettings = LevelSettings("Ultra", 0);
            //window.Add(ultraSettings);

            return window;
        }

        Foldout LevelSettings(string name, int level) 
        {
            string basebindingPath = "SettingsLevels.Array.data[" + level + "]";


            Foldout settingsFoldout = new Foldout();
            settingsFoldout.text = name;
            FloatField nearField = new FloatField("Near Clip");
            nearField.bindingPath = basebindingPath + ".near";
            settingsFoldout.Add(nearField);
            
            FloatField farField = new FloatField("Far Clip");
            farField.bindingPath = basebindingPath + ".far";
            settingsFoldout.Add(farField);
            
            Vector3IntField froxelResField = new Vector3IntField("Froxel Resolution");
            froxelResField.bindingPath = basebindingPath + ".froxelResolution";
            settingsFoldout.Add(froxelResField);
            
            FloatField clipScaleField = new FloatField("Near Clipmap Scale");
            clipScaleField.bindingPath = basebindingPath + ".clipmapScale";
            settingsFoldout.Add(clipScaleField);
            
            FloatField clip2ScaleField = new FloatField("Far Clipmap Scale");
            clip2ScaleField.bindingPath = basebindingPath + ".clipmapScale2";
            settingsFoldout.Add(clip2ScaleField);
            
            FloatField clipResampleField = new FloatField("Clipmap Refresh Distance");
            clipResampleField.bindingPath = basebindingPath + ".clipmapResampleThreshold";
            settingsFoldout.Add(clipResampleField);

            return settingsFoldout;
        }

        VisualElement CreateRow<T, T2>(string name, string bindingPath) where T : TextValueField<T2>, new() where T2 : struct
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignSelf = Align.Stretch;
            row.style.justifyContent = Justify.SpaceBetween;

            Label title = new Label(name);
            title.style.flexGrow = 0.3f;
            title.style.flexShrink = 0.3f;
            title.style.flexBasis = 0.3f;
            title.style.textOverflow = TextOverflow.Clip;
            title.style.overflow = Overflow.Hidden;
            row.Add(title);

            string basebindingPath = "SettingsLevels.Array.data";

            T lowField = new T();
            lowField.style.flexGrow = 0.175f;
            lowField.style.flexShrink = 0.175f;
            lowField.style.flexBasis = 0.175f;
            lowField.bindingPath = string.Format("{0}[0].{1}",basebindingPath, bindingPath);
            row.Add(lowField);

            T medField = new T();
            medField.style.flexGrow = 0.175f;
            medField.style.flexShrink = 0.175f;
            medField.style.flexBasis = 0.175f;
            medField.bindingPath = string.Format("{0}[1].{1}", basebindingPath, bindingPath);
            row.Add(medField);

            T highField = new T();
            highField.style.flexGrow = 0.175f;
            highField.style.flexShrink = 0.175f;
            highField.style.flexBasis = 0.175f;
            highField.bindingPath = string.Format("{0}[2].{1}", basebindingPath, bindingPath);
            row.Add(highField);

            T ultraField = new T();
            ultraField.style.flexGrow = 0.175f;
            ultraField.style.flexShrink = 0.175f;
            ultraField.style.flexBasis = 0.175f;
            ultraField.bindingPath = string.Format("{0}[3].{1}", basebindingPath, bindingPath);
            row.Add(ultraField);

            return row;
        }

        VisualElement CreateTitle()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignSelf = Align.Stretch;
            row.style.justifyContent = Justify.SpaceBetween;

            Label emptyLabel = new Label(" ");
            emptyLabel.style.flexGrow = 0.3f;
            emptyLabel.style.flexShrink = 0.3f;
            emptyLabel.style.flexBasis = 0.3f;
            emptyLabel.style.textOverflow = TextOverflow.Clip;
            emptyLabel.style.overflow = Overflow.Hidden;
            emptyLabel.style.unityTextAlign = TextAnchor.LowerCenter;
            row.Add(emptyLabel);

            string[] levelNames = new string[] {"Low", "Medium", "High", "Ultra" };
            for (int i = 0; i < 4; i++)
            {
                Label levelLabel = new Label(levelNames[i]);
                levelLabel.style.flexGrow = 0.175f;
                levelLabel.style.flexShrink = 0.175f;
                levelLabel.style.flexBasis = 0.175f;
                levelLabel.style.textOverflow = TextOverflow.Clip;
                levelLabel.style.overflow = Overflow.Hidden;
                levelLabel.style.unityTextAlign = TextAnchor.LowerCenter;
                row.Add(levelLabel);
            }


            return row;
        }
    }
}