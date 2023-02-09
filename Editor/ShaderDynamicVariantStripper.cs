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


        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {

            List<LocalKeyword> dynamicKW = new List<LocalKeyword>();
            LocalKeyword[] localKW = ShaderUtil.GetPassKeywords(shader, snippet.pass, snippet.shaderType);

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
                bool useVar = true;

                for (int j = 0; j < numDynamic; ++j)
                {
                    useVar = useVar && data[i].shaderKeywordSet.IsEnabled(dynamicKW[j]);
                }

                if (useVar)
                {
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
