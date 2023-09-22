using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using static UnityEngine.Rendering.DebugUI.MessageBox;

namespace UnityEditor.UIElements
{
    public class GIFlagsPopup : PopupField<int>
    {
        
        static Dictionary<int, string> flagLabels = new Dictionary<int, string>()
        {
            {0, "None" },
            {1, "Realtime" },
            {2, "Baked" },
            };

        public GIFlagsPopup(SerializedObject serializedMaterial)
        {

            List<int> flags = new List<int>() { 0, 1, 2 };
            this.choices = flags;
            this.index = 2;
            this.formatSelectedValueCallback = GetCurrentFlagName;
            this.formatListItemCallback = GetValidFlagName;



            this.bindingPath = "m_LightmapFlags";
            this.label = "Global Illumination";
            VisualElement label = ElementAt(0);
            label.AddToClassList("materialGUILeftBox");
            label.style.overflow = Overflow.Hidden;
            label.style.minWidth = 0;
            VisualElement dropdown = ElementAt(1);
            dropdown.AddToClassList("materialGUIRightBox");
            style.justifyContent = Justify.FlexStart;
            style.marginRight = 3;
        }

        static string GetCurrentFlagName(int flag)
        {
            string label;
            if (flagLabels.TryGetValue(flag, out label))
            {
                return label;
            }
            else
            {
                return "-";
            }
        }

        static string GetValidFlagName(int flag)
        {
            return flagLabels[flag];
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
