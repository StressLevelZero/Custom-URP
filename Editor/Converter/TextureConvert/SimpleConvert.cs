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
    public static ComputeShader convertCS;

    private static Texture2D SaveTextureToFile(Texture2D texture, string path, bool focus = false)
    {
        byte[] pixels = texture.EncodeToPNG();

        string relative = path;
        if (string.IsNullOrEmpty(path))
        {
            path = EditorUtility.SaveFilePanel("Save Image", "", "", "png");
            relative = "Assets" + path.Replace(Application.dataPath, "");
        }

        File.WriteAllBytes(path, pixels);
        AssetDatabase.ImportAsset(relative);

        var assetTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(relative);
        if (focus)
        {
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = assetTexture;
        }

        return assetTexture;
    }

    private static Texture2D SaveBuffer(RenderTexture target = null, string path = null, bool focus = false)
    {
        RenderTexture.active = target;

        int width = target.width, height = target.height;
        Texture2D temp = new Texture2D(width, height, TextureFormat.ARGB32, true);

        temp.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        temp.Apply();

        var texture = SaveTextureToFile(temp, path, focus);
        RenderTexture.active = null;

        return texture;
    }


    private static int GetTile(int size, int tile) => Mathf.FloorToInt((float)size / (float)tile);

    public static Texture2D ConvertToMAS(Texture2D ambientOcclusion, Texture2D metallicSmoothness, string path = null, bool focus = false)
    {
        if (convertCS == null)
            convertCS = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Editor/Converter/TextureConvert/ConvertToMAS.compute");

        RenderTexture buffer = new RenderTexture(ambientOcclusion.width, ambientOcclusion.height, 0) { enableRandomWrite = true };
        buffer.Create();

        var kernel = convertCS.FindKernel("CSMain");

        convertCS.SetTexture(kernel, "MetallicSmoothness", metallicSmoothness);
        convertCS.SetTexture(kernel, "OcclusionMap", ambientOcclusion);
        convertCS.SetTexture(kernel, "Result", buffer);

        convertCS.Dispatch(kernel, GetTile(buffer.width, 8), GetTile(buffer.height, 8), 1);

        var texture = SaveBuffer(buffer, path, focus);
        buffer.Release();

        return texture;
    }

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
                if (GUILayout.Button("Convert To MAS"))
                    ConvertToMAS(ambientOcclusion, metallicSmoothness, null, true);
        }
        else
            EditorGUILayout.HelpBox(helpMessage, MessageType.Info);
    }
}