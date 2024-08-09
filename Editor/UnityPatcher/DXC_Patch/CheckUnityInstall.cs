using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

internal static class CheckDXCInstall
{
    [InitializeOnLoadMethod]
    static void Check()
    {
        string unity = EditorApplication.applicationPath;
        string toolsDir = Path.Combine(Path.GetDirectoryName(unity), "Data", "Tools");
        string unityDxcPath = Path.Combine(toolsDir, "dxcompiler.dll");
        string unityDxilPath = Path.Combine(toolsDir, "dxil.dll");
        bool unityDXCExists = File.Exists(unityDxcPath) && File.Exists(unityDxilPath);
        FileVersionInfo installDXCVersion = unityDXCExists ? FileVersionInfo.GetVersionInfo(unityDxcPath) : null;

        string localPath = Path.GetFullPath("Packages/com.unity.render-pipelines.universal/Editor/UnityPatcher/DXC_Patch/dxc~");
        string localDxcPath = Path.Combine(localPath, "dxcompiler.dll");
        string localDxilPath = Path.Combine(localPath, "dxil.dll");
        bool localDXCExists = File.Exists(localDxcPath) && File.Exists(localDxilPath);
        FileVersionInfo localDXCVersion = localDXCExists ? FileVersionInfo.GetVersionInfo(localDxcPath) : null;
 
        if (!localDXCExists || !unityDXCExists)
        {
            int defaultNewDXCVersionMajor = 1;
            int defaultNewDXCVersionMinor = 8;
            if (!localDXCExists) Debug.LogError("URP: Could not find local DXC compiler dlls! Will not attempt to update DXC!");
            if (!unityDXCExists) Debug.LogError("URP: Could not find unity's DXC compiler dlls! Will not attempt to update DXC!");
            bool needsUpdate = !unityDXCExists || (installDXCVersion.FileMajorPart < defaultNewDXCVersionMajor || installDXCVersion.FileMinorPart < defaultNewDXCVersionMinor);
            if (needsUpdate)
            {
                Debug.LogError("ABORTING: DXC update failed. To prevent corrupting the cache server, unity will now close");
                if (!Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog("DXC Update Failed", string.Format("ABORTING: DXC update failed.\n{0}{1}{2}" +
                        "\nTo prevent corrupting the cache server, unity will now close",
                        !unityDXCExists ? $"Could not find unity's DXC dlls at:\n{unityDxcPath}\n{unityDxilPath}\n" : "",
                        !localDXCExists ? $"Could not find replacement dlls at:\n{localDxcPath}\n{localDxilPath}\n" : "",
                        needsUpdate && unityDXCExists ? $"Unity's DXC was found and out of date (Found {installDXCVersion.FileMajorPart}.{installDXCVersion.FileMinorPart}, expected {defaultNewDXCVersionMajor}.{defaultNewDXCVersionMinor})\n" +
                        $"Try manually updating {unityDxcPath} and dxil.dll with https://github.com/microsoft/DirectXShaderCompiler/releases/tag/v1.8.2407\n" : ""
                        ), "Exit");
                }
                Instagib();
            }
            return;
        }

        bool unityNeedsUpdate = installDXCVersion.FileMajorPart < localDXCVersion.FileMajorPart || installDXCVersion.FileMinorPart < localDXCVersion.FileMinorPart;
        int choice = 2;

        if (unityNeedsUpdate)
        {
            if (Application.isBatchMode)
            {
                Debug.LogError("\n\nWARNING! The URP is going to attempt to modify the Unity install! The DXC shader compiler needs to be updated " +
                    $"to support multiview on Quest. Found version {installDXCVersion.ProductVersion}, working version is {localDXCVersion.ProductVersion}" +
                    "This will exit Unity and attempt to replace the following files:\n" +
                    $"{unityDxcPath}\n{unityDxilPath}\n" +
                    "with more up-to-date versions found at:\n" +
                    $"{localDxcPath}\n{localDxilPath}\n" +
                    $"The old DLLs will be moved to a folder named \"DXC_Backup\" in the unity folder.\n" +
                    "This will almost certainly fail if other editor instances are running! In the event that it does, manually backup unity's dlls and " +
                    "overwrite them with the new dlls from this package. You may also obtain the dlls from https://github.com/microsoft/DirectXShaderCompiler/releases . " +
                    "This is tested working with the June 2024 release (1.8.2407), later releases may be incompatible!\n\n"
                    );
                choice = 0;
            }
            else
            {

                choice = EditorUtility.DisplayDialogComplex($"DXC update requested ({installDXCVersion.ProductVersion}->{localDXCVersion.ProductVersion})",
                    "The DirectX Shader Compiler needs to be updated to support Quest. Allow Update?\n\n" +
                    "WARNING: This will replace dxcompiler.dll and dxil.dll in your Unity Editor install, affecting all projects on this unity version. " +
                    "Backups of the original dlls can be found in Editor/Data/Tools/DXC_Backup.\n\n" +
                    "If this is not updated, Quest will instead fall back to the FXC compiler.\n\n" +
                    "Before updating, close all other unity editor applications.",
                    "Update",
                    "Quit",
                    "Proceed without updating"
                    );
            }
        }
        if (choice == 0)
        {
            try
            {
                UpdateDXC(localDxcPath, localDxilPath, unityDxcPath, unityDxilPath);
            }
            finally
            {
                Instagib();
            }
            return;
        }
        if (choice == 1)
        {
            Instagib();
            return;
        }
        

    }

    static void UpdateDXC(string inDXCPath, string inDXILPath, string outDXCPath, string outDXILPath)
    {
        string backupPath = Path.Combine(Path.GetDirectoryName(outDXCPath), "DXC_Backup");
        Directory.CreateDirectory(backupPath);
        string backupDXC = Path.Combine(backupPath, "dxcompiler.dll");
        string backupDXIL = Path.Combine(backupPath, "dxil.dll");

        if (!File.Exists(backupDXC)) File.Copy(outDXCPath, Path.Combine(backupPath, "dxcompiler.dll"));
        if (!File.Exists(backupDXIL)) File.Copy(outDXILPath, Path.Combine(backupPath, "dxil.dll"));

        Process cmd = new Process();
        cmd.StartInfo.FileName = "cmd.exe";
        cmd.StartInfo.Arguments = $"/c timeout /t 5 & copy /b/v/y {inDXCPath} {outDXCPath} & copy /b/v/y {inDXILPath} {outDXILPath}";
        cmd.StartInfo.UseShellExecute = true;
        cmd.StartInfo.CreateNoWindow = true;
        cmd.Start();
    }


    static void Instagib()
    {
        Debug.LogWarning("Unity should kill itself. NOW.");
        int procID = Process.GetCurrentProcess().Id;
        Process cmd = new Process();
        cmd.StartInfo.FileName = "taskkill";
        cmd.StartInfo.Arguments = $"/F /PID {procID}";
        cmd.StartInfo.UseShellExecute = false;
        cmd.Start();
    }
}
