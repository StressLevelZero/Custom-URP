using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.UIElements
{
    public class RenderQueueDropdown : VisualElement
    {
        public PopupField<int> renderQueuePresets;
        RenderQueueIntField renderQueueCustom;
        int defaultRenderQueue;
        Dictionary<int, string> queueLabels = new Dictionary<int, string>()
        {
            {-1, " From Shader " },
            {1000, " Background " },
            {2000, " Geometry " },
            {2450, " Alpha Test " },
            {3000, " Transparent " }
            };

        public RenderQueueDropdown(SerializedObject serializedMaterial, Shader shader)
        {
            this.style.flexDirection = FlexDirection.Row;
            //this.style.
            this.style.alignSelf = Align.Stretch;
            this.style.alignItems = Align.Stretch;
            this.style.flexWrap = Wrap.NoWrap;
            this.style.justifyContent = Justify.SpaceBetween; 

            List<int> queues = new List<int>() { -1, 1000, 2000, 2450, 3000 };

            SerializedProperty renderQueueProp = serializedMaterial.FindProperty("m_CustomRenderQueue");
            
            renderQueuePresets = new PopupField<int>(queues, -1, GetCurrentQueueName, GetValidQueueName);
            //renderQueuePresets.style.minWidth = 9 * 12;
            renderQueuePresets.bindingPath = "m_CustomRenderQueue";
            renderQueuePresets.Bind(serializedMaterial);
            renderQueuePresets.style.width = 100;
            renderQueueCustom = new RenderQueueIntField();

            renderQueueCustom.defaultQueue = shader.renderQueue;
            renderQueueCustom.style.width = 48;
            renderQueueCustom.bindingPath = "m_CustomRenderQueue";
            renderQueueCustom.Bind(serializedMaterial);
            renderQueueCustom.style.unityTextAlign = TextAnchor.MiddleRight;
            VisualElement renderQueueGroup = new VisualElement();
            renderQueueGroup.style.flexDirection = FlexDirection.Row;
            renderQueueGroup.style.flexShrink = 0;
            renderQueueGroup.Add(renderQueuePresets);
            renderQueueGroup.Add(renderQueueCustom);

            Label label = new Label("Render Queue");
            label.style.alignSelf = Align.Center;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.flexShrink = 1;
            Add(label);
            Add(renderQueueGroup);
        }

        string GetCurrentQueueName(int queue)
        {
            string label;
            if (queueLabels.TryGetValue(queue, out label))
            {
                return label;
            }
            else
            {
                return "Custom";
            }
        }

        string GetValidQueueName(int queue)
        {
            return queueLabels[queue];
        }

        class RenderQueueIntField : IntegerField
        {
            public int defaultQueue = 2000;
            protected override string ValueToString(int v)
            {
                if (v == -1)
                {
                    return defaultQueue.ToString();
                }
                return v.ToString(base.formatString, CultureInfo.InvariantCulture.NumberFormat);
            }
        }
    }
}
