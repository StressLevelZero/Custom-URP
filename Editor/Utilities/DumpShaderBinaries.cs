using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditorInternal;
using UnityEditorInternal.FrameDebuggerInternal;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor.Rendering;
using System.IO;
using System.Linq;
using System.Reflection;

using System;
using Object = UnityEngine.Object;

namespace SLZ.SLZEditorTools
{
    public class DumpShaderBinaries : EditorWindow
    {
        SerializedObject thisSerialized;
        public BuildTarget buildTarget;
        public ShaderCompilerPlatform graphicsAPI = ShaderCompilerPlatform.Vulkan;
        public Shader shader;
        public int subShaderIndex;
        public string passName;
        public int passIndex;
        public List<string> shaderKeywords;
        public string outputPath;
        public bool dumpPreprocessed;

        public void OnEnable()
        {
            if ((int)buildTarget == 0 || buildTarget == BuildTarget.NoTarget)
                buildTarget = EditorUserBuildSettings.activeBuildTarget;
        }

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
        { ShaderCompilerPlatform.GLES3x, ".glsl" },
        { ShaderCompilerPlatform.OpenGLCore, ".glsl" },
    };

        void UpdatePassNames(int subShaderIndex)
        {
            ShaderData shaderData = ShaderUtil.GetShaderData(shader);
            ShaderData.Subshader subShader = shaderData.GetSubshader(subShaderIndex);
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

        void UpdateShader(Shader s)
        {
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
            int numSubShaders = shaderData.SubshaderCount;
            //Debug.Log($"Num subshaders: {numSubShaders}");
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

        void OnShaderChanged(ChangeEvent<Object> e)
        {
            UpdateShader((Shader)e.newValue);
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
            m_PassField.bindingPath = "passIndex";
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

            Toggle dumpPreprocessedToggle = new Toggle("Dump Preprocessed HLSL Instead");
            dumpPreprocessedToggle.bindingPath = "dumpPreprocessed";
            dumpPreprocessedToggle.Bind(thisSerialized);

            VisualElement pathHorizontal = new VisualElement();
            pathHorizontal.style.flexDirection = FlexDirection.Row;
            pathHorizontal.style.justifyContent = Justify.SpaceBetween;
            pathHorizontal.style.overflow = Overflow.Hidden;

            TextField outputPathField = new TextField();
            outputPathField.label = "Output File Path";
            outputPathField.bindingPath = "outputPath";
            outputPathField.style.flexGrow = 1;
            outputPathField.style.minWidth = 0;
            outputPathField.style.flexBasis = 0;
            outputPathField.style.textOverflow = TextOverflow.Ellipsis;
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

            Button FromFrameDbg = new Button(PullFromFrameDebugger);
            FromFrameDbg.text = "Get From Frame Debugger";

            root.Add(FromFrameDbg);
            root.Add(graphicsAPIField);
            root.Add(buildTargetField);
            root.Add(dumpPreprocessedToggle);
            root.Add(shaderField);
            root.Add(m_SubShaderField);
            root.Add(m_PassField);
            root.Add(shaderKeywordsField);
            root.Add(pathHorizontal);
            root.Add(button);
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
        public struct SerializedTextureBindingInfo
        {
            public string Name;
            public int Index;
            public int SamplerIndex;
            public bool Multisampled;
            public int ArraySize;
            public string Dim;

            public SerializedTextureBindingInfo(ShaderData.TextureBindingInfo info)
            {
                Name = info.Name;
                Index = info.Index;
                SamplerIndex = info.SamplerIndex;
                Multisampled = info.Multisampled;
                ArraySize = info.ArraySize;
                Dim = info.Dim.ToString();
            }
        }

        class BindingInfo
        {
            public List<string> VertexAttributes;
            public List<SerializedConstantBufferInfo> ConstantBuffers;
            public List<SerializedTextureBindingInfo> TextureBindings;

        }

        public void PrintStats()
        {
            int numPasses = shader.GetPassCountInSubshader(subShaderIndex);
            ShaderTagId lightmode = new ShaderTagId("LightMode");
            ShaderTagId lightmodeName = new ShaderTagId(passName);
            int passIdx = passIndex;

            {
                BuildInfo buildInfo = new BuildInfo(buildTarget, graphicsAPI, shader, subShaderIndex, m_PassNames[passIdx], shaderKeywords);
                File.WriteAllText(outputPath + apiExt[graphicsAPI] + "_info.json", JsonUtility.ToJson(buildInfo, true));
            }

            ShaderData shaderData = ShaderUtil.GetShaderData(shader);
            ShaderData.Subshader subShader = shaderData.GetSubshader(subShaderIndex);
            ShaderData.Pass pass = subShader.GetPass(passIdx);

            for (ShaderType stage = ShaderType.Vertex; stage <= ShaderType.Count; stage++)
            {
                if (!pass.HasShaderStage(stage)) continue;

                if (dumpPreprocessed)
                {
                    ShaderData.PreprocessedVariant processed = pass.PreprocessVariant(stage, shaderKeywords.ToArray(), graphicsAPI, buildTarget, true);
                    ShaderMessage[] messages = processed.Messages;
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
                    if (!processed.Success) break;
                    File.WriteAllText(outputPath + $".{stageExt[stage]}.hlsl", processed.PreprocessedCode);
                }
                else
                {
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

                    File.WriteAllBytes(outputPath + $".{stageExt[stage]}{apiExt[graphicsAPI]}", compileInfo.ShaderData);

                    BindingInfo bindingInfo = new BindingInfo();

                    int numVtxAttributes = compileInfo.Attributes.Length;
                    bindingInfo.VertexAttributes = new List<string>(numVtxAttributes);
                    for (int aIdx = 0; aIdx < numVtxAttributes; aIdx++)
                    {
                        bindingInfo.VertexAttributes.Add(compileInfo.Attributes[aIdx].ToString());
                    }

                    int numConstBuffers = compileInfo.ConstantBuffers.Length;
                    bindingInfo.ConstantBuffers = new List<SerializedConstantBufferInfo>(compileInfo.ConstantBuffers.Length);
                    for (int cbIdx = 0; cbIdx < numConstBuffers; cbIdx++)
                    {
                        bindingInfo.ConstantBuffers.Add(new SerializedConstantBufferInfo(compileInfo.ConstantBuffers[cbIdx]));
                    }

                    int numTexBinds = compileInfo.TextureBindings.Length;
                    bindingInfo.TextureBindings = new List<SerializedTextureBindingInfo>(numTexBinds);
                    for (int tIdx = 0; tIdx < numTexBinds; tIdx++)
                    {
                        bindingInfo.TextureBindings.Add(new SerializedTextureBindingInfo(compileInfo.TextureBindings[tIdx]));
                    }

                    File.WriteAllText(outputPath + $".{stageExt[stage]}{apiExt[graphicsAPI]}_bindings.json", JsonUtility.ToJson(bindingInfo, true));
                }
            }
        }


        #region SerializedConstantInfo
        /// <summary>
        /// Stupid hack to fix recursion depth limit warning. Just have a unique struct type for each level of depth lmao. only 5 levels, final level has no StructFields list
        /// </summary>
        [Serializable]
        public struct SerializedConstantInfo
        {
            public string Name;
            public int Index;
            public string ConstantType;
            public string DataType;
            public int Rows;
            public int Columns;
            public int ArraySize;
            public int StructSize;

            public List<SerializedConstantInfo1> StructFields;
            public static implicit operator SerializedConstantInfo(ShaderData.ConstantInfo ci) => new SerializedConstantInfo(ci);
            public SerializedConstantInfo(in ShaderData.ConstantInfo ci, int depth = 0)
            {
                this.Name = ci.Name;
                this.Index = ci.Index;
                this.ConstantType = ci.ConstantType.ToString();
                this.DataType = ci.DataType.ToString();
                this.Rows = ci.Rows;
                this.Columns = ci.Columns;
                this.ArraySize = ci.ArraySize;
                this.StructSize = ci.StructSize;
                int numSubFields = ci.StructFields.Length;
                StructFields = new List<SerializedConstantInfo1>(numSubFields);
            }
        }

        [Serializable]
        public struct SerializedConstantInfo1
        {
            public string Name;
            public int Index;
            public string ConstantType;
            public string DataType;
            public int Rows;
            public int Columns;
            public int ArraySize;
            public int StructSize;

            public List<SerializedConstantInfo2> StructFields;
            public SerializedConstantInfo1(in ShaderData.ConstantInfo ci)
            {
                this.Name = ci.Name;
                this.Index = ci.Index;
                this.ConstantType = ci.ConstantType.ToString();
                this.DataType = ci.DataType.ToString();
                this.Rows = ci.Rows;
                this.Columns = ci.Columns;
                this.ArraySize = ci.ArraySize;
                this.StructSize = ci.StructSize;
                int numSubFields = ci.StructFields.Length;
                StructFields = new List<SerializedConstantInfo2>(numSubFields);
            }
        }

        [Serializable]
        public struct SerializedConstantInfo2
        {
            public string Name;
            public int Index;
            public string ConstantType;
            public string DataType;
            public int Rows;
            public int Columns;
            public int ArraySize;
            public int StructSize;

            public List<SerializedConstantInfo3> StructFields;
            public SerializedConstantInfo2(in ShaderData.ConstantInfo ci)
            {
                this.Name = ci.Name;
                this.Index = ci.Index;
                this.ConstantType = ci.ConstantType.ToString();
                this.DataType = ci.DataType.ToString();
                this.Rows = ci.Rows;
                this.Columns = ci.Columns;
                this.ArraySize = ci.ArraySize;
                this.StructSize = ci.StructSize;
                int numSubFields = ci.StructFields.Length;
                StructFields = new List<SerializedConstantInfo3>(numSubFields);
            }
        }

        [Serializable]
        public struct SerializedConstantInfo3
        {
            public string Name;
            public int Index;
            public string ConstantType;
            public string DataType;
            public int Rows;
            public int Columns;
            public int ArraySize;
            public int StructSize;

            public List<SerializedConstantInfo4> StructFields;
            public SerializedConstantInfo3(in ShaderData.ConstantInfo ci)
            {
                this.Name = ci.Name;
                this.Index = ci.Index;
                this.ConstantType = ci.ConstantType.ToString();
                this.DataType = ci.DataType.ToString();
                this.Rows = ci.Rows;
                this.Columns = ci.Columns;
                this.ArraySize = ci.ArraySize;
                this.StructSize = ci.StructSize;
                int numSubFields = ci.StructFields.Length;
                StructFields = new List<SerializedConstantInfo4>(numSubFields);
            }
        }

        [Serializable]
        public struct SerializedConstantInfo4
        {
            public string Name;
            public int Index;
            public string ConstantType;
            public string DataType;
            public int Rows;
            public int Columns;
            public int ArraySize;
            public int StructSize;
            public int StructFieldsCount;
            public SerializedConstantInfo4(in ShaderData.ConstantInfo ci)
            {
                this.Name = ci.Name;
                this.Index = ci.Index;
                this.ConstantType = ci.ConstantType.ToString();
                this.DataType = ci.DataType.ToString();
                this.Rows = ci.Rows;
                this.Columns = ci.Columns;
                this.ArraySize = ci.ArraySize;
                this.StructSize = ci.StructSize;
                this.StructFieldsCount = ci.StructFields.Length;
            }
        }

        #endregion // SerializedConstantInfo



        #region Frame_Debugger_Reflection

        bool reflectionInitialized = false;
        Type FrameDebuggerUtility_Type;
        Type FrameDebuggerEventData_Type;
        Type ShaderInfo_Type;
        Type ShaderKeywordInfo_Type;
        MethodInfo FrameDebuggerUtility_GetFrameEventData;
        PropertyInfo FrameDebuggerUtility_limit;
        FieldInfo FrameDebuggerEventData_m_RealShaderName;
        FieldInfo FrameDebuggerEventData_m_ShaderInfo;
        FieldInfo FrameDebuggerEventData_m_SubShaderIndex;
        FieldInfo FrameDebuggerEventData_m_ShaderPassIndex;
        FieldInfo FrameDebuggerEventData_shaderKeywords;
        FieldInfo ShaderInfo_m_Keywords;
        FieldInfo ShaderKeywordInfo_m_Name;

        bool InitReflection()
        {
            FrameDebuggerUtility_Type = typeof(UnityEditorInternal.InternalEditorUtility).Assembly.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility");
            if (FrameDebuggerUtility_Type == null) { Debug.LogError("Failed to get internal type 'FrameDebuggerUtility'"); return false; }

            FrameDebuggerEventData_Type = typeof(UnityEditorInternal.InternalEditorUtility).Assembly.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData");
            if (FrameDebuggerEventData_Type == null) { Debug.LogError("Failed to get internal type 'FrameDebuggerEventData'"); return false; }

            ShaderInfo_Type = typeof(UnityEditorInternal.InternalEditorUtility).Assembly.GetType("UnityEditorInternal.FrameDebuggerInternal.ShaderInfo");
            if (ShaderInfo_Type == null) { Debug.LogError("Failed to get internal type 'ShaderInfo'"); return false; }

            ShaderKeywordInfo_Type = typeof(UnityEditorInternal.InternalEditorUtility).Assembly.GetType("UnityEditorInternal.FrameDebuggerInternal.ShaderKeywordInfo");
            if (ShaderKeywordInfo_Type == null) { Debug.LogError("Failed to get internal type 'ShaderKeywordInfo'"); return false; }

            FrameDebuggerUtility_GetFrameEventData = FrameDebuggerUtility_Type.GetMethod("GetFrameEventData", BindingFlags.Public | BindingFlags.Static);
            if (FrameDebuggerUtility_GetFrameEventData == null) { Debug.LogError("Failed to get info for method 'FrameDebuggerUtility.GetFrameEventData'"); return false; }

            FrameDebuggerUtility_limit = FrameDebuggerUtility_Type.GetProperty("limit", BindingFlags.Public | BindingFlags.Static);
            if (FrameDebuggerUtility_limit == null) { Debug.LogError("Failed to get info for property 'FrameDebuggerUtility.limit'"); return false; }

            FrameDebuggerEventData_m_RealShaderName = FrameDebuggerEventData_Type.GetField("m_RealShaderName", BindingFlags.Instance | BindingFlags.Public);
            if (FrameDebuggerEventData_m_RealShaderName == null) { Debug.LogError("Failed to get info for field 'FrameDebuggerEventData.m_RealShaderName'"); return false; }

            FrameDebuggerEventData_m_ShaderInfo = FrameDebuggerEventData_Type.GetField("m_ShaderInfo", BindingFlags.Instance | BindingFlags.Public);
            if (FrameDebuggerEventData_m_ShaderInfo == null) { Debug.LogError("Failed to get info for field 'FrameDebuggerEventData.m_ShaderInfo'"); return false; }

            FrameDebuggerEventData_m_SubShaderIndex = FrameDebuggerEventData_Type.GetField("m_SubShaderIndex", BindingFlags.Instance | BindingFlags.Public);
            if (FrameDebuggerEventData_m_SubShaderIndex == null) { Debug.LogError("Failed to get info for field 'FrameDebuggerEventData.m_SubShaderIndex'"); return false; }

            // Always empty, legacy field?
            // FrameDebuggerEventData_shaderKeywords = FrameDebuggerEventData_Type.GetField("shaderKeywords", BindingFlags.Instance | BindingFlags.Public);
            // if (FrameDebuggerEventData_shaderKeywords == null) { Debug.LogError("Failed to get info for field 'FrameDebuggerEventData.shaderKeywords'"); return false; }

            FrameDebuggerEventData_m_ShaderPassIndex = FrameDebuggerEventData_Type.GetField("m_ShaderPassIndex", BindingFlags.Instance | BindingFlags.Public);
            if (FrameDebuggerEventData_m_ShaderPassIndex == null) { Debug.LogError("Failed to get info for field 'FrameDebuggerEventData.m_ShaderPassIndex'"); return false; }

            ShaderInfo_m_Keywords = ShaderInfo_Type.GetField("m_Keywords", BindingFlags.Instance | BindingFlags.Public);
            if (ShaderInfo_m_Keywords == null) { Debug.LogError("Failed to get info for field 'ShaderInfo.m_Keywords'"); return false; }

            ShaderKeywordInfo_m_Name = ShaderKeywordInfo_Type.GetField("m_Name", BindingFlags.Instance | BindingFlags.Public);
            if (ShaderInfo_m_Keywords == null) { Debug.LogError("Failed to get info for field 'ShaderKeywordInfo.m_Name'"); return false; }

            reflectionInitialized = true;
            return true;
        }

        void PullFromFrameDebugger()
        {
            if (FrameDebugger.enabled)
            {
                if (!reflectionInitialized || FrameDebuggerUtility_GetFrameEventData == null) // additional null check as assembly reloads seem to garbage collect objects but not clear primitive type fields
                {
                    bool reflectionSuccess = InitReflection();
                    if (!reflectionSuccess) return;
                }

                int limit = (int)FrameDebuggerUtility_limit.GetValue(null);

                object frameEventData = Activator.CreateInstance(FrameDebuggerEventData_Type);
                bool success = (bool)FrameDebuggerUtility_GetFrameEventData.Invoke(null, new object[] { limit - 1, frameEventData });
                if (!success)
                {
                    Debug.Log($"Could not get frame debugger event data for event {limit - 1}, FrameDebuggerUtility.GetFrameEventData failed. Frame data may be stale, or the debugger isn't actually running?");
                    return;
                }

                string shaderName = (string)FrameDebuggerEventData_m_RealShaderName.GetValue(frameEventData);
                Debug.Log("Shader name " + shaderName);

                if (string.IsNullOrEmpty(shaderName))
                {
                    Debug.LogError("Could not pull draw info from frame debugger. Shader name is empty, is a non-drawing or compute event currently selected?");
                    return;
                }

                Shader eventShader = Shader.Find(shaderName);
                if (eventShader == null)
                {
                    Debug.LogError($"Could not find shader '{shaderName}' listed as the used shader in the current frame debugger event.");
                    return;
                }

                this.shader = eventShader;
                this.subShaderIndex = (int)FrameDebuggerEventData_m_SubShaderIndex.GetValue(frameEventData);
                this.passIndex = (int)FrameDebuggerEventData_m_ShaderPassIndex.GetValue(frameEventData);

                object shaderInfo = FrameDebuggerEventData_m_ShaderInfo.GetValue(frameEventData);

                this.shaderKeywords.Clear();
                IEnumerable keywordArray = (IEnumerable)ShaderInfo_m_Keywords.GetValue(shaderInfo);
                foreach (object keyword in keywordArray)
                {
                    string kw = (string)ShaderKeywordInfo_m_Name.GetValue(keyword);
                    shaderKeywords.Add(kw);
                }

                // Update the serialized object representation of this window, and update the subpass, pass, and keyword dropdown options
                this.thisSerialized.Update();
                UpdateShader(shader);
            }
            else // !FrameDebugger.Enabled
            {
                Debug.LogError("Frame debugger not active, can't get draw info!");
                EditorUtility.DisplayDialog("Frame Debugger Not Running", "Frame debugger must be enabled with a drawing event selected in the debugger window.", "Ok");
            }
        }

        #endregion // Frame_Debugger_Reflection
    }
}