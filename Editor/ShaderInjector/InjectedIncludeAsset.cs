using System.Collections;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using SLZ.Bonelab;

namespace SLZ.SLZEditorTools
{
    public class InjectedIncludeAsset : ScriptableObject
	{
		public LazyLoadReference<ShaderInclude> outputInclude;
		public LazyLoadReference<ShaderInclude> baseInclude;
		public List<LazyLoadReference<ShaderInclude>> injectableIncludes;

		public string UpdateInjection()
		{
			if (!outputInclude.isSet)
			{
				//Debug.LogError(this.name + ": Missing output file");
				return null;
			}

			if (!baseInclude.isSet)
			{
				//Debug.LogError(this.name + ": Missing base file");
				return null;
			}

			string projectDir = Path.GetDirectoryName(Application.dataPath);
			string outputDir =  Path.GetFullPath(AssetDatabase.GetAssetPath(outputInclude.instanceID));
			string baseDir =  Path.GetFullPath(AssetDatabase.GetAssetPath(baseInclude.instanceID));

			List<LazyLoadReference<ShaderInclude>> injectionsCleaned = new List<LazyLoadReference<ShaderInclude>>();
            int numInjections = injectableIncludes.Count;
			for (int i = 0; i < numInjections; i++)
			{
                LazyLoadReference<ShaderInclude> injection = injectableIncludes[i];
				if (injection.isSet)
				{
					injectionsCleaned.Add(injection);
				}
			}
            //Debug.Log($"Num Injections: {numInjections}, post cleaning: {injectionsCleaned.Count}");
			string[] injectionDirs = new string[injectionsCleaned.Count];
			for (int i = 0; i < injectionsCleaned.Count; i++)
			{
				string injProjPath = AssetDatabase.GetAssetPath(injectionsCleaned[i].instanceID);
                //Debug.Log($"Injection Path: {injProjPath}");
				injectionDirs[i] = Path.GetFullPath(injProjPath);
			}
           
			ShaderInjector shaderInjector = new ShaderInjector();
			shaderInjector.outputFileDir = outputDir;
			shaderInjector.inputFileDir = baseDir;
			shaderInjector.injectionDirs = injectionDirs;
			string file = shaderInjector.CreateShader();
            File.WriteAllText(outputDir, file);
            return file;
		}
	}
}
