using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEngine.Rendering;
using UnityEditor.UIElements;
using System.Diagnostics;
using System.IO;
using UnityEditor.SceneManagement;
using Debug = UnityEngine.Debug;

namespace SLZ.SLZEditorTools
{
    public class GraphicsAPISwitch : EditorWindow
    {
        [MenuItem("Stress Level Zero/Switch Graphics API",priority = 1)]
        public static void ShowWindow()
        {
            GraphicsAPISwitch wnd = GetWindow<GraphicsAPISwitch>();
            wnd.titleContent = new GUIContent("Switch Graphics API");
        }

        [SerializeField]
        public GraphicsDeviceType newAPI = GraphicsDeviceType.Vulkan;

        static Dictionary<GraphicsDeviceType, string> launchParams = new Dictionary<GraphicsDeviceType, string>()
        {
            {GraphicsDeviceType.Vulkan, "-force-vulkan"},
            {GraphicsDeviceType.Direct3D12, "-force-d3d12"},
            {GraphicsDeviceType.Direct3D11, "-force-d3d11"},
        };

        public void CreateGUI()
        {
            
            VisualElement root = rootVisualElement;
            root.style.paddingBottom = 5;
            root.style.paddingTop = 5;
            root.style.paddingLeft = 5;
            root.style.paddingRight = 5;
            SerializedObject thisSerialized = new SerializedObject(this);

            GraphicsDeviceType currentAPI = SystemInfo.graphicsDeviceType;
            Label status = new Label($"Current API: {currentAPI.ToString()}");
            root.Add(status);

            
            List<GraphicsDeviceType> validDeviceTypes = new List<GraphicsDeviceType>(launchParams.Keys);
            
            SerializedProperty apiProp = thisSerialized.FindProperty("newAPI");
            GraphicsDeviceType currValue = (GraphicsDeviceType)apiProp.intValue;
            //Debug.Log($"currValue: {currValue.ToString()}");
            PopupField<GraphicsDeviceType> apiSelector =
                new PopupField<GraphicsDeviceType>(validDeviceTypes, currValue,
                formatValue,
                formatValue
                );
            apiSelector.RegisterValueChangedCallback((ChangeEvent<GraphicsDeviceType> evt) => newAPI = evt.newValue);
            
            if (apiProp == null )
            {
                Debug.LogError("Could not find property newAPI!");
            }
            apiSelector.BindProperty(apiProp);

            root.Add(apiSelector);
            Button switchButton = new Button(this.SwtichToNewAPI);
            switchButton.text = "Switch Graphics API";
            root.Add(switchButton);
        }

        public static string formatValue(GraphicsDeviceType api)
        {
            return System.Enum.GetName(typeof(GraphicsDeviceType), api);
        }

        public void SwtichToNewAPI()
        {
            //Debug.Log($"New API: {newAPI.ToString()}");
            if (newAPI == SystemInfo.graphicsDeviceType)
            {
                Debug.Log($"Graphics API is already set to {newAPI.ToString()}!");
                return;
            }
            if (!launchParams.ContainsKey(newAPI))
            {
                Debug.LogError($"Chosen graphics API is {newAPI.ToString()}, but this script has no launch parameters for this API?");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Process unity = new Process();
                string projectPath = Path.GetDirectoryName(Application.dataPath);
                unity.StartInfo.FileName = EditorApplication.applicationPath;
                unity.StartInfo.Arguments = $"-projectPath \"{projectPath}\" {launchParams[newAPI]}";
                unity.Start();
                EditorApplication.Exit(0);
            }
        }

        public static void SwitchToAPI(GraphicsDeviceType gfxAPI)
        {
            if (gfxAPI == SystemInfo.graphicsDeviceType)
            {
                return;
            }
            if (!launchParams.ContainsKey(gfxAPI))
            {
                Debug.LogError($"Chosen graphics API is {gfxAPI.ToString()}, but this script has no launch parameters for this API?");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Process unity = new Process();
                string projectPath = Path.GetDirectoryName(Application.dataPath);
                unity.StartInfo.FileName = EditorApplication.applicationPath;
                unity.StartInfo.Arguments = $"-projectPath \"{projectPath}\" {launchParams[gfxAPI]}";
                unity.Start();
                EditorApplication.Exit(0);
            }
        }
    }
}