using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Scripting.APIUpdating;
// SLZ MODIFIED
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;
// END SLZ MODIFIED

namespace UnityEditor
{
    /// <summary>
    /// Editor script for the Lighting Explorer.
    /// </summary>
    [LightingExplorerExtensionAttribute(typeof(UniversalRenderPipelineAsset))]
    public class LightExplorer : DefaultLightingExplorerExtension
    {
        private static class Styles
        {
            public static readonly GUIContent Enabled = EditorGUIUtility.TrTextContent("Enabled");
            public static readonly GUIContent Name = EditorGUIUtility.TrTextContent("Name");
            public static readonly GUIContent Mode = EditorGUIUtility.TrTextContent("Mode");

            public static readonly GUIContent HDR = EditorGUIUtility.TrTextContent("HDR");
            public static readonly GUIContent ShadowDistance = EditorGUIUtility.TrTextContent("Shadow Distance");
            public static readonly GUIContent NearPlane = EditorGUIUtility.TrTextContent("Near Plane");
            public static readonly GUIContent FarPlane = EditorGUIUtility.TrTextContent("Far Plane");
            public static readonly GUIContent Resolution = EditorGUIUtility.TrTextContent("Resolution");

            public static readonly GUIContent[] ReflectionProbeModeTitles = { EditorGUIUtility.TrTextContent("Baked"), EditorGUIUtility.TrTextContent("Realtime"), EditorGUIUtility.TrTextContent("Custom") };
            public static readonly int[] ReflectionProbeModeValues = { (int)ReflectionProbeMode.Baked, (int)ReflectionProbeMode.Realtime, (int)ReflectionProbeMode.Custom };
            public static readonly GUIContent[] ReflectionProbeSizeTitles = { EditorGUIUtility.TrTextContent("16"),
                                                                              EditorGUIUtility.TrTextContent("32"),
                                                                              EditorGUIUtility.TrTextContent("64"),
                                                                              EditorGUIUtility.TrTextContent("128"),
                                                                              EditorGUIUtility.TrTextContent("256"),
                                                                              EditorGUIUtility.TrTextContent("512"),
                                                                              EditorGUIUtility.TrTextContent("1024"),
                                                                              EditorGUIUtility.TrTextContent("2048") };
            public static readonly int[] ReflectionProbeSizeValues = { 16, 32, 64, 128, 256, 512, 1024, 2048 };
            // SLZ MODIFIED
            public static readonly GUIContent[] ReflectionProbeBackgroundTitles = { EditorGUIUtility.TrTextContent("Skybox"), EditorGUIUtility.TrTextContent("SolidColor") };
            public static readonly int[] ReflectionProbeBackgroundValues = { (int)ReflectionProbeClearFlags.Skybox, (int)ReflectionProbeClearFlags.SolidColor };
            // END SLZ MODIFIED
        }

        /// <inheritdoc />
        protected override LightingExplorerTableColumn[] GetReflectionProbeColumns()
        {
            return new[]
            {
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Checkbox, Styles.Enabled, "m_Enabled", 50), // 0: Enabled
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Name, Styles.Name, null, 200),  // 1: Name
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Int, Styles.Mode, "m_Mode", 70, (r, prop, dep) =>
                {
                    EditorGUI.IntPopup(r, prop, Styles.ReflectionProbeModeTitles, Styles.ReflectionProbeModeValues, GUIContent.none);
                }),     // 2: Mode
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Checkbox, Styles.HDR, "m_HDR", 35),  // 3: HDR
                // SLZ MODIFIED
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Color,  EditorGUIUtility.TrTextContent("Box Projection"), "m_BoxProjection", 80), // 7: Far Plane
                // END SLZ MODIFIED
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Enum, Styles.Resolution, "m_Resolution", 100, (r, prop, dep) =>
                {
                    EditorGUI.IntPopup(r, prop, Styles.ReflectionProbeSizeTitles, Styles.ReflectionProbeSizeValues, GUIContent.none);
                }), // 4: Probe Resolution
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Float, Styles.ShadowDistance, "m_ShadowDistance", 100), // 5: Shadow Distance
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Float, Styles.NearPlane, "m_NearClip", 70), // 6: Near Plane
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Float, Styles.FarPlane, "m_FarClip", 70), // 7: Far Plane
                // SLZ MODIFIED
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Float,  EditorGUIUtility.TrTextContent("Blend Distance"), "m_BlendDistance", 80), // 7: Far Plane
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Enum,  EditorGUIUtility.TrTextContent("Clear Flag"), "m_ClearFlags", 80, (r, prop, dep) =>
                {
                EditorGUI.IntPopup(r, prop, Styles.ReflectionProbeBackgroundTitles, Styles.ReflectionProbeBackgroundValues, GUIContent.none);
                }),
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Color,  EditorGUIUtility.TrTextContent("Background Color"), "m_BackGroundColor", 80), // 7: Far Plane
                // END SLZ MODIFIED
            };
        }

        // SLZ MODIFIED

        static Dictionary<Volume, VolumeData> volumeDataPairing = new Dictionary<Volume, VolumeData>();
        static Dictionary<Light, LightData> lightDataPairing = new Dictionary<Light, LightData>();

        struct VolumeData
        {
            public bool isGlobal;
            //public bool hasVisualEnvironment;
            public VolumeProfile profile;
            //public bool fogEnabled;
            //public bool volumetricEnabled;
            //public int skyType;

            public VolumeData(bool isGlobal, VolumeProfile profile)
            {
                this.isGlobal = isGlobal;
                this.profile = profile;
                // VisualEnvironment visualEnvironment = null;
                //Fog fog = null;
                //this.hasVisualEnvironment = profile != null ? profile.TryGet(out visualEnvironment) : false;
                //bool hasFog = profile != null ? profile.TryGet(out fog) : false;
                //this.skyType = this.hasVisualEnvironment ? visualEnvironment.skyType.value : 0;
                //this.fogEnabled = hasFog ? fog.enabled.value : false;
                //this.volumetricEnabled = hasFog ? fog.enableVolumetricFog.value : false;
            }
        }

        struct LightData
        {
            public UniversalAdditionalLightData UniAdditionalLightData;
            public bool isPrefab;
            public Object prefabRoot;

            public LightData(UniversalAdditionalLightData UniAdditionalLightData, bool isPrefab, Object prefabRoot)
            {
                this.UniAdditionalLightData = UniAdditionalLightData;
                this.isPrefab = isPrefab;
                this.prefabRoot = prefabRoot;
            }
        }

        protected static class HDStyles
        {
            public static readonly GUIContent Name = EditorGUIUtility.TrTextContent("Name");
            public static readonly GUIContent Enabled = EditorGUIUtility.TrTextContent("Enabled");
            public static readonly GUIContent Type = EditorGUIUtility.TrTextContent("Type");
            public static readonly GUIContent Shape = EditorGUIUtility.TrTextContent("Shape");
            public static readonly GUIContent Mode = EditorGUIUtility.TrTextContent("Mode");
            public static readonly GUIContent Range = EditorGUIUtility.TrTextContent("Range");
            public static readonly GUIContent Color = EditorGUIUtility.TrTextContent("Color");
            public static readonly GUIContent ColorFilter = EditorGUIUtility.TrTextContent("Color Filter");
            public static readonly GUIContent Intensity = EditorGUIUtility.TrTextContent("Intensity");
            public static readonly GUIContent IndirectMultiplier = EditorGUIUtility.TrTextContent("Indirect Multiplier");
            public static readonly GUIContent Unit = EditorGUIUtility.TrTextContent("Unit");
            public static readonly GUIContent ColorTemperature = EditorGUIUtility.TrTextContent("Color Temperature");
            public static readonly GUIContent Shadows = EditorGUIUtility.TrTextContent("Shadows");
            public static readonly GUIContent ContactShadowsLevel = EditorGUIUtility.TrTextContent("Contact Shadows Level");
            public static readonly GUIContent ContactShadowsValue = EditorGUIUtility.TrTextContent("Contact Shadows Value");
            public static readonly GUIContent ShadowResolutionLevel = EditorGUIUtility.TrTextContent("Shadows Resolution Level");
            public static readonly GUIContent ShadowUpdateMode = EditorGUIUtility.TrTextContent("Shadows Update Mode");
            public static readonly GUIContent ShadowFitAtlas = EditorGUIUtility.TrTextContent("Shadows Fit Atlas");
            public static readonly GUIContent ShadowResolutionValue = EditorGUIUtility.TrTextContent("Shadows Resolution Value");
            public static readonly GUIContent ShapeWidth = EditorGUIUtility.TrTextContent("Shape Width");
            public static readonly GUIContent VolumeProfile = EditorGUIUtility.TrTextContent("Volume Profile");
            public static readonly GUIContent ColorTemperatureMode = EditorGUIUtility.TrTextContent("Use Color Temperature");
            public static readonly GUIContent AffectDiffuse = EditorGUIUtility.TrTextContent("Affect Diffuse");
            public static readonly GUIContent AffectSpecular = EditorGUIUtility.TrTextContent("Affect Specular");
            public static readonly GUIContent FadeDistance = EditorGUIUtility.TrTextContent("Fade Distance");
            public static readonly GUIContent ShadowFadeDistance = EditorGUIUtility.TrTextContent("Shadow Fade Distance");
            public static readonly GUIContent LightLayer = EditorGUIUtility.TrTextContent("Light Layer");
            public static readonly GUIContent IsPrefab = EditorGUIUtility.TrTextContent("Prefab");

            public static readonly GUIContent VolumeMode = EditorGUIUtility.TrTextContent("Mode");
            public static readonly GUIContent Priority = EditorGUIUtility.TrTextContent("Priority");
            public static readonly GUIContent BlendDistance = EditorGUIUtility.TrTextContent("Blend Distance");

            public static readonly GUIContent TexelDensity = EditorGUIUtility.TrTextContent("Texel Density");

            public static readonly GUIContent HasVisualEnvironment = EditorGUIUtility.TrTextContent("Has Visual Environment");
            public static readonly GUIContent Fog = EditorGUIUtility.TrTextContent("Fog");
            public static readonly GUIContent Volumetric = EditorGUIUtility.TrTextContent("Volumetric");
            public static readonly GUIContent SkyType = EditorGUIUtility.TrTextContent("Sky Type");

            public static readonly GUIContent ShadowDistance = EditorGUIUtility.TrTextContent("Shadow Distance");
            public static readonly GUIContent NearClip = EditorGUIUtility.TrTextContent("Near Clip");
            public static readonly GUIContent FarClip = EditorGUIUtility.TrTextContent("Far Clip");
            public static readonly GUIContent ParallaxCorrection = EditorGUIUtility.TrTextContent("Influence Volume as Proxy Volume");
            public static readonly GUIContent Weight = EditorGUIUtility.TrTextContent("Weight");

            public static readonly GUIContent[] LightTypeTitles = { EditorGUIUtility.TrTextContent("Spot"), EditorGUIUtility.TrTextContent("Directional"), EditorGUIUtility.TrTextContent("Point"), EditorGUIUtility.TrTextContent("Area") };
            // public static readonly int[] LightTypeValues = { (int)HDLightType.Spot, (int)HDLightType.Directional, (int)HDLightType.Point, (int)HDLightType.Area };
            internal static readonly GUIContent DrawProbes = EditorGUIUtility.TrTextContent("Draw");
            internal static readonly GUIContent DebugColor = EditorGUIUtility.TrTextContent("Debug Color");
            internal static readonly GUIContent ResolutionX = EditorGUIUtility.TrTextContent("Resolution X");
            internal static readonly GUIContent ResolutionY = EditorGUIUtility.TrTextContent("Resolution Y");
            internal static readonly GUIContent ResolutionZ = EditorGUIUtility.TrTextContent("Resolution Z");
            internal static readonly GUIContent FadeStart = EditorGUIUtility.TrTextContent("Fade Start");
            internal static readonly GUIContent FadeEnd = EditorGUIUtility.TrTextContent("Fade End");

            public static readonly GUIContent[] globalModes = { new GUIContent("Global"), new GUIContent("Local") };
        }


        //Adding Volume and volumetric tabs
        public override LightingExplorerTab[] GetContentTabs()
        {
            return new[]
            {
                new LightingExplorerTab("Lights", GetHDLights, GetLightColumns, true),
                new LightingExplorerTab("Volumes", GetVolumes, GetVolumeColumns, true),
                new LightingExplorerTab("Baked Volumetrics", GetBakedVolumetrics, GetBakedVolumetricColumns, true),
                new LightingExplorerTab("Reflection Probes", GetReflectionProbes, GetReflectionProbeColumns, true),
                new LightingExplorerTab("Light Probes", GetLightProbes, GetLightProbeColumns, true),
                new LightingExplorerTab("Emissive Materials", GetEmissives, GetEmissivesColumns, false)
            };
        }

        protected virtual UnityEngine.Object[] GetHDLights()
        {
#if UNITY_2020_1_OR_NEWER
            var lights = Resources.FindObjectsOfTypeAll<Light>();
#else
            var lights = UnityEngine.Object.FindObjectsOfType<Light>();
#endif

            foreach (Light light in lights)
            {
                if (PrefabUtility.GetCorrespondingObjectFromSource(light) != null) // We have a prefab
                {
                    lightDataPairing[light] = new LightData(light.GetComponent<UniversalAdditionalLightData>(), true, PrefabUtility.GetCorrespondingObjectFromSource(PrefabUtility.GetOutermostPrefabInstanceRoot(light.gameObject)));
                }
                else
                {
                    lightDataPairing[light] = new LightData(light.GetComponent<UniversalAdditionalLightData>(), false, null);
                }
            }
            return lights;
        }



        ////////////
        ///VOLUMETRIC BAKE
        ///////////////
        protected virtual UnityEngine.Object[] GetBakedVolumetrics()
        {
#if UNITY_2020_1_OR_NEWER
            var volumes = Resources.FindObjectsOfTypeAll<BakedVolumetricArea>();
#else
            var volumes = UnityEngine.Object.FindObjectsOfType<Volume>();
#endif

            //foreach (var volume in volumes)
            //{
            //    volumeDataPairing[volume] = !volume.HasInstantiatedProfile() && volume.sharedProfile == null
            //        ? new VolumeData(volume.isGlobal, null)
            //        : new VolumeData(volume.isGlobal, volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile);
            //}
            return volumes;
        }

        protected virtual LightingExplorerTableColumn[] GetBakedVolumetricColumns()
        {
            return new[]
            {
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Checkbox, HDStyles.Enabled, "m_Enabled", 60), // 0: Enabled
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Name, HDStyles.Name, null, 200), //Name
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Float,  EditorGUIUtility.TrTextContent("Texel Density"), "TexelDensity", 60), // 3: Density
            };
        }


        ////////////
        ///VOLUMES
        ////////////

        //Why do I have to do this? This is mostly from HDRP. It has been done fo a long time. Just fucking add it to URP unity.
        protected virtual UnityEngine.Object[] GetVolumes()
        {
#if UNITY_2020_1_OR_NEWER
            var volumes = Resources.FindObjectsOfTypeAll<Volume>();
#else
            var volumes = UnityEngine.Object.FindObjectsOfType<Volume>();
#endif

            foreach (var volume in volumes)
            {
                volumeDataPairing[volume] = !volume.HasInstantiatedProfile() && volume.sharedProfile == null
                    ? new VolumeData(volume.isGlobal, null)
                    : new VolumeData(volume.isGlobal, volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile);
            }
            return volumes;
        }

        private bool TryGetAdditionalVolumeData(SerializedProperty prop, out VolumeData volumeData)
        {
            Volume volume = prop.serializedObject.targetObject as Volume;

            if (volume == null || !volumeDataPairing.ContainsKey(volume))
            {
                volumeData = new VolumeData();
                return false;
            }

            volumeData = volumeDataPairing[volume];
            return true;
        }





        protected virtual LightingExplorerTableColumn[] GetVolumeColumns()
        {
            return new[]
            {
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Checkbox, HDStyles.Enabled, "m_Enabled", 60),                                      // 0: Enabled
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Name, HDStyles.Name, null, 200),                                                   // 1: Name
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Enum, HDStyles.VolumeMode, "m_IsGlobal", 75, (r, prop, dep) =>                       // 2: Is Global
                {
                    if (!TryGetAdditionalVolumeData(prop, out var volumeData))
                    {
                        EditorGUI.LabelField(r, "--");
                        return;
                    }

                    int isGlobal = volumeData.isGlobal ? 0 : 1;
                    EditorGUI.BeginChangeCheck();
                    isGlobal = EditorGUI.Popup(r, isGlobal, HDStyles.globalModes);
                    if (EditorGUI.EndChangeCheck())
                        prop.boolValue = isGlobal == 0;
                }, (lprop, rprop) =>
                    {
                        bool lHasVolume = TryGetAdditionalVolumeData(lprop, out var lVolumeData);
                        bool rHasVolume = TryGetAdditionalVolumeData(rprop, out var rVolumeData);

                        return (lHasVolume ? lVolumeData.isGlobal : false).CompareTo((rHasVolume ? rVolumeData.isGlobal : false));
                    }),
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Float, HDStyles.Priority, "priority", 60),                                         // 3: Priority
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Float, HDStyles.BlendDistance, "blendDistance", 60),                                        // 3: Priority
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Custom, HDStyles.VolumeProfile, "sharedProfile", 200, (r, prop, dep) =>            // 4: Profile
                {
                    EditorGUI.PropertyField(r, prop, GUIContent.none);
                }, (lprop, rprop) =>
                    {
                        return EditorUtility.NaturalCompare(((lprop == null || lprop.objectReferenceValue == null) ? "--" : lprop.objectReferenceValue.name), ((rprop == null || rprop.objectReferenceValue == null) ? "--" : rprop.objectReferenceValue.name));
                    }),
            };
        }

        private bool TryGetAdditionalLightData(SerializedProperty prop, out UniversalAdditionalLightData lightData)
        {
            return TryGetAdditionalLightData(prop, out lightData, out var light);
        }
        private bool TryGetAdditionalLightData(SerializedProperty prop, out UniversalAdditionalLightData lightData, out Light light)
        {
            light = prop.serializedObject.targetObject as Light;

            if (light == null || !lightDataPairing.ContainsKey(light))
                lightData = null;
            else
                lightData = lightDataPairing[light].UniAdditionalLightData;

            return lightData != null;
        }


        protected virtual LightingExplorerTableColumn[] GetHDLightColumns()
        {
            return new[]
            {
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Checkbox, HDStyles.Enabled, "m_Enabled", 60),                                  // 0: Enabled
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Name, HDStyles.Name, null, 200),                                               // 1: Name
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Enum, HDStyles.Type, "m_Type", 100), 
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Enum, HDStyles.Mode, "m_Lightmapping", 90),                                    // 3: Mixed mode
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Float, HDStyles.Range, "m_Range", 60),                                         // 4: Range
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Color, HDStyles.Color, "m_Color", 60),                                         // 5: Color
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Checkbox, HDStyles.ColorTemperatureMode, "m_UseColorTemperature", 150),        // 6: Color Temperature Mode
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Float, HDStyles.ColorTemperature, "m_ColorTemperature", 120, (r, prop, dep) => // 7: Color Temperature
                {
                    // Sometimes during scene transition, the target object can be null, causing exceptions.
                    using (new EditorGUI.DisabledScope(prop.serializedObject.targetObject == null || !prop.serializedObject.FindProperty("m_UseColorTemperature").boolValue))
                    {
                        //EditorGUI.BeginChangeCheck();
                        //EditorGUI.PropertyField(r, prop, GUIContent.none);
                        //if (EditorGUI.EndChangeCheck())
                        //    TemperatureSliderUIDrawer.ClampValue(prop);
                    }
                }, (lprop, rprop) =>
                    {
                        float lTemp = lprop.serializedObject.FindProperty("m_UseColorTemperature").boolValue ? lprop.floatValue : 0.0f;
                        float rTemp = rprop.serializedObject.FindProperty("m_UseColorTemperature").boolValue ? rprop.floatValue : 0.0f;

                        return lTemp.CompareTo(rTemp);
                    }),
                new LightingExplorerTableColumn(LightingExplorerTableColumn.DataType.Float, HDStyles.Intensity, "m_Intensity", 60, (r, prop, dep) =>                // 8: Intensity
                {
                    if (!TryGetAdditionalLightData(prop, out var lightData))
                    {
                        EditorGUI.LabelField(r, "--");
                        return;
                    }

                    float intensity = lightData.intensity;

                    EditorGUI.BeginProperty(r, GUIContent.none, prop);
                    EditorGUI.BeginChangeCheck();
                    intensity = EditorGUI.FloatField(r, intensity);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObjects(new Object[] { prop.serializedObject.targetObject, lightData }, "Changed light intensity");
                        lightData.intensity = intensity;
                    }
                    EditorGUI.EndProperty();
                }, (lprop, rprop) =>
                    {
                        TryGetAdditionalLightData(lprop, out var lLightData);
                        TryGetAdditionalLightData(rprop, out var rLightData);

                        if (IsNullComparison(lLightData, rLightData, out var order))
                            return order;

                        return ((float)lLightData.intensity).CompareTo((float)rLightData.intensity);
                    }, (target, source) =>
                    {
                        if (!TryGetAdditionalLightData(target, out var tLightData) || !TryGetAdditionalLightData(source, out var sLightData))
                            return;

                        Undo.RecordObjects(new Object[] { target.serializedObject.targetObject, tLightData }, "Changed light intensity");
                        tLightData.intensity = sLightData.intensity;
                    }),
            };
        }



        //
        // Summary:
        //     Returns column definitions for Light Probes.
        //
        // Returns:
        //     Column definitions for Light Probes.
        //protected virtual LightingExplorerTableColumn[] GetVolumeColumns();
        ////
        //// Summary:
        ////     Returns Light Probes.
        ////
        //// Returns:
        ////     Light Probes.
        //protected virtual Object[] GetVolumes();

        public override void OnDisable()
        {
            lightDataPairing.Clear();
            volumeDataPairing.Clear();
            //     serializedReflectionProbeDataPairing.Clear();
        }

        private bool IsNullComparison<T>(T l, T r, out int order)
        {
            if (l == null)
            {
                order = r == null ? 0 : -1;
                return true;
            }
            else if (r == null)
            {
                order = 1;
                return true;
            }

            order = 0;
            return false;
        }

        // END SLZ MODIFIED
    }
}
