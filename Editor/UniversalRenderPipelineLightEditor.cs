using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{


    [CanEditMultipleObjects]
    [CustomEditorForRenderPipeline(typeof(Light), typeof(UniversalRenderPipelineAsset))]
    class UniversalRenderPipelineLightEditor : LightEditor
    {
        UniversalRenderPipelineSerializedLight serializedLight { get; set; }

        protected override void OnEnable()
        {
            serializedLight = new UniversalRenderPipelineSerializedLight(serializedObject, settings);
        }

        // IsPreset is an internal API - lets reuse the usable part of this function
        // 93 is a "magic number" and does not represent a combination of other flags here
        internal static bool IsPresetEditor(UnityEditor.Editor editor)
        {
            return (int)((editor.target as Component).gameObject.hideFlags) == 93;
        }


        bool showvolume = false;

        public override void OnInspectorGUI()
        {
            serializedLight.Update();

            if (IsPresetEditor(this))
            {
                UniversalRenderPipelineLightUI.PresetInspector.Draw(serializedLight, this);
            }
            else
            {
                UniversalRenderPipelineLightUI.Inspector.Draw(serializedLight, this);
            }

            Light light = (Light)target ;
            if (light.lightmapBakeType != LightmapBakeType.Realtime)
            {
                //Clamping range to the lowset possible value in 12bit precision.
                //Baked Lights should techically be infinite range with a simple inverse square falloff, but Unity has a bug with how range affects baked lights and will not render correctly if the values are too high.
                //light.range = Mathf.Sqrt(light.intensity / 0.000244140625f); 
            }
            //  CED.FoldoutGroup(LightUI.Styles.shapeHeader, Expandable.Shape, k_ExpandedState, DrawShapeContent),


            //CoreEditorDrawer.FoldoutGroup(LightUI.Styles.shapeHeader);

            //showvolume = EditorGUILayout.BeginFoldoutHeaderGroup(showvolume, "Dropdown");
            //if (showvolume)
            //{
            //    GUILayout.Label("This is a Label in a Custom Editor");
            //}
            //EditorGUILayout.EndFoldoutHeaderGroup();

            serializedLight.Apply();
        }

        protected override void OnSceneGUI()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
                return;

            if (!(target is Light light) || light == null)
                return;

            switch (light.type)
            {
                case LightType.Spot:
                    using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one)))
                    {
                        CoreLightEditorUtilities.DrawSpotLightGizmo(light);
                    }
                    break;

                case LightType.Point:
                    using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, Quaternion.identity, Vector3.one)))
                    {
                        CoreLightEditorUtilities.DrawPointLightGizmo(light);
                    }
                    break;

                case LightType.Rectangle:
                    using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one)))
                    {
                        CoreLightEditorUtilities.DrawRectangleLightGizmo(light);
                    }
                    break;

                case LightType.Disc:
                    using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one)))
                    {
                        CoreLightEditorUtilities.DrawDiscLightGizmo(light);
                    }
                    break;

                case LightType.Directional:
                    using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one)))
                    {
                        CoreLightEditorUtilities.DrawDirectionalLightGizmo(light);
                    }
                    break;

                default:
                    base.OnSceneGUI();
                    break;
            }
        }
    }
}
