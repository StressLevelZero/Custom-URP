using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using static UnityEngine.UI.InputField;
using Object = UnityEngine.Object;
using System;

namespace UnityEditor
{
    public class TextureField : BindableElement, INotifyValueChanged<Object>
    {
        public MaterialProperty textureProperty;
        UnityEditor.UIElements.ObjectField texObjField;
        Texture2D currentValue;
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
        public TextureField(MaterialProperty textureProperty)
        {
            this.RegisterCallback<AttachToPanelEvent>(evt => Undo.undoRedoPerformed += fixUndoNotUpdatingValue);
            this.RegisterCallback<DetachFromPanelEvent>(evt => Undo.undoRedoPerformed -= fixUndoNotUpdatingValue);
            this.textureProperty = textureProperty;
            this.currentValue = textureProperty.textureValue as Texture2D;
            style.flexDirection = FlexDirection.Row;
            texObjField = new UnityEditor.UIElements.ObjectField();
            //List<SearchProvider> providers = new List<SearchProvider>() { new SearchProvider("sfklahlkjhsa", "hello") };
            //textureField.searchContext = new SearchContext(providers, "t:Texture2D", SearchFlags.Default);
            //textureField.bindingPath = "m_SavedProperties.m_TexEnvs.Array.data[0].second.m_Texture";
            texObjField.objectType = typeof(Texture2D);

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


            VisualElement contents = background.ElementAt(0);
            VisualElement label = contents.ElementAt(1);
            label.style.display = DisplayStyle.None;

            VisualElement thumbnail = contents.ElementAt(0);
            
            thumbnail.AddToClassList("textureFieldThumb");
            thumbnail.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            texObjField.RegisterValueChangedCallback(OnObjectFieldChanged);
            //texObjField.RegisterCallback<ChangeEvent<Object>>(x =>
            //{
            //    Debug.Log("Changed Event");
            //    Debug.Log("Undo Before texture change: " + Undo.GetCurrentGroup());
            //    textureProperty.textureValue = x.newValue as Texture;
            //    string currentUndoName = Undo.GetCurrentGroupName();
            //    if (string.IsNullOrEmpty(currentUndoName))
            //    {
            //        Debug.Log("No Undo currently");
            //    }
            //    else
            //    {
            //        Debug.Log("Current Undo: " + currentUndoName + " " + Undo.GetCurrentGroup());
            //    }
            //});

            if (textureProperty.hasMixedValue)
            {
                texObjField.value = null;
            }
            else
            {
                texObjField.value = textureProperty.textureValue as Texture2D;
            }
            //if (textureField.value == null)
            //{
            //    Debug.LogError("NULL Texture??????");
            //}
            Add(texObjField);
        }

        void OnObjectFieldChanged(ChangeEvent<Object> evt)
        {
            value = evt.newValue;
        }

        public void SetValueWithoutNotify(Object newValue)
        {
            if (newValue == null || newValue is Texture2D)
            {
                currentValue = (Texture2D) newValue;
                textureProperty.textureValue = currentValue;
                texObjField.SetValueWithoutNotify(currentValue);
                UpdateObjDelegate.Invoke(texObjField);
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

                using (var evt = ChangeEvent<Object>.GetPooled(previous, value))
                {
                    evt.target = this;
                    SendEvent(evt);
                }
            }
        }



        private void fixUndoNotUpdatingValue() 
        {
            Debug.Log("Fired Undo");
            if (texObjField.value != currentValue)
            {
                currentValue = (Texture2D) textureProperty.textureValue;
                texObjField.SetValueWithoutNotify(currentValue);
                UpdateObjDelegate.Invoke(texObjField);
            }
            else
            {
                Debug.Log("currentValue was equal to textureValue " + texObjField.value.name + " : " + textureProperty.textureValue.name);
                UpdateObjDelegate.Invoke(texObjField);
                texObjField.MarkDirtyRepaint();
            }
        }

    }
}
