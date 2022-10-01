using UnityEngine;
using UnityEditor;

using System.IO;

// Feel free to remove this
// I'm aware of ConvertToMAS, but I figured I'd make a simpler tool!
public class SimpleTextureConvert : EditorWindow
{
    private static GUIContent windowContent = new GUIContent("Convert To MAS", "Tool to help in the creation of MAS maps without external software");
    private static string helpMessage = "Please provide a metallic smoothness map and a occlusion map. They must be the same width and height!";

    [MenuItem("Stress Level Zero/Convert To MAS")]
    static void Init()
    {
        SimpleTextureConvert window = EditorWindow.GetWindow<SimpleTextureConvert>();
        window.titleContent = new GUIContent(windowContent);
        window.Show();
    }

    public Texture2D metallicSmoothness;
    public Texture2D ambientOcclusion;
    public ComputeShader convertCS;

    private void SaveTextureToFile(Texture2D texture)
    {
        byte[] pixels = texture.EncodeToPNG();

        string path = EditorUtility.SaveFilePanel("Save Image", "", "", "png");

        File.WriteAllBytes(path, pixels);

        string relative = "Assets" + path.Replace(Application.dataPath, "");
        AssetDatabase.ImportAsset(relative);

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(relative);
    }

    private void SaveBuffer(RenderTexture target = null)
    {
        RenderTexture.active = target;

        int width = target.width, height = target.height;
        Texture2D temp = new Texture2D(width, height, TextureFormat.ARGB32, true);

        temp.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        temp.Apply();

        SaveTextureToFile(temp);
        RenderTexture.active = null;
    }


    private int GetTile(int size, int tile) => Mathf.FloorToInt((float)size / (float)tile);

    public void OnGUI()
    {
        if (convertCS == null)
            convertCS = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Editor/Converter/TextureConvert/ConvertToMAS.compute");

        if (convertCS == null)
        {
            EditorGUILayout.HelpBox("Failed to load ComputeShader!", MessageType.Error);
            return;
        }

        metallicSmoothness = EditorGUILayout.ObjectField("Metallic Smoothness", metallicSmoothness, typeof(Texture2D), false) as Texture2D;
        ambientOcclusion = EditorGUILayout.ObjectField("Occlusion", ambientOcclusion, typeof(Texture2D), false) as Texture2D;

        if (metallicSmoothness && ambientOcclusion)
        {
            if (metallicSmoothness.width != ambientOcclusion.width || metallicSmoothness.height != ambientOcclusion.height)
            {
                EditorGUILayout.HelpBox("Mismatched texture dimensions!", MessageType.Error);
                EditorGUILayout.HelpBox($"{metallicSmoothness.width}x{metallicSmoothness.height} != {ambientOcclusion.width}x{ambientOcclusion.height}!", MessageType.Error);
                return;
            }
            else
            {
                if (GUILayout.Button("Convert To MAS"))
                {
                    RenderTexture buffer = new RenderTexture(ambientOcclusion.width, ambientOcclusion.height, 0) { enableRandomWrite = true };
                    buffer.Create();

                    var kernel = convertCS.FindKernel("CSMain");

                    convertCS.SetTexture(kernel, "MetallicSmoothness", metallicSmoothness);
                    convertCS.SetTexture(kernel, "OcclusionMap", ambientOcclusion);
                    convertCS.SetTexture(kernel, "Result", buffer);

                    convertCS.Dispatch(kernel, GetTile(buffer.width, 8), GetTile(buffer.height, 8), 1);

                    SaveBuffer(buffer);
                    buffer.Release();
                }
            }
        }
        else
            EditorGUILayout.HelpBox(helpMessage, MessageType.Info);
    }
}