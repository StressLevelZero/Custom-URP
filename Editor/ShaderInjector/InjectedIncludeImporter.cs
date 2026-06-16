using System.Collections;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.AssetImporters;
using SLZ.Bonelab;
using UnityEditorInternal;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Unity.Collections.LowLevel.Unsafe;

namespace SLZ.SLZEditorTools
{
    [ScriptedImporter(4, new string[] { "injinc" }, null, -3000, AllowCaching = true)]
    public class InjectedIncludeImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string outputPath = "null";
            Object[] boxedInclude = InternalEditorUtility.LoadSerializedFileAndForget(ctx.assetPath);
            InjectedIncludeAsset injInc = boxedInclude[0] as InjectedIncludeAsset;
            if (injInc != null)
            {
                if (injInc.baseInclude.isSet) ctx.DependsOnSourceAsset(AssetDatabase.GetAssetPath(injInc.baseInclude.instanceID));
                foreach (LazyLoadReference<ShaderInclude> s in injInc.injectableIncludes)
                {
                    if (s.isSet) ctx.DependsOnSourceAsset(AssetDatabase.GetAssetPath(s.instanceID) );
                }
                if (injInc.outputInclude.isSet)
                {
                    outputPath = AssetDatabase.GetAssetPath(injInc.outputInclude.instanceID);
                    injInc.UpdateInjection();
                    AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.Default);
                }
                if (string.IsNullOrEmpty(outputPath)) outputPath = "null";
            }
            else
            {
                Debug.LogError("Failed to load InjectedIncludeAsset");
            }
            TextAsset dummy = new TextAsset(outputPath);
            Texture2D Icon = EditorGUIUtility.IconContent("ShaderVariantCollection Icon").image as Texture2D;
            ctx.AddObjectToAsset("path", dummy, Icon);
            ctx.SetMainObject(dummy);
        }
    }


    [CustomEditor(typeof(InjectedIncludeImporter))]
    public class InjectedIncludeImporterEditor : ScriptedImporterEditor
    {
        [SerializeField] List<InjectedIncludeAsset> deserializedTargets = new List<InjectedIncludeAsset>();
        [SerializeField] List<InjectedIncludeAsset> deserializedTargets2 = new List<InjectedIncludeAsset>();
        [SerializeField] List<string> deserializedTargetPaths = new List<string>();
        [SerializeField] SerializedObject serializedTargets;
        [SerializeField] SerializedProperty propOutput;
        [SerializeField] SerializedProperty propBase;
        [SerializeField] SerializedProperty propInjections;

        protected override bool needsApplyRevert => true;
        [SerializeField] private bool isModified = false; 

        [SerializeField] List<System.Tuple<IBindable, string>> fields = new List<System.Tuple<IBindable, string>>(8);

        public override VisualElement CreateInspectorGUI()
		{
            UpdateObjRepresentations();

			VisualElement Inspector = new VisualElement();
			Inspector.style.flexGrow = 1;
			Inspector.style.flexShrink = 1f;
			Inspector.style.flexDirection = FlexDirection.Column;

            propOutput = serializedTargets.FindProperty("outputInclude");
			PropertyField outputField = new PropertyField();
            outputField.label = "Output Include File";
            //outputField.bindingPath = "outputInclude";
            //outputField.Bind(serializedTargets);
			//outputField.objectType = typeof(ShaderInclude);
            outputField.BindProperty(propOutput);
            //outputField.RegisterValueChangeCallback(outputField.MarkDirty);
            fields.Add(new (outputField, "outputInclude"));

            propBase = serializedTargets.FindProperty("baseInclude");
			PropertyField baseField = new PropertyField();
            baseField.label = "Base Include File";
            //baseField.bindingPath = "baseInclude";
            //baseField.Bind(serializedTargets);
			//baseField.objectType = typeof(ShaderInclude);
            //baseField.Bind(serializedTargets);
			baseField.BindProperty(propBase);
            //baseField.RegisterValueChangeCallback(baseField.MarkDirty);
            fields.Add(new (baseField, "baseInclude"));

            propInjections = serializedTargets.FindProperty("injectableIncludes");
			ListView injectField = new ListView();

			injectField.headerTitle = "Injections";
			injectField.showFoldoutHeader = true;
			injectField.reorderable = true;
			injectField.showAddRemoveFooter = true;
			injectField.showBorder = true;

            injectField.BindProperty(propInjections);
            fields.Add(new (injectField, "injectableIncludes"));

            IMGUIContainer applyRevertContainer = new IMGUIContainer(ApplyRevertGUI);

			//Button updateInjButton = new Button();
			//updateInjButton.text = "Inject and Create Output";

			//Inspector.Add(updateInjButton);
			Inspector.Add(outputField);
			Inspector.Add(baseField);
			Inspector.Add(injectField);
            Inspector.Add(applyRevertContainer);

			return Inspector;
		}

        void UpdateObjRepresentations()
        {
            deserializedTargets.Clear();
            if (targets.Length > 0)
            {
            foreach (Object tgt in targets)
            {
                if (tgt is InjectedIncludeImporter injIncImp)
                {
                    Object[] boxedInclude = InternalEditorUtility.LoadSerializedFileAndForget(injIncImp.assetPath);
                    //Debug.Log($"Tried loading assets at {injIncImp.assetPath}, returned {boxedInclude.Length} objects");
                    //Debug.Log($"Typeof object[0]: {boxedInclude[0].GetType()}");
                    InjectedIncludeAsset injInc = boxedInclude[0] as InjectedIncludeAsset;
                    if (injInc == null) 
                    {
                        injInc = ScriptableObject.CreateInstance<InjectedIncludeAsset>();
                        Debug.LogError("Failed to load InjectedIncludeAsset");
                    }
                    deserializedTargets.Add(injInc);
                    deserializedTargets2.Add(InjectedIncludeAsset.Instantiate(injInc));
                    deserializedTargetPaths.Add(injIncImp.assetPath);
                }
            }
            }
            else
            {
                if (target is InjectedIncludeImporter injIncImp)
                {
                    Object[] boxedInclude = InternalEditorUtility.LoadSerializedFileAndForget(injIncImp.assetPath);
                    InjectedIncludeAsset injInc = boxedInclude[0] as InjectedIncludeAsset;
                    if (injInc == null) 
                    {
                        injInc = ScriptableObject.CreateInstance<InjectedIncludeAsset>();
                        Debug.LogError("Failed to load InjectedIncludeAsset");
                    }
                    deserializedTargets.Add(injInc);
                }
            }
            if (deserializedTargets.Count == 0)
            {
                Debug.LogError("Failed to get targets");
            }
            if (targets.Length > 1)
            {
                serializedTargets = new SerializedObject(deserializedTargets.ToArray());
            }
            else
            {
                serializedTargets = new SerializedObject(deserializedTargets[0]);
            }

        }

        public override void SaveChanges()
        {
            //Debug.Log("Saving changes"); 
            serializedTargets.ApplyModifiedProperties();

            int numTargets = deserializedTargets.Count;
            UnityEngine.Object[] dummyArray = new Object[1];
            for (int tIdx = 0; tIdx < numTargets; tIdx++)
            {
                //Debug.Log("Saving " + deserializedTargetPaths[tIdx] + "\nBase File " + deserializedTargets[tIdx].baseInclude.asset?.name);
                dummyArray[0] = deserializedTargets[tIdx];
                InternalEditorUtility.SaveToSerializedFileAndForget(
                    dummyArray, 
                    deserializedTargetPaths[tIdx],
                    true
                    );
                InternalEditorUtility.SaveToSerializedFileAndForget(
                    dummyArray, 
                    deserializedTargetPaths[tIdx]+".bak",
                    true
                    );
            }

            base.SaveChanges();
            isModified = false;
            UpdateObjRepresentations();
            foreach (var i in fields) i.Item1.BindProperty(serializedTargets.FindProperty(i.Item2));
        }

        public override void DiscardChanges()
        {
            UpdateObjRepresentations();
            foreach (var i in fields) i.Item1.BindProperty(serializedTargets.FindProperty(i.Item2));
            base.DiscardChanges();
            isModified = false;
        }

        public override bool HasModified()
        {
            int numTargets = deserializedTargets.Count;
            bool isModified = false;
            for (int tIdx = 0; tIdx < numTargets; tIdx++)
            {
                InjectedIncludeAsset a = deserializedTargets[tIdx];
                InjectedIncludeAsset b = deserializedTargets2[tIdx];
                isModified = isModified || (a.baseInclude.instanceID != b.baseInclude.instanceID);
                if (isModified) break;
                isModified = isModified || (a.outputInclude.instanceID != b.outputInclude.instanceID);
                if (isModified) break;
                isModified = isModified || ((a.injectableIncludes == null) != (b.injectableIncludes == null)) || (a.injectableIncludes.Count != b.injectableIncludes.Count);
                if (isModified) break;
                int numInj = a.injectableIncludes.Count;
                for (int jIdx = 0; jIdx < numInj; jIdx++)
                {
                    isModified = isModified || (a.injectableIncludes[jIdx].instanceID != b.injectableIncludes[jIdx].instanceID);
                    if (isModified) goto end;
                }
            }
            end:
            return isModified;
        }



        void MarkDirtyIntl()
        {
            isModified = true;
            // hasUnsavedChanges = true;
            // Debug.Log("Marked Dirty?");
            // EditorUtility.SetDirty(target);
            // foreach (Object t in targets) EditorUtility.SetDirty(t);
        }

        void MarkDirty<T>(ChangeEvent<T> evt) where T : System.IEquatable<T>
        {
            if (!evt.previousValue.Equals(evt.newValue)) Debug.Log($"MarkDirty<{typeof(T).Name}> marked dirty (old: {evt.previousValue}, new: {evt.newValue})?");
             MarkDirtyIntl();
        }

        void MarkDirtyObj<T>(ChangeEvent<T> evt) where T : UnityEngine.Object
        {
            if (evt.previousValue != evt.newValue) Debug.Log($"MarkDirty Object marked dirty (old: {evt.previousValue.name}, new: {evt.newValue.name})?");
             MarkDirtyIntl();
        }

        void MarkDirtyEvtCallback(EventCallback<Object> evt)
        {
             Debug.Log($"MarkDirty EventCallback<Object> marked dirty?");
             MarkDirtyIntl();
        }

        void MarkDirty(SerializedPropertyChangeEvent evt)
        {
            Debug.Log("SerializedPropertyChangeEvent marked dirty?");
            MarkDirtyIntl();
        }

        void MarkDirty(int a, int b)
        {
             Debug.Log("MarkDirty<int, int> marked dirty?");
             if (a != b) MarkDirtyIntl();
        }

        void MarkDirtyIE(IEnumerable<int> dummy)
        {
             Debug.Log("MarkDirtyIE marked dirty?");
             MarkDirtyIntl();
        }

        VisualElement MakeListItem()
        {
            FuckUnity p = new FuckUnity();
            p._parent = this;
            p.RegisterValueChangeCallback(p.MarkDirty);
            return p;
        }
    
        class FuckUnity : PropertyField
        {
            public InjectedIncludeImporterEditor _parent;
            bool initialized = false;
            internal void MarkDirty(SerializedPropertyChangeEvent evt)
            {
                if (initialized)
                {
                    Debug.Log("SerializedPropertyChangeEvent marked dirty?");
                    _parent.MarkDirtyIntl();
                }
                else
                {
                    initialized = true;
                }
            }
        }

    }
}