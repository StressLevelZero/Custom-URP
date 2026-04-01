using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace SLZ.SLZEditorTools
{
    public static class VolBakeLaunchMenu
    {
        const string k_ExecuteMethod   = "VolumetricBakeBatchRunner.Run";
        const string kPrefsRayChunk    = "VolBake.vb_rayChunk";
        const string kPrefsEnvSamples  = "VolBake.vb_envSamples";
        const string kPrefsAreaSamples = "VolBake.vb_areaSamples";
        const string kPrefsSkybox      = "VolBake.vb_skybox";

        // [MenuItem("Stress Level Zero/Volumetrics/Bake Active Scene (DX12 Batch)")]
        // public static void Menu_BakeActiveScene() => VolumetricBake_BatchAndRelaunch();

        [MenuItem("Stress Level Zero/Volumetrics/Bake Volumes (DX12 Batch + Relaunch)", priority = 000)]
        public static void VolumetricBake_BatchAndRelaunch()
        {
            // ── Collect all currently loaded, saved scenes ────────────────────
            var scenePaths = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.IsValid() && s.isLoaded && !string.IsNullOrEmpty(s.path))
                    scenePaths.Add(s.path.Replace('\\', '/'));
            }

            if (scenePaths.Count == 0)
            {
                EditorUtility.DisplayDialog("Volumetric Bake",
                    "No saved scenes are currently loaded. Please save your scene(s) first.", "OK");
                return;
            }

            // Active scene is first so the runner opens it with Single mode,
            // making it the active scene in batch mode too.
            string activePath = SceneManager.GetActiveScene().path.Replace('\\', '/');
            if (scenePaths.Remove(activePath))
                scenePaths.Insert(0, activePath);

            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            string unityExe    = EditorApplication.applicationPath;
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            int    rayChunk    = EditorPrefs.GetInt(kPrefsRayChunk,    4096);
            int    envSamples  = EditorPrefs.GetInt(kPrefsEnvSamples,  2048);
            int    areaSamples = EditorPrefs.GetInt(kPrefsAreaSamples, 256);
            bool   skybox      = EditorPrefs.GetBool(kPrefsSkybox,     true);
            string skyboxVal   = skybox ? "1" : "0";

            // Semicolon-joined for the -vb_scenes command-line arg
            string scenesArg   = string.Join(";", scenePaths);

            // Display name: active scene name + count of additives
            string activeNameDisplay = Path.GetFileNameWithoutExtension(activePath);
            string scenesSummary     = scenePaths.Count == 1
                ? activeNameDisplay
                : $"{activeNameDisplay} (+{scenePaths.Count - 1} additive)";

            string batchLog = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SLZ", "VolBake", "VolBake_DX12.log");
            string cmdPath     = Path.Combine(Path.GetTempPath(), "VolBake_RunAndRelaunch.cmd");
            string ps1MainPath = Path.Combine(Path.GetTempPath(), "VolBake_Main.ps1");
            string ps1TailPath = Path.Combine(Path.GetTempPath(), "VolBake_TailLog.ps1");
            
            // Ensure the directory exists before Unity tries to write to it
            Directory.CreateDirectory(Path.GetDirectoryName(batchLog));

            File.WriteAllText(ps1MainPath,
                BuildPS1Main(unityExe, projectRoot, batchLog, scenePaths, scenesSummary,
                             scenesArg, rayChunk, envSamples, areaSamples, skyboxVal),
                Encoding.UTF8);

            File.WriteAllText(ps1TailPath, BuildPS1Tail(batchLog), Encoding.UTF8);

            File.WriteAllText(cmdPath,
                BuildCmd(projectRoot, unityExe, batchLog, ps1MainPath, ps1TailPath),
                Encoding.ASCII);

            Process.Start(new ProcessStartInfo
            {
                FileName         = "cmd.exe",
                Arguments        = "/C " + Q(cmdPath),
                UseShellExecute  = true,
                WorkingDirectory = projectRoot
            });

            EditorApplication.Exit(0);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PS1 MAIN
        // ─────────────────────────────────────────────────────────────────────
        static string BuildPS1Main(
            string unityExe, string projectRoot, string logPath,
            List<string> scenePaths, string scenesSummary, string scenesArg,
            int rayChunk, int envSamples, int areaSamples, string skyboxVal)
        {
            var b = new StringBuilder();

            // ── Variables ─────────────────────────────────────────────────────
            b.AppendLine("# VolBake_Main.ps1 — auto-generated, do not edit");
            b.AppendLine("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8");
            b.AppendLine("chcp 65001 | Out-Null");
            b.AppendLine($"$logPath      = '{EscPS1(logPath)}'");
            b.AppendLine($"$unityExe     = '{EscPS1(unityExe)}'");
            b.AppendLine($"$projectPath  = '{EscPS1(projectRoot)}'");
            b.AppendLine($"$scenesArg    = '{EscPS1(scenesArg)}'");
            b.AppendLine($"$scenesSummary= '{EscPS1(scenesSummary)}'");
            b.AppendLine($"$rayChunk     = {rayChunk}");
            b.AppendLine($"$envSamples   = {envSamples}");
            b.AppendLine($"$areaSamples  = {areaSamples}");
            b.AppendLine($"$useSkybox    = {skyboxVal}");

            // ── Notification provider config (baked in from EditorPrefs) ──────
            b.AppendLine($"$discordEnabled    = {(VolBakeNotifier.DiscordEnabled    ? "$true" : "$false")}");
            b.AppendLine($"$discordWebhookUrl = '{EscPS1(VolBakeNotifier.DiscordWebhookUrl)}'");
            b.AppendLine($"$discordMention    = '{EscPS1(VolBakeNotifier.DiscordMention)}'");
            b.AppendLine($"$ntfyEnabled       = {(VolBakeNotifier.NtfyEnabled       ? "$true" : "$false")}");
            b.AppendLine($"$ntfyTopic         = '{EscPS1(VolBakeNotifier.NtfyTopic)}'");
            b.AppendLine( "");

            // Emit the scene list as a PS1 array for the info block
            b.AppendLine( "$sceneList    = @(");
            for (int si = 0; si < scenePaths.Count; si++)
            {
                string comma = si < scenePaths.Count - 1 ? "," : "";
                b.AppendLine($"    '{EscPS1(scenePaths[si])}'{comma}");
            }
            b.AppendLine( ")");

            b.AppendLine( "$unityArgs = @(");
            b.AppendLine( "    '-batchmode',");
            b.AppendLine( "    '-projectPath', $projectPath,");
            b.AppendLine( "    '-force-d3d12',");
            b.AppendLine($"    '-executeMethod', '{k_ExecuteMethod}',");
            b.AppendLine( "    '-logFile', $logPath,");
            b.AppendLine( "    \"-vb_scenes=$scenesArg\",");
            b.AppendLine( "    \"-vb_rayChunk=$rayChunk\",");
            b.AppendLine( "    \"-vb_envSamples=$envSamples\",");
            b.AppendLine( "    \"-vb_areaSamples=$areaSamples\",");
            b.AppendLine( "    \"-vb_skybox=$useSkybox\"");
            b.AppendLine( ")");
            b.AppendLine( "$startTime     = Get-Date");
            b.AppendLine( "$lastPrintTime = Get-Date");
            b.AppendLine( "$lastAreaInfo  = ''");
            b.AppendLine( "$lastPos       = 0");
            b.AppendLine( "$spinner       = @('◐','◓','◑','◒')");
            b.AppendLine( "$frame         = 0");
            b.AppendLine( "");

            // ── Win32 P/Invoke for Z-ordering ─────────────────────────────────
            b.AppendLine( "Add-Type -TypeDefinition @'");
            b.AppendLine( "using System;");
            b.AppendLine( "using System.Runtime.InteropServices;");
            b.AppendLine( "public class VolBakeWin32 {");
            b.AppendLine( "    [DllImport(\"user32.dll\")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);");
            b.AppendLine( "    [DllImport(\"user32.dll\")] public static extern bool SetForegroundWindow(IntPtr hWnd);");
            b.AppendLine( "    [DllImport(\"kernel32.dll\")] public static extern IntPtr GetConsoleWindow();");
            b.AppendLine( "}");
            b.AppendLine( "'@");
            b.AppendLine( "$ownHwnd = [VolBakeWin32]::GetConsoleWindow()");
            b.AppendLine( "");

            b.AppendLine( "$host.UI.RawUI.WindowTitle = 'VolBake | Starting...'");
            b.AppendLine( "");

                  // ── ASCII art ─────────────────────────────────────────────────────
            b.AppendLine( "$art = @(");
            b.AppendLine( "    ' ██╗   ██╗ ██████╗ ██╗     ██████╗  █████╗ ██╗  ██╗███████╗',");
            b.AppendLine( "    ' ██║   ██║██╔═══██╗██║     ██╔══██╗██╔══██╗██║ ██╔╝██╔════╝',");
            b.AppendLine( "    ' ██║   ██║██║   ██║██║     ██████╔╝███████║█████╔╝ █████╗  ',");
            b.AppendLine( "    ' ╚██╗ ██╔╝██║   ██║██║     ██╔══██╗██╔══██║██╔═██╗ ██╔══╝  ',");
            b.AppendLine( "    '  ╚████╔╝ ╚██████╔╝███████╗██████╔╝██║  ██║██║  ██╗███████╗',");
            b.AppendLine( "    '   ╚═══╝   ╚═════╝ ╚══════╝╚═════╝ ╚═╝  ╚═╝╚═╝  ╚═╝╚══════╝'");
            b.AppendLine( ")");
            b.AppendLine( "$artW  = ($art | ForEach-Object { $_.Length } | Measure-Object -Maximum).Maximum");
            b.AppendLine( "$pad   = 3");
            b.AppendLine( "$inner = $artW + ($pad * 2)");
            b.AppendLine( "$boxTop   = '  ╔' + ('═' * $inner) + '╗'");
            b.AppendLine( "$boxMid   = '  ╠' + ('═' * $inner) + '╣'");
            b.AppendLine( "$boxBot   = '  ╚' + ('═' * $inner) + '╝'");
            b.AppendLine( "$boxBlank = '  ║' + (' ' * $inner) + '║'");
            b.AppendLine( "$sepLine  = '  ' + ('━' * ($inner + 2))");
            b.AppendLine( "");
            b.AppendLine( "Write-Host ''");
            b.AppendLine( "Write-Host $boxTop   -ForegroundColor DarkCyan");
            b.AppendLine( "Write-Host $boxBlank -ForegroundColor DarkCyan");
            b.AppendLine( "$artColors = @('DarkCyan','DarkCyan','Cyan','Cyan','Cyan','DarkCyan')");
            b.AppendLine( "$rightBorder = 68   # fixed column index for the right ║");
            b.AppendLine( "for ($i = 0; $i -lt $art.Count; $i++) {");
            b.AppendLine( "    Write-Host '  ║   ' -ForegroundColor DarkCyan -NoNewline");
            b.AppendLine( "    Write-Host $art[$i] -ForegroundColor $artColors[$i] -NoNewline");
            b.AppendLine( "    $pad = [Math]::Max(0, $rightBorder - [Console]::CursorLeft)");
            b.AppendLine( "    Write-Host (' ' * $pad + '║') -ForegroundColor DarkCyan");
            b.AppendLine( "}");
            b.AppendLine( "Write-Host $boxBlank -ForegroundColor DarkCyan");
            b.AppendLine( "Write-Host $boxMid   -ForegroundColor DarkCyan");
            b.AppendLine( "$tag  = '  ✦  DX12 RAY TRACED  ·  BATCH MODE  ·  STRESS LEVEL ZERO  ✦'");
            b.AppendLine( "$tpad = ' ' * [Math]::Max(0, ($inner - $tag.Length))");
            b.AppendLine( "Write-Host ('  ║' + $tag + $tpad + '║') -ForegroundColor DarkGray");
            b.AppendLine( "Write-Host $boxBot -ForegroundColor DarkCyan");
            b.AppendLine( "Write-Host ''");
    
            // // Tagline — pure ASCII/box chars, fixed width, no measurement needed
            // b.AppendLine( "Write-Host  '  ║  ✦  RAY TRACED VOLUMETRICS  ·  DX12  ·  STRESS LEVEL ZERO  ✦  ║' -ForegroundColor Magenta");
            // b.AppendLine( "Write-Host ('  ╚' + ('═' * 68) + '╝') -ForegroundColor DarkMagenta");
            // b.AppendLine( "Write-Host ''");

            // ── Scene list info block ─────────────────────────────────────────
            b.AppendLine( "Write-Host $sepLine -ForegroundColor DarkGray");
            b.AppendLine($"Write-Host '  Scenes ({scenePaths.Count}):' -ForegroundColor Gray");
            // Active scene (first) gets a different marker
            b.AppendLine( "for ($i = 0; $i -lt $sceneList.Count; $i++) {");
            b.AppendLine( "    $marker = if ($i -eq 0) { '  ► [active] ' } else { '    [additive]' }");
            b.AppendLine( "    Write-Host ($marker + ' ' + $sceneList[$i]) -ForegroundColor Gray");
            b.AppendLine( "}");
            b.AppendLine( "Write-Host ''");
            b.AppendLine( "Write-Host \"  Settings : rayChunk=$rayChunk  envSamples=$envSamples  areaSamples=$areaSamples  skybox=$useSkybox\" -ForegroundColor Gray");
            b.AppendLine( "Write-Host $sepLine -ForegroundColor DarkGray");
            b.AppendLine( "Write-Host ''");

            // ── Launch Unity ──────────────────────────────────────────────────
            b.AppendLine( "$proc = Start-Process -FilePath $unityExe -ArgumentList $unityArgs -PassThru -WindowStyle Hidden");
            b.AppendLine( "if (-not $proc) { Write-Host 'ERROR: Failed to launch Unity.' -ForegroundColor Red; exit 1 }");
            b.AppendLine( "Write-Host \"  Unity launched  ·  PID $($proc.Id)\" -ForegroundColor DarkGray");
            b.AppendLine( "Write-Host ''");

            // ── Z-order: push full-log window behind ──────────────────────────
            b.AppendLine( "Start-Sleep -Milliseconds 1500");
            b.AppendLine( "$HWND_BOTTOM = [IntPtr]::new(1)");
            b.AppendLine( "$SWP_FLAGS   = 0x0003");
            b.AppendLine( "$tailProc    = Get-Process | Where-Object { $_.MainWindowTitle -like '*Full Log*' } | Select-Object -First 1");
            b.AppendLine( "if ($tailProc -and $tailProc.MainWindowHandle -ne [IntPtr]::Zero) {");
            b.AppendLine( "    [VolBakeWin32]::SetWindowPos($tailProc.MainWindowHandle, $HWND_BOTTOM, 0, 0, 0, 0, $SWP_FLAGS) | Out-Null");
            b.AppendLine( "}");
            b.AppendLine( "if ($ownHwnd -ne [IntPtr]::Zero) {");
            b.AppendLine( "    [VolBakeWin32]::SetForegroundWindow($ownHwnd) | Out-Null");
            b.AppendLine( "}");
            b.AppendLine( "");

            // ── Live feed header ──────────────────────────────────────────────
            b.AppendLine( "Write-Host $sepLine -ForegroundColor DarkGray");
            b.AppendLine( "Write-Host '  ◈  LIVE FEED  —  [VolBake] lines only  ·  full log in background window' -ForegroundColor DarkGray");
            b.AppendLine( "Write-Host $sepLine -ForegroundColor DarkGray");
            b.AppendLine( "Write-Host ''");

            // ── Read-VolBakeLines ─────────────────────────────────────────────
            b.AppendLine( "function Read-VolBakeLines {");
            b.AppendLine( "    param([string]$Path, [ref]$Pos)");
            b.AppendLine( "    if (-not (Test-Path $Path)) { return }");
            b.AppendLine( "    try {");
            b.AppendLine( "        $fs = [IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')");
            b.AppendLine( "        $fs.Seek($Pos.Value, 'Begin') | Out-Null");
            b.AppendLine( "        $sr = [IO.StreamReader]::new($fs)");
            b.AppendLine( "        while (-not $sr.EndOfStream) {");
            b.AppendLine( "            $line = $sr.ReadLine()");
            b.AppendLine( "            if ($line -match '\\[VolBake\\]') {");
            b.AppendLine( "                $script:lastPrintTime = Get-Date");
            b.AppendLine( "                if ($line -match 'Area (\\d+)/(\\d+).*Overall ([\\d.]+)%') {");
            b.AppendLine( "                    $script:lastAreaInfo = \"A$($Matches[1])/$($Matches[2])  $($Matches[3])%\"");
            b.AppendLine( "                }");
            b.AppendLine( "                if      ($line -match 'COMPLETE|succeeded|SUCCESS') { Write-Host $line -ForegroundColor Green  }");
            b.AppendLine( "                elseif  ($line -match 'FAILED|TIMEOUT')             { Write-Host $line -ForegroundColor Red    }");
            b.AppendLine( "                elseif  ($line -match 'WATCHDOG|WARNING')           { Write-Host $line -ForegroundColor Yellow }");
            b.AppendLine( "                else                                                 { Write-Host $line -ForegroundColor Cyan   }");
            b.AppendLine( "            }");
            b.AppendLine( "        }");
            b.AppendLine( "        $Pos.Value = $fs.Position");
            b.AppendLine( "        $sr.Close(); $fs.Close()");
            b.AppendLine( "    } catch { }");
            b.AppendLine( "}");
            b.AppendLine( "");

            // ── Poll loop ─────────────────────────────────────────────────────
            b.AppendLine( "while (-not $proc.HasExited) {");
            b.AppendLine( "    Read-VolBakeLines -Path $logPath -Pos ([ref]$lastPos)");
            b.AppendLine( "    $elapsed    = (Get-Date) - $startTime");
            b.AppendLine( "    $elapsedStr = '{0:hh\\:mm\\:ss}' -f $elapsed");
            b.AppendLine( "    $spin       = $spinner[$frame % 4]; $frame++");
            b.AppendLine( "    $info       = if ($lastAreaInfo) { \"  $lastAreaInfo\" } else { '' }");
            b.AppendLine( "    $host.UI.RawUI.WindowTitle = \"VolBake $spin  [$elapsedStr]  $scenesSummary$info\"");
            b.AppendLine( "    $quietSec = ((Get-Date) - $lastPrintTime).TotalSeconds");
            b.AppendLine( "    if (-not (Test-Path $logPath) -and $quietSec -ge 20) {");
            b.AppendLine( "        Write-Host \"  [$elapsedStr] waiting for Unity log to appear...\" -ForegroundColor DarkGray");
            b.AppendLine( "        $lastPrintTime = Get-Date");
            b.AppendLine( "    } elseif ((Test-Path $logPath) -and $quietSec -ge 60) {");
            b.AppendLine( "        Write-Host \"  [$elapsedStr] still baking — quiet for $([int]$quietSec)s...\" -ForegroundColor DarkGray");
            b.AppendLine( "        $lastPrintTime = Get-Date");
            b.AppendLine( "    }");
            b.AppendLine( "    Start-Sleep -Milliseconds 500");
            b.AppendLine( "}");

            // Drain final lines
            b.AppendLine( "Start-Sleep -Milliseconds 500");
            b.AppendLine( "Read-VolBakeLines -Path $logPath -Pos ([ref]$lastPos)");
            b.AppendLine( "");
            b.AppendLine( "$code     = $proc.ExitCode");
            b.AppendLine( "$elapsed  = (Get-Date) - $startTime");
            b.AppendLine( "$duration = '{0:hh\\:mm\\:ss}' -f $elapsed");
            b.AppendLine( "");
            b.AppendLine( "Write-Host ''");
            b.AppendLine( "Write-Host $sepLine -ForegroundColor DarkGray");
            b.AppendLine( "if ($code -eq 0) {");
            b.AppendLine( "    Write-Host \"  ✓  BAKE COMPLETE  —  Duration: $duration\" -ForegroundColor Green");
            b.AppendLine( "    Write-Host ''");
            b.AppendLine( "    Write-Host '  Relaunching Unity editor...' -ForegroundColor DarkGray");
            b.AppendLine( "    $host.UI.RawUI.WindowTitle = \"VolBake  ✓  Done  [$duration]\"");
            b.AppendLine( "} else {");
            b.AppendLine( "    Write-Host \"  ✗  BAKE FAILED  —  Exit code: $code  —  Duration: $duration\" -ForegroundColor Red");
            b.AppendLine( "    Write-Host \"     Log: $logPath\" -ForegroundColor DarkRed");
            b.AppendLine( "    $host.UI.RawUI.WindowTitle = \"VolBake  ✗  Failed (exit $code)\"");
            b.AppendLine( "}");
            b.AppendLine( "Write-Host $sepLine -ForegroundColor DarkGray");
            b.AppendLine( "");
            
             
            // ── Auto-close full log window on success ─────────────────────────
            // Give the tail window a moment to flush its last lines, then close
            // it gracefully so the user isn't left with a stale window.
            // On failure we leave it open so they can scroll back through errors.
            b.AppendLine( "if ($code -eq 0) {");
            b.AppendLine( "    Start-Sleep -Milliseconds 1500");
            b.AppendLine( "    $tailProc = Get-Process | Where-Object { $_.MainWindowTitle -like '*Full Log*' } | Select-Object -First 1");
            b.AppendLine( "    if ($tailProc) {");
            b.AppendLine( "        $tailProc.CloseMainWindow() | Out-Null");
            b.AppendLine( "        Write-Host '  Full log window closed.' -ForegroundColor DarkGray");
            b.AppendLine( "    }");
            b.AppendLine( "}");
            b.AppendLine( "");

            // ── Notification providers ────────────────────────────────────────
            // Each provider is a self-contained function. Add new ones below
            // Send-DiscordNotification and call them from Send-VolBakeNotification.
            b.AppendLine( "# ── Notification providers ─────────────────────────────────────────");

            // ── Discord ───────────────────────────────────────────────────────
            // Uses a webhook URL. For a personal/DM-style notification, create
            // a private Discord server with only yourself and add a webhook to
            // any channel — messages there push to your phone just like DMs.
            b.AppendLine( "function Send-DiscordNotification {");
            b.AppendLine( "    param([string]$WebhookUrl, [string]$Mention, [bool]$Success, [string]$Scene, [string]$Duration)");
            b.AppendLine( "    if ([string]::IsNullOrEmpty($WebhookUrl)) { return }");
            b.AppendLine( "    try {");
            b.AppendLine( "        $icon   = if ($Success) { ':white_check_mark:' } else { ':x:' }");
            b.AppendLine( "        $status = if ($Success) { 'COMPLETE' } else { 'FAILED' }");
            b.AppendLine( "        $color  = if ($Success) { 3066993 } else { 15158332 }");
            b.AppendLine( "        $prefix = if ($Mention) { $Mention + ' ' } else { '' }");
            b.AppendLine( "        $body   = @{");
            b.AppendLine( "            content = ($prefix + $icon + ' VolBake **' + $status + '**')");
            b.AppendLine( "            embeds  = @(@{");
            b.AppendLine( "                color     = $color");
            b.AppendLine( "                fields    = @(");
            b.AppendLine( "                    @{ name = 'Scene';    value = $Scene;    inline = $true }");
            b.AppendLine( "                    @{ name = 'Duration'; value = $Duration; inline = $true }");
            b.AppendLine( "                    @{ name = 'Machine';  value = $env:COMPUTERNAME; inline = $true }");
            b.AppendLine( "                )");
            b.AppendLine( "                timestamp = (Get-Date).ToUniversalTime().ToString('o')");
            b.AppendLine( "            })");
            b.AppendLine( "        } | ConvertTo-Json -Depth 6");
            b.AppendLine( "        Invoke-RestMethod -Uri $WebhookUrl -Method Post -Body $body -ContentType 'application/json' | Out-Null");
            b.AppendLine( "        Write-Host '  Notification sent → Discord' -ForegroundColor DarkGray");
            b.AppendLine( "    } catch {");
            b.AppendLine( "        Write-Host ('  Discord notification failed: ' + $_.Exception.Message) -ForegroundColor DarkYellow");
            b.AppendLine( "    }");
            b.AppendLine( "}");
            b.AppendLine( "");

            // ── ntfy.sh ───────────────────────────────────────────────────────
            // Free, open-source push to Android/iOS with zero account setup.
            // Just pick a unique topic name and subscribe to it in the ntfy app.
            b.AppendLine( "function Send-NtfyNotification {");
            b.AppendLine( "    param([string]$Topic, [bool]$Success, [string]$Scene, [string]$Duration)");
            b.AppendLine( "    if ([string]::IsNullOrEmpty($Topic)) { return }");
            b.AppendLine( "    try {");
            b.AppendLine( "        $title    = if ($Success) { 'VolBake Complete' } else { 'VolBake Failed' }");
            b.AppendLine( "        $message  = $Scene + ' — ' + $Duration");
            b.AppendLine( "        $priority = if ($Success) { 'default' } else { 'high' }");
            b.AppendLine( "        $tags     = if ($Success) { 'white_check_mark' } else { 'x' }");
            b.AppendLine( "        $headers  = @{");
            b.AppendLine( "            'Title'    = $title");
            b.AppendLine( "            'Priority' = $priority");
            b.AppendLine( "            'Tags'     = $tags");
            b.AppendLine( "        }");
            b.AppendLine( "        Invoke-RestMethod -Uri ('https://ntfy.sh/' + $Topic) -Method Post -Body $message -Headers $headers | Out-Null");
            b.AppendLine( "        Write-Host '  Notification sent → ntfy.sh' -ForegroundColor DarkGray");
            b.AppendLine( "    } catch {");
            b.AppendLine( "        Write-Host ('  ntfy notification failed: ' + $_.Exception.Message) -ForegroundColor DarkYellow");
            b.AppendLine( "    }");
            b.AppendLine( "}");
            b.AppendLine( "");

            // ── Central dispatcher ────────────────────────────────────────────
            b.AppendLine( "function Send-VolBakeNotification {");
            b.AppendLine( "    param([bool]$Success, [string]$Duration)");
            b.AppendLine( "    $anyEnabled = $discordEnabled -or $ntfyEnabled");
            b.AppendLine( "    if (-not $anyEnabled) { return }");
            b.AppendLine( "    Write-Host ''");
            b.AppendLine( "    Write-Host '  Sending notifications...' -ForegroundColor DarkGray");
            b.AppendLine( "    if ($discordEnabled) {");
            b.AppendLine( "        Send-DiscordNotification -WebhookUrl $discordWebhookUrl `");
            b.AppendLine( "                                 -Mention    $discordMention `");
            b.AppendLine( "                                 -Success    $Success `");
            b.AppendLine( "                                 -Scene      $scenesSummary `");
            b.AppendLine( "                                 -Duration   $Duration");
            b.AppendLine( "    }");
            b.AppendLine( "    if ($ntfyEnabled) {");
            b.AppendLine( "        Send-NtfyNotification   -Topic      $ntfyTopic `");
            b.AppendLine( "                                -Success    $Success `");
            b.AppendLine( "                                -Scene      $scenesSummary `");
            b.AppendLine( "                                -Duration   $Duration");
            b.AppendLine( "    }");
            b.AppendLine( "    # Add future providers here:");
            b.AppendLine( "    # if ($slackEnabled) { Send-SlackNotification ... }");
            b.AppendLine( "}");
            b.AppendLine( "");
            b.AppendLine( "Send-VolBakeNotification -Success ($code -eq 0) -Duration $duration");
            b.AppendLine( "exit $code");

            return b.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PS1 TAIL
        // ─────────────────────────────────────────────────────────────────────
        static string BuildPS1Tail(string logPath)
        {
            var b = new StringBuilder();
            b.AppendLine("# VolBake_TailLog.ps1 — auto-generated, do not edit");
            b.AppendLine("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8");
            b.AppendLine("chcp 65001 | Out-Null");
            b.AppendLine($"$logPath = '{EscPS1(logPath)}'");
            b.AppendLine( "$host.UI.RawUI.WindowTitle = 'VolBake - Full Log'");
            b.AppendLine( "Write-Host '  ─── VolBake Full Log ───────────────────────────────────────────' -ForegroundColor DarkGray");
            b.AppendLine( "Write-Host \"  Log: $logPath\" -ForegroundColor DarkGray");
            b.AppendLine( "Write-Host '  ────────────────────────────────────────────────────────────────' -ForegroundColor DarkGray");
            b.AppendLine( "Write-Host ''");
            b.AppendLine( "$waited = 0");
            b.AppendLine( "while (-not (Test-Path $logPath) -and $waited -lt 30) {");
            b.AppendLine( "    Write-Host '  Waiting for Unity to start...' -ForegroundColor DarkGray");
            b.AppendLine( "    Start-Sleep 1; $waited++");
            b.AppendLine( "}");
            b.AppendLine( "if (-not (Test-Path $logPath)) {");
            b.AppendLine( "    Write-Host 'Log file never appeared.' -ForegroundColor Red");
            b.AppendLine( "    Read-Host 'Press Enter to close'; exit 1");
            b.AppendLine( "}");
            b.AppendLine( "Get-Content $logPath -Wait -Tail 0 | ForEach-Object {");
            b.AppendLine( "    if      ($_ -match '\\[VolBake\\].*COMPLETE|succeeded') { Write-Host $_ -ForegroundColor Green  }");
            b.AppendLine( "    elseif  ($_ -match '\\[VolBake\\]')                    { Write-Host $_ -ForegroundColor Cyan   }");
            b.AppendLine( "    elseif  ($_ -match 'Exception|\\bError\\b|FAILED')     { Write-Host $_ -ForegroundColor Red    }");
            b.AppendLine( "    elseif  ($_ -match 'WARNING|warning')                  { Write-Host $_ -ForegroundColor Yellow }");
            b.AppendLine( "    else                                                    { Write-Host $_                         }");
            b.AppendLine( "}");
            return b.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  CMD launcher
        // ─────────────────────────────────────────────────────────────────────
        static string BuildCmd(
            string projectRoot, string unityExe, string logPath,
            string ps1MainPath, string ps1TailPath)
        {
            var b = new StringBuilder();
            b.AppendLine("@echo off");
            b.AppendLine("setlocal");
            b.AppendLine($"set PROJECT={Q(projectRoot)}");
            b.AppendLine($"set LOG={Q(logPath)}");
            b.AppendLine($"set PS1_MAIN={Q(ps1MainPath)}");
            b.AppendLine($"set PS1_TAIL={Q(ps1TailPath)}");
            b.AppendLine( "cd /d %PROJECT%");
            b.AppendLine( "if exist %LOG% del /f /q %LOG%");
            b.AppendLine( "timeout /t 2 /nobreak >nul");
            b.AppendLine( "start \"VolBake - Full Log\" powershell.exe -NoProfile -ExecutionPolicy Bypass -File %PS1_TAIL%");
            b.AppendLine( "powershell.exe -NoProfile -ExecutionPolicy Bypass -File %PS1_MAIN%");
            b.AppendLine( "set EXITCODE=%ERRORLEVEL%");
            b.AppendLine( "timeout /t 3 /nobreak >nul");
            b.AppendLine( "if NOT %EXITCODE%==0 (");
            b.AppendLine( "  echo.");
            b.AppendLine( "  echo VolBake FAILED ^(exit %EXITCODE%^). See log: %LOG%");
            b.AppendLine( "  pause");
            b.AppendLine( "  exit /b %EXITCODE%");
            b.AppendLine( ")");
            b.AppendLine($"start \"\" {Q(unityExe)} -projectPath %PROJECT%");
            b.AppendLine( "exit /b 0");
            return b.ToString();
        }

        static string EscPS1(string s) => s.Replace("'", "''");
        static string Q(string s)      => $"\"{s}\"";
    }
}
    