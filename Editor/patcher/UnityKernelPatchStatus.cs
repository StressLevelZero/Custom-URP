using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public static class UnityKernelDirectLightingPatchStatus
{
    
    private const string SessionKey = "SLZ.KernelPatched";

    // A string that exists ONLY in the patched file.
    private const string PatchSentinel = "const float falloff = 1 / (distance * distance)";

    public static readonly bool IsPatched;

    static UnityKernelDirectLightingPatchStatus()
    {
        // -1 = not yet set this session
        int cached = SessionState.GetInt(SessionKey, -1);
        if (cached != -1)
        {
            IsPatched = cached == 1;
            return;
        }

        bool result = CheckSentinel(); // or CheckViaGit()
        //Debug.Log("Sentinel: " + result);
        SessionState.SetInt(SessionKey, result ? 1 : 0);
        IsPatched = result;
    }

    private static bool CheckSentinel()
    {
        try
        {
            string kernelPath = Path.Combine(
                EditorApplication.applicationContentsPath,
                "Resources", "OpenCL", "kernels", "directLighting.h");

            if (!File.Exists(kernelPath))
                return false;

            // ReadAllText is fine — kernel files are small.
            return File.ReadAllText(kernelPath).Contains(PatchSentinel);
        }
        catch
        {
            return false;
        }
    }
    
    // Call this if you want to force a recheck (e.g. after applying the patch)
    public static void Invalidate() => SessionState.EraseInt(SessionKey);
}

#if MARROW_INTERNAL || SLZ_RENDERPIPELINE_DEV
// using UnityEditor;
// using UnityEngine;

[InitializeOnLoad]
public static class KernelPatchSplashBoot
{
    private const string DismissedKey = "SLZ.KernelPatchSplash.Dismissed";

    static KernelPatchSplashBoot()
    {
        // Don't nag if already dismissed this session or already patched or if headless
        if (SessionState.GetBool(DismissedKey, false) || Application.isBatchMode )
            return;

        // Delay so the editor is fully initialized before showing the window
        EditorApplication.delayCall += ShowIfNeeded;
    }

    private static void ShowIfNeeded()
    {
        if (UnityKernelDirectLightingPatchStatus.IsPatched)
            return;

        KernelPatchSplashWindow.ShowWindow();
    }

    public static void Dismiss()
    {
        SessionState.SetBool(DismissedKey, true);
    }
}

public class KernelPatchSplashWindow : EditorWindow
{
    private const float Width = 420f;
    private const float Height = 180f;

    public static void ShowWindow()
    {
        var window = GetWindow<KernelPatchSplashWindow>(utility: true, title: "OpenCL Kernel Patch Required");
        window.minSize = new Vector2(Width, Height);
        window.maxSize = new Vector2(Width, Height);
        window.ShowUtility();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(12);

        var iconRect = EditorGUILayout.GetControlRect(false, 40);
        iconRect.width = 40;
        iconRect.x = (position.width - 40) * 0.5f;
        GUI.DrawTexture(iconRect, EditorGUIUtility.IconContent("console.warnicon@2x").image, ScaleMode.ScaleToFit);

        EditorGUILayout.Space(8);

        var style = new GUIStyle(EditorStyles.wordWrappedLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13
        };
        EditorGUILayout.LabelField(
            "The Unity OpenCL direct lighting kernel is unpatched.\nLightmap baking will use broken falloff.",
            style);

        EditorGUILayout.Space(12);

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Remind Me Later", GUILayout.Width(130), GUILayout.Height(28)))
        {
            KernelPatchSplashBoot.Dismiss();
            Close();
        }

        GUILayout.Space(8);

        var defaultBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.3f, 0.7f, 1f, 1f);
        if (GUILayout.Button("Apply Patch…", GUILayout.Width(130), GUILayout.Height(28)))
        {
            KernelPatchSplashBoot.Dismiss();
            Close();
            UnityOpenCLKernelPatchWindow.ShowWindow();
        }
        GUI.backgroundColor = defaultBg;

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }
}
#endif