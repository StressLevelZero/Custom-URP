using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Unity.Collections;
using Unity.Mathematics;

namespace SLZ.SLZEditorTools
{
    public static class ShaderGUIUtils
    {

        const string shaderGUIStylePath = "Packages/com.unity.render-pipelines.universal/Editor/ShaderGUI/Styles/ShaderGUIStyles.uss";
        static StyleSheet s_ShaderGUISheet;
        public static StyleSheet shaderGUISheet
        {
            get
            {
                if (s_ShaderGUISheet == null)
                {
                    s_ShaderGUISheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(shaderGUIStylePath);
                    if (s_ShaderGUISheet == null)
                    {
                        Debug.LogError("Failed to find Shader GUI Style Sheet at " + shaderGUIStylePath);
                    }
                }

                return s_ShaderGUISheet;
            }
        }

        static FieldInfo s_InaccessibleToggle;
        /// <summary>
        /// Makes a foldout look like a unity inspector header.
        /// </summary>
        /// <param name="f"></param>
        /// <param name="title"></param>
        /// <param name="iconTex"></param>
        public static void SetHeaderStyle(Foldout f, string title, Texture iconTex = null, Toggle headerToggle = null)
        {

            f.AddToClassList("headerRoot");
            if (s_InaccessibleToggle == null)
            {
                s_InaccessibleToggle = typeof(Foldout).GetField("m_Toggle", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            Toggle actualFuckingToggle = (Toggle)s_InaccessibleToggle.GetValue(f);
            actualFuckingToggle.AddToClassList("headerTogglebar");
            actualFuckingToggle.style.paddingBottom = 3;
            actualFuckingToggle.style.paddingTop = 2;
            actualFuckingToggle.style.marginBottom = 1;
            VisualElement container = actualFuckingToggle.ElementAt(0);
            container.style.marginRight = 0;
            container.style.alignItems = Align.Center;
            VisualElement dropdown = container.ElementAt(0);
            dropdown.style.marginLeft = 5;

            Label titleLabel = new Label(title);
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.marginLeft = 6;
            titleLabel.style.unityTextAlign = TextAnchor.UpperLeft;
            titleLabel.style.paddingTop = 0;
            if (iconTex != null)
            {
                Image icon = new Image();
                icon.image = iconTex;
                icon.style.height = 16;
                icon.style.width = 16;
                icon.style.maxWidth = 16;
                icon.style.maxHeight = 16;
                icon.style.marginRight = 0;
               // icon.style.ba
                //icon.style.scale = new Vector2(1.1f, 1.1f);
                container.Add(icon);
            }
            if (headerToggle == null) 
            { 
                headerToggle = new Toggle();
                headerToggle.style.visibility = Visibility.Hidden;
            }
            headerToggle.style.marginLeft = 6;
            headerToggle.style.marginRight = 0;
            headerToggle.style.marginBottom = 1;
            container.Add(headerToggle);
            container.Add(titleLabel);
            f.contentContainer.AddToClassList("headerContent");
        }

        // Gets the shader property index corresponding to each element of a material property array
        public static int[] GetMaterialPropertyShaderIdx(MaterialProperty[] materialProperties, Shader shader)
        {
            int numMatProps = materialProperties.Length;
            int[] propertyIdx = new int[numMatProps];
            for (int i = 0; i < numMatProps; i++)
            {
                propertyIdx[i] = shader.FindPropertyIndex(materialProperties[i].name);
            }
            return propertyIdx;
        }

        public static int[] GetShaderIdxToMaterialProp(MaterialProperty[] materialProperties, Shader shader)
        {
            int numMatProps = materialProperties.Length;
            int[] propertyIdx = new int[shader.GetPropertyCount()];
            for (int i = 0; i < numMatProps; i++)
            {
                propertyIdx[shader.FindPropertyIndex(materialProperties[i].name)] = i;
            }
            return propertyIdx;
        }

        /// <summary>
        /// Strip out unused texture references in materials to avoid unity bundling/loading them
        /// </summary>
        /// <param name="targets">target objects, assumed to all be materials</param>
        /// <param name="materialProperties">Array of material properties retrieved from MaterialEditor.GetMaterialProperties</param>
        /// <param name="propertyIdx">map from each element of materialProperties to the index of its shader property</param>
        /// <param name="shader">Shader used by all the target materials</param>
        public static void SanitizeMaterials(Object[] targets, MaterialProperty[] materialProperties, int[] propertyIdx, Shader shader)
        {
            int numMatProps = materialProperties.Length;

            HashSet<string> validTextureNames = new HashSet<string>();

            for (int i = 0; i < numMatProps; i++)
            {
                ShaderPropertyType type = shader.GetPropertyType(propertyIdx[i]);
                switch (type)
                {
                    case ShaderPropertyType.Texture:
                        validTextureNames.Add(materialProperties[i].name);
                        break;
                }
            }
            int numMats = targets.Length;
            for (int mat = 0; mat < numMats; mat++)
            {
                SerializedObject smat = new SerializedObject(targets[mat]);
                SerializedProperty texEnv = smat.FindProperty("m_SavedProperties.m_TexEnvs");
                bool removedProp = false;
                string removedPropNames = "\n    ";
                if (texEnv != null)
                {
                    int numTex = texEnv.arraySize;
                    for (int tIdx = numTex - 1; tIdx >= 0; tIdx--)
                    {
                        SerializedProperty nameProp = smat.FindProperty("m_SavedProperties.m_TexEnvs.Array.data[" + tIdx.ToString() + "].first");
                        string name = nameProp.stringValue;
                        if (!validTextureNames.Contains(name))
                        {
                            texEnv.DeleteArrayElementAtIndex(tIdx);
                            removedPropNames += name + "\n    ";
                            removedProp = true;
                        }
                    }
                }
                if (removedProp)
                {
                    Debug.LogWarning("LitMAS GUI: Removed the following unused texture properties from " + targets[mat].name + removedPropNames);
                    smat.ApplyModifiedProperties();
                }
                smat.Dispose();
            }
        }

        static Dictionary<string, Texture2D> icon16px = new Dictionary<string, Texture2D>();
        public static Texture2D GetClosestUnityIconMip(string textureName, int iconHeightInPts)
        {
            int closestPow2Res = (int)math.round(math.log2(iconHeightInPts * EditorGUIUtility.pixelsPerPoint));
            int iconRes = 1 << closestPow2Res;
            string key = textureName + "X" + iconRes.ToString();

            if (icon16px.ContainsKey(key))
            {
                Texture2D storedIcon = icon16px[key];
                if (storedIcon != null)
                {
                    return icon16px[key];
                }
                else
                {
                    icon16px.Remove(key);
                }
            }
            GUIContent imguiIcon = EditorGUIUtility.IconContent(textureName);
            if (imguiIcon == null)
            {
                return null;
            }
            Texture2D icon = imguiIcon.image as Texture2D;
           
            if (icon == null)
            {
                return null;
            }
            if (icon.width < (iconRes + (iconRes / 2)))
            {
                return icon;
            }
            int numMips = icon.mipmapCount;
            int desiredMip = numMips - (closestPow2Res + 1);
            if (desiredMip < 1) 
            {
                return icon;
            }
            Texture2D tex = new Texture2D(
                Mathf.Max(icon.width >> desiredMip, 1),
                Mathf.Max(icon.height >> desiredMip, 1),
                icon.graphicsFormat,
                TextureCreationFlags.DontUploadUponCreate | TextureCreationFlags.DontInitializePixels | TextureCreationFlags.MipChain);
            tex.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            tex.name = textureName;
            int currentMip = 0;
            NativeArray<byte> iconData = new NativeArray<byte>((int)GraphicsFormatUtility.ComputeMipmapSize(tex.width, tex.height, tex.graphicsFormat), Allocator.Persistent);
            while (desiredMip < numMips && currentMip < tex.mipmapCount)
            {
                NativeArray<byte> texData = tex.GetPixelData<byte>(currentMip);
                AsyncGPUReadbackRequest request = AsyncGPUReadback.RequestIntoNativeArray<byte>(ref iconData, icon, desiredMip);
                request.WaitForCompletion();
                NativeArray<byte>.Copy(iconData, texData, texData.Length);
                currentMip++;
                desiredMip++;
                texData.Dispose();
            }
            iconData.Dispose();
            tex.Apply(false, true);
            icon16px.Add(key, tex);
            return tex;
        }
    }
}
