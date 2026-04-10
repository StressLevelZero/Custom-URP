using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed class UnityOpenCLKernelPatchWindow : EditorWindow
{
    private const string WindowTitle = "OpenCL Kernel Patch";
    private const string MenuPath = "Tools/OpenCL Kernel Patch";

    // Project-relative paths
    private const string FixRelativePath   = "Tools~/raw/directLighting.h";
    private const string PatchRelativePath = "Tools~/Patches/directLighting.portable.patch";

    // Portable path inside the Unity Editor contents folder.
    // This is what the patch headers will target.
    private const string PortablePatchPath = "Resources/OpenCL/kernels/directLighting.h";


    private Vector2 _scroll;

    // Generator status
    private string      _generatorStatus;
    private MessageType _generatorStatusType = MessageType.Info;
    private bool        _generatorReady;

    // Installer status
    private string      _installerStatus;
    private MessageType _installerStatusType = MessageType.Info;
    private bool        _installerReady;

    private bool _checkedStatus;
    private float _pulseStart = 0;
    
    [MenuItem(MenuPath)]
    public static void ShowWindow()
    {
        var window = GetWindow<UnityOpenCLKernelPatchWindow>();
        window.titleContent = new GUIContent(WindowTitle);
        window.minSize = new Vector2(640f, 420f);
        window.RefreshStatus();
        window.Show();
    }

    // Optional command-line entry point:
    // Unity.exe -batchmode -quit -projectPath "X:\YourProject" -executeMethod UnityOpenCLKernelPatchWindow.ApplyPatchCommandLine
    public static void ApplyPatchCommandLine()
    {
        var runner = new PatchRunner();
        var result = runner.ApplyPortablePatch();

        if (!result.Success)
            throw new Exception(result.Message);

        Debug.Log(result.Message);
    }

    private void OnGUI()
    {
        var runner = new PatchRunner();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Portable OpenCL Kernel Patch", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "This tool has two separate jobs:\n" +
            "1) Generate a portable patch file from the fix source.\n" +
            "2) Apply that patch to the active Unity install using an external elevated helper.",
            MessageType.None);

        DrawEnvironmentSection(runner);

        EditorGUILayout.Space(8f);
        DrawPatchGeneratorSection(runner);

        EditorGUILayout.Space(8f);
        DrawPatchInstallerSection(runner);

        EditorGUILayout.Space(8f);
        DrawUtilitySection(runner);

        EditorGUILayout.Space();

        if (!_checkedStatus)
            RefreshStatus();

        EditorGUILayout.Space();
        DrawNotesSection();
    }

    private void DrawEnvironmentSection(PatchRunner runner)
    {
        EditorGUILayout.LabelField("Environment", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope("box"))
        {
            DrawReadOnlyField("Project Root",       runner.ProjectRoot);
            DrawReadOnlyField("Unity Editor EXE",   EditorApplication.applicationPath);
            DrawReadOnlyField("Unity Contents Path", runner.EditorContentsPath);
            DrawReadOnlyField("Kernel Target",       runner.TargetKernelPath);
            DrawReadOnlyField("Fix Source",          runner.FixAbsolutePath);
            DrawReadOnlyField("Portable Patch",      runner.PatchAbsolutePath);
        }
    }

    private void DrawPatchGeneratorSection(PatchRunner runner)
    {
        EditorGUILayout.LabelField("Patch Generator", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.HelpBox(
                "Use this to generate or regenerate the portable patch file from the current fix source.",
                MessageType.None);

            // Status for this section
            EditorGUILayout.HelpBox(
                _generatorStatus ?? "Status not checked yet.",
                _generatorStatusType);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!_generatorReady))
                {
                    if (GUILayout.Button("Generate Portable Patch", GUILayout.Height(30)))
                    {
                        ShowGeneratorResult(runner.GeneratePortablePatch());
                        // Generating a new patch may change installer readiness too.
                        RefreshInstallerStatus(runner);
                    }
                }

                if (GUILayout.Button("Reveal Patch File", GUILayout.Height(30)))
                    runner.RevealPatch();

                if (GUILayout.Button("Reveal Fix File", GUILayout.Height(30)))
                    runner.RevealFix();
            }
        }
    }

    private void DrawPatchInstallerSection(PatchRunner runner)
    {
        EditorGUILayout.LabelField("Patch Installer", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.HelpBox(
                "Applying the patch modifies the Unity Editor installation. " +
                "This uses the external elevated helper, closes Unity, applies the patch, and relaunches the project.",
                MessageType.Warning);

            // Status for this section
            EditorGUILayout.HelpBox(
                _installerStatus ?? "Status not checked yet.",
                _installerStatusType);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh Status", GUILayout.Height(30)))
                    RefreshStatus();



                using (new EditorGUI.DisabledScope(!_installerReady))
                {
                    
                float t = (float)((EditorApplication.timeSinceStartup - _pulseStart) % 2.0 / 2.0);
               // float pulse = Mathf.SmoothStep(0f, 1f, Mathf.PingPong(t * 2f, 1f));
               
               var prevBg  = GUI.backgroundColor;
               var prevColor = GUI.color;
               
               var magicStyle = new GUIStyle(GUI.skin.button);
               if (_installerReady)
               {
                   // Full hue rotation over 3 seconds
                   float hue = (float)(EditorApplication.timeSinceStartup % 3.0 / 3.0);
                   float pulse = (float)(EditorApplication.timeSinceStartup % 1.0); // 0→1 brightness pulse
                   float bright = Mathf.Lerp(0.55f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.PingPong(pulse * 2f, 1f)));

                    magicStyle = new GUIStyle(GUI.skin.button)
                   {
                       fontStyle = FontStyle.Bold,
                       fontSize = 13,
                       normal = { textColor = new Color(0.8f, 0.6f, 1f) }, // lavender text
                       hover = { textColor = new Color(1f, 0.8f, 1f) }, // brighter on hover
                   };
                   
                   GUI.backgroundColor = Color.HSVToRGB(hue, 0.85f, bright);
                   GUI.color           = Color.HSVToRGB(hue, 0.4f,  1f);     // tints the label text too
               }

 

      

                if (GUILayout.Button("Apply Patch Externally (Admin)", magicStyle, GUILayout.Height(34)))
                    ShowInstallerResult(runner.PatchExternallyElevated());

                GUI.backgroundColor = prevBg;
                GUI.color           = prevColor;

                // Keep the editor repainting so the animation runs
                Repaint();
                }
            }
        }
    }

    private void DrawUtilitySection(PatchRunner runner)
    {
        EditorGUILayout.LabelField("Utilities", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope("box"))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reveal Kernel File", GUILayout.Height(24)))
                    runner.RevealTarget();
            }
        }
    }

    private void DrawNotesSection()
    {
        EditorGUILayout.LabelField("Notes", EditorStyles.boldLabel);

        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(110f));
        EditorGUILayout.HelpBox(
            "The generated patch rewrites its headers to:\n" +
            "a/Resources/OpenCL/kernels/directLighting.h\n" +
            "b/Resources/OpenCL/kernels/directLighting.h\n\n" +
            "At apply time, git maps that onto the active Unity editor using:\n" +
            "-p1 --directory=\"<EditorApplication.applicationContentsPath>\"",
            MessageType.None);
        EditorGUILayout.EndScrollView();
    }

    // -------------------------------------------------------------------------
    // Status helpers
    // -------------------------------------------------------------------------

    private void RefreshStatus()
    {
        var runner = new PatchRunner();
        RefreshGeneratorStatus(runner);
        RefreshInstallerStatus(runner);
        _checkedStatus = true;
        Repaint();
    }

    private void RefreshGeneratorStatus(PatchRunner runner)
    {
        var result = runner.GetGeneratorStatus();
        _generatorStatus     = result.Message;
        _generatorStatusType = result.Type;
        _generatorReady      = result.Ready;
    }

    private void RefreshInstallerStatus(PatchRunner runner)
    {
        var result = runner.GetInstallerStatus();
        _installerStatus     = result.Message;
        _installerStatusType = result.Type;
        _installerReady      = result.Ready;
    }

    private void ShowGeneratorResult(OperationResult result)
    {
        _generatorStatus     = result.Message;
        _generatorStatusType = result.Type;
        _generatorReady      = result.Ready;

        if (result.Success)
            Debug.Log("[UnityOpenCLKernelPatchWindow] " + result.Message);
        else
            Debug.LogError("[UnityOpenCLKernelPatchWindow] " + result.Message);

        Repaint();
    }

    private void ShowInstallerResult(OperationResult result)
    {
        _installerStatus     = result.Message;
        _installerStatusType = result.Type;
        _installerReady      = result.Ready;

        if (result.Success)
            Debug.Log("[UnityOpenCLKernelPatchWindow] " + result.Message);
        else
            Debug.LogError("[UnityOpenCLKernelPatchWindow] " + result.Message);

        Repaint();
    }

    private static void DrawReadOnlyField(string label, string value)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel(label);
            EditorGUILayout.SelectableLabel(
                value ?? "<null>",
                EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }
    }

    // =========================================================================
    // PatchRunner
    // =========================================================================

    private sealed class PatchRunner
    {
        public string ProjectRoot       => GetProjectRoot();
        public string EditorContentsPath => EditorApplication.applicationContentsPath;
        public string PackageToolsPath => Path.Combine(GetPackagePath("com.unity.render-pipelines.universal"));
        public string TargetKernelPath  => Path.Combine(EditorContentsPath, "Resources", "OpenCL", "kernels", "directLighting.h");
        public string FixAbsolutePath   => Path.Combine(PackageToolsPath, FixRelativePath);
        public string PatchAbsolutePath => Path.Combine(PackageToolsPath, PatchRelativePath);
        

        private static string GetPackagePath(string packageName)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath($"Packages/{packageName}");
            if (package == null)
                throw new InvalidOperationException($"Package '{packageName}' not found.");
            return package.resolvedPath;
        }
        // ------------------------------------------------------------------
        // Status queries
        // ------------------------------------------------------------------

        /// <summary>
        /// Reports whether the generator has everything it needs (git, fix source, target kernel).
        /// Ready == true means the "Generate Portable Patch" button should be enabled.
        /// </summary>
        public OperationResult GetGeneratorStatus()
        {
            if (!EnsureGitAvailable(out var gitError))
                return Fail("Git is not available.\n\n" + gitError);

            if (!File.Exists(FixAbsolutePath))
                return Fail("Fix source not found:\n" + FixAbsolutePath);

            if (!File.Exists(TargetKernelPath))
                return Fail("Unity kernel target not found:\n" + TargetKernelPath);

            bool patchExists = File.Exists(PatchAbsolutePath);
            return Ready(
                patchExists
                    ? "Ready to regenerate the portable patch."
                    : "Ready to generate the portable patch for the first time.");
        }

        /// <summary>
        /// Reports whether the installer can apply the patch.
        /// Ready == true means the "Apply Patch Externally (Admin)" button should be enabled.
        /// </summary>
        public OperationResult GetInstallerStatus()
        {
            if (!EnsureGitAvailable(out var gitError))
                return Fail("Git is not available.\n\n" + gitError);

            if (!File.Exists(TargetKernelPath))
                return Fail("Unity kernel target not found:\n" + TargetKernelPath);

            if (!File.Exists(PatchAbsolutePath))
                return Warn(
                    "Portable patch not found. Generate it first.\n\n" +
                    "Expected:\n" + PatchAbsolutePath);

            // Already applied?
            var reverseCheck = RunGitApplyCheck(reverse: true);
            if (reverseCheck.ExitCode == 0)
                return AlreadyApplied(
                    "Patch is already applied to the current Unity editor.\n\n" +
                    "Editor contents:\n" + EditorContentsPath);

            // Applies cleanly?
            var check = RunGitApplyCheck(reverse: false);
            if (check.ExitCode == 0)
                return Ready(
                    "Patch is ready to apply to the current Unity editor.\n\n" +
                    "Editor contents:\n" + EditorContentsPath);

            return Warn(
                "Patch is present but does not apply cleanly.\n\n" +
                "Possible causes:\n" +
                "  • Different Unity version\n" +
                "  • File contents already changed\n" +
                "  • Line-ending drift\n\n" +
                "stderr:\n" + check.StdErr);
        }

        // ------------------------------------------------------------------
        // Operations
        // ------------------------------------------------------------------

        public OperationResult GeneratePortablePatch()
        {
            if (!EnsureGitAvailable(out var gitError))
                return Fail("Git is not available.\n\n" + gitError);

            if (!File.Exists(FixAbsolutePath))
                return Fail("Fix source not found:\n" + FixAbsolutePath);

            if (!File.Exists(TargetKernelPath))
                return Fail("Unity kernel target not found:\n" + TargetKernelPath);

            Directory.CreateDirectory(Path.GetDirectoryName(PatchAbsolutePath)!);
            
            string fixContent = File.ReadAllText(FixAbsolutePath);
            string fixNormalized = fixContent.Replace("\r\n", "\n");
            string tempFix = Path.GetTempFileName();
            File.WriteAllText(tempFix, fixNormalized, new UTF8Encoding(false));
 
            // Generate raw patch from actual target -> fix.
            var diff = RunProcess(
                "git",
                $"diff --no-index --ignore-cr-at-eol -- {Q(TargetKernelPath)} {Q(tempFix)}",
                ProjectRoot);

            File.Delete(tempFix);

            // git diff --no-index returns:
            // 0 => no differences
            // 1 => differences found (expected when generating a patch)
            // >1 => actual error
            if (diff.ExitCode > 1)
                return Fail(
                    "git diff --no-index failed.\n\n" +
                    "stdout:\n" + diff.StdOut + "\n\n" +
                    "stderr:\n" + diff.StdErr);

            if (diff.ExitCode == 0 || string.IsNullOrWhiteSpace(diff.StdOut))
                return Warn(
                    "No differences found between the active Unity kernel and the fix file.\n\n" +
                    "No patch was generated.");

            string normalized = NormalizePatchToPortablePaths(diff.StdOut);
            File.WriteAllText(PatchAbsolutePath, normalized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            AssetDatabase.Refresh();

            // Validate newly-written portable patch.
            var check = RunGitApplyCheck(reverse: false);
            if (check.ExitCode != 0)
                return Warn(
                    "Portable patch file was generated, but it did not validate cleanly against the current editor.\n\n" +
                    "Patch:\n" + PatchAbsolutePath + "\n\n" +
                    "stderr:\n" + check.StdErr);

            return Success(
                "Portable patch generated successfully.\n\n" +
                "Patch:\n" + PatchAbsolutePath);
        }

        private static string WinPath(string path) => path.Replace('/', '\\');

        private static string ResolveGitExePath(string workingDirectory)
        {
            var whereGitExe = RunProcess("where", "git.exe", workingDirectory);
            if (whereGitExe.ExitCode == 0)
            {
                foreach (var line in whereGitExe.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = line.Trim();
                    if (File.Exists(candidate)) return WinPath(candidate);
                }
            }

            var whereGit = RunProcess("where", "git", workingDirectory);
            if (whereGit.ExitCode == 0)
            {
                foreach (var line in whereGit.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = line.Trim();
                    if (File.Exists(candidate)) return WinPath(candidate);
                }
            }

            return "git";
        }

        private static string GetStableHelperDir()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SLZ",
                "UnityKernelPatch");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public OperationResult PatchExternallyElevated()
        {
            if (!EnsureGitAvailable(out var gitError))
                return Fail("Git is not available.\n\n" + gitError);

            if (!File.Exists(PatchAbsolutePath))
                return Fail("Portable patch not found:\n" + PatchAbsolutePath);

            if (!File.Exists(TargetKernelPath))
                return Fail("Unity kernel target not found:\n" + TargetKernelPath);

            string gitExe      = ResolveGitExePath(ProjectRoot);
            string helperDir   = GetStableHelperDir();
            string cmdPath     = WinPath(Path.Combine(helperDir, "ApplyUnityKernelPatch.cmd"));
            string relaunchCmd = WinPath(Path.Combine(helperDir, "RelaunchUnityNormally.cmd"));
            string donePath    = WinPath(Path.Combine(helperDir, "ApplyUnityKernelPatch.done.txt"));
            string errPath     = WinPath(Path.Combine(helperDir, "ApplyUnityKernelPatch.error.txt"));
            string backupPath  = WinPath(TargetKernelPath + ".bak");
            int    unityPid    = Process.GetCurrentProcess().Id;
            string unityExe    = WinPath(EditorApplication.applicationPath);
            string projectPath = WinPath(ProjectRoot);
            string editorData  = WinPath(EditorApplication.applicationContentsPath);
            string patchPath   = WinPath(PatchAbsolutePath);
            string targetFile  = WinPath(TargetKernelPath);

            string relaunchCmdText =
$@"@echo on
setlocal
cd /d ""%~dp0""

set ""DONE_FILE={donePath}""
set ""ERROR_FILE={errPath}""
set ""UNITY_EXE={unityExe}""
set ""PROJECT_PATH={projectPath}""

:wait_result
if exist ""%DONE_FILE%"" goto launch
if exist ""%ERROR_FILE%"" goto fail
timeout /t 1 /nobreak >nul
goto wait_result

:launch
start """" /D ""%PROJECT_PATH%"" ""%UNITY_EXE%"" -projectPath ""%PROJECT_PATH%""
exit /b 0

:fail
exit /b 1
";
            File.WriteAllText(relaunchCmd,
                relaunchCmdText.Replace("\r\n", "\n").Replace("\n", Environment.NewLine),
                new UTF8Encoding(false));

            string cmdText =
$@"@echo off
setlocal
cd /d ""%~dp0""

set ""GIT_EXE={gitExe}""
set ""UNITY_PID={unityPid}""
set ""UNITY_EXE={unityExe}""
set ""PROJECT_PATH={projectPath}""
set ""EDITOR_CONTENTS={editorData}""
set ""PATCH_PATH={patchPath}""
set ""TARGET_FILE={targetFile}""
set ""BACKUP_FILE={backupPath}""
set ""DONE_FILE={donePath}""
set ""ERROR_FILE={errPath}""

echo.
echo ==== Elevated Unity patch helper ====
echo GIT_EXE      = [%GIT_EXE%]
echo UNITY_PID    = [%UNITY_PID%]
echo UNITY_EXE    = [%UNITY_EXE%]
echo PROJECT_PATH = [%PROJECT_PATH%]
echo EDITOR_PATH  = [%EDITOR_CONTENTS%]
echo PATCH_PATH   = [%PATCH_PATH%]
echo TARGET_FILE  = [%TARGET_FILE%]
echo BACKUP_FILE  = [%BACKUP_FILE%]
echo.

echo Checking git...
""%GIT_EXE%"" --version
if errorlevel 1 (
    echo ERROR: git could not be launched.
    echo.
    pause
    exit /b 1
)

echo Waiting for Unity (PID %UNITY_PID%) to close...

:wait_unity
tasklist /FI ""PID eq %UNITY_PID%"" 2>NUL | find ""%UNITY_PID%"" >NUL
if not errorlevel 1 (
    timeout /t 1 /nobreak >NUL
    goto wait_unity
)

echo Unity has exited.
echo.

if not exist ""%PATCH_PATH%"" (
    echo ERROR: Patch file not found:
    echo %PATCH_PATH%
    echo.
    pause
    exit /b 1
)

if not exist ""%TARGET_FILE%"" (
    echo ERROR: Target file not found:
    echo %TARGET_FILE%
    echo.
    pause
    exit /b 1
)

echo.
echo Checking patch applies cleanly...
""%GIT_EXE%"" apply --check --unsafe-paths -p1 --directory=""%EDITOR_CONTENTS%"" ""%PATCH_PATH%""
if errorlevel 1 (
    echo.
    echo ERROR: Patch does not apply cleanly. No files were modified.
    echo.
    pause
    exit /b 1
)

echo.
echo Creating backup...
copy /y ""%TARGET_FILE%"" ""%BACKUP_FILE%""
if errorlevel 1 (
    echo.
    echo ERROR: Failed to create backup.
    echo.
    pause
    exit /b 1
)

echo.
echo Applying patch...
""%GIT_EXE%"" apply --unsafe-paths -p1 --directory=""%EDITOR_CONTENTS%"" ""%PATCH_PATH%""
if errorlevel 1 (
    echo.
    echo ERROR: Patch failed. Restoring backup...
    copy /y ""%BACKUP_FILE%"" ""%TARGET_FILE%""
    echo.
    pause
    exit /b 1
)

echo.
echo Patch applied successfully.

echo Normalizing line endings...
powershell -NoProfile -Command ""(Get-Content -Raw '%TARGET_FILE%') -replace \""`r`n\"", \""`n\"" | Set-Content -NoNewline '%TARGET_FILE%'""

echo Signaling watcher to relaunch Unity...
echo done > ""%DONE_FILE%""

echo.
echo Done. Press any key to close this window.
pause > nul
exit /b 0
";
            File.WriteAllText(cmdPath,
                cmdText.Replace("\r\n", "\n").Replace("\n", Environment.NewLine),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // 1. Start the NON-ELEVATED watcher first (inherits current token)
            Process.Start(new ProcessStartInfo
            {
                FileName         = "cmd.exe",
                Arguments        = $"/c call \"{relaunchCmd}\"",
                UseShellExecute  = false,
                WorkingDirectory = helperDir
            });

            // 2. Then start the elevated helper
            var psi = new ProcessStartInfo
            {
                FileName         = "cmd.exe",
                Arguments        = $"/c call \"{cmdPath}\"",
                UseShellExecute  = true,
                Verb             = "runas",
                WorkingDirectory = helperDir
            };

            try
            {
                Process.Start(psi);
                EditorApplication.Exit(0);
                return Success("Launched elevated patch helper. Unity will close and reopen after patching.");
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                return Fail(
                    "The elevated patch helper was not launched.\n\n" +
                    "The user may have canceled the UAC prompt.\n\n" +
                    ex.Message);
            }
        }

        public OperationResult ApplyPortablePatch()
        {
            if (!EnsureGitAvailable(out var gitError))
                return Fail("Git is not available.\n\n" + gitError);

            if (!File.Exists(PatchAbsolutePath))
                return Fail("Portable patch not found:\n" + PatchAbsolutePath);

            if (!File.Exists(TargetKernelPath))
                return Fail("Unity kernel target not found:\n" + TargetKernelPath);

            var reverseCheck = RunGitApplyCheck(reverse: true);
            if (reverseCheck.ExitCode == 0)
                return Success("Patch is already applied.");

            var check = RunGitApplyCheck(reverse: false);
            if (check.ExitCode != 0)
                return Fail(
                    "Patch does not apply cleanly.\n\n" +
                    "stdout:\n" + check.StdOut + "\n\n" +
                    "stderr:\n" + check.StdErr);

            var apply = RunGitApply(reverse: false);
            if (apply.ExitCode != 0)
                return Fail(
                    "git apply failed.\n\n" +
                    "stdout:\n" + apply.StdOut + "\n\n" +
                    "stderr:\n" + apply.StdErr);
            // After successful git apply:
            string content = File.ReadAllText(TargetKernelPath);
            string normalized = content.Replace("\r\n", "\n");
            File.WriteAllBytes(TargetKernelPath, Encoding.UTF8.GetBytes(normalized));
            AssetDatabase.Refresh();
            return Success(
                "Patch applied successfully to the current Unity editor.\n\n" +
                "Target:\n" + TargetKernelPath);
        }

        // ------------------------------------------------------------------
        // Reveal helpers
        // ------------------------------------------------------------------

        public void RevealPatch()
        {
            if (File.Exists(PatchAbsolutePath))
                EditorUtility.RevealInFinder(PatchAbsolutePath);
            else
                EditorUtility.DisplayDialog("Patch File", "Patch file does not exist yet:\n\n" + PatchAbsolutePath, "OK");
        }

        public void RevealFix()
        {
            if (File.Exists(FixAbsolutePath))
                EditorUtility.RevealInFinder(FixAbsolutePath);
            else
                EditorUtility.DisplayDialog("Fix File", "Fix file not found:\n\n" + FixAbsolutePath, "OK");
        }

        public void RevealTarget()
        {
            if (File.Exists(TargetKernelPath))
                EditorUtility.RevealInFinder(TargetKernelPath);
            else
                EditorUtility.DisplayDialog("Kernel File", "Kernel target not found:\n\n" + TargetKernelPath, "OK");
        }

        // ------------------------------------------------------------------
        // git apply wrappers
        // ------------------------------------------------------------------

        private ProcessResult RunGitApplyCheck(bool reverse) => RunGitApplyCore(check: true,  reverse: reverse);
        private ProcessResult RunGitApply(bool reverse)      => RunGitApplyCore(check: false, reverse: reverse);

        private ProcessResult RunGitApplyCore(bool check, bool reverse)
        {
            var sb = new StringBuilder();
            sb.Append("-c core.autocrlf=false ");
            sb.Append("apply --unsafe-paths ");
            if (check)   sb.Append("--check ");
            if (reverse) sb.Append("--reverse ");
            sb.Append("-p1 ");
            sb.Append("--directory=");
            sb.Append(Q(EditorContentsPath));
            sb.Append(' ');
            sb.Append(Q(PatchAbsolutePath));

            // Use temp dir, NOT ProjectRoot — running from the project picks up
            // its .gitattributes (e.g. "* text=auto eol=crlf") and converts
            // every line to CRLF even when core.autocrlf=false.
            return RunProcess("git", sb.ToString(), Path.GetTempPath());
        }

        private bool EnsureGitAvailable(out string error)
        {
            var version = RunProcess("git", "--version", ProjectRoot);
            if (version.ExitCode == 0) { error = ""; return true; }

            error = "git --version failed.\n\nstdout:\n" + version.StdOut + "\n\nstderr:\n" + version.StdErr;
            return false;
        }

        // ------------------------------------------------------------------
        // Patch normalization
        // ------------------------------------------------------------------

        private static string NormalizePatchToPortablePaths(string rawPatchText)
        {
            string portableA = "a/" + PortablePatchPath.Replace("\\", "/");
            string portableB = "b/" + PortablePatchPath.Replace("\\", "/");

            rawPatchText = Regex.Replace(
                rawPatchText,
                @"^diff --git .+$",
                $"diff --git {portableA} {portableB}",
                RegexOptions.Multiline);

            rawPatchText = Regex.Replace(
                rawPatchText,
                @"^--- .+$",
                $"--- {portableA}",
                RegexOptions.Multiline);

            rawPatchText = Regex.Replace(
                rawPatchText,
                @"^\+\+\+ .+$",
                $"+++ {portableB}",
                RegexOptions.Multiline);

            return rawPatchText;
        }

        // ------------------------------------------------------------------
        // Utilities
        // ------------------------------------------------------------------

        private static string GetProjectRoot()
        {
            var assetsDir = new DirectoryInfo(Application.dataPath);
            if (assetsDir.Parent == null)
                throw new InvalidOperationException("Could not determine project root from Application.dataPath.");
            return assetsDir.Parent.FullName;
        }

        private static string Q(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static ProcessResult RunProcess(string fileName, string arguments, string workingDirectory)
        {
            var psi = new ProcessStartInfo
            {
                FileName               = fileName,
                Arguments              = arguments,
                WorkingDirectory       = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding  = Encoding.UTF8
            };

            using var process = new Process();
            process.StartInfo = psi;
            process.Start();

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new ProcessResult(process.ExitCode, stdout, stderr);
        }

        // ------------------------------------------------------------------
        // OperationResult factories
        // ------------------------------------------------------------------

        /// <summary>Success + ready (button enabled, green-ish Info box).</summary>
        private static OperationResult Ready(string message)        => new OperationResult(true,  true,  message, MessageType.Info);
        /// <summary>Success + NOT ready (e.g. already applied — no action needed).</summary>
        private static OperationResult AlreadyApplied(string message) => new OperationResult(true,  false, message, MessageType.Info);
        /// <summary>Operation succeeded (used for action results).</summary>
        private static OperationResult Success(string message)      => new OperationResult(true,  false, message, MessageType.Info);
        private static OperationResult Warn(string message)         => new OperationResult(false, false, message, MessageType.Warning);
        private static OperationResult Fail(string message)         => new OperationResult(false, false, message, MessageType.Error);
    }

    // =========================================================================
    // Value types
    // =========================================================================

    private readonly struct ProcessResult
    {
        public readonly int    ExitCode;
        public readonly string StdOut;
        public readonly string StdErr;

        public ProcessResult(int exitCode, string stdOut, string stdErr)
        {
            ExitCode = exitCode;
            StdOut   = stdOut;
            StdErr   = stdErr;
        }

        public static implicit operator ProcessResult((int exitCode, string stdout, string stderr) v)
            => new ProcessResult(v.exitCode, v.stdout, v.stderr);
    }

    private readonly struct OperationResult
    {
        public readonly bool        Success;
        /// <summary>True when the corresponding action button should be enabled.</summary>
        public readonly bool        Ready;
        public readonly string      Message;
        public readonly MessageType Type;

        public OperationResult(bool success, bool ready, string message, MessageType type)
        {
            Success = success;
            Ready   = ready;
            Message = message;
            Type    = type;
        }
    }
}