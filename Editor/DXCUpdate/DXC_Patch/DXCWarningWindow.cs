
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using System.Text;
using static UnityEditor.Rendering.FilterWindow;


namespace SLZ.DXCUpdater
{
    public class DXCWarningWindow : EditorWindow
    {
        SerializedObject thisSerialized;
        public static string unityDXCInfo;
        public static string slzDXCInfo;
        public static string unityDXCPath;
        public static string slzDXCPath;

        [SerializeField] public string n_unityDXCInfo;
        [SerializeField] public string n_slzDXCInfo;
        [SerializeField] public string n_unityDXCPath;
        [SerializeField] public string n_slzDXCPath;
        

        class FileHyperLink : Button
        {
            public string path;

            public FileHyperLink(string path) : base(() => EditorUtility.RevealInFinder(path))
            {
                this.path = path;
                //string underscore = "\u0332";
                //StringBuilder hyperlinkpath = new StringBuilder(3 * path.Length);
                //int pathLen = path.Length;
                //for (int cIdx = 0; cIdx < pathLen; cIdx++)
                //{
                //    hyperlinkpath.Append(path[cIdx]);
                //    hyperlinkpath.Append(underscore); 
                //}
                //this.style.unityFont = EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font;
                //this.style.unityFontDefinition = new StyleFontDefinition(EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font);
                this.text = path;
                this.RemoveFromClassList("unity-button");
                //this.AddToClassList("unity-toolbar-button");
                ColorUtility.TryParseHtmlString("#4C7EFFFF", out Color hyperlinkColor);
                this.style.color = hyperlinkColor;
                this.style.borderBottomColor = hyperlinkColor;
                this.style.borderBottomWidth = 1;
                this.style.alignSelf = Align.FlexStart;
                this.style.whiteSpace = WhiteSpace.NoWrap;
                this.style.textOverflow = TextOverflow.Ellipsis;
                this.style.unityTextOverflowPosition = TextOverflowPosition.Start;
                this.style.overflow = Overflow.Hidden;
                this.RegisterCallback<MouseOverEvent>((MouseOverEvent evt) => { this.style.color = new Color(0.433f, 0.5913797f, 1f); });
                this.RegisterCallback<MouseLeaveEvent>((MouseLeaveEvent evt) => { this.style.color = new Color(0.2980392f, 0.4941176f, 1f); });
                this.AddManipulator(new ContextualMenuManipulator((ContextualMenuPopulateEvent evt) =>
                {
                    evt.menu.AppendAction("Copy Path", SaveToClipBoard);
                }));
            }
            void SaveToClipBoard(DropdownMenuAction evt)
            {
                EditorGUIUtility.systemCopyBuffer = path;
            }
        }

        public void CreateGUI()
        {
            thisSerialized = new SerializedObject(this);

            if (!string.IsNullOrEmpty(unityDXCInfo))
            {
                n_unityDXCInfo = unityDXCInfo;
                n_slzDXCInfo = slzDXCInfo;
                n_unityDXCPath = unityDXCPath;
                n_slzDXCPath = slzDXCPath;
                thisSerialized.Update();
            }

            rootVisualElement.style.paddingTop    = 6;
            rootVisualElement.style.paddingLeft   = 6;
            rootVisualElement.style.paddingBottom = 6;
            rootVisualElement.style.paddingRight  = 6;
            VisualElement mainRoot = new VisualElement();
            mainRoot.style.flexGrow = 1;

            Label mainText = new Label();
            //label.style.flexWrap = Wrap.Wrap;
            mainText.style.whiteSpace = WhiteSpace.Normal;
            mainText.text =
                        $"The DirectX Shader Compiler (DXC) in the Unity Editor installation is too old to support Quest.\n\nCurrent version is {n_unityDXCInfo}, " +
                        $"version 1.7 or above is needed to support multiview stereo. The legacy compiler will be used instead. Shaders may be less efficient," +
                        $" compilation times may be longer, and some advanced features will be unavailable.\n\n" +
                        "Unity uses the DXC shared library at:";// +
            mainRoot.Add(mainText);

            FileHyperLink toolsDirHotlink = new FileHyperLink(n_unityDXCPath);
            mainRoot.Add(toolsDirHotlink);

            Label mainText2 = new Label();
            mainText2.text = $"\nA unity-compatible fork of DXC {n_slzDXCInfo} is included at:";
            mainText2.style.whiteSpace = WhiteSpace.Normal;
            mainRoot.Add(mainText2);

            FileHyperLink slzDxcHotlink = new FileHyperLink(n_slzDXCPath);

            mainRoot.Add(slzDxcHotlink);

            VisualElement buttonRoot = new VisualElement();
            buttonRoot.style.minHeight = 24;
            buttonRoot.style.maxHeight = 24;
            buttonRoot.style.flexDirection = FlexDirection.Row;
            buttonRoot.style.justifyContent = Justify.SpaceBetween;

            VisualElement toggleBox = new VisualElement();
            toggleBox.style.flexDirection = FlexDirection.Row;
            toggleBox.style.alignContent = Align.Center;
            Toggle toggle = new Toggle();
            toggle.style.alignSelf = Align.Center;
            toggle.RegisterValueChangedCallback((ChangeEvent<bool> evt) => { EditorPrefs.SetBool("SkipDXCUpdate", evt.newValue); });
            Label toggleLabel = new Label("Don't show again");
            toggleLabel.style.alignSelf = Align.Center;
            toggleBox.Add(toggle);
            toggleBox.Add(toggleLabel);
            buttonRoot.Add(toggleBox);

            Button closeButton = new Button(() => this.Close());
            closeButton.text = "Close";
            buttonRoot.Add(closeButton);

            rootVisualElement.Add(mainRoot);
            rootVisualElement.Add(buttonRoot);
        }
    }
}