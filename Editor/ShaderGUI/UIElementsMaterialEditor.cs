using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Rendering.Universal;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using SLZ.SLZEditorTools;

namespace UnityEditor.SLZMaterialUI
{
    public abstract class UIElementsMaterialEditor : MaterialEditor
    {
        public Shader shader;
        public BaseMaterialField[] materialFields;
        public virtual void UpdateUI()
        {
            MaterialProperty[] materialProperties = MaterialEditor.GetMaterialProperties(this.targets);
            int[] shaderProp2MatProp = ShaderGUIUtils.GetShaderIdxToMaterialProp(materialProperties, shader);
            if (materialFields == null)
            {
                return;
            }
            int numFields = materialFields.Length;
            for (int fIdx = 0; fIdx < numFields; fIdx++)
            {
                int propIndex = shaderProp2MatProp[materialFields[fIdx].shaderPropIdx];
                materialFields[fIdx].UpdateMaterialProperty(materialProperties[propIndex]);
            }
        }

        /// <summary>
        /// Initialize the material inspector, getting the shader used by the materials and assigning it to the shader field.
        /// In a sane world, unity would only attempt to create the material editor when all materials share the same shader.
        /// But I don't trust unity, so check to make sure that they're all the same and not null.
        /// </summary>
        /// <returns>true on success, false on failure</returns>
        public bool Initialize()
        {
            SerializedProperty serializedShader = serializedObject.FindProperty("m_Shader");
            if (serializedShader.hasMultipleDifferentValues || serializedShader.objectReferenceValue == null)
            {
                Debug.LogError("SLZ Material Inspector: attempted to draw custom inspector for materials with different shaders");
                return false;
            }
            this.shader = (Shader)serializedShader.objectReferenceValue;
            if (shader == null)
            {
                Debug.LogError("SLZ Material Inspector: attempted to draw custom inspector for material with null shader");
                return false;
            }
            return true;
        }
    }
}