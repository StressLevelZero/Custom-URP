using System.Collections.Generic;
using System;
using System.IO;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using System.Text;
using UnityEditor;
using System.Reflection;

namespace SLZ.SLZEditorTools
{
    class DynamicVariantStripper : IPreprocessShaders
    {

        public int callbackOrder { get { return 0; } }

        //MethodInfo SetKW;
        //public DynamicVariantStripper()
        //{
        //    SetKW = typeof(ShaderKeywordSet).GetMethod("EnableKeywordName", BindingFlags.Static | BindingFlags.NonPublic);
        //}

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {

            if (snippet.shaderType == ShaderType.Surface || snippet.shaderType == ShaderType.RayTracing || snippet.passType == PassType.Meta)
            {
                //Debug.Log("Skipping Raytracing Stage");
                return;
            }

            LocalKeyword[] localKW = ShaderUtil.GetPassKeywords(shader, snippet.pass, snippet.shaderType);
            List<LocalKeyword> dynamicKW = new List<LocalKeyword>(localKW.Length);

            for (int i = 0; i < localKW.Length; i++)
            {
                if (localKW[i].isDynamic)
                {
                    dynamicKW.Add(localKW[i]);
                }
            }

            int numDynamic = dynamicKW.Count;
            int max = data.Count;
            int currentSize = 0;
            for (int i = 0; i < max; ++i)
            {
                bool isDynVariant = true;

                for (int j = 0; j < numDynamic; ++j)
                {
                    isDynVariant = isDynVariant && data[i].shaderKeywordSet.IsEnabled(dynamicKW[j]);
                }

                if (isDynVariant)
                {
                    //for (int j = 0; j < numDynamic; ++j)
                    //{
                    //    SetKW.Invoke(null, new object[] { data[i].shaderKeywordSet, dynamicKW[j].name });
                    //}

                    data[currentSize] = data[i];
                    currentSize++;
                }
            }

            System.Exception ex;
            data.TryRemoveElementsInRange(currentSize, max - currentSize, out ex);
            if (ex != null)
            {
                Debug.LogError("Dynamic Variant Stripper Error: " + ex.Message);
            }
        }
    }
}
