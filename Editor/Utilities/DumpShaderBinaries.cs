using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor.Rendering;
using System.IO;
using System.Linq;

using System;
using Object = UnityEngine.Object;

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
        public int passIndex;
        public List<string> shaderKeywords;
        public string outputPath;

        class BuildInfo
        {
            public string buildTarget;
            public string graphicsAPI;
            public string shader;
            public int subShaderIndex;
            public string passName;
            public List<string> shaderKeywords;

            public BuildInfo(BuildTarget buildTarget, ShaderCompilerPlatform graphicsAPI, Shader shader, int subShaderIndex, string passName, List<string> shaderKeywords)
            {
                this.buildTarget = buildTarget.ToString();
                this.graphicsAPI = graphicsAPI.ToString();
                this.shader = AssetDatabase.GetAssetPath(shader);
                this.subShaderIndex = subShaderIndex;
                this.passName = passName;
                this.shaderKeywords = shaderKeywords;
            }
        }

        PopupField<int> m_SubShaderField;
        PopupField<int> m_PassField;
        List<string> m_PassNames = new List<string>();
        public List<string> m_availableShaderKeywords = new List<string>();


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
        { ShaderCompilerPlatform.Vulkan, ".spv"},
        { ShaderCompilerPlatform.GLES3x, "" },
    };

        void UpdatePassNames(int subShaderIndex)
        {
            ShaderData shaderData = ShaderUtil.GetShaderData(shader);
            ShaderData.Subshader subShader = shaderData.GetSerializedSubshader(subShaderIndex);
            int passCount = subShader.PassCount;
            
            m_PassField.choices.Clear();
            for (int pIdx = 0; pIdx < passCount; pIdx++)
            {
                m_PassField.choices.Add(pIdx);
            }

            m_PassNames.Clear();
            ShaderTagId lightmode = new ShaderTagId("LightMode");
            for (int pIdx = 0; pIdx < passCount; pIdx++)
            {
                ShaderData.Pass pass = subShader.GetPass(pIdx);
                ShaderTagId lmName = pass.FindTagValue(lightmode);
                if (lmName != ShaderTagId.none)
                {
                    m_PassNames.Add(lmName.name);
                }
                else
                {
                    m_PassNames.Add(pIdx.ToString());
                }
            }
            m_PassField.SetValueWithoutNotify(m_PassField.value);
            m_PassField.MarkDirtyRepaint();
        }

        void OnShaderChanged(ChangeEvent<Object> e)
        {
            Shader s = (Shader) e.newValue;
            m_SubShaderField.choices.Clear();
            m_PassField.choices.Clear();
            m_PassNames.Clear();
            if (s == null)
            {
                subShaderIndex = 0;
                passIndex = 0;
                m_SubShaderField.MarkDirtyRepaint();
                m_PassField.MarkDirtyRepaint();
                return;
            }

            ShaderData shaderData = ShaderUtil.GetShaderData(shader);
            m_availableShaderKeywords.Clear();
            m_availableShaderKeywords.AddRange(shader.keywordSpace.keywordNames);
            int numSubShaders = shaderData.SerializedSubshaderCount;
            Debug.Log($"Num subshaders: {numSubShaders}");
            if (subShaderIndex >= numSubShaders)
            {
                subShaderIndex = 0;
            }

            for (int ssIdx = 0; ssIdx < numSubShaders; ssIdx++)
            {
                m_SubShaderField.choices.Add(ssIdx);
            }
            m_SubShaderField.MarkDirtyRepaint();
            m_SubShaderField.SetValueWithoutNotify(m_SubShaderField.value);
            UpdatePassNames(subShaderIndex);
        }

        void OnSubpassChanged(ChangeEvent<int> e)
        {
            UpdatePassNames(e.newValue);
        }

        string FormatSubShaderItem(int subShaderIndex)
        {
            return subShaderIndex.ToString();
        }

        string FormatPassItem(int passIndex)
        {
            return passIndex < m_PassNames.Count ? m_PassNames[passIndex] : "INVALID";
        }

        class KeywordListItem : VisualElement
        {
            public int index;
            public DropdownField kwSelect;
            public PropertyField textInput;
            public SerializedProperty textProperty;
            public List<string> keywords;
            public KeywordListItem(List<string> keywords)
            {
                this.keywords = keywords;
                this.AddToClassList("unity-property-field");
                this.style.flexDirection = FlexDirection.Row;
                this.style.alignContent = Align.Stretch;
                textInput = new PropertyField();
                textInput.style.flexGrow = 1;
                kwSelect = new DropdownField();
                kwSelect.choices = keywords;
                //kwSelect.RegisterValueChangedCallback(SelectKeyword);
                kwSelect.formatSelectedValueCallback = formatSelectedValue;
                kwSelect.style.maxWidth = 64;
                kwSelect.Children().First().style.minWidth = 28;
                this.Add(textInput);
                this.Add(kwSelect);
            }



            void SelectKeyword(ChangeEvent<string> evt)
            {
                textProperty.stringValue = evt.newValue;
                textInput.MarkDirtyRepaint();
            }
            string formatSelectedValue(string value)
            {
                return " ... ";
            }
        }

        KeywordListItem MakeKeywordListItem()
        {
            return new KeywordListItem(m_availableShaderKeywords);
        }

        void BindKeywordListItem(VisualElement element, int index)
        {
            KeywordListItem kwItem = element as KeywordListItem;
            if (kwItem == null)
            {
                return;
            }
            SerializedProperty kwProp = thisSerialized.FindProperty($"shaderKeywords.Array.data[{index}]");
            kwItem.textProperty = kwProp;
            kwItem.textInput.BindProperty(kwProp);
            kwItem.kwSelect.BindProperty(kwProp);
        }


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

            ObjectField shaderField = new ObjectField();
            shaderField.label = "Shader";
            shaderField.objectType = typeof(Shader);
            shaderField.bindingPath = "shader";
            shaderField.Bind(thisSerialized);
            shaderField.RegisterValueChangedCallback(OnShaderChanged);

            m_SubShaderField = new PopupField<int>();
            m_SubShaderField.label = "Subshader Index";
            m_SubShaderField.choices = new List<int>();
            m_SubShaderField.formatListItemCallback = FormatSubShaderItem;
            m_SubShaderField.formatSelectedValueCallback = FormatSubShaderItem;
            m_SubShaderField.bindingPath = "subShaderIndex";
            m_SubShaderField.Bind(thisSerialized);

            m_PassField = new PopupField<int>();
            m_PassField.label = "Pass";
            m_PassField.choices = new List<int>();
            m_PassField.formatListItemCallback = FormatPassItem;
            m_PassField.formatSelectedValueCallback = FormatPassItem;
            m_PassField.bindingPath = "passName";
            m_PassField.Bind(thisSerialized);

            //PropertyField shaderKeywordsField = new PropertyField();
            ListView shaderKeywordsField = new ListView();
            shaderKeywordsField.AddToClassList("unity-collection-view");
            shaderKeywordsField.AddToClassList("unity-list-view");
            shaderKeywordsField.makeItem = MakeKeywordListItem;
            shaderKeywordsField.bindItem = BindKeywordListItem;
            shaderKeywordsField.showAddRemoveFooter = true;
            shaderKeywordsField.showFoldoutHeader = true;
            shaderKeywordsField.headerTitle = "Keywords";
            shaderKeywordsField.showBorder = true;
            shaderKeywordsField.reorderable = true;

            shaderKeywordsField.bindingPath = "shaderKeywords";
            shaderKeywordsField.Bind(thisSerialized);

            VisualElement pathHorizontal = new VisualElement();
            pathHorizontal.style.flexDirection = FlexDirection.Row;
            pathHorizontal.style.justifyContent = Justify.SpaceBetween;

            TextField outputPathField = new TextField();
            outputPathField.label = "Output File Path";
            outputPathField.bindingPath = "outputPath";
            outputPathField.style.flexGrow = 1;
            outputPathField.Bind(thisSerialized);
            pathHorizontal.Add(outputPathField);

            Button fileBrowser = new Button(() => 
            {
               
                string defaultPath = "";
                string fileName = "";
                if (string.IsNullOrEmpty(outputPath))
                {
                    defaultPath = Application.dataPath;
                    fileName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(shader));
                }
                else
                {
                    string outputDir = Path.GetDirectoryName(outputPath);
                    if (Directory.Exists(outputDir))
                    {
                        defaultPath = outputDir;
                        fileName = Path.GetFileNameWithoutExtension(outputPath);
                    }
                    else
                    {
                        defaultPath = Application.dataPath;
                        fileName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(shader));
                    }
                }

                string path = EditorUtility.SaveFilePanel("Output Path", defaultPath, fileName, "");
                if (!string.IsNullOrEmpty(path)) outputPathField.value = path;
            });


            GUIContent imguiIcon = EditorGUIUtility.IconContent("Folder Icon");
            Texture2D icon = imguiIcon.image as Texture2D;
            Image fileImg = new Image();
            fileImg.image = icon;
            fileImg.style.flexGrow = 1;
            fileImg.style.flexBasis = 0;

            fileBrowser.Add(fileImg);
            fileBrowser.style.flexDirection = FlexDirection.Column;
            fileBrowser.style.alignContent = Align.FlexStart;
            fileBrowser.style.overflow = Overflow.Hidden;
            fileBrowser.style.maxWidth = 64;
            pathHorizontal.Add(fileBrowser);

            Button button = new Button(PrintStats);
            button.text = "Dump Shader Programs";


            root.Add(graphicsAPIField);
            root.Add(buildTargetField);
            root.Add(shaderField);
            root.Add(m_SubShaderField);
            root.Add(m_PassField);
            root.Add(shaderKeywordsField);
            root.Add(pathHorizontal);
            root.Add(button);
        }

        class BindingInfo
        {
            public List<VertexAttribute> VertexAttributes;
            public List<SerializedConstantBufferInfo> ConstantBuffers;
            public List<ShaderData.TextureBindingInfo> TextureBindings;
        
        }
        [Serializable]
        public struct SerializedConstantBufferInfo
        {
            public string Name;
            public int Size;
            public List<SerializedConstantInfo> Fields;
            public SerializedConstantBufferInfo(in ShaderData.ConstantBufferInfo ci)
            {
                this.Name = ci.Name;
                this.Size = ci.Size;
                int numFields = ci.Fields.Length;
                this.Fields = new List<SerializedConstantInfo>(numFields);
                for (int i = 0; i < numFields; i++)
                {
                    Fields.Add(new SerializedConstantInfo(ci.Fields[i]));
                }
            }
        }

        [Serializable]
        public struct SerializedConstantInfo
        {
            //
            // Summary:
            //     The name of this constant (Read Only).
            public string Name;
            public int Index;
            public string ConstantType;
            public string DataType;
            public int Rows;
            public int Columns;
            public int ArraySize;
            public int StructSize;

            public List<SerializedConstantInfo> StructFields;
            public static implicit operator SerializedConstantInfo(ShaderData.ConstantInfo ci) => new SerializedConstantInfo(ci);
            public SerializedConstantInfo(in ShaderData.ConstantInfo ci)
            {
                this.Name   = ci.Name;
                this.Index  = ci.Index;
                this.ConstantType = ci.ConstantType.ToString();
                this.DataType = ci.DataType.ToString();
                this.Rows = ci.Rows;
                this.Columns = ci.Columns;
                this.ArraySize = ci.ArraySize;
                this.StructSize = ci.StructSize;
                int numSubFields = ci.StructFields.Length;
                StructFields = new List<SerializedConstantInfo>(numSubFields);
                for (int i = 0; i < numSubFields; i++)
                {
                    StructFields.Add(new SerializedConstantInfo(ci.StructFields[i]));
                }
            }
        }

        public void PrintStats()
        {
            int numPasses = shader.GetPassCountInSubshader(subShaderIndex);
            ShaderTagId lightmode = new ShaderTagId("LightMode");
            ShaderTagId lightmodeName = new ShaderTagId(passName);
            int passIdx = passIndex;
            //int passIdx = -1;
            //for (int i = 0; i < numPasses; i++)
            //{
            //    if (shader.FindPassTagValue(subShaderIndex, i, lightmode) == lightmodeName)
            //    {
            //        passIdx = i;
            //    }
            //}
            {
                BuildInfo buildInfo = new BuildInfo(buildTarget, graphicsAPI, shader, subShaderIndex, m_PassNames[passIdx], shaderKeywords);
                File.WriteAllText(outputPath + apiExt[graphicsAPI] + "_info.json", JsonUtility.ToJson(buildInfo,true));
            }
            ShaderData shaderData = ShaderUtil.GetShaderData(shader);
            ShaderData.Subshader subShader = shaderData.GetSerializedSubshader(subShaderIndex);
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

                BindingInfo bindingInfo = new BindingInfo();
                bindingInfo.VertexAttributes = new List<VertexAttribute>(compileInfo.Attributes);
                int numConstBuffers = compileInfo.ConstantBuffers.Length;
                bindingInfo.ConstantBuffers = new List<SerializedConstantBufferInfo>(compileInfo.ConstantBuffers.Length);
                for (int cbIdx = 0; cbIdx < numConstBuffers; cbIdx++)
                {
                    bindingInfo.ConstantBuffers.Add(new SerializedConstantBufferInfo(compileInfo.ConstantBuffers[cbIdx]));
                }
                bindingInfo.TextureBindings = new List<ShaderData.TextureBindingInfo>(compileInfo.TextureBindings);
                File.WriteAllText(outputPath + apiExt[graphicsAPI] + "_bindings.json", JsonUtility.ToJson(bindingInfo, true));

                File.WriteAllBytes(outputPath + $".{stageExt[stage]}{apiExt[graphicsAPI]}", compileInfo.ShaderData);
            }
        }
    }
}