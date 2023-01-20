using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Text;

#if SLZ_RENDERPIPELINE_DEV
public static class CreateGUIDList
{
    [MenuItem("Stress Level Zero/Create GUID list/SLZShaders")]
    public static void CreateSLZShaderList()
    {
        string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Packages/com.unity.render-pipelines.universal/ModResources/SLZShaders~");
        CreateListFromDirectory(dir);
    }

    [MenuItem("Stress Level Zero/Create GUID list/Amplify")]
    public static void CreateAmplifyList()
    {
        string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Packages/com.unity.render-pipelines.universal/ModResources/AmplifyExtensions~");
        CreateListFromDirectory(dir);
    }

    internal static void CreateListFromDirectory(string dir)
    {
      
        string[] metaFiles = Directory.GetFiles(dir, "*.meta", SearchOption.AllDirectories);
        int metaCount = metaFiles.Length;
        StringBuilder sb = new StringBuilder(33 * metaCount);
        
        for (int i = 0; i < metaCount; i++)
        {
            // Don't add folder meta files to list, we don't want to delete directories in case the user added other files to them!
            if (!Directory.Exists(metaFiles[i].Substring(0, metaFiles[i].Length - 5)))
            {
                Debug.Log(metaFiles[i]);
                using (StreamReader meta = new StreamReader(metaFiles[i]))
                {
                    // Read two lines, the GUID is always on the second line, and starts with "guid: " followed by the 32 character GUID
                    meta.ReadLine();
                    string guid = meta.ReadLine().Substring(6, 32);
                    sb.Append(guid + "\n");
                }
            }
        }
        
        File.WriteAllText(Path.Combine(dir, ".GUIDList.txt"), sb.ToString());
    }
}
#endif
