using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;

[System.Serializable]
public struct TemplateDefaultSetting
{
    public Shader shader;
    public TexturePackingTemplate DefaultTemplate;
}


//[CreateAssetMenu(fileName = "Template Settings", menuName = "Rendering/Template Settings", order = 5)]
public class ConverterSettings : ScriptableObject
{
    [Tooltip("Default template for target shader")]
    public TemplateDefaultSetting[] ShaderDefaultTemplate;


    private void OnValidate()
    {
        //Make sure that we only have one template per shader


       // Debug.Log(ShaderDefaultTemplate.Distinct().Count());
       // ShaderDefaultTemplate = ShaderDefaultTemplate.Distinct() as TemplateDefaultSetting[];
        //for (int i=0; i < ShaderDefaultTemplate.Length; i++)
        //{
        //    ShaderDefaultTemplate.Distinct

        //  if ShaderDefaultTemplate.c  ShaderDefaultTemplate[i];
        //}

    }
}