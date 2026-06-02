#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SLZ.Bonelab
{
	[CreateAssetMenu(fileName = "injectedInclude.asset", menuName = "Shader/Injected Include", order = 9)]
	public class InjectedInclude : ScriptableObject
	{
		public ShaderInclude outputInclude;
		public ShaderInclude baseInclude;
		public List<ShaderInclude> injectableIncludes;

		public void UpdateInjection()
		{
			if (outputInclude == null)
			{
				Debug.LogError(this.name + ": Missing output file");
				return;
			}

			if (baseInclude == null)
			{
				Debug.LogError(this.name + ": Missing base file");
				return;
			}

			string projectDir = Path.GetDirectoryName(Application.dataPath);
			string outputDir = Path.Combine(projectDir, AssetDatabase.GetAssetPath(outputInclude));
			string baseDir = Path.Combine(projectDir, AssetDatabase.GetAssetPath(baseInclude));

			List<ShaderInclude> injectionsCleaned = new List<ShaderInclude>();
			foreach (ShaderInclude injection in injectableIncludes)
			{
				if (injection != null)
				{
					injectionsCleaned.Add(injection);
				}
			}

			//if(injectionsCleaned.Count == 0)
			//{
			//    Debug.Log("No injections!");
			//    return;
			//}
			string[] injectionDirs = new string[injectionsCleaned.Count];
			for (int i = 0; i < injectionsCleaned.Count; i++)
			{
				string injProjPath = AssetDatabase.GetAssetPath(injectionsCleaned[i]);
				injectionDirs[i] = Path.Combine(projectDir, injProjPath);
			}

			ShaderInjector shaderInjector = new ShaderInjector();
			shaderInjector.outputFileDir = outputDir;
			shaderInjector.inputFileDir = baseDir;
			shaderInjector.injectionDirs = injectionDirs;
			shaderInjector.CreateShaderAndSaveToDisk();
		}
	}

	[CustomEditor(typeof(InjectedInclude))]
	[CanEditMultipleObjects]
	public class InjectedIncludeEditor : Editor
	{
		public override VisualElement CreateInspectorGUI()
		{
			VisualElement Inspector = new VisualElement();
			Inspector.style.flexGrow = 1;
			Inspector.style.flexShrink = 1f;
			Inspector.style.flexDirection = FlexDirection.Column;

			ObjectField outputField = new ObjectField("Output Include File");
			outputField.objectType = typeof(ShaderInclude);
			outputField.bindingPath = "outputInclude";

			ObjectField baseField = new ObjectField("Base Include File");
			baseField.objectType = typeof(ShaderInclude);
			baseField.bindingPath = "baseInclude";

			ListView injectField = new ListView();
			injectField.bindingPath = "injectableIncludes";
			injectField.headerTitle = "Injections";
			injectField.showFoldoutHeader = true;
			injectField.reorderable = true;
			injectField.showAddRemoveFooter = true;
			injectField.showBorder = true;

			Button updateInjButton = new Button();
			updateInjButton.text = "Inject and Create Output";

			Inspector.Add(updateInjButton);
			Inspector.Add(outputField);
			Inspector.Add(baseField);
			Inspector.Add(injectField);


			updateInjButton.RegisterCallback<ClickEvent>((evt) =>
			{
				Object[] selectedObj = this.serializedObject.targetObjects;
				Debug.Log(selectedObj[0].GetType());

				foreach (Object selected in selectedObj)
				{
					InjectedInclude injShader = (InjectedInclude)selected;
					Debug.Log("Updating Injection");
					UpdateInjection(injShader.outputInclude, injShader.baseInclude, injShader.injectableIncludes,
						injShader);
				}

				AssetDatabase.Refresh();
			});
			return Inspector;
		}

		internal void UpdateInjection(ShaderInclude outp, ShaderInclude baseInj, List<ShaderInclude> injections,
			InjectedInclude thisObj)
		{
			if (outp == null)
			{
				Debug.LogError(thisObj.name + ": Missing output file");
				return;
			}

			if (baseInj == null)
			{
				Debug.LogError(thisObj.name + ": Missing base file");
				return;
			}

			string projectDir = Path.GetDirectoryName(Application.dataPath);
			string outputDir = Path.Combine(projectDir, AssetDatabase.GetAssetPath(outp));
			string baseDir = Path.Combine(projectDir, AssetDatabase.GetAssetPath(baseInj));

			List<ShaderInclude> injectionsCleaned = new List<ShaderInclude>();
			foreach (ShaderInclude injection in injections)
			{
				if (injection != null)
				{
					injectionsCleaned.Add(injection);
				}
			}

			//if(injectionsCleaned.Count == 0)
			//{
			//    Debug.Log("No injections!");
			//    return;
			//}
			string[] injectionDirs = new string[injectionsCleaned.Count];
			for (int i = 0; i < injectionsCleaned.Count; i++)
			{
				string injProjPath = AssetDatabase.GetAssetPath(injectionsCleaned[i]);
				injectionDirs[i] = Path.Combine(projectDir, injProjPath);
			}

			ShaderInjector shaderInjector = new ShaderInjector();
			shaderInjector.outputFileDir = outputDir;
			shaderInjector.inputFileDir = baseDir;
			shaderInjector.injectionDirs = injectionDirs;
			shaderInjector.CreateShaderAndSaveToDisk();
		}
	}
}
#endif