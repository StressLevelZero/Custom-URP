using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using System.IO;

#if !MARROW_PROJECT || SLZ_RENDERPIPELINE_DEV
namespace SLZ.URPModResources
{
    public class URPModSetupUI : EditorWindow
    {
        [MenuItem("Stress Level Zero/URP Additional Asset Manager",priority = 2)]
        public static void ShowWindow()
        {
            var window = GetWindow<URPModSetupUI>();
            window.titleContent = new GUIContent("Custom URP Assets For Mod Projects");
            window.minSize = new Vector2(280, 150);
            window.maxSize = new Vector2(280, 150);
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.alignContent = Align.Center;
            root.style.alignItems = Align.Center;
            
            Button btnGraphicsSettings = new Button() { text = "Reset Graphics Settings" };
            Button btnShaders = new Button() { text = "Install Bonelab Shaders" };
            Button btnAmplify = new Button() { text = "Install Amplify Extensions" };

            btnGraphicsSettings.style.width = 260;
            btnShaders.style.width = 260;
            btnAmplify.style.width = 260;

            btnGraphicsSettings.style.height = 30;
            btnShaders.style.height = 30;
            btnAmplify.style.height = 30;


            btnGraphicsSettings.RegisterCallback<ClickEvent>(e =>
            {
                PlatformQualitySetter.OverrideQualitySettings(EditorUserBuildSettings.activeBuildTarget);
            });

            btnShaders.RegisterCallback<ClickEvent>(e =>
            {
                ExtractAssets.ExtractShaders(true);
            });

            btnAmplify.RegisterCallback<ClickEvent>(e =>
            {
                ExtractAssets.ExtractAmplify(true);
            });

            root.Add(btnGraphicsSettings);
            root.Add(btnShaders);
            root.Add(btnAmplify);
        }
    }
    /*
    public class URPModUpdateShaderUI : EditorWindow
    {
        private int numButtons = 1;

#if SLZ_RENDERPIPELINE_DEV
        [MenuItem("Stress Level Zero/Graphics Update")]
#endif
        public static void ShowWindow()
        {
            var window = GetWindow<URPModUpdateShaderUI>();
            window.titleContent = new GUIContent("Update Bonelab Shaders");
            window.minSize = new Vector2(360, 300);
            window.maxSize = new Vector2(360, 300);
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.alignContent = Align.Center;
            root.style.alignItems = Align.Center;

            string ShaderPath = Path.Combine(Application.dataPath, "SLZShaders");
            bool hasShaders = Directory.Exists(ShaderPath);

            string updateText =
                "\nSLZ Custom URP package updated. The package may include updated versions of the essential Bonelab shaders (SLZShaders folder) in your project. Click below to update the shaders now.\n\n" +
                "Note: It is highly recommended to avoid modifying the included files, as any updates to this package will overwrite the files and delete your changes.\n\n" +
                "These shaders can be updated/installed later from the menu \"Stress Level Zero/URP Additional Asset Manager\".\n";
            string installText =
                "\nSLZ Custom URP package updated. The package includes some of Bonelab's core shaders for your use. In order to use them, they must be installed into your project directly. Click below to install the shaders\n\n" +
                "Note: It is highly recommended to avoid modifying the included files, as any updates to this package will overwrite the files and delete your changes.\n\n" +
                "These shaders can be updated/installed later from the menu \"Stress Level Zero/URP Additional Asset Manager\".\n";
            Label warn = new Label() { text = hasShaders ? updateText : installText
            };
            warn.style.flexWrap = Wrap.Wrap;
            warn.style.width = 334;
            warn.style.whiteSpace = WhiteSpace.Normal;
            warn.style.paddingLeft = 8;
            warn.style.paddingRight = 8;



            Button btnShaders = new Button() { text = hasShaders ? "Update Bonelab Shaders" : "Install Bonelab Shaders" };

            btnShaders.style.width = 304;
            btnShaders.style.height = 30;
            btnShaders.style.paddingLeft = 8;
            btnShaders.style.paddingRight = 8;

            btnShaders.RegisterCallback<ClickEvent>(e =>
            {
                ExtractAssets.ExtractShaders(true);
                numButtons--;
                if (numButtons < 1)
                {
                    Close();
                }
            });

            root.Add(warn);
            root.Add(btnShaders);

            string AmplifyPath = Path.Combine(Application.dataPath, "AmplifyShaderEditor");
            bool hasAmplify = Directory.Exists(AmplifyPath);

            Button btnAmplify = new Button() { text = "Update Amplify Extensions" };

            btnAmplify.style.width = 304;
            btnAmplify.style.height = 30;
            btnAmplify.style.paddingLeft = 8;
            btnAmplify.style.paddingRight = 8;

            if (hasAmplify)
            {
                numButtons++;
                root.Add(btnAmplify);
            }

            btnAmplify.RegisterCallback<ClickEvent>(e =>
            {
                ExtractAssets.ExtractAmplify(true);
                numButtons--;
                if (numButtons < 1)
                {
                    Close();
                }
            });
        }
    }
    */
}
#endif