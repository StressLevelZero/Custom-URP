#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public class TextureHistogramWindow : EditorWindow
{
    enum ChannelView { All, R, G, B, A }
    enum DisplaySpace { Auto, Linear, sRGB }

    [SerializeField] ChannelView view = ChannelView.All;
    [SerializeField] DisplaySpace displaySpace = DisplaySpace.Auto;

    [SerializeField] bool autoUpdate = true;
    [SerializeField] int bins = 256;
    [SerializeField] int sampleSize = 512; // downsample for speed
    [SerializeField] bool showStats = true;
    [SerializeField, Range(0f, 0.49f)] float percentile = 0.01f;

    int[] histR, histG, histB, histA;
    int totalSamples;

    float[] min = new float[4];
    float[] max = new float[4];
    float[] mean = new float[4];
    float[] pLo = new float[4];
    float[] pHi = new float[4];

    Texture2D selectedTex;
    string selectedPath;

    bool importerSRGB;
    bool projectLinear;

    RenderTexture rt;
    Texture2D readback;

    [MenuItem("Window/Analysis/Texture Histogram")]
    static void Open() => GetWindow<TextureHistogramWindow>("Texture Histogram");

    public static void OpenFor(Texture2D tex)
    {
        var w = GetWindow<TextureHistogramWindow>("Texture Histogram");
        w.SetTexture(tex, true);
        w.Show();
        w.Focus();
    }

    void OnEnable()
    {
        projectLinear = (QualitySettings.activeColorSpace == ColorSpace.Linear);
        Selection.selectionChanged += OnSelectionChanged;
        OnSelectionChanged();
    }

    void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;
        ReleaseTemps();
    }

    void OnSelectionChanged()
    {
        if (!autoUpdate) { Repaint(); return; }
        var tex = Selection.activeObject as Texture2D;
        if (tex) SetTexture(tex, true);
        else { selectedTex = null; selectedPath = null; Repaint(); }
    }

    void SetTexture(Texture2D tex, bool recompute)
    {
        selectedTex = tex;
        selectedPath = selectedTex ? AssetDatabase.GetAssetPath(selectedTex) : null;

        importerSRGB = false;
        if (!string.IsNullOrEmpty(selectedPath))
        {
            if (AssetImporter.GetAtPath(selectedPath) is TextureImporter ti)
                importerSRGB = ti.sRGBTexture;
        }

        if (recompute && selectedTex) ComputeHistogram();
        Repaint();
    }

    void ReleaseTemps()
    {
        if (rt) RenderTexture.ReleaseTemporary(rt);
        rt = null;
        if (readback) DestroyImmediate(readback);
        readback = null;
    }

    void EnsureBuffers()
    {
        bins = Mathf.Clamp(bins, 16, 4096);
        histR = new int[bins];
        histG = new int[bins];
        histB = new int[bins];
        histA = new int[bins];
    }

    // Exact-ish sRGB encode (for histogram display), only meaningful in Linear projects.
    static float LinearToSRGB(float x)
    {
        x = Mathf.Clamp01(x);
        return (x <= 0.0031308f) ? (12.92f * x) : (1.055f * Mathf.Pow(x, 1f / 2.4f) - 0.055f);
    }

    float ApplyDisplaySpace(float linearValue)
    {
        // If the project is Gamma, don’t try to “compensate” — it’s ambiguous what you want.
        if (!projectLinear) return Mathf.Clamp01(linearValue);

        switch (displaySpace)
        {
            case DisplaySpace.Linear:
                return Mathf.Clamp01(linearValue);

            case DisplaySpace.sRGB:
                return LinearToSRGB(linearValue);

            case DisplaySpace.Auto:
            default:
                // “Compensate”: show sRGB-style histogram for color textures, linear for technical textures.
                return importerSRGB ? LinearToSRGB(linearValue) : Mathf.Clamp01(linearValue);
        }
    }

    void ComputeHistogram()
    {
        if (!selectedTex) return;

        projectLinear = (QualitySettings.activeColorSpace == ColorSpace.Linear);

        EnsureBuffers();
        Array.Clear(histR, 0, bins);
        Array.Clear(histG, 0, bins);
        Array.Clear(histB, 0, bins);
        Array.Clear(histA, 0, bins);

        for (int i = 0; i < 4; i++)
        {
            min[i] = 1f;
            max[i] = 0f;
            mean[i] = 0f;
            pLo[i] = 0f;
            pHi[i] = 1f;
        }

        int w = Mathf.Min(sampleSize, selectedTex.width);
        int h = Mathf.Min(sampleSize, selectedTex.height);

        ReleaseTemps();

        // Force a LINEAR render target so our readback is linear samples.
        // sRGB textures will be decoded to linear on sample (in Linear projects).
        rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

        Graphics.Blit(selectedTex, rt);

        // Readback as "linear" texture to avoid editor-side gamma fiddling.
        readback = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        readback.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
        readback.Apply(false, false);
        RenderTexture.active = prev;

        var pixels = readback.GetPixels32();
        totalSamples = pixels.Length;

        double[] sum = new double[4];

        for (int i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];

            // These are LINEAR values in 0..1 from the render target.
            float rL = c.r / 255f;
            float gL = c.g / 255f;
            float bL = c.b / 255f;
            float aL = c.a / 255f;

            // Convert for histogram display as requested (Auto/Linear/sRGB)
            float r = ApplyDisplaySpace(rL);
            float g = ApplyDisplaySpace(gL);
            float b = ApplyDisplaySpace(bL);
            float a = ApplyDisplaySpace(aL);

            Accum(r, histR, 0, ref sum[0]);
            Accum(g, histG, 1, ref sum[1]);
            Accum(b, histB, 2, ref sum[2]);
            Accum(a, histA, 3, ref sum[3]);
        }

        for (int ch = 0; ch < 4; ch++)
            mean[ch] = (float)(sum[ch] / Math.Max(1, totalSamples));

        ComputePercentiles(histR, 0, percentile);
        ComputePercentiles(histG, 1, percentile);
        ComputePercentiles(histB, 2, percentile);
        ComputePercentiles(histA, 3, percentile);
    }

    void Accum(float v, int[] hist, int ch, ref double sum)
    {
        v = Mathf.Clamp01(v);
        int bin = Mathf.Min(bins - 1, Mathf.FloorToInt(v * (bins - 1)));
        hist[bin]++;

        sum += v;
        if (v < min[ch]) min[ch] = v;
        if (v > max[ch]) max[ch] = v;
    }

    void ComputePercentiles(int[] hist, int ch, float p)
    {
        pLo[ch] = PercentileFromHist(hist, p);
        pHi[ch] = PercentileFromHist(hist, 1f - p);
    }

    float PercentileFromHist(int[] hist, float p)
    {
        int target = Mathf.Clamp(Mathf.RoundToInt((totalSamples - 1) * p), 0, Math.Max(0, totalSamples - 1));
        int cum = 0;
        for (int i = 0; i < hist.Length; i++)
        {
            cum += hist[i];
            if (cum >= target)
                return (float)i / (hist.Length - 1);
        }
        return 1f;
    }

    void OnGUI()
    {
        DrawTopBar();

        if (!selectedTex)
        {
            EditorGUILayout.HelpBox("Select a Texture asset (or click Histogram in the Texture Importer header).", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField(selectedTex.name, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Importer sRGB", importerSRGB ? "On" : "Off");
        EditorGUILayout.LabelField("Project Color Space", projectLinear ? "Linear" : "Gamma");
        EditorGUILayout.LabelField("Path", selectedPath ?? "(unknown)");

        int sw = Mathf.Min(sampleSize, selectedTex.width);
        int sh = Mathf.Min(sampleSize, selectedTex.height);
        EditorGUILayout.LabelField("Sample", $"{sw}×{sh}  ({totalSamples:n0} px)");

        var graphRect = GUILayoutUtility.GetRect(10, 220, GUILayout.ExpandWidth(true));
        DrawHistogramGraph(graphRect);

        if (showStats)
        {
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField($"Channel stats: min / mean / max   (p{percentile * 100f:0.#} – p{(1f - percentile) * 100f:0.#})");
                DrawStatsLine("R", 0);
                DrawStatsLine("G", 1);
                DrawStatsLine("B", 2);
                DrawStatsLine("A", 3);
            }
        }
    }

    void DrawTopBar()
    {
        // “Button on top”: visible action row above everything.
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            autoUpdate = GUILayout.Toggle(autoUpdate, "Auto", EditorStyles.toolbarButton, GUILayout.Width(50));

            if (GUILayout.Button("Recompute", EditorStyles.toolbarButton, GUILayout.Width(80)) && selectedTex)
                ComputeHistogram();

            GUILayout.Space(8);

            view = (ChannelView)EditorGUILayout.EnumPopup(view, GUILayout.Width(80));
            displaySpace = (DisplaySpace)EditorGUILayout.EnumPopup(displaySpace, GUILayout.Width(90));

            GUILayout.FlexibleSpace();

            bins = EditorGUILayout.IntField(new GUIContent("Bins"), bins, GUILayout.Width(120));
            sampleSize = EditorGUILayout.IntField(new GUIContent("Sample"), sampleSize, GUILayout.Width(140));
            showStats = GUILayout.Toggle(showStats, "Stats", EditorStyles.toolbarButton, GUILayout.Width(50));
        }
    }

    void DrawStatsLine(string label, int ch)
    {
        EditorGUILayout.LabelField($"{label}: {min[ch]:0.###} / {mean[ch]:0.###} / {max[ch]:0.###}    ({pLo[ch]:0.###} – {pHi[ch]:0.###})");
    }

    void DrawHistogramGraph(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f, 1f));

        Handles.BeginGUI();
        DrawGrid(rect);

        switch (view)
        {
            case ChannelView.R: DrawLine(rect, histR, new Color(1f, 0.25f, 0.25f, 1f)); break;
            case ChannelView.G: DrawLine(rect, histG, new Color(0.25f, 1f, 0.25f, 1f)); break;
            case ChannelView.B: DrawLine(rect, histB, new Color(0.25f, 0.6f, 1f, 1f)); break;
            case ChannelView.A: DrawLine(rect, histA, new Color(1f, 1f, 1f, 1f)); break;
            default:
                DrawLine(rect, histR, new Color(1f, 0.25f, 0.25f, 1f));
                DrawLine(rect, histG, new Color(0.25f, 1f, 0.25f, 1f));
                DrawLine(rect, histB, new Color(0.25f, 0.6f, 1f, 1f));
                DrawLine(rect, histA, new Color(1f, 1f, 1f, 0.75f));
                break;
        }

        Handles.EndGUI();
    }

    void DrawGrid(Rect rect)
    {
        Handles.color = new Color(1f, 1f, 1f, 0.08f);
        for (int i = 1; i < 4; i++)
        {
            float x = Mathf.Lerp(rect.xMin, rect.xMax, i / 4f);
            Handles.DrawLine(new Vector3(x, rect.yMin), new Vector3(x, rect.yMax));

            float y = Mathf.Lerp(rect.yMax, rect.yMin, i / 4f);
            Handles.DrawLine(new Vector3(rect.xMin, y), new Vector3(rect.xMax, y));
        }
    }

    void DrawLine(Rect rect, int[] hist, Color color)
    {
        if (hist == null || hist.Length == 0) return;

        int maxCount = 1;
        for (int i = 0; i < hist.Length; i++) maxCount = Mathf.Max(maxCount, hist[i]);

        var pts = new Vector3[hist.Length];
        for (int i = 0; i < hist.Length; i++)
        {
            float x = Mathf.Lerp(rect.xMin, rect.xMax, i / (float)(hist.Length - 1));
            float yN = hist[i] / (float)maxCount;
            float y = Mathf.Lerp(rect.yMax, rect.yMin, yN);
            pts[i] = new Vector3(x, y, 0);
        }

        Handles.color = color;
        Handles.DrawAAPolyLine(2f, pts);
    }
}

/// <summary>
/// Adds a "Histogram" button to the TOP (header) of the Texture Importer inspector.
/// This avoids replacing Unity's importer UI.
/// </summary>
[InitializeOnLoad]
public static class TextureHistogramImporterHeaderButton
{
    static TextureHistogramImporterHeaderButton()
    {
        Editor.finishedDefaultHeaderGUI += OnFinishedHeaderGUI;
    }

    static void OnFinishedHeaderGUI(Editor editor)
    {
        // Texture Importer inspector target is usually TextureImporter (AssetImporter)
        if (editor == null || editor.targets == null || editor.targets.Length == 0) return;

        // Only show for texture importers
        TextureImporter ti = editor.target as TextureImporter;
        if (ti == null) return;

        // Load the Texture2D asset for this importer
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ti.assetPath);
        if (!tex) return;

        using (new GUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Histogram", GUILayout.Width(90)))
                TextureHistogramWindow.OpenFor(tex);
        }
    }
}
#endif
