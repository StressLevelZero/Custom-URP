using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;

public class TextureConverter : ScriptableWizard
{

    public Material     TargetMaterial;

    [Header("Packing")]
    public TexturePackingTemplate template;
    [Space(20)]
    public Shader TargetShader;
    public PackingTargetLayout[] PackingArray;

    TexturePackingTemplate PrevTemplate;



    [MenuItem("CONTEXT/Material/Convert Texture")]
    public static void CreateWizard(MenuCommand menuCommand)
    {
        var wiz = ScriptableWizard.DisplayWizard<TextureConverter>("Convert Textures",  "Convert & Close", "Convert Textures");
        var contexMat = menuCommand.context as Material;
        wiz.TargetMaterial = contexMat;       //Set material from menu context
        wiz.template = GrabDefault(contexMat.shader);   //Get default template
        wiz.LoadTemplate();           //Intial Load
    }

    public static TexturePackingTemplate GrabDefault(Shader shader)
    {
        string filename = "Template Settings";
        string path = "Assets/Settings/" + filename+".asset";

        AssetDatabase.SaveAssets();
        ConverterSettings templateSettings =  AssetDatabase.LoadAssetAtPath<ConverterSettings>(path);
        if (templateSettings == null) //Generate intial default settings
        {
            Debug.Log("No settings file found. Generating...");
            //FileUtil.CopyFileOrDirectory("Packages/com.unity.render-pipelines.universal/Editor/Converter/TextureConvert/DefaultTemplateSettings.asset", path); //Copys even the name :/
            ConverterSettings defaultset =  AssetDatabase.LoadAssetAtPath<ConverterSettings>("Packages/com.unity.render-pipelines.universal/Editor/Converter/TextureConvert/DefaultTemplateSettings.asset");
            ConverterSettings parseset = ScriptableObject.CreateInstance<ConverterSettings>();
            parseset.ShaderDefaultTemplate = defaultset.ShaderDefaultTemplate;//Copy data rather than ref
            AssetDatabase.CreateAsset(parseset, path);
            AssetDatabase.ImportAsset(path);
            templateSettings = AssetDatabase.LoadAssetAtPath<ConverterSettings>(path);

            if (templateSettings != null) Debug.Log("Created template settings file inside the settings folder");
        } 

        if (templateSettings.ShaderDefaultTemplate.Length < 1) return null;
        int tempint = WhereOnArray(templateSettings, shader);
        if (tempint < 0) return null;

        return  templateSettings.ShaderDefaultTemplate[tempint].DefaultTemplate ;
    }



    static int WhereOnArray(ConverterSettings templateSettings, Shader shader)
    {
        for (int i = 0; i < templateSettings.ShaderDefaultTemplate.Length; i++) //Extracting shaders to compare
        {
            if (templateSettings.ShaderDefaultTemplate[i].shader == shader)
            {
                return i;
            }
        }
        return -1;
    }

    void OnWizardUpdate()
    {
        if (PrevTemplate != template) //Checking currently loaded template and reloading if different
        {
            LoadTemplate();
        } 
    }

    void LoadTemplate()
    {
        if (template == null) return;

        TargetShader = template.TargetShader;
        PackingArray = new PackingTargetLayout[template.Packing.Length];
        for (int i = 0; i < template.Packing.Length; i++)
        {
            PackingArray[i] = ParseTemplate(template.Packing[i]);
        }

        PrevTemplate = template;

    }

    private void OnWizardOtherButton() //Convert
    {
        TextureConvert();
    }

    public void OnWizardCreate() //Convert and close
    {
        TextureConvert();
    }

    public PackingTargetLayout ParseTemplate(PackingLayout layout) //Read template and load any connected textures
    {
        PackingTargetLayout targetLayout = new PackingTargetLayout();

        var props = TargetMaterial.GetTexturePropertyNames();

        targetLayout.PropertyName = layout.PropertyName;
        if (ArrayUtility.Contains(props, layout.RedInputProperty))
        {
            targetLayout.RedInputTexture = TargetMaterial.GetTexture(layout.RedInputProperty) as Texture2D;
        }
            targetLayout.RedOptions = layout.RedOptions;
        if (ArrayUtility.Contains(props, layout.GreenInputProperty)) targetLayout.GreenInputTexture = TargetMaterial.GetTexture(layout.GreenInputProperty) as Texture2D;
        targetLayout.GreenOptions = layout.GreenOptions;
        if (ArrayUtility.Contains(props, layout.BlueInputProperty)) targetLayout.BlueInputTexture = TargetMaterial.GetTexture(layout.BlueInputProperty) as Texture2D;
        targetLayout.BlueOptions = layout.BlueOptions;
        if (ArrayUtility.Contains(props, layout.AlphaInputProperty)) targetLayout.AlphaInputTexture = TargetMaterial.GetTexture(layout.AlphaInputProperty) as Texture2D;
        targetLayout.AlphaOptions = layout.AlphaOptions;

        targetLayout.packingOptions = layout.packingOptions;

        return targetLayout;
    }

    string SavedPath;

    public void TextureConvert()
    {
        if (PackingArray.Length < 1) return;


        //SHADER CONVERSION//
        //Set textures and blit using a custom shader

        for (int i = 0; i < PackingArray.Length; i++) //Convert Textures
        {
            SavedPath = null;

            UncompressBeforeTask(PackingArray[i]);

            Vector2Int maxSize;

            //Be a bit smarter about this
            maxSize = PackingArray[i].RedInputTexture.GetImageSize();
            maxSize = Vector2Int.Max(maxSize, PackingArray[i].GreenInputTexture.GetImageSize() );
            maxSize = Vector2Int.Max(maxSize, PackingArray[i].BlueInputTexture.GetImageSize()  );
            maxSize = Vector2Int.Max(maxSize, PackingArray[i].AlphaInputTexture.GetImageSize() );


            //Read Render texture and convert to texture2D

            var packedtexture = PackToTexture(PackingArray[i], maxSize).ConvertToTexture2D();

            //////

            //Save as new Texture

            if (PackingArray[i].packingOptions.OverrideTexture == OverrideSlot.New)
            {
                SaveNewTexture(packedtexture, PackingArray[i]);
            }

            else
            {
                string OverridePath;

                switch (PackingArray[i].packingOptions.OverrideTexture)
                {

                    case OverrideSlot.Red:
                        OverridePath = AssetDatabase.GetAssetPath(PackingArray[i].RedInputTexture);
                        break;
                    case OverrideSlot.Green:
                        OverridePath = AssetDatabase.GetAssetPath(PackingArray[i].GreenInputTexture);
                        break;
                    case OverrideSlot.Blue:
                        OverridePath = AssetDatabase.GetAssetPath(PackingArray[i].BlueInputTexture);
                        break;
                    case OverrideSlot.Alpha:
                        OverridePath = AssetDatabase.GetAssetPath(PackingArray[i].AlphaInputTexture);
                        break;
                    default:
                        OverridePath = null;
                        break;
                }

                if (OverridePath == null || OverridePath.Length == 0)
                {
                    Debug.Log("Texture override not valid. Making new texture instead.");
                    SaveNewTexture(packedtexture, PackingArray[i]);
                    break;
                }

                object extractedext;

                if (!System.Enum.TryParse(typeof(TextureFileExtension), Path.GetExtension(OverridePath).Substring(1), true, out extractedext)) {
                    Debug.Log("Texture type " + Path.GetExtension(OverridePath).Substring(1) + " is not supported. Making new texture instead.");
                    SaveNewTexture(packedtexture, PackingArray[i]);
                    break;
                }
                else
                {
                    TextureFileExtension ext = (TextureFileExtension)extractedext;

                    byte[] pixels = packedtexture.EncodeTexture(ext);

                    File.WriteAllBytes(OverridePath, pixels);
                    SavedPath = OverridePath;

                    AssetDatabase.Refresh();

                    DeleteOldTextures(PackingArray[i]);

                    ResetTextureCompressions(PackingArray[i]);

                    AssignProperty(PackingArray[i]);
                }

            }


        }

        if (TargetShader!=null) TargetMaterial.shader = TargetShader;

    }

    TextureImporterCompression cRed;
    TextureImporterCompression cGreen;
    TextureImporterCompression cBlue;
    TextureImporterCompression cAlpha;


    void UncompressBeforeTask(PackingTargetLayout targetLayout)
    {
        if (targetLayout.packingOptions.UncompressBeforeTask) return;

        cRed=UncompressFile(targetLayout.RedInputTexture);
        cGreen=UncompressFile(targetLayout.GreenInputTexture);
        cBlue=UncompressFile(targetLayout.BlueInputTexture);
        cAlpha=UncompressFile(targetLayout.AlphaInputTexture);
    }

    TextureImporterCompression UncompressFile(Texture2D texture)
    {
        TextureImporterCompression compression;

        var path = AssetDatabase.GetAssetPath(texture);
        if (path == null || path.Length == 0) return TextureImporterCompression.Uncompressed; 
        TextureImporter ti = (TextureImporter) TextureImporter.GetAtPath( path);
        compression = ti.textureCompression; //Storing compression
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = 8192;
        ti.SaveAndReimport();
        AssetDatabase.Refresh();
        return compression;
    }

    void AssignProperty(PackingTargetLayout packingTargetLayout)
    {
        AssetDatabase.Refresh();
        //Load new asset and set it to the correct slot
        TextureImporter ti = (TextureImporter)TextureImporter.GetAtPath(SavedPath);
        if (!packingTargetLayout.packingOptions.EnableAlphaChannel) ti.alphaSource = TextureImporterAlphaSource.None;
        ti.textureCompression = packingTargetLayout.packingOptions.textureCompression;
        ti.sRGBTexture = packingTargetLayout.packingOptions.sRGB;
        ti.SaveAndReimport();

        TargetMaterial.SetTexture(packingTargetLayout.PropertyName, AssetDatabase.LoadAssetAtPath<Texture2D>(SavedPath));
        AssetDatabase.Refresh();
    }

    void ResetTextureCompressions(PackingTargetLayout targetLayout)
    {
        if (targetLayout.packingOptions.UncompressBeforeTask) return;

        ResetCompression(targetLayout.RedInputTexture, cRed);
        ResetCompression(targetLayout.GreenInputTexture, cGreen);
        ResetCompression(targetLayout.BlueInputTexture, cBlue);
        ResetCompression(targetLayout.AlphaInputTexture, cAlpha);
    }

    void ResetCompression(Texture2D texture, TextureImporterCompression compression)
    {
        var path = AssetDatabase.GetAssetPath(texture);
        if (path == null || path.Length == 0) return;
        TextureImporter ti = (TextureImporter)TextureImporter.GetAtPath(path);
        ti.textureCompression = compression;
        ti.SaveAndReimport();
        AssetDatabase.Refresh();
    }

    void DeleteOldTextures(PackingTargetLayout targetLayout)
    {
        if (targetLayout.packingOptions.DeleteSource == false) return;

        deleteTexture(targetLayout.RedInputTexture);
        deleteTexture(targetLayout.GreenInputTexture);
        deleteTexture(targetLayout.BlueInputTexture);
        deleteTexture(targetLayout.AlphaInputTexture);

        AssetDatabase.Refresh();
    }

    void deleteTexture(Texture2D texture)
    {
        var path = AssetDatabase.GetAssetPath(texture);
        if (path == null || path.Length == 0 || SavedPath == path)  return;
        File.Delete(path);
        File.Delete(path+ ".meta");
        Debug.Log("Deleted " + path);
        AssetDatabase.Refresh();
    }

    public void SaveNewTexture(Texture2D packedtexture, PackingTargetLayout packingTargetLayout)
    {
        //    string fullPath = Path.GetDirectoryName(path) + "/" + Path.GetFileNameWithoutExtension(path) + "_MAS.png";

        string caughtFilePath = GetPathFromInputs(packingTargetLayout);

        //Path.GetDirectoryName(caughtFilePath);

        string UserVerifiedPath = EditorUtility.SaveFilePanel("Save Texture",
            Path.GetDirectoryName(caughtFilePath),
            Path.GetFileNameWithoutExtension(caughtFilePath) + packingTargetLayout.packingOptions.Suffix,
            packingTargetLayout.packingOptions.textureFileExtension.ToString());

        if (UserVerifiedPath == null || UserVerifiedPath.Length == 0) return;

        byte[] pixels = packedtexture.EncodeTexture(packingTargetLayout.packingOptions.textureFileExtension);
        //  File.WriteAllBytes(Application.dataPath + "/../" + fullPath, pixels);
        File.WriteAllBytes(UserVerifiedPath, pixels);

        string relativepath;
        if (UserVerifiedPath.StartsWith(Application.dataPath)) relativepath = "Assets" + UserVerifiedPath.Substring(Application.dataPath.Length);
        else relativepath = UserVerifiedPath;

        SavedPath = relativepath;
        Debug.Log("Saved texture at " + UserVerifiedPath);
        AssetDatabase.Refresh();
        DeleteOldTextures(packingTargetLayout);
        AssignProperty(packingTargetLayout);
        ResetTextureCompressions(packingTargetLayout);

    }

    public string GetPathFromInputs(PackingTargetLayout targetLayout)
    {
        if (targetLayout.RedInputTexture != null) return AssetDatabase.GetAssetPath(targetLayout.RedInputTexture);
        else if (targetLayout.GreenInputTexture != null) return AssetDatabase.GetAssetPath(targetLayout.GreenInputTexture);
        else if (targetLayout.BlueInputTexture != null) return AssetDatabase.GetAssetPath(targetLayout.BlueInputTexture);
        else if (targetLayout.AlphaInputTexture != null) return AssetDatabase.GetAssetPath(targetLayout.RedInputTexture);
        else return "/Asset/";

    }


    public RenderTexture PackToTexture(PackingTargetLayout targetLayout, Vector2Int Resolution )
    {
        //RenderTextureDescriptor descriptor = new RenderTextureDescriptor();
        //descriptor.width = (int)Resolution.x;
        //descriptor.height = (int)Resolution.y;
        //descriptor.colorFormat = RenderTextureFormat.ARGBHalf;
        //descriptor.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
        //descriptor.sRGB = true;
        //descriptor.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;
        //descriptor.volumeDepth = 1;
        //descriptor.msaaSamples = 1;
        //RenderTexture render = new RenderTexture(descriptor);

         RenderTexture render = new RenderTexture((int)Resolution.x, (int)Resolution.y, 0, RenderTextureFormat.Default);


        render.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;
        render.enableRandomWrite = true;
        render.wrapMode = TextureWrapMode.Clamp;
        render.Create();

       // Debug.Log(render.sRGB);
        ComputeShader Packer = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Editor/Converter/TextureConvert/TexturePacker.compute"); //Todo: Fix build error. Find better way to load?

        int kernelIndex = Packer.FindKernel("CSMain");

        Packer.SetTexture(kernelIndex, "InputTextureRed", GrabChannelTexture(targetLayout, RGBA.Red) ) ;
        Packer.SetTexture(kernelIndex, "InputTextureGreen", GrabChannelTexture(targetLayout, RGBA.Green) );
        Packer.SetTexture(kernelIndex, "InputTextureBlue", GrabChannelTexture(targetLayout, RGBA.Blue) );
        Packer.SetTexture(kernelIndex, "InputTextureAlpha", GrabChannelTexture(targetLayout, RGBA.Alpha) ) ;

        Packer.SetBool("invertRed", targetLayout.RedOptions.invert);
        Packer.SetBool("invertGreen", targetLayout.GreenOptions.invert);
        Packer.SetBool("invertBlue", targetLayout.BlueOptions.invert);
        Packer.SetBool("invertAlpha", targetLayout.AlphaOptions.invert);

        Packer.SetInt("inputChannelRed", ChannelToInt(targetLayout.RedOptions.InputChannel));
        Packer.SetInt("inputChannelGreen", ChannelToInt(targetLayout.GreenOptions.InputChannel));
        Packer.SetInt("inputChannelBlue", ChannelToInt(targetLayout.BlueOptions.InputChannel));
        Packer.SetInt("inputChannelAlpha", ChannelToInt(targetLayout.AlphaOptions.InputChannel));

        //Packer.SetBool("RedSRGB", ChannelToInt(targetLayout.RedOptions.InputChannel));
        //Packer.SetBool("GreenSRGB", ChannelToInt(targetLayout.GreenOptions.InputChannel));
        //Packer.SetBool("BlueSRGB", ChannelToInt(targetLayout.BlueOptions.InputChannel));
        //Packer.SetBool("AlphaSRGB", ChannelToInt(targetLayout.BlueOptions.InputChannel));

        Packer.SetTexture(kernelIndex, "Result", render);

        Packer.Dispatch(kernelIndex, (int)Resolution.x, (int)Resolution.y, 1);

        return render;
    }

    Texture2D GrabChannelTexture(PackingTargetLayout targetLayout, RGBA rgba )
    {
        switch (rgba)
        {
            case RGBA.Red:
                if (targetLayout.RedInputTexture != null) return targetLayout.RedInputTexture;
                else return GrabDefault(targetLayout.RedOptions.defaultColor);
            case RGBA.Green:
                if (targetLayout.GreenInputTexture != null) return targetLayout.GreenInputTexture; 
                else return GrabDefault(targetLayout.GreenOptions.defaultColor);
            case RGBA.Blue:
                if (targetLayout.BlueInputTexture != null) return targetLayout.BlueInputTexture;
                 else return GrabDefault(targetLayout.BlueOptions.defaultColor);
            case RGBA.Alpha:
                if (targetLayout.AlphaInputTexture != null) return targetLayout.AlphaInputTexture;
                 else return GrabDefault(targetLayout.AlphaOptions.defaultColor);
        }
        return Texture2D.whiteTexture;
    }

    Texture2D GrabDefault(DefaultColor defaultColor)
    {
        switch (defaultColor)
        {
            case DefaultColor.Black:
                return Texture2D.blackTexture;
            case DefaultColor.White:
                return Texture2D.whiteTexture;
            case DefaultColor.Gray:
                return Texture2D.grayTexture;
            case DefaultColor.LinearGray:
                return Texture2D.linearGrayTexture;
            default:
                return Texture2D.blackTexture;
        }
    }

    int ChannelToInt(RGBA rgba)
    {
        switch (rgba)
        {
            case RGBA.Red:
                return 0;
            case RGBA.Green:
                return 1;
            case RGBA.Blue:
                return 2;
            case RGBA.Alpha:
                return 3;
            default:
                return -1;
        }
    }
}
