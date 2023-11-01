using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace UnityEditor.SLZMaterialUI
{
    public class SurfaceTypeField : PopupField<int>, BaseMaterialField
    {
        public int shaderPropertyIdx;
        public int GetShaderPropIdx() { return shaderPropertyIdx; }
        public MaterialProperty materialProperty;
        MaterialProperty MaterialProperty {  get { return materialProperty; } }
        public INotifyValueChanged<float> blendSrc;
        public INotifyValueChanged<float> blendDst;
        public INotifyValueChanged<float> zWrite;
        public INotifyValueChanged<int> renderQueue;


        enum SurfaceTypes
        {
            Opaque = 0,
            Transparent = 1,
            Fade = 2,
        }

        static Dictionary<int, string> surfaceLabels = new Dictionary<int, string>()
        {
            {0, "Opaque" },
            {1, "Transparent" },
            {2, "Fade" },
        };


        static Span<int> SurfaceQueue => new int[] { 2000, 3000, 3000 };
        static Span<int> SurfaceBlendSrc => new int[] { (int)BlendMode.One, (int)BlendMode.One, (int)BlendMode.SrcAlpha };
        static Span<int> SurfaceBlendDst => new int[] { (int)BlendMode.Zero, (int)BlendMode.OneMinusSrcAlpha, (int)BlendMode.OneMinusSrcAlpha };
        static Span<int> SurfaceZWrite => new int[] { 1, 0, 0 };

        public void Initialize(MaterialProperty materialProperty, int shaderPropertyIdx, 
            INotifyValueChanged<float> blendSrc, 
            INotifyValueChanged<float> blendDst, 
            INotifyValueChanged<float> zWrite, 
            INotifyValueChanged<int> queueField)
        {
            this.formatSelectedValueCallback = GetCurrentFlagName;
            this.formatListItemCallback = GetValidFlagName;

            this.materialProperty = materialProperty;
            this.shaderPropertyIdx = shaderPropertyIdx;
            this.blendSrc = blendSrc;
            this.blendDst = blendDst;
            this.zWrite = zWrite;
            this.renderQueue = queueField;

            this.label = "Surface Type";
            this.choices = new List<int>() { 0, 1, 2 };
            VisualElement label = ElementAt(0);
            label.AddToClassList("materialGUILeftBox");
            label.style.overflow = Overflow.Hidden;
            label.style.minWidth = 0;
            VisualElement dropdown = ElementAt(1);
            dropdown.AddToClassList("materialGUIRightBox");
            style.justifyContent = Justify.FlexStart;
            style.marginRight = 3;

            RegisterCallback<ChangeEvent<int>>(evt =>
            {
                
                int newVal = evt.newValue;
                Debug.Log("Trying to set surface to " + newVal);
                UnityEngine.Object[] targets = MaterialProperty.targets;
                //MaterialProperty.floatValue = newVal;
                int numTargets = targets.Length;
                Undo.IncrementCurrentGroup();
                Undo.RecordObjects(targets, "Set Surface Type");
                Shader s = ((Material)targets[0]).shader;
                int surfIdx = s.FindPropertyIndex("_Surface");
                int blendSrcIdx = s.FindPropertyIndex("_BlendSrc");
                int blendDstIdx = s.FindPropertyIndex("_BlendDst");
                int zWriteIdx = s.FindPropertyIndex("_ZWrite");
                Debug.Log(string.Format("Num Targets: {4}, _Surface: {0}, _BlendSrc:{1}, _BlendDst:{2}, _ZWrite:{3} ", surfIdx, blendSrcIdx, blendDstIdx, zWriteIdx, numTargets));
                int queue = SurfaceQueue[newVal];
                queue = queue == s.renderQueue ? -1 : queue;
                for (int i = 0; i < numTargets; i++)
                {
                    Material mat = (Material)targets[i];
                    mat.SetFloat("_Surface", newVal);
                    mat.SetFloat("_BlendSrc", SurfaceBlendSrc[newVal]);
                    mat.SetFloat("_BlendDst", SurfaceBlendDst[newVal]);
                    mat.SetFloat("_ZWrite", SurfaceZWrite[newVal]);
                    mat.renderQueue = queue;

                }
                queueField.SetValueWithoutNotify(SurfaceQueue[newVal]);
                Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
                
            }
            );
            this.SetValueWithoutNotify((int)materialProperty.floatValue);
        }



        public void UpdateMaterialProperty(MaterialProperty boundProp)
        {
            materialProperty = boundProp;
            int newVal = (int)boundProp.floatValue;
            if (this.value != newVal)
            {
                this.SetValueWithoutNotify(newVal);
            }
            this.showMixedValue = boundProp.hasMixedValue;
        }

        static string GetCurrentFlagName(int type)
        {
            string label;
            if (surfaceLabels.TryGetValue(type, out label))
            {
                return label;
            }
            else
            {
                return "-";
            }
        }

        static string GetValidFlagName(int type)
        {
            return surfaceLabels[type];
        }
    }
}
