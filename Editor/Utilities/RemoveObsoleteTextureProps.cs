using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;
using UnityEngine.Rendering;
using System.Text;

public class RemoveObsoleteTextureProps
{

    public static void RemoveObsoleteFromFolder()
    {
        string absolutePath = EditorUtility.OpenFolderPanel("Select folder to clean all materials in", Application.dataPath, "");
        string projPath = Path.GetDirectoryName(Application.dataPath);
        string path = Path.GetRelativePath(projPath, absolutePath);
        Debug.Log(path);

        string[] matGUIDS = AssetDatabase.FindAssets("t:material", new string[1] { path });
        int numGUIDs = matGUIDS.Length;
        StringBuilder report = new StringBuilder();
       
        for (int i = 0; i < numGUIDs; i++)
        {
           
            string matPath = AssetDatabase.GUIDToAssetPath(matGUIDS[i]);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            int numRemoved = RemoveObsoleteTexProps(mat, matPath, report);
        }
        Debug.Log(report.ToString());
    }

    [MenuItem("CONTEXT/Material/Remove Unused Texture References")]
    public static void ContextOption(MenuCommand command)
    {    
        Material mat = (Material)command.context;
        if (mat == null) { Debug.Log("Removing unused texture references failed. Material was null?"); return; }
        StringBuilder report = new StringBuilder("Removing Unused Properties: ");
        RemoveObsoleteTexProps(mat, mat.name, report);
        Debug.Log(report.ToString());
    }

    static int RemoveObsoleteTexProps(Material mat, string matPath = "", StringBuilder report = null)
    {
        Shader s = mat.shader;
        int numProps = s.GetPropertyCount();
        HashSet<string> texProps = new HashSet<string>(numProps);
        for (int pIdx = 0; pIdx < numProps; pIdx++) 
        { 
            if (s.GetPropertyType(pIdx) == UnityEngine.Rendering.ShaderPropertyType.Texture)
            {
                texProps.Add(s.GetPropertyName(pIdx));
            }
        }
        int numRemoved = 0;
        bool firstRemoved = true;
        SerializedObject matSerialized = new SerializedObject(mat);
        SerializedProperty texPropArray = matSerialized.FindProperty("m_SavedProperties.m_TexEnvs");
        int numTexProps = texPropArray.arraySize;
        for (int tIdx = numTexProps - 1; tIdx >= 0; tIdx--)
        {
            SerializedProperty texPropName = matSerialized.FindProperty($"m_SavedProperties.m_TexEnvs.Array.data[{tIdx}].first");
            string tpName = texPropName.stringValue;
            if (!texProps.Contains(tpName))
            {
                if (firstRemoved)
                {
                    report?.AppendLine($"{matPath}:");
                    firstRemoved = false;
                }
                report?.AppendLine($"    {tpName}");
                texPropArray.DeleteArrayElementAtIndex(tIdx);
                numRemoved += 1;
            }
        }
        matSerialized.ApplyModifiedProperties();
        matSerialized.Dispose();

        return numRemoved;
    }
}
