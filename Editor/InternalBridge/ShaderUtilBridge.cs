using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;
using UnityEditorInternal;
using UnityEditor.Rendering;

namespace SLZ.URPEditorBridge
{
    public static class ShaderUtilBridge
    {
        //[MenuItem("TEST/Try dump litMAS RT")]
        public static void TryCompileLitMASRT()
        {
            Shader litMAS = Shader.Find("SLZ/LitMAS/LitMAS Standard");
            if (litMAS == null)
            {
                Debug.LogError("Could not find LitMAS");
                return;
            }
            ShaderData.PreprocessedVariant processed = PreprocessShaderVariant(litMAS, 0, 5, ShaderType.Vertex, new string[]{"_DUMMY_VERT_FRAG"}, ShaderCompilerPlatform.Vulkan, BuildTarget.StandaloneWindows, true);
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
            if (!processed.Success) Debug.LogError("Did not preprocess shader");
            string saveDir = EditorUtility.SaveFilePanel("Save", Application.dataPath, "litMasRt", "hlsl");
            using (System.IO.File.Create(saveDir))
            {
            }
            System.IO.File.WriteAllText(saveDir, processed.PreprocessedCode);

        }
        public static ShaderData.PreprocessedVariant PreprocessShaderVariant(Shader shader, int subShaderIndex, int passId,
            ShaderType shaderType, BuiltinShaderDefine[] platformKeywords, string[] keywords, ShaderCompilerPlatform shaderCompilerPlatform, 
            BuildTarget buildTarget, GraphicsTier tier, bool stripLineDirectives)
        {
            return ShaderUtil.PreprocessShaderVariant(shader, subShaderIndex, passId, 
            shaderType, platformKeywords, keywords, shaderCompilerPlatform, 
            buildTarget, tier, stripLineDirectives);
        }

        public static ShaderData.PreprocessedVariant PreprocessShaderVariant(Shader shader, int subShaderIndex, int passId,
            ShaderType shaderType, string[] keywords, ShaderCompilerPlatform shaderCompilerPlatform, 
            BuildTarget buildTarget, bool stripLineDirectives)
        {
            BuiltinShaderDefine[] shaderPlatformKeywordsForBuildTarget = ShaderUtil.GetShaderPlatformKeywordsForBuildTarget (shaderCompilerPlatform, buildTarget, ShaderData.Pass.kNoGraphicsTier);
            return ShaderUtil.PreprocessShaderVariant(shader, subShaderIndex, passId, 
            shaderType, shaderPlatformKeywordsForBuildTarget, keywords, shaderCompilerPlatform, 
            buildTarget, ShaderData.Pass.kNoGraphicsTier, stripLineDirectives);
        }

        public static ShaderData.Subshader Subshader_ctor(ShaderData data, int subshaderIndex, int type = (int)ShaderData.Subshader.Type.Runtime)
        {
            ShaderData.Subshader.Type enumType = (ShaderData.Subshader.Type)type;
            return new ShaderData.Subshader(data, subshaderIndex, enumType);
        }

        public static ShaderData.Pass Pass_ctor(ShaderData.Subshader subshader, int passIndex)
		{
			return new ShaderData.Pass(subshader, passIndex);
		}
    }
}