using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

#if !MARROW_PROJECT || SLZ_RENDERPIPELINE_DEV
namespace SLZ.URPModResources
{
    public class URPModSetupUI : EditorWindow
    {
        [MenuItem("Stress Level Zero/Mod Graphics Setup")]
        public static void ShowWindow()
        {
            var window = GetWindow<URPModSetupUI>();
            window.titleContent = new GUIContent("Custom URP Setup For Mod Projects");
            window.minSize = new Vector2(280, 120);
            window.maxSize = new Vector2(280, 120);
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.alignContent = Align.Center;
            root.style.alignItems = Align.Center;
            
            // Creates our button and sets its Text property.
            Button btnGraphicsSettings = new Button() { text = "Set Graphics Settings for Bonelab Mods" };
            Button btnShaders = new Button() { text = "Copy Bonelab Shaders to Project" };

            // Give it some style.
            btnGraphicsSettings.style.width = 260;
            btnGraphicsSettings.style.height = 30;

            btnShaders.style.width = 260;
            btnShaders.style.height = 30;

            btnGraphicsSettings.RegisterCallback<ClickEvent>(e =>
            {
                InitializeProject.OverrideQualitySettings();
            });

            btnShaders.RegisterCallback<ClickEvent>(e =>
            {
                InitializeProject.CopyShadersToProject();
            });
            // Adds it to the root.
            root.Add(btnGraphicsSettings);
            root.Add(btnShaders);
        }
    }

    public class URPModUpdateShaderUI : EditorWindow
    {
#if SLZ_RENDERPIPELINE_DEV
        [MenuItem("Stress Level Zero/Graphics Update")]
#endif
        public static void ShowWindow()
        {
            var window = GetWindow<URPModUpdateShaderUI>();
            window.titleContent = new GUIContent("Update Bonelab Shaders");
            window.minSize = new Vector2(320, 160);
            window.maxSize = new Vector2(320, 160);
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.alignContent = Align.Center;
            root.style.alignItems = Align.Center;
            // root.style.flexWrap = Wrap.Wrap;
            // Creates our button and sets its Text property.
            Label warn = new Label() { text = "\nSLZ Custom URP updated, the included Bonelab shaders for mod projects (SLZShaders folder) may have updated as well. This can be updated later from the menu \"Stress Level Zero/Mod Graphics Setup\" Click below to update the shaders now. Any changes you have made to the included shaders will be overwritten!\n" };
            warn.style.flexWrap = Wrap.Wrap;
            warn.style.width = 304;
            warn.style.whiteSpace = WhiteSpace.Normal;
            warn.style.paddingLeft = 8;
            warn.style.paddingRight = 8;
            Button btnShaders = new Button() { text = "Update SLZShaders Folder in Project" };

            // Give it some style.
            btnShaders.style.width = 304;
            btnShaders.style.height = 30;
            btnShaders.style.paddingLeft = 8;
            btnShaders.style.paddingRight = 8;
            btnShaders.RegisterCallback<ClickEvent>(e =>
            {
                InitializeProject.CopyShadersToProject();
                Close();
            });
            // Adds it to the root.
            root.Add(warn);
            root.Add(btnShaders);
        }
    }
}
#endif