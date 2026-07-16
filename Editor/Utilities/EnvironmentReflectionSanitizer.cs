#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using DefaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode;

public static class EnvironmentReflectionSanitizer
{
    // Permanent fallback if the scene doesn't have a baked-lighting folder.
    private const string FallbackBakeFolder = "Assets/Generated/EnvironmentReflections";

    // Temp outputs for Play Mode (deleted on exit).
    private const string TempPlayFolder = "Assets/__Temp/EnvironmentReflections";

    // SessionState keys (survive domain reloads within the same editor session).
    private const string SS_HasTemp = "EnvReflSan_HasTemp";
    private const string SS_OrigMode = "EnvReflSan_OrigMode";
    private const string SS_OrigTexPath = "EnvReflSan_OrigTexPath";
    private const string SS_TempTexPath = "EnvReflSan_TempTexPath";

    [MenuItem("Stress Level Zero/Lighting Tools/Sanitize Environment Reflection (Active Scene, Permanent)")]
    public static void SanitizeActiveScenePermanentMenu()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        if (TrySanitizeScenePermanent(scene, out var log))
            Debug.Log(log);
        else
            Debug.LogWarning(log);
    }

    [MenuItem("Stress Level Zero/Lighting Tools/Sanitize Environment Reflection (All Build Scenes, Permanent)")]
    public static void SanitizeAllBuildScenesPermanentMenu()
    {
        var setup = EditorSceneManager.GetSceneManagerSetup();

        try
        {
            var enabledScenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            int changed = 0;

            foreach (var scenePath in enabledScenes)
            {
                var sc = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                if (TrySanitizeScenePermanent(sc, out var log))
                {
                    changed++;
                    EditorSceneManager.SaveScene(sc);
                    Debug.Log(log);
                }
                else
                {
                    Debug.Log(log);
                }
            }

            Debug.Log($"Environment reflection sanitize: updated {changed} scene(s).");
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }

    /// <summary>
    /// Permanent sanitize: bake a skybox-only reflection probe from the current custom texture
    /// and assign the baked cubemap back as the scene's custom default reflection.
    /// Output path: scene baked-lighting folder if it exists, otherwise fallback folder.
    /// </summary>
    public static bool TrySanitizeScenePermanent(Scene scene, out string message)
    {
        var prevActive = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scene);

        try
        {
            if (RenderSettings.defaultReflectionMode != DefaultReflectionMode.Custom)
            {
                message = $"[{scene.name}] Skipped: Environment Reflections are not Custom.";
                return false;
            }

            var inputTex = GetCustomReflectionTexture();
            if (inputTex == null)
            {
                message = $"[{scene.name}] Skipped: Custom reflection texture is not assigned.";
                return false;
            }

            string outFolder = GetPreferredBakeFolderForSceneOrFallback(scene);
            EnsureFolder(outFolder);

            string outPath = GetStableOutputPath(outFolder, scene, suffix: "");
            int resolution = GetSaneDefaultReflectionResolution();

            if (!TryBakeSanitizedCubemap(inputTex, outPath, resolution, out var baked, out var err))
            {
                message = $"[{scene.name}] Failed: {err}";
                return false;
            }

            SetCustomReflectionTexture(baked);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

            EditorSceneManager.MarkSceneDirty(scene);

            message = $"[{scene.name}] Sanitized: '{inputTex.name}' -> baked '{baked.name}' ({resolution}px) at '{outPath}'.";
            return true;
        }
        finally
        {
            if (prevActive.IsValid() && prevActive.isLoaded)
                SceneManager.SetActiveScene(prevActive);
        }
    }

    /// <summary>
    /// Play-mode sanitize: bakes to Assets/__Temp and assigns at runtime.
    /// Stores original values in SessionState and reverts + deletes temp on exiting play mode.
    /// </summary>
    public static void ApplyTempSanitizeForPlayIfNeeded()
    {
        if (SessionState.GetBool(SS_HasTemp, false))
            return;

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        if (RenderSettings.defaultReflectionMode != DefaultReflectionMode.Custom)
            return;

        var inputTex = GetCustomReflectionTexture();
        if (inputTex == null)
            return;

        // Store originals for explicit revert (covers "no scene reload" playmode settings).
        SessionState.SetInt(SS_OrigMode, (int)RenderSettings.defaultReflectionMode);
        SessionState.SetString(SS_OrigTexPath, AssetDatabase.GetAssetPath(inputTex) ?? "");
        SessionState.SetBool(SS_HasTemp, true);

        EnsureFolder(TempPlayFolder);

        string outPath = GetStableOutputPath(TempPlayFolder, scene, suffix: "_PlayTemp");
        int resolution = GetSaneDefaultReflectionResolution();

        if (!TryBakeSanitizedCubemap(inputTex, outPath, resolution, out var baked, out var err))
        {
            Debug.LogWarning($"[PlayTemp:{scene.name}] Failed to sanitize env reflection: {err}");
            // Clear state so we don't try to revert/delete nonexistent asset.
            SessionState.SetBool(SS_HasTemp, false);
            SessionState.EraseInt(SS_OrigMode);
            SessionState.EraseString(SS_OrigTexPath);
            SessionState.EraseString(SS_TempTexPath);
            return;
        }

        SessionState.SetString(SS_TempTexPath, outPath);

        // Assign for play.
        SetCustomReflectionTexture(baked);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

        Debug.Log($"[PlayTemp:{scene.name}] Using sanitized env reflection '{baked.name}' ({resolution}px).");
    }

    public static void RevertTempSanitizeAfterPlayIfNeeded()
    {
        if (!SessionState.GetBool(SS_HasTemp, false))
            return;

        try
        {
            // Restore original texture if it was an asset (best-effort).
            string origTexPath = SessionState.GetString(SS_OrigTexPath, "");
            Texture origTex = string.IsNullOrEmpty(origTexPath) ? null : AssetDatabase.LoadAssetAtPath<Texture>(origTexPath);

            // Restore mode and texture.
            var mode = (DefaultReflectionMode)SessionState.GetInt(SS_OrigMode, (int)DefaultReflectionMode.Skybox);

            RenderSettings.defaultReflectionMode = mode;

            // If original was missing/unloadable, restore "null" (still safer than keeping temp).
            if (origTex != null)
                SetCustomReflectionTexture(origTex);
            else
                SetCustomReflectionTexture(null);

            // Delete temp asset if it lives where we expect.
            string tempPath = SessionState.GetString(SS_TempTexPath, "");
            if (!string.IsNullOrEmpty(tempPath) && tempPath.Replace('\\', '/').StartsWith(TempPlayFolder, StringComparison.Ordinal))
            {
                AssetDatabase.DeleteAsset(tempPath);
                AssetDatabase.DeleteAsset(TempPlayFolder.Replace("/EnvironmentReflections",""));  // Clean up empty folders 
            }
        }
        finally
        {
            // Clear SessionState.
            SessionState.SetBool(SS_HasTemp, false);
            SessionState.EraseInt(SS_OrigMode);
            SessionState.EraseString(SS_OrigTexPath);
            SessionState.EraseString(SS_TempTexPath);
        }
    }

    // ---------- Core bake helpers ----------

    private static bool TryBakeSanitizedCubemap(Texture inputTex, string outPath, int resolution, out Cubemap baked, out string error)
    {
        baked = null;
        error = null;

        // Stash scene settings we touch during bake.
        var prevSkybox = RenderSettings.skybox;
        var prevMode = RenderSettings.defaultReflectionMode;

        Material tempSkyMat = null;
        GameObject tempGO = null;

        try
        {
            tempSkyMat = CreateSkyboxMaterialForTexture(inputTex);
            if (tempSkyMat == null)
            {
                error = $"Could not create skybox material for '{inputTex.name}' (dim={inputTex.dimension}).";
                return false;
            }

            RenderSettings.skybox = tempSkyMat;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            tempGO = new GameObject("~Temp_EnvReflSanitizerProbe");
            tempGO.hideFlags = HideFlags.HideAndDontSave;

            var probe = tempGO.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.cullingMask = 0;            // render NOTHING but skybox
            probe.hdr = true;                 // EXR output
            probe.resolution = resolution;
            probe.size = Vector3.one * 100000f;
            probe.nearClipPlane = 0.3f;
            probe.farClipPlane = 100000f;

            // Make sure we overwrite in-place consistently.
            // If asset exists, BakeReflectionProbe will overwrite its contents.
            bool ok = Lightmapping.BakeReflectionProbe(probe, outPath);
            if (!ok)
            {
                error = "Lightmapping.BakeReflectionProbe returned false.";
                return false;
            }
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
        finally
        {
            if (tempGO != null) UnityEngine.Object.DestroyImmediate(tempGO);
            if (tempSkyMat != null) UnityEngine.Object.DestroyImmediate(tempSkyMat);

            RenderSettings.skybox = prevSkybox;
            RenderSettings.defaultReflectionMode = prevMode;
        }

        AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
        baked = AssetDatabase.LoadAssetAtPath<Cubemap>(outPath);

        if (baked == null)
        {
            error = $"Bake output was not loadable as Cubemap at '{outPath}'.";
            return false;
        }

        return true;
    }

    private static Material CreateSkyboxMaterialForTexture(Texture inputTex)
    {
        // Supports:
        // - Cubemap input: Skybox/Cubemap (_Tex)
        // - 2D HDRI input: Skybox/Panoramic (_MainTex)
        Shader shader = null;

        if (inputTex != null && inputTex.dimension == TextureDimension.Cube)
            shader = Shader.Find("SLZ/Skybox/SLZ Cubemap");
        else
            shader = Shader.Find("SLZ/Skybox/SLZ Procedural");

        if (shader == null)
            return null;

        var mat = new Material(shader);

        // Safe to set both; only the active shader property matters.
        mat.SetTexture("_Tex", inputTex);
        mat.SetTexture("_MainTex", inputTex);

        return mat;
    }

    private static int GetSaneDefaultReflectionResolution()
    {
        // RenderSettings.defaultReflectionResolution is commonly power-of-two.
        // Clamp to something reasonable so a bad project setting doesn't explode bake time.
        int r = RenderSettings.defaultReflectionResolution;
        r = Mathf.Clamp(r, 16, 2048);
        r = Mathf.ClosestPowerOfTwo(r);
        return r;
    }

    private static string GetStableOutputPath(string folder, Scene scene, string suffix)
    {
        folder = folder.Replace('\\', '/');
        string sceneName = string.IsNullOrEmpty(scene.path) ? scene.name : Path.GetFileNameWithoutExtension(scene.path);
        string file = $"DefaultEnvReflection_{sceneName}{suffix}.exr";
        return $"{folder}/{file}";
    }

    private static string GetPreferredBakeFolderForSceneOrFallback(Scene scene)
    {
        // 1) Folder containing the LightingDataAsset (if it exists in the Project).
        // 2) Otherwise, only if we can find an *existing* scene bake folder near the scene.
        // 3) Otherwise, fallback.
        if (TryGetExistingSceneBakeFolder(scene, out var bakeFolder))
            return bakeFolder;

        return FallbackBakeFolder;
    }

    private static bool TryGetExistingSceneBakeFolder(Scene scene, out string folder)
    {
        folder = null;

        // Try current scene LightingDataAsset folder (best signal when it exists).
        var lda = Lightmapping.lightingDataAsset;
        if (lda != null)
        {
            string ldaPath = AssetDatabase.GetAssetPath(lda);
            if (!string.IsNullOrEmpty(ldaPath) && ldaPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                var dir = Path.GetDirectoryName(ldaPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir) && AssetDatabase.IsValidFolder(dir))
                {
                    folder = dir;
                    return true;
                }
            }
        }

        // Fallback heuristic: common scene bake folder patterns next to the .unity file.
        if (string.IsNullOrEmpty(scene.path))
            return false;

        string sceneDir = Path.GetDirectoryName(scene.path)?.Replace('\\', '/');
        string sceneName = Path.GetFileNameWithoutExtension(scene.path);

        if (string.IsNullOrEmpty(sceneDir) || string.IsNullOrEmpty(sceneName))
            return false;

        string[] candidates =
        {
            $"{sceneDir}/{sceneName}",
            $"{sceneDir}/{sceneName}_LightingData",
            $"{sceneDir}/{sceneName} LightingData",
            $"{sceneDir}/{sceneName}_Lighting",
        };

        foreach (var c in candidates)
        {
            if (!AssetDatabase.IsValidFolder(c))
                continue;

            // Prefer candidates that actually contain a LightingDataAsset.
            var guids = AssetDatabase.FindAssets("t:LightingDataAsset", new[] { c });
            if (guids != null && guids.Length > 0)
            {
                folder = c;
                return true;
            }
        }

        // If none contain LightingDataAsset, still allow the primary "<sceneDir>/<sceneName>" folder if it exists.
        string primary = $"{sceneDir}/{sceneName}";
        if (AssetDatabase.IsValidFolder(primary))
        {
            folder = primary;
            return true;
        }

        return false;
    }

    private static void EnsureFolder(string folderPath)
    {
        folderPath = folderPath.Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        var parts = folderPath.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    private static Texture GetCustomReflectionTexture()
    {
#if true // UNITY_6000_0_OR_NEWER
        return RenderSettings.customReflectionTexture;
#else
        // Older versions: either customReflectionTexture exists, or customReflection is Cubemap.
        // Keep it simple:
        try { return  RenderSettings.customReflection; }
        catch { return null; }
         
#endif
    }

    private static void SetCustomReflectionTexture(Texture tex)
    {
#if true //UNITY_6000_0_OR_NEWER
        RenderSettings.customReflectionTexture = tex;
#else
        RenderSettings.customReflection = tex as Cubemap;
#endif
    }
}

// ---------- Play mode hook (temp sanitize + revert) ----------
[InitializeOnLoad]
internal static class EnvironmentReflectionSanitizer_PlayHook
{
    static EnvironmentReflectionSanitizer_PlayHook()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // Do the temp sanitize once we're in play mode (won't dirty the scene).
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            EnvironmentReflectionSanitizer.ApplyTempSanitizeForPlayIfNeeded();
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            // Revert + delete temp asset.
            EnvironmentReflectionSanitizer.RevertTempSanitizeAfterPlayIfNeeded();
        }
    }
}

// ---------- Build hook (permanent sanitize before building) ----------
internal sealed class EnvironmentReflectionSanitizer_PreBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        // If user has unsaved open scenes, don't silently discard work.
        if (!Application.isBatchMode)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new BuildFailedException("Build cancelled: unsaved scene changes.");
        }

        var setup = EditorSceneManager.GetSceneManagerSetup();

        try
        {
            var enabledScenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            int fixedCount = 0;

            foreach (var scenePath in enabledScenes)
            {
                var sc = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                if (EnvironmentReflectionSanitizer.TrySanitizeScenePermanent(sc, out var log))
                {
                    fixedCount++;
                    EditorSceneManager.SaveScene(sc);
                    Debug.Log($"[PreBuild] {log}");
                }
                else
                {
                    Debug.Log($"[PreBuild] {log}");
                }
            }

            Debug.Log($"[PreBuild] Environment reflection sanitize complete. Updated {fixedCount} scene(s).");
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }
}
#endif
