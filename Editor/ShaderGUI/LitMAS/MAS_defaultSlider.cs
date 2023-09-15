using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SLZMaterialUI;
using UnityEngine;
using UnityEngine.UIElements;

public class MAS_defaultSlider : VisualElement
{
    public TextureField MASField;

    struct MASProperties
    {
        public int metallic;
        public int smoothness;
    }
    static Dictionary<Texture, MASProperties> MASProps;
    static Texture[] MASTextures;
    const int numSmoothness = 10;
    const int numMetallic = 1;
    const int smoothIncr = 100 / numSmoothness;
    const int metalIncr = 100 / numMetallic;

    const string defaultTexPath = "Packages/com.unity.render-pipelines.universal/Textures/Default/MAS/";
    SliderInt SmoothnessSlider;
    Toggle MetallicCheck;

    static void Init()
    {
        int numTex = (numSmoothness + 1) * (numMetallic+1);
        MASTextures = new Texture[numTex];
        MASProps = new Dictionary<Texture, MASProperties>(numTex);

        int index = 0;
        for (int m = 0; m <= 100; m += metalIncr)
        {
            string mDigits = Mathf.Min(m, 99).ToString().PadLeft(2,'0');
            for (int s = 0; s <= 100; s += smoothIncr)
            {
                string sDigits = Mathf.Min(s, 99).ToString().PadLeft(2,'0');
                string path = string.Format("{0}/MAS_M{1}_S{2}.png", defaultTexPath, mDigits, sDigits);
                //Debug.Log(path);
                Texture2D mas = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                MASTextures[index] = mas;
                index++;
                MASProps.Add(mas, new MASProperties { metallic = m, smoothness = s });
            }
        }
    }

    public MAS_defaultSlider(TextureField MASField)
    {
        this.MASField = MASField;
        SmoothnessSlider = new SliderInt(0, smoothIncr);
        MetallicCheck = new Toggle();
        MetallicCheck.style.marginRight = 8;
        MetallicCheck.style.marginBottom = 0;

        this.style.flexDirection = FlexDirection.Row;
        this.style.alignItems = Align.Center;
        this.style.justifyContent = Justify.SpaceBetween;
        this.style.flexGrow = 1;
        this.style.paddingTop = 0;
        this.style.marginTop = 0;
        SmoothnessSlider.style.flexGrow = 1;
        SmoothnessSlider.style.flexShrink = 1;
        SmoothnessSlider.style.marginTop = 0;
        SmoothnessSlider.style.marginRight = 4;
        //SmoothnessSlider.style.alignSelf = Align.Center;
        //SmoothnessSlider.label = "Smoothness";
        Label metalLabel = new Label("Metal");
        
        Label SmoothLabel = new Label("Smoothness");
        Add(metalLabel);
        Add(MetallicCheck);
        Add(SmoothLabel);
        Add(SmoothnessSlider);
        CheckIfDefault();
        MASField.texObjField.RegisterValueChangedCallback(evt => CheckIfDefault());
        SmoothnessSlider.RegisterValueChangedCallback(evt =>
        {
            int index = evt.newValue + (MetallicCheck.value ? numSmoothness + 1 : 0);
            MASField.texObjField.value = MASTextures[index];
        });

        MetallicCheck.RegisterValueChangedCallback(evt =>
        {
            int index = SmoothnessSlider.value + (evt.newValue ? numSmoothness + 1 : 0);
            MASField.texObjField.value = MASTextures[index];
        });
    }

    void CheckIfDefault()
    {
        if (MASProps == null || MASTextures == null)
        {
            Init();
        }
        MASProperties props;
        Texture2D mas = MASField.texObjField.value as Texture2D;
        if (mas != null && MASProps.TryGetValue(mas, out props))
        {
            this.style.display = DisplayStyle.Flex;
            SmoothnessSlider.SetValueWithoutNotify(props.smoothness / numSmoothness);
            MetallicCheck.SetValueWithoutNotify(props.metallic > 0);
        }
        else
        {
            this.style.display = DisplayStyle.None;
        }
    }
}
