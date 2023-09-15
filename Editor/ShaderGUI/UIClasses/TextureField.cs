using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using static UnityEngine.UI.InputField;
using Object = UnityEngine.Object;
using System;
using SLZ.SLZEditorTools;
using UnityEngine.Rendering;

namespace UnityEditor.SLZMaterialUI
{
    public class TextureField : VisualElement, BaseMaterialField
    {

        public MaterialProperty textureProperty;
        public VisualElement leftAlignBox { get; private set; }
        public VisualElement rightAlignBox { get; private set; }

        public string tooltip2 { get { return texObjField.tooltip; } set { texObjField.tooltip = value; } }
        public Texture defaultTexture;

        public UnityEditor.UIElements.ObjectField texObjField;
        Texture currentValue;
        
        Image thumbnail;
        bool updateScheduled = false;
        bool isNormalMap;
        RenderTexture thumbnailRT;

        UnityEngine.Rendering.TextureDimension textureType;

        public int shaderPropertyIdx;
        public int GetShaderPropIdx() { return shaderPropertyIdx; }

        static Action<ObjectField> s_updateObjDelegate;
        static Action<ObjectField> UpdateObjDelegate
        {
            get { 
                if (s_updateObjDelegate == null) 
                {
                    MethodInfo m = typeof(UnityEditor.UIElements.ObjectField).GetMethod("UpdateDisplay", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (m == null)
                    {
                        Debug.LogError("Missing UpdateDisplay method");
                    }
                    s_updateObjDelegate = (Action<ObjectField>)m.CreateDelegate(typeof(Action<ObjectField>));
                }
                return s_updateObjDelegate;
            }

        }
        public TextureField(MaterialProperty textureProperty, int texturePropertyIdx, bool isNormalMap, Texture defaultTexture = null)
        {
            this.textureProperty = textureProperty;
            this.currentValue = textureProperty.textureValue;
            this.shaderPropertyIdx = texturePropertyIdx;
            this.isNormalMap = isNormalMap;
            this.defaultTexture = defaultTexture;
            RegisterCallback<DetachFromPanelEvent>(evt => Dispose());
            leftAlignBox = new VisualElement();
            leftAlignBox.AddToClassList("materialGUILeftBox");
            rightAlignBox = new VisualElement();
            rightAlignBox.AddToClassList("materialGUIRightBox");


            style.flexDirection = FlexDirection.Row;
            texObjField = new UnityEditor.UIElements.ObjectField();
            //List<SearchProvider> providers = new List<SearchProvider>() { new SearchProvider("sfklahlkjhsa", "hello") };
            //textureField.searchContext = new SearchContext(providers, "t:Texture2D", SearchFlags.Default);
            //textureField.bindingPath = "m_SavedProperties.m_TexEnvs.Array.data[0].second.m_Texture";
            textureType = textureProperty.textureDimension;
            switch (textureProperty.textureDimension)
            {
                case (UnityEngine.Rendering.TextureDimension.Tex2D):
                    texObjField.objectType = typeof(Texture2D);
                    break;
                case (UnityEngine.Rendering.TextureDimension.Tex3D):
                    texObjField.objectType = typeof(Texture3D);
                    break;
                case (UnityEngine.Rendering.TextureDimension.Tex2DArray):
                    texObjField.objectType = typeof(Texture2DArray);
                    break;
                case (UnityEngine.Rendering.TextureDimension.Cube):
                    texObjField.objectType = typeof(Cubemap);
                    break;
                case (UnityEngine.Rendering.TextureDimension.CubeArray):
                    texObjField.objectType = typeof(CubemapArray);
                    break;
            }


            VisualElement background = texObjField.ElementAt(0);
            background.style.backgroundColor = StyleKeyword.None;
            background.style.borderBottomColor = StyleKeyword.None;
            background.style.borderLeftColor = StyleKeyword.None;
            background.style.borderRightColor = StyleKeyword.None;
            background.style.borderTopColor = StyleKeyword.None;

            background.style.borderBottomWidth = StyleKeyword.None;
            background.style.borderLeftWidth = StyleKeyword.None;
            background.style.borderRightWidth = StyleKeyword.None;
            background.style.borderTopWidth = StyleKeyword.None;

            background.style.borderTopLeftRadius = StyleKeyword.None;
            background.style.borderTopRightRadius = StyleKeyword.None;
            background.style.borderBottomLeftRadius = StyleKeyword.None;
            background.style.borderBottomRightRadius = StyleKeyword.None;
            background.style.justifyContent = Justify.FlexStart;
            background.style.flexWrap = Wrap.NoWrap;
    
            background.style.overflow = Overflow.Hidden;
            VisualElement contents = background.ElementAt(0);
            contents.style.flexShrink = 0;
            contents.style.flexGrow = 1;
            contents.style.overflow = Overflow.Hidden;
            contents.style.flexBasis = StyleKeyword.Auto;
            
            VisualElement oldlabel = contents.ElementAt(1);
            oldlabel.style.display = DisplayStyle.None;

            VisualElement searchButton = background.ElementAt(1);

            searchButton.style.width = StyleKeyword.Auto;
            searchButton.style.backgroundImage = StyleKeyword.None;
            searchButton.style.flexDirection = FlexDirection.Row;
            searchButton.style.overflow = Overflow.Hidden;
            searchButton.style.flexBasis = StyleKeyword.Auto;
            
            VisualElement fakeRadial = new VisualElement();
            fakeRadial.AddToClassList("unity-object-field__selector");
            fakeRadial.pickingMode = PickingMode.Ignore;
            fakeRadial.style.backgroundColor = Color.clear;

            Label label = new Label(textureProperty.displayName);
            label.pickingMode = PickingMode.Ignore;

            label.style.textOverflow = TextOverflow.Ellipsis;
            searchButton.Add(fakeRadial);
            searchButton.Add(label);

            Image oldThumbnail = contents.ElementAt(0) as Image;
            oldThumbnail.style.display = DisplayStyle.None;

            thumbnail = new Image();
            thumbnail.AddToClassList("textureFieldThumb");
            thumbnail.AddToClassList("unity-object-field-display__icon");
            thumbnail.pickingMode = PickingMode.Ignore;
            thumbnail.scaleMode = ScaleMode.StretchToFill;
            thumbnail.tintColor = Color.white;
            thumbnailRT = new RenderTexture((int)(32.0f * EditorGUIUtility.pixelsPerPoint), (int)(32.0f * EditorGUIUtility.pixelsPerPoint), 1, RenderTextureFormat.ARGB32, 1);
            thumbnailRT.depth = 0;
            thumbnailRT.name = textureProperty.name + "_icon";
            thumbnailRT.Create();
            thumbnail.image = thumbnailRT;

            contents.Insert(0, thumbnail);

            texObjField.RegisterValueChangedCallback(OnObjectFieldChanged);


            if (textureProperty.hasMixedValue)
            {
                currentValue = null;
                
                texObjField.showMixedValue = true;
            }
            else
            {
                //if (textureProperty.textureValue == null && defaultTexture != null) 
                //{
                //    textureProperty.textureValue = defaultTexture;
                //    currentValue = defaultTexture;
                //}
                
                texObjField.showMixedValue = false;
            }
            texObjField.SetValueWithoutNotify(currentValue);

            UpdateThumbnail();

            leftAlignBox.Add(texObjField);
            Add(leftAlignBox);
            Add(rightAlignBox);
        }

        void OnObjectFieldChanged(ChangeEvent<Object> evt)
        {
            value = evt.newValue;
        }

        public void SetValueWithoutNotify(Object newValue)
        {
            if (newValue == null || newValue is Texture)
            {
                currentValue = (Texture) newValue;

                if (currentValue == null && defaultTexture != null)
                {
                    currentValue = defaultTexture;
                }

                UpdateThumbnail();
                textureProperty.textureValue = currentValue;
                texObjField.SetValueWithoutNotify(currentValue);
                UpdateObjDelegate.Invoke(texObjField);
                texObjField.showMixedValue = newValue == null;
            }
            else throw new System.ArgumentException($"Expected object of type {typeof(Texture2D)}");
        }

        public Object value
        {
            get => currentValue;
            set
            {
                if (value == currentValue)
                    return;

                Object previous = currentValue;
                SetValueWithoutNotify(value);
            }
        }

        void UpdateThumbnail()
        {
            BlitTextureIcon(thumbnailRT, currentValue, textureType);
        }

        public void UpdateMaterialProperty(MaterialProperty boundProp)
        {

            textureProperty = boundProp;
            currentValue = boundProp.textureValue;
            texObjField.SetValueWithoutNotify(currentValue);
            if (boundProp.hasMixedValue)
            {
                texObjField.showMixedValue = true;
                currentValue = null;
            }
            else
            {
                texObjField.showMixedValue = false;
            }
            UpdateThumbnail();
        }

        void Dispose() 
        {
            if (thumbnailRT != null)
            {
                thumbnailRT.Clear();
            }
        }

        static Material s_blitMaterial;
        static LocalKeyword[] dimKeywords;
        static LocalKeyword normalMapKeyword;
        static int prop_Blit2D = Shader.PropertyToID("_Blit2D");
        static int prop_Blit2DArray = Shader.PropertyToID("_Blit2DArray");
        static int prop_BlitCube = Shader.PropertyToID("_BlitCube");
        static int prop_BlitCubeArray = Shader.PropertyToID("_BlitCubeArray");
        static int prop_Blit3D = Shader.PropertyToID("_Blit3D");
        static int prop_BlitDim = Shader.PropertyToID("_BlitDim");

        static void UpdateKeywords()
        {
            Shader blitShader = s_blitMaterial.shader;
            dimKeywords[(int)TextureDimension.Tex2D - 2] = new LocalKeyword(blitShader, "DIM_2D");
            dimKeywords[(int)TextureDimension.Tex2DArray - 2] = new LocalKeyword(blitShader, "DIM_2DARRAY");
            dimKeywords[(int)TextureDimension.Cube - 2] = new LocalKeyword(blitShader, "DIM_CUBE");
            dimKeywords[(int)TextureDimension.CubeArray - 2] = new LocalKeyword(blitShader, "DIM_CUBEARRAY");
            dimKeywords[(int)TextureDimension.Tex3D - 2] = new LocalKeyword(blitShader, "DIM_3D");
            normalMapKeyword = new LocalKeyword(blitShader, "NORMAL_MAP");
        }
        void BlitTextureIcon(RenderTexture icon, Texture tex, TextureDimension texType)
        {
            if (icon == null) return;
            RenderTexture active = RenderTexture.active;
            if (tex == null)
            {
               
                RenderTexture.active = icon;
                GL.Clear(false, true, Color.clear, 0);
                RenderTexture.active = active;
                return;
            }
            if (s_blitMaterial == null || s_blitMaterial.shader == null || dimKeywords == null) 
            {
                Shader blitShader = Shader.Find("Hidden/ShaderGUITextureIconBlit");
                if (blitShader == null)
                {
                    Debug.LogError("Could not find Blit_ShaderGUI.shader (Hidden/ShaderGUITextureIconBlit), cannot generate material thumbnails");
                    return;
                }
                s_blitMaterial = new Material(blitShader);
                dimKeywords = new LocalKeyword[5];
               
            }
          
            UpdateKeywords();


            int offsetDimEnum = (int)texType - 2;
            for (int i = 0; i < 5; i++)
            {
                if (i == offsetDimEnum)
                    s_blitMaterial.EnableKeyword(dimKeywords[i]);
                else
                    s_blitMaterial.DisableKeyword(dimKeywords[i]);
            }
            switch (texType) 
            { 
                case TextureDimension.Tex2D:
                    s_blitMaterial.SetTexture(prop_Blit2D, tex);
                    break;
                case TextureDimension.Tex2DArray:
                    s_blitMaterial.SetTexture(prop_Blit2DArray, tex);
                    break;
                case TextureDimension.Cube:
                    s_blitMaterial.SetTexture(prop_BlitCube, tex);
                    s_blitMaterial.SetVector(prop_BlitDim, new Vector4(tex.width, tex.height, 0, 0));
                    break;
                case TextureDimension.CubeArray:
                    s_blitMaterial.SetTexture(prop_BlitCubeArray, tex);
                    s_blitMaterial.SetVector(prop_BlitDim, new Vector4(tex.width, tex.height, 0, 0));
                    break;
                case TextureDimension.Tex3D:
                    s_blitMaterial.SetTexture(prop_Blit3D, tex);
                    s_blitMaterial.SetVector(prop_BlitDim, new Vector4(tex.width, tex.height, 0, 0));
                    break;
                default:
                    Debug.LogError("Shader GUI Icon Blitter: Unknown texture dimension " +  texType);
                    return;
            }
            if (isNormalMap)
            {
                s_blitMaterial.EnableKeyword(normalMapKeyword);
            }
            Graphics.Blit(tex, icon, s_blitMaterial);
            RenderTexture.active = active;
            if (isNormalMap)
            {
                s_blitMaterial.DisableKeyword(normalMapKeyword);
            }
            switch (texType)
            {
                case TextureDimension.Tex2D:
                    s_blitMaterial.SetTexture(prop_Blit2D, null);
                    break;
                case TextureDimension.Tex2DArray:
                    s_blitMaterial.SetTexture(prop_Blit2DArray, null);
                    break;
                case TextureDimension.Cube:
                    s_blitMaterial.SetTexture(prop_BlitCube, null);
                    break;
                case TextureDimension.CubeArray:
                    s_blitMaterial.SetTexture(prop_BlitCubeArray, null);
                    break;
                case TextureDimension.Tex3D:
                    s_blitMaterial.SetTexture(prop_Blit3D, null);
                    break;
            }
        }
    }
}
