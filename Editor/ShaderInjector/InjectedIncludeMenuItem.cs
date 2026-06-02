using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.ProjectWindowCallback;
using UnityEditor;
using UnityEngine;
using UnityEditorInternal;

namespace SLZ.SLZEditorTools
{
    public static class InjectedIncludeMenuItem
    {
        const string defaultName = "injectedInclude.injinc";
        [MenuItem("Assets/Create/Shader/Injected Include (new)")]
        static void MenuItem()
        {
            DoCreateInjInc action = new();
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, action, defaultName, null, null);
        }

        internal class DoCreateInjInc : EndNameEditAction
        {
            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                string hlslPath = pathName.Substring(0, pathName.LastIndexOf('.')) + ".hlsl";
                string fullHlslPath = Path.GetFullPath(hlslPath);
                File.WriteAllText(fullHlslPath, "null");
                AssetDatabase.ImportAsset(hlslPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

                InjectedIncludeAsset injInc = ScriptableObject.CreateInstance<InjectedIncludeAsset>();
                injInc.outputInclude = AssetDatabase.LoadAssetAtPath<ShaderInclude>(hlslPath);
                InternalEditorUtility.SaveToSerializedFileAndForget(new UnityEngine.Object[] {injInc}, pathName, true);
                AssetDatabase.ImportAsset(pathName, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                ProjectWindowUtil.ShowCreatedAsset(AssetDatabase.LoadMainAssetAtPath(pathName));
            }
        }

        public delegate UnityEngine.Object d_CreateScriptAssetWithContent(string pathName, string templateContent);
        static d_CreateScriptAssetWithContent s_CreateScriptAssetWithContent;
        public static d_CreateScriptAssetWithContent CreateScriptAssetWithContent
        {
            get
            {
                if (s_CreateScriptAssetWithContent == null)
                {
                    MethodInfo mi = typeof(ProjectWindowUtil).GetMethod("CreateScriptAssetWithContent", BindingFlags.Static | BindingFlags.NonPublic);
                    if (mi == null)
                    {
                        throw new System.Exception("CreateScriptAssetWithContent not found in ProjectWindowUtil");
                    }
                    s_CreateScriptAssetWithContent = (d_CreateScriptAssetWithContent)mi.CreateDelegate(typeof(d_CreateScriptAssetWithContent));
                }
                return s_CreateScriptAssetWithContent;
            }
        }

    }
}
