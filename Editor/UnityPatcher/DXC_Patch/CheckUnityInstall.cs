using SLZ.SLZEditorTools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SLZ.EditorPatcher
{
    /// <summary>
    /// Checks the state of Unity's DXC compiler dlls to determine if they're up-to-date
    /// </summary>
    internal static class CheckDXCInstall
    {
        [InitializeOnLoadMethod]
        static void CheckDXC()
        {
            // avoid running this method every domain reload
            if (SessionState.GetBool("DXCChecked", false))
            {
                //Debug.Log("Early Exit from CheckDXCInstall");
                return;
            }

#if ERROR_SPOOKY_DONT_USE
            CheckDXCSpooky();
#else
            CheckDXCSafe();
#endif
            SessionState.SetBool("DXCChecked", true);
        }


        static void CheckDXCSafe()
        {
            string unity = EditorApplication.applicationPath;
            string toolsDir = Path.Combine(Path.GetDirectoryName(unity), "Data", "Tools");
            string unityDxcPath = Path.Combine(toolsDir, "dxcompiler.dll");
            // string unityDxilPath = Path.Combine(toolsDir, "dxil.dll");
            bool unityDXCExists = File.Exists(unityDxcPath) /* && File.Exists(unityDxilPath) */;
            FileVersionInfo installDXCVersion = unityDXCExists ? FileVersionInfo.GetVersionInfo(unityDxcPath) : null;
            uint major = installDXCVersion != null ? (uint)installDXCVersion.FileMajorPart : 0;
            uint minor = installDXCVersion != null ? (uint)installDXCVersion.FileMinorPart : 0;
            uint build = installDXCVersion != null ? (uint)installDXCVersion.FileBuildPart : 0;
            uint priv =  installDXCVersion != null ? (uint)installDXCVersion.FilePrivatePart : 0;
            bool isUpdated = major >= 1 && minor >= 7;
            URPConfigManager.Initialize();
            SetDXCIncludeState.Set(isUpdated,major,minor,build,priv);
        }

        [MenuItem("Stress Level Zero/Graphics/Experimental/Upgrade DXC Compiler")]
        static void ManualCheckDXC()
        {
            CheckDXCSpooky();
        }

        #region ERROR_SPOOKY_DONT_USE
        [MenuItem("Stress Level Zero/Graphics/Experimental/Enable DXC Check")]
        static void EnableDXCCheck()
        {
            EditorPrefs.SetBool("SkipDXCUpdate", false);
            EditorUtility.DisplayDialog("DXC Update Check Enabled", "DXC update check enabled, restart editor to update", "Ok");
        }

        static void CheckDXCSpooky()
        {
            // skip if set in editorprefs
            if (EditorPrefs.GetBool("SkipDXCUpdate", false))
            {
                URPConfigManager.Initialize();
                if (Application.platform == RuntimePlatform.WindowsEditor) SetDXCIncludeState.Set(false, 0, 0, 0, 0);
                return;
            }

            // I don't include linux or OSX libs for DXC, and I can't test them to make sure this works. Leave it to the user to update.
            if (Application.platform != RuntimePlatform.WindowsEditor && !Application.isBatchMode)
            {
                bool warnNonWin = EditorUtility.DisplayDialog($"DXC update requested",
                      "The DirectX Shader Compiler needs to be manually updated to support Quest. " +
                      "On non-windows platforms, this is not set up to update manually as this package does not contain pre-compiled binaries. " +
                      "If you wish to use the DXC Compiler, clone release-1.8.2407 from Microsoft's DXC github " +
                      "(https://github.com/microsoft/DirectXShaderCompiler/releases/tag/v1.8.2407). " +
                      "Apply the patch files included in the following directory and build:\n" +
                      $"{Path.GetFullPath("Packages/com.unity.render-pipelines.universal/Editor/UnityPatcher/DXC_Patch/dxc~")}\n" +
                      "Replace libdxcompiler.so in your editor install with the one you've built. " +
                      "Additionally, in your project locate Packages/com.stresslevelzero.urpconfig/include/DXCUpdateState.hlsl and uncomment '#define SLZ_DXC_UPDATED' " +
                      "to enable DXC compilation on Quest and enable other DXC features",
                      "Dismiss",
                      "Dismiss - Don't ask again for this machine"
                      );
                if (!warnNonWin)
                {
                    EditorPrefs.SetBool("SkipDXCUpdate", true);
                }
                // Don't set DXCUpdateState.hlsl, leave that to the user
                URPConfigManager.Initialize();
                return;
            }

            string unity = EditorApplication.applicationPath;
            string toolsDir = Path.Combine(Path.GetDirectoryName(unity), "Data", "Tools");
            string unityDxcPath = Path.Combine(toolsDir, "dxcompiler.dll");
            // string unityDxilPath = Path.Combine(toolsDir, "dxil.dll");
            bool unityDXCExists = File.Exists(unityDxcPath) /* && File.Exists(unityDxilPath) */;
            FileVersionInfo installDXCVersion = unityDXCExists ? FileVersionInfo.GetVersionInfo(unityDxcPath) : null;

            string localPath = Path.GetFullPath("Packages/com.unity.render-pipelines.universal/Editor/UnityPatcher/DXC_Patch/dxc~");
            string localDxcPath = Path.Combine(localPath, "dxcompiler.dll");
            //string localDxilPath = Path.Combine(localPath, "dxil.dll");
            bool localDXCExists = File.Exists(localDxcPath) /* && File.Exists(localDxilPath) */;
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
                            !unityDXCExists ? $"Could not find unity's DXC dlls at:\n{unityDxcPath}\n" : "",
                            !localDXCExists ? $"Could not find replacement dlls at:\n{localDxcPath}\n" : "",
                            needsUpdate && unityDXCExists ? $"Unity's DXC was found and out of date (Found {installDXCVersion.FileMajorPart}.{installDXCVersion.FileMinorPart}, expected {defaultNewDXCVersionMajor}.{defaultNewDXCVersionMinor})\n" +
                            $"Try manually replacing {unityDxcPath} with {localDxcPath}\n" : ""
                            ), "Exit");
                    }
                    Instagib();
                }
                return;
            }

            bool unityNeedsUpdate = installDXCVersion.FileMajorPart < localDXCVersion.FileMajorPart || installDXCVersion.FileMinorPart < localDXCVersion.FileMinorPart;
            int choice = -1;

            if (unityNeedsUpdate)
            {
                if (Application.isBatchMode)
                {
                    Debug.LogError("\n\nWARNING! The URP is going to attempt to modify the Unity install! The DXC shader compiler needs to be updated " +
                        $"to support multiview on Quest. Found version {installDXCVersion.ProductVersion}, working version is {localDXCVersion.ProductVersion}" +
                        "This will exit Unity and attempt to replace the following files:\n" +
                        $"{unityDxcPath}\n" +
                        "with more up-to-date versions found at:\n" +
                        $"{localDxcPath}\n" +
                        $"The old DLLs will be moved to a folder named \"DXC_Backup\" in the unity folder.\n" +
                        "This will almost certainly fail if other editor instances are running! In the event that it does, manually backup unity's dlls and " +
                        "overwrite them with the new dlls from this package. You may also build the dlls from https://github.com/microsoft/DirectXShaderCompiler/releases . " +
                        "This is tested working with the June 2024 release (1.8.2407), later releases may be incompatible!\n\n"
                        );
                    choice = 0;
                }
                else
                {

                    choice = EditorUtility.DisplayDialogComplex($"DXC update requested ({installDXCVersion.ProductVersion}->{localDXCVersion.ProductVersion})",
                        "The DirectX Shader Compiler (DXC) needs to be updated to support Quest. Allow Update?\n\n" +
                        "This will replace dxcompiler.dll in your Unity Editor install, affecting all projects on this unity version. " +
                        "A backup of the original dll can be found in Editor/Data/Tools/DXC_Backup.\n\n" +
                        "This is optional. If DXC is not updated Quest will instead use the default shader compiler, which is slower and missing some advanced features.\n\n" +
                        "Before updating, close all other unity editor applications.",
                        "Update and Quit",
                        "Quit",
                        "Don't Update"
                        );
                }
            }
            if (choice == 0)
            {
                try
                {

                    bool updateSuccess = UpdateDXC(localDxcPath, unityDxcPath, out string message);
                    if (!updateSuccess)
                    {
                        string errMsg = $"DXC Update Failed:\n{message}\n\n" +
                                "Close any other instance of the editor, and kill any remaining unity or unityshadercompiler processes from task manager!\n" +
                                $"You may also try manually moving these files:\n{localDxcPath}\nTo:\n{unityDxcPath}\n\n" +
                                "Aborting!";
                        Debug.LogError(message);
                        if (!Application.isBatchMode)
                        {
                            EditorUtility.DisplayDialog("Failed to update DXC", errMsg, "Abort");
                            Instagib();
                            return;
                        }
                    }
                    URPConfigManager.Initialize();
                    Instagib();
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to Update DXC: {ex.Message}");
                    Instagib();
                }
                return;
            }
            else if (choice == 1)
            {
                Instagib();
                return;
            }
            else if (choice == 2)
            {
                EditorUtility.DisplayDialog("Skipping DXC Update", "Skipping DXC Update. This message will not show again.\n\n" +
                    "If you wish to update DXC at a future point, go to the menu bar->Stress Level Zero->Graphics->Enable DXC Check", "Ok");
                EditorPrefs.SetBool("SkipDXCUpdate", true);
            }

            bool success = false;
            try
            {
                Debug.Log($"DXC Version: {localDXCVersion.FileMajorPart}.{localDXCVersion.FileMinorPart}.{localDXCVersion.FileBuildPart}");
                success = SetDXCIncludeState.Set(!unityNeedsUpdate, 
                    (uint)localDXCVersion.FileMajorPart, 
                    (uint)localDXCVersion.FileMinorPart, 
                    (uint)localDXCVersion.FileBuildPart,
                    (uint)localDXCVersion.FilePrivatePart
                    );
            }
            finally
            {
                if (!success)
                {
                    Debug.LogError("ERROR: Updating DXCUpdateState.hlsl failed");
                    if (!Application.isBatchMode)
                    {
                        EditorUtility.DisplayDialog("Missing critical shader include",
                            "Unable to update critical shader include: Packages/com.stresslevelzero.urpconfig/include/DXCUpdateState.hlsl" +
                            "\n\nExiting Unity to prevent shader corruption",
                            "Abort");
                    }
                    Instagib();
                }
            }
            SessionState.SetBool("DXCChecked", true);
        }

        static bool UpdateDXC(string inDXCPath, string outDXCPath, /*string outDXILPath,*/ out string errMsg)
        {
            //StringBuilder errBuilder = new StringBuilder();
            string backupPath = Path.Combine(Path.GetDirectoryName(outDXCPath), "DXC_Backup");
            Directory.CreateDirectory(backupPath);
            string backupDXC = Path.Combine(backupPath, "dxcompiler.dll");
            //string backupDXIL = Path.Combine(backupPath, "dxil.dll");

            if (!File.Exists(backupDXC))
            {
                try
                {
                    Debug.Log($"Backing up {outDXCPath} to {backupDXC}");
                    //throw new UnauthorizedAccessException();
                    File.Copy(outDXCPath, backupDXC);
                }
                catch (UnauthorizedAccessException)
                {
                    UpdateDXCCmd(backupPath, backupDXC, inDXCPath, outDXCPath, true, true);
                    errMsg = "";
                    return true;
                }
                catch (IOException)
                {
                    UpdateDXCCmd(backupPath, backupDXC, inDXCPath, outDXCPath, false, true);
                    errMsg = "";
                    return true;
                }
                catch (Exception ex)
                {
                    //Debug.Log($"Failed to backup dxcompiler.dll, aborting: {ex.Message}");
                    errMsg = $"Failed to backup dxcompiler.dll, aborting: {ex.Message}";
                    return false;
                }
            }
            else
            {
                Debug.Log($"dxcompiler.dll already found at {backupDXC}, skipping backup");
            }

            //if (!File.Exists(backupDXIL))
            //{
            //    try
            //    {
            //        Debug.Log($"Backing up {outDXILPath} to {backupDXIL}");
            //        File.Copy(outDXILPath, backupDXIL);
            //    }
            //    catch (Exception ex)
            //    {
            //        errMsg = $"Failed to backup dxil.dll, aborting: {ex.Message}";
            //        return false;
            //    }
            //}
            //else
            //{
            //    Debug.Log($"dxil.dll already found at {backupDXIL}, skipping backup");
            //}

            try
            {
                Debug.Log($"Ovewriting {outDXCPath} with {inDXCPath}");
                //throw new UnauthorizedAccessException();
                File.Copy(inDXCPath, outDXCPath, true);
            }
            catch (UnauthorizedAccessException)
            {
                UpdateDXCCmd(backupPath, backupDXC, inDXCPath, outDXCPath, true, true);
                errMsg = "";
                return true;
            }
            catch (IOException)
            {
                UpdateDXCCmd(backupPath, backupDXC, inDXCPath, outDXCPath, false, true);
                errMsg = "";
                return true;
            }
            catch (Exception ex)
            {
                errMsg = $"Failed to overwrite dxcompiler.dll, aborting: {ex.Message}";
                return false;
            }

            //try
            //{
            //    Debug.Log($"Ovewriting {outDXILPath} with {inDXILPath}");
            //    File.Copy(inDXILPath, outDXILPath, true);
            //}
            //catch (Exception ex)
            //{
            //    errMsg = $"Failed to overwrite dxil.dll, attempting to restore dxcompiler.dll and aborting: {ex.Message}";
            //    try
            //    {
            //        File.Copy(backupDXC, outDXCPath, true);
            //    }
            //    catch (Exception ex2)
            //    {
            //        errMsg += $"\nFailed to restore dxcompiler.dll!: {ex2.Message}";
            //    }
            //    return false;
            //}

            // // Update via cmd, less safe but allows us to copy the files after the editor has closed
            // string pause = Application.isBatchMode ? "" : "& pause";
            // Process cmd = new Process();
            // cmd.StartInfo.FileName = "cmd.exe";
            // cmd.StartInfo.Arguments = $"/c timeout /t 5 & copy /b/v/y \"{inDXCPath}\" \"{outDXCPath}\" & copy /b/v/y \"{inDXILPath}\" \"{outDXILPath}\" {pause}";
            // cmd.StartInfo.UseShellExecute = true;
            // cmd.StartInfo.CreateNoWindow = false;
            // cmd.Start();
            
            errMsg = "No error";
            return true;
        }

        static void UpdateDXCCmd(string backupFolder, string backupPath, string inDXCPath, string outDXCPath, bool elevated, bool delay)
        {
            string pause = Application.isBatchMode ? "" : "& pause";
            string timeout = delay ? "timeout /t 5 & " : "";
            bool backupExists = File.Exists(backupPath);
            string backup = !backupExists ? $"mkdir \"{backupFolder}\" & copy \"{outDXCPath}\" /b/v/y \"{backupFolder}\" & " : "";
            string command = $"{timeout}{backup}copy /b/v/y \"{inDXCPath}\" \"{outDXCPath}\"";
            string echo = command.Replace("&", "^&");
            Process cmd = new Process();
            cmd.StartInfo.FileName = "cmd.exe";
            cmd.StartInfo.Arguments = $"/C {command} {pause}";
            cmd.StartInfo.UseShellExecute = true;
            if (elevated) cmd.StartInfo.Verb = "RunAs";
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

#endregion // ERROR_SPOOKY_DONT_USE
    }
}
