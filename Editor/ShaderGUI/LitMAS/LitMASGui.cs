using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Unity.Collections.LowLevel.Unsafe;
using System.Reflection;
using SLZ.SLZEditorTools;
using UnityEditor.SLZMaterialUI;

namespace UnityEditor // This MUST be in the base editor namespace!!!!!
{
    [CanEditMultipleObjects]
    public class LitMASGUI : UIElementsMaterialEditor
    {

        public override VisualElement CreateInspectorGUI()
        {

            bool success = base.Initialize();
            if (!success)
            {
                return null;
            }

            VisualElement MainWindow = new VisualElement();
            MainWindow.styleSheets.Add(ShaderGUIUtils.shaderGUISheet);

            MaterialProperty[] props = GetMaterialProperties(this.targets);

            int[] propIdx = ShaderGUIUtils.GetMaterialPropertyShaderIdx(props, shader);
            ShaderGUIUtils.SanitizeMaterials(this.targets, props, propIdx, shader);

         
            Foldout drawProps = new Foldout();

            RenderQueueDropdown renderQueue = new RenderQueueDropdown(serializedObject, shader);
            drawProps.contentContainer.Add(renderQueue);

            Texture2D RTIcon = ShaderGUIUtils.GetClosestUnityIconMip("RenderTexture Icon", 16);
            ShaderGUIUtils.SetHeaderStyle(drawProps, "Rendering Properties", RTIcon);
            drawProps.value = false;

            Foldout baseProps = new Foldout();
            Texture2D MaterialIcon = ShaderGUIUtils.GetClosestUnityIconMip("Material Icon", 16);
            ShaderGUIUtils.SetHeaderStyle(baseProps, "Core Shading", MaterialIcon);

            Toggle emissionToggle = new Toggle();
            Foldout emissionProps = new Foldout();
            Texture2D LightIcon = ShaderGUIUtils.GetClosestUnityIconMip("Light Icon", 16);
            ShaderGUIUtils.SetHeaderStyle(emissionProps, "Emission", LightIcon, emissionToggle);

            MaterialProperty baseMapProp = null;
            for (int i = 0; i <  props.Length; i++)
            {
                if (props[i].name == "_BaseMap")
                {
                    baseMapProp = props[i];
                    break;
                }
            }

            TextureField baseMap = new TextureField(baseMapProp);
            baseProps.Add(baseMap);
            //testBox.name = "headerRoot";
            MainWindow.Add(drawProps);
            MainWindow.Add(baseProps);
            MainWindow.Add(emissionProps);

            return MainWindow;
        }


        //public override void OnInspectorGUI()
        //{
        //    Debug.Log("Called OnInspectorGUI");
        //}

    }
}