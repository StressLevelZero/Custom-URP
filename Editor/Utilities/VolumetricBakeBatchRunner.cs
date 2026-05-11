using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using SLZ.SLZEditorTools;

public static class VolumetricBakeBatchRunner
{
    static double   s_startTime;
    static string   s_outputDirAbs;   // tracks active scene's output dir for file watchdog
    static int      s_expectedAreas;
    static bool     s_done;
    static bool     s_ok;

    // --- supplementary heartbeat ---
    static double s_lastProgressLog  = -1;
    static int    s_lastFileCount    = -1;
    static int    s_lastTextureCount = -1;
    const  double kProgressInterval  = 30.0;

    // --- log-file watchdog ---
    static long   s_lastLogFileSize   = -1;
    static double s_lastLogGrowthTime = -1;
    static double s_lastWatchdogCheck = -1;
    const  double kWatchdogCheckInterval = 5.0;
    const  double kWatchdogWarnAfter     = 60.0;
    const  double kWatchdogErrorAfter    = 300.0;

    // --- hard timeout ---
    const double kTimeoutSeconds = 60 * 60;

    // -----------------------------------------------------------------------
    //  Entry point
    // -----------------------------------------------------------------------
    public static void Run()
    {
        try
        {
            Debug.Log("[VolBake] === VolumetricBakeBatchRunner: START ===");

            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.SetCurrentDirectory(projectRoot);

            Debug.Log($"[VolBake] CWD={Directory.GetCurrentDirectory()}");
            Debug.Log($"[VolBake] BatchMode={Application.isBatchMode}");
            Debug.Log($"[VolBake] GraphicsDeviceType={SystemInfo.graphicsDeviceType}");

            var  args        = Environment.GetCommandLineArgs();
            int  rayChunk    = GetArgInt(args,  "-vb_rayChunk",    4096);
            int  envSamples  = GetArgInt(args,  "-vb_envSamples",  2048);
            int  indirectSamples  = GetArgInt(args,  "-vb_indirectSamples",  2048);
            int  indirectIterations  = GetArgInt(args,  "-vb_indirectIterations",  5);
            int  areaSamples = GetArgInt(args,  "-vb_areaSamples", 1024);
            bool skybox      = GetArgBool(args, "-vb_skybox",      true);

            // -vb_scenes=  is a semicolon-separated list of scene asset paths.
            // Fall back to the legacy single-scene arg for backwards compat.
            string scenesArg  = GetArg(args, "-vb_scenes");
            string singleArg  = GetArg(args, "-vb_scene");

            string[] scenePaths = !string.IsNullOrEmpty(scenesArg)
                ? scenesArg.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                : !string.IsNullOrEmpty(singleArg)
                    ? new[] { singleArg }
                    : Array.Empty<string>();

            if (scenePaths.Length == 0)
                throw new Exception("No scene paths supplied. Pass -vb_scenes=path1;path2 or -vb_scene=path.");

            Debug.Log($"[VolBake] Scenes to load ({scenePaths.Length}):");
            for (int i = 0; i < scenePaths.Length; i++)
                Debug.Log($"[VolBake]   [{i}] {scenePaths[i]}");

            Debug.Log($"[VolBake] Args: rayChunk={rayChunk} envSamples={envSamples} areaSamples={areaSamples} indirectSamples={indirectSamples} indirectIterations={indirectIterations} skybox={skybox}");

            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
                throw new Exception($"DX12 not active. Current API: {SystemInfo.graphicsDeviceType}");

            // Open scenes —————————————————————————————————————————————————————
            // First scene is opened Single (becomes active); the rest Additive.
            for (int i = 0; i < scenePaths.Length; i++)
            {
                string p    = scenePaths[i].Replace('\\', '/');
                var    mode = i == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive;
                var    s    = EditorSceneManager.OpenScene(p, mode);
                Debug.Log($"[VolBake] Opened scene ({mode}): {s.path}");
            }

            Debug.Log($"[VolBake] Volumetric area count = {VolumetricRegisters.volumetricAreas.Count}");
            s_expectedAreas = VolumetricRegisters.volumetricAreas.Count;

            // Output dir is rooted at the active (first) scene — that is where
            // VolumetricBakingV2.CheckDirectoryAndReturnPath() will write files.
            var activeScenePath  = SceneManager.GetActiveScene().path;
            var sceneFolderAsset = activeScenePath.Replace(".unity", "");
            s_outputDirAbs       = Path.Combine(projectRoot, sceneFolderAsset);

            Debug.Log($"[VolBake] Output folder (active scene): {s_outputDirAbs}");

            s_startTime         = EditorApplication.timeSinceStartup;
            s_lastProgressLog   = s_startTime;
            s_lastLogGrowthTime = s_startTime;
            s_lastWatchdogCheck = s_startTime;
            s_lastLogFileSize   = GetLogFileSize(args);

            VolumetricBakingV2.BakeCompleted += OnBakeCompleted;

            foreach (var a in VolumetricRegisters.volumetricAreas)
                a.bakedTexture = null;

            Debug.Log("[VolBake] Calling VolumetricBakingV2.BakeDXR(…)");
            VolumetricBakingV2.BakeDXR(rayChunk, envSamples, areaSamples, indirectSamples,indirectIterations,skybox, null);

            EditorApplication.update += Poll;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    // -----------------------------------------------------------------------
    //  Completion callback
    // -----------------------------------------------------------------------
    static void OnBakeCompleted(bool ok)
    {
        s_done = true;
        s_ok   = ok;
    }

    // -----------------------------------------------------------------------
    //  Poll
    // -----------------------------------------------------------------------
    static void Poll()
    {
        double now     = EditorApplication.timeSinceStartup;
        double elapsed = now - s_startTime;

        if (!s_done && elapsed > kTimeoutSeconds)
        {
            Debug.LogError($"[VolBake] TIMEOUT after {elapsed / 60:F1} min — aborting.");
            Teardown(success: false);
            return;
        }

        if (!s_done)
        {
            if (now - s_lastWatchdogCheck >= kWatchdogCheckInterval)
            {
                s_lastWatchdogCheck = now;
                CheckLogFileGrowth(now, elapsed);
            }

            if (now - s_lastProgressLog >= kProgressInterval)
            {
                s_lastProgressLog = now;
                LogSupplementaryProgress(elapsed);
            }
            return;
        }

        double totalSec = elapsed;
        string duration = totalSec >= 60
            ? $"{(int)(totalSec / 60)}m {(int)(totalSec % 60)}s"
            : $"{totalSec:F1}s";

        if (s_ok)
            Debug.Log($"[VolBake] BakeCompleted(ok=true) after {duration}. Saving assets…");
        else
            Debug.LogError($"[VolBake] BakeCompleted(ok=false) after {duration}. See errors above.");

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        VolumetricBakingV2.AssignTexturesToVolumes();
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveOpenScenes();

        Teardown(success: s_ok);
    }

    // -----------------------------------------------------------------------
    //  Log-file watchdog
    // -----------------------------------------------------------------------
    static void CheckLogFileGrowth(double now, double elapsed)
    {
        long currentSize = GetLogFileSize(Environment.GetCommandLineArgs());
        if (currentSize < 0) return;

        if (currentSize > s_lastLogFileSize)
        {
            s_lastLogFileSize   = currentSize;
            s_lastLogGrowthTime = now;
            return;
        }

        double silentFor = now - s_lastLogGrowthTime;
        if (silentFor >= kWatchdogErrorAfter)
            Debug.LogError($"[VolBake] WATCHDOG — log silent for {silentFor:F0}s (total: {elapsed / 60:F1} min). Unity may have stalled or crashed.");
        else if (silentFor >= kWatchdogWarnAfter)
            Debug.LogWarning($"[VolBake] WATCHDOG — no log activity for {silentFor:F0}s (total: {elapsed / 60:F1} min). Still waiting…");
    }

    // -----------------------------------------------------------------------
    //  Supplementary heartbeat
    // -----------------------------------------------------------------------
    static void LogSupplementaryProgress(double elapsed)
    {
        int texturesDone = s_expectedAreas > 0
            ? VolumetricRegisters.volumetricAreas.Count(a => a != null && a.bakedTexture != null)
            : 0;

        int fileCount = 0;
        if (Directory.Exists(s_outputDirAbs))
            fileCount = Directory.GetFiles(s_outputDirAbs, "Volumemap-*", SearchOption.TopDirectoryOnly).Length;

        if (texturesDone == s_lastTextureCount && fileCount == s_lastFileCount)
            return;

        s_lastTextureCount = texturesDone;
        s_lastFileCount    = fileCount;

        string elapsedStr = elapsed >= 60
            ? $"{(int)(elapsed / 60)}m {(int)(elapsed % 60)}s"
            : $"{elapsed:F0}s";

        Debug.Log($"[VolBake] [{elapsedStr}] Runner check — areas with texture: {texturesDone}/{s_expectedAreas} | output files: {fileCount}");
    }

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------
    static long GetLogFileSize(string[] args)
    {
        string logPath = GetArg(args, "-logFile");
        if (string.IsNullOrEmpty(logPath)) return -1;
        try { var fi = new FileInfo(logPath); return fi.Exists ? fi.Length : -1; }
        catch { return -1; }
    }

    static void Teardown(bool success)
    {
        VolumetricBakingV2.BakeCompleted -= OnBakeCompleted;
        EditorApplication.update         -= Poll;
        EditorApplication.Exit(success ? 0 : 1);
    }

    static string GetArg(string[] args, string key)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == key && i + 1 < args.Length)
                return args[i + 1];
            if (args[i].StartsWith(key + "=", StringComparison.Ordinal))
                return args[i].Substring(key.Length + 1).Trim('"');
        }
        return null;
    }

    static int  GetArgInt(string[] args, string key, int def)   => int.TryParse(GetArg(args, key), out var v) ? v : def;
    static bool GetArgBool(string[] args, string key, bool def)
    {
        var s = GetArg(args, key);
        if (s == null)             return def;
        if (bool.TryParse(s, out var b)) return b;
        if (int.TryParse(s, out var i))  return i != 0;
        return def;
    }
}