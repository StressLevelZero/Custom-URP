using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor.IMGUI.Controls;

namespace SLZ.SLZEditorTools
{
    [CustomEditor(typeof(ProjectShaderSymbols))]
    public class ProjectShaderSymbolsEditor : Editor
    {

        private static readonly string[] knownSymbols = new string[]
        {
            "_SLZ_DISABLE_BAKED_SPEC",
            "SLZ_LM_BICUBIC",
            "SLZ_LM_MONOSH"
        };

        private static readonly string[] knownSymbolDescs = new string[]
        {
            "Disables fake specular highlights based on the directional lightmap or light probes. Use if the majority of light sources are physical emissive surfaces visible to the reflection probe system rather than invisible light components",
            "Use B-Spline bicubic interpolation for lightmaps. Smooths lightmaps at the cost of 4x the texture samples and a non-trivial amount of math ops",
            "Interpret the data stored in lightmap and the directional map as the coefficients of a linear spherical harmonic where the 1st order coefficients are the same for all color channels. Intended to be used with the Bakery lightmapper's MonoSH mode."
        };

        private class SymbolListView : ListView
        {
            public List<string> symbols;
            public SymbolListView(string title, List<string> symbols) : base()
            {
                this.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
                this.showFoldoutHeader = true;
                this.headerTitle = title;
                this.showAddRemoveFooter = true;
                this.reorderMode = ListViewReorderMode.Animated;
                this.reorderable = true;
                this.showBorder = true;
                this.symbols = symbols;
                this.itemsSource = symbols;
                this.makeItem = slvMakeItem;
                this.bindItem = slvBindItem;
            }

            VisualElement slvMakeItem()
            {
                SymbolListItem s = new SymbolListItem();
                s.symbols = this.symbols;
                s.RegisterValueChangedCallback<string>(s.ValueChangedCallback);
                return s;
            }

            void slvBindItem(VisualElement v, int i)
            {
                SymbolListItem t = (SymbolListItem)v;
                t.index = i;
                t.label = $"Element {i}";
                t.SetValueWithoutNotify(symbols[i]);
            }

        }

        private class SymbolListItem : TextField
        {
            public int index;
            public List<string> symbols;
            public void ValueChangedCallback(ChangeEvent<string> evt)
            {
                symbols[index] = evt.newValue;
            }
        }


        //[SerializeField] public List<string> globalSymbols = new List<string>();
        private SerializedObject thisSerialized;
        private SymbolListView globalSymbolsField;
        private SymbolListView mobileOnlySymbolsField;
        private SymbolListView pcOnlySymbolsField;

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            ProjectShaderSymbols tgt = (ProjectShaderSymbols)target;

            List<string> globalSymbols = new List<string>(tgt.globalSymbols);
            List<string> mobileOnlySymbols = new List<string>(tgt.mobileOnlySymbols);
            List<string> pcOnlySymbols = new List<string>(tgt.pcOnlySymbols);



            thisSerialized = new SerializedObject(this);

            globalSymbolsField = new SymbolListView("Global Symbols", globalSymbols);
            root.Add(globalSymbolsField);

            mobileOnlySymbolsField = new SymbolListView("Mobile Only Symbols", mobileOnlySymbols);
            root.Add(mobileOnlySymbolsField);

            pcOnlySymbolsField = new SymbolListView("PC Only Symbols", pcOnlySymbols);
            root.Add(pcOnlySymbolsField);

            Button applyBtn = new Button(ApplyModified);
            applyBtn.text = "Apply Modified";
            root.Add(applyBtn);

            MultiColumnListView help = new MultiColumnListView();
            
            help.showBoundCollectionSize = false;
            help.showFoldoutHeader = true;
            help.headerTitle = "Built-in Shader Symbols";
            help.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            help.itemsSource = knownSymbols;
            help.showAlternatingRowBackgrounds = AlternatingRowBackground.All;
            help.style.flexGrow = 1;

            help.horizontalScrollingEnabled = false;

            Column symbolColumn = new Column();
            symbolColumn.makeCell = MakeHelpSymbol;
            symbolColumn.bindCell = BindHelpSymbol;
            symbolColumn.sortable = false;
            symbolColumn.width = 256;
            symbolColumn.optional = false;


            //symbolColumn.minWidth = 256;
            help.columns.Add(symbolColumn);

            Column descColumn = new Column();
            descColumn.makeCell = MakeHelpDesc;
            descColumn.bindCell = BindHelpDesc;
            descColumn.sortable = false;
            descColumn.width = 256;
            //descColumn.minWidth = 256;
            descColumn.optional = false;
            help.style.justifyContent =Justify.SpaceBetween;
            help.columns.Add(descColumn);
            help.columns.stretchMode = Columns.StretchMode.GrowAndFill;
            Debug.Log("Column count: " +  help.columns.Count);
            root.Add(help);
          

            return root;
        }

        VisualElement MakeHelpSymbol()
        {
            TextField t = new TextField();
            t.isReadOnly = true;
            foreach (VisualElement c in t.Children())
            {
                c.style.backgroundColor = Color.clear;
                c.style.borderTopWidth = 0;
                c.style.borderBottomWidth = 0;
                c.style.borderLeftWidth = 0;
                c.style.borderRightWidth = 0;
            }
            return t;
        }

        void BindHelpSymbol(VisualElement v, int index)
        {
            TextField l = (TextField)v;
            l.value = knownSymbols[index];
        }

        VisualElement MakeHelpDesc()
        {
            Label l = new Label();
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        void BindHelpDesc(VisualElement v, int index)
        {
            Label l = (Label)v;
            l.style.paddingTop = 4;
            l.style.paddingBottom = 4;
            l.text = knownSymbolDescs[index];
        }

        void ApplyModified()
        {
            if (!EditorUtility.DisplayDialog("Update Symbols?", "Update Shader Symbols? This will cause almost every shader to reimport!", "Ok"))
            {
                return;
            }
            ProjectShaderSymbols tgt = (ProjectShaderSymbols)target;
            tgt.globalSymbols.Clear();
            tgt.globalSymbols.AddRange(globalSymbolsField.symbols);
            tgt.mobileOnlySymbols.Clear();
            tgt.mobileOnlySymbols.AddRange(mobileOnlySymbolsField.symbols);
            tgt.pcOnlySymbols.Clear();
            tgt.pcOnlySymbols.AddRange(pcOnlySymbolsField.symbols);

            EditorUtility.SetDirty(tgt);
            AssetDatabase.SaveAssetIfDirty(tgt);
            //serializedObject.Update();
            //serializedObject.ApplyModifiedProperties();
            URPConfigManager.UpdateProjectDefines(tgt);
        }
    }
}