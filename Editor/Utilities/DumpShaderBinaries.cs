using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor.Rendering;
using System.IO;

namespace SLZ.SLZEditorTools
{
    public class DumpShaderBinaries : EditorWindow
    {
        SerializedObject thisSerialized;
        public BuildTarget buildTarget;
        public ShaderCompilerPlatform graphicsAPI;
        public Shader shader;
        public int subShaderIndex;
        public string passName;
        public List<string> shaderKeywords;
        public string outputPath;

        Dictionary<ShaderType, string> stageExt = new Dictionary<ShaderType, string>() {
        { ShaderType.Vertex, "vert" },
        { ShaderType.Fragment, "frag" },
        { ShaderType.Geometry, "geom" },
        { ShaderType.Hull, "hs" },
        { ShaderType.Domain, "ds" },
        { ShaderType.Surface, "surf" },
        { ShaderType.RayTracing, "rayt" }
    };

        Dictionary<ShaderCompilerPlatform, string> apiExt = new Dictionary<ShaderCompilerPlatform, string>()
    {
        { ShaderCompilerPlatform.D3D, ".dxbc" },
        { ShaderCompilerPlatform.Vulkan, ".spv"}
    };

        [MenuItem("Stress Level Zero/Graphics/Dump Compiled Shader Binaries")]
        public static void ShowWindow()
        {
            DumpShaderBinaries window = GetWindow<DumpShaderBinaries>();
            window.titleContent = new GUIContent("Dump Compiled Shader Binaries");
        }

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            thisSerialized = new SerializedObject(this);
            if (buildTarget == 0) buildTarget = EditorUserBuildSettings.activeBuildTarget;
            //if (graphicsAPI == ShaderCompilerPlatform.None) graphicsAPI = PlayerSettings.GetGraphicsAPIs(buildTarget)[0];
            PropertyField graphicsAPIField = new PropertyField();
            graphicsAPIField.bindingPath = "graphicsAPI";
            graphicsAPIField.Bind(thisSerialized);



            PropertyField buildTargetField = new PropertyField();
            buildTargetField.bindingPath = "buildTarget";
            buildTargetField.Bind(thisSerialized);

            PropertyField shaderField = new PropertyField();
            shaderField.bindingPath = "shader";
            shaderField.Bind(thisSerialized);

            PropertyField subShaderField = new PropertyField();
            subShaderField.bindingPath = "subShaderIndex";
            subShaderField.Bind(thisSerialized);

            PropertyField passNameField = new PropertyField();
            passNameField.bindingPath = "passName";
            passNameField.Bind(thisSerialized);

            PropertyField shaderKeywordsField = new PropertyField();
            shaderKeywordsField.bindingPath = "shaderKeywords";
            shaderKeywordsField.Bind(thisSerialized);

            PropertyField outputPathField = new PropertyField();
            outputPathField.bindingPath = "outputPath";
            outputPathField.Bind(thisSerialized);

            Button button = new Button(PrintStats);
            button.text = "Print Stats";

            root.Add(graphicsAPIField);
            root.Add(buildTargetField);
            root.Add(shaderField);
            root.Add(subShaderField);
            root.Add(passNameField);
            root.Add(shaderKeywordsField);
            root.Add(outputPathField);
            root.Add(button);
        }

        public void PrintStats()
        {
            int numPasses = shader.GetPassCountInSubshader(subShaderIndex);
            ShaderTagId lightmode = new ShaderTagId("LightMode");
            ShaderTagId lightmodeName = new ShaderTagId(passName);
            int passIdx = -1;
            for (int i = 0; i < numPasses; i++)
            {
                if (shader.FindPassTagValue(subShaderIndex, i, lightmode) == lightmodeName)
                {
                    passIdx = i;
                }
            }


            ShaderData shaderData = ShaderUtil.GetShaderData(shader);
            ShaderData.Subshader subShader = shaderData.GetSubshader(subShaderIndex);
            ShaderData.Pass pass = subShader.GetPass(passIdx);

            for (ShaderType stage = ShaderType.Vertex; stage <= ShaderType.Count; stage++)
            {
                if (!pass.HasShaderStage(stage)) continue;

                ShaderData.VariantCompileInfo compileInfo = pass.CompileVariant(stage, shaderKeywords.ToArray(), graphicsAPI, buildTarget, true);
                ShaderMessage[] messages = compileInfo.Messages;
                foreach (var message in messages)
                {
                    string logMsg = $"{message.severity}: {message.message}\nfile:{message.file}, line: {message.line}";
                    switch (message.severity)
                    {
                        case ShaderCompilerMessageSeverity.Error:
                            Debug.LogError(logMsg);
                            break;
                        case ShaderCompilerMessageSeverity.Warning:
                            Debug.LogWarning(logMsg);
                            break;
                    }
                }
                if (!compileInfo.Success) break;
                Debug.Log($"Success: {compileInfo.Success}, {shader.name}, {graphicsAPI}, {buildTarget} {passName}, {passIdx}, {stage}, {compileInfo.ShaderData.LongLength}");

                File.WriteAllBytes(outputPath + $".{stageExt[stage]}{apiExt[graphicsAPI]}", compileInfo.ShaderData);
            }
        }
    }
}