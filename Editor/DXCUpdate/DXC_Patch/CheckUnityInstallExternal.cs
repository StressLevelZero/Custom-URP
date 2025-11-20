//#define SIMULATE_ADMIN_NECESSARY
#if !SLZ_RP_INTERNAL

using SLZ.SLZEditorTools;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SLZ.DXCUpdater
{
    /// <summary>
    /// Checks the state of Unity's DXC compiler dlls to determine if they're up-to-date
    /// </summary>
    internal static class CheckDXCInstallExternal
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

#if !SKIP_DXC_UPGRADE
            CheckDXCExternal();
#else
            UpdateDXCIncludeState();
#endif
            SessionState.SetBool("DXCChecked", true);
        }

        static ulong FileVersionToLong(uint major, uint minor, uint build)
        {
            return (((ulong)major) << 48) | (((ulong)minor) << 32) | ((ulong)build);
        }

        static ulong FileVersionToLong(FileVersionInfo version)
        {
            return FileVersionToLong((uint)version.FileMajorPart, (uint)version.FileMinorPart, (uint)version.FileBuildPart);
        }

        static ulong MinSupportedVersion { get => FileVersionToLong(1, 7, 0); }

        static void UpdateDXCIncludeState()
        {
            string unity = EditorApplication.applicationPath;
            string toolsDir = Path.Combine(Path.GetDirectoryName(unity), "Data", "Tools");
            string unityDxcPath = Path.Combine(toolsDir, "dxcompiler.dll");
            // string unityDxilPath = Path.Combine(toolsDir, "dxil.dll");
            bool unityDXCExists = File.Exists(unityDxcPath) /* && File.Exists(unityDxilPath) */;
            FileVersionInfo installDXCVersion = unityDXCExists ? FileVersionInfo.GetVersionInfo(unityDxcPath) : null;
            UpdateDXCIncludeState(installDXCVersion);
        }

        static bool UpdateDXCIncludeState(FileVersionInfo versionInfo)
        {
            uint major = versionInfo != null ? (uint)versionInfo.FileMajorPart : 0;
            uint minor = versionInfo != null ? (uint)versionInfo.FileMinorPart : 0;
            uint build = versionInfo != null ? (uint)versionInfo.FileBuildPart : 0;
            uint priv = versionInfo != null ? (uint)versionInfo.FilePrivatePart : 0;
            bool isUpdated = FileVersionToLong(major, minor, build) >= MinSupportedVersion;
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, $"DXC Version: {major}.{minor}.{build}.{priv}");

            URPConfigManager.Initialize();
            return SetDXCIncludeState.Set(isUpdated, major, minor, build, priv);
        }


        [MenuItem("Stress Level Zero/Graphics/DXC Updater/Enable DXC Check")]
        static void EnableDXCCheck()
        {
            EditorPrefs.SetBool("SkipDXCUpdate", false);
            EditorUtility.DisplayDialog("DXC Update Check Enabled", "DXC update check enabled, restart editor to update", "Ok");
        }

        static void CheckDXCExternal()
        {
            if (EditorPrefs.GetBool("SkipDXCUpdate", false))
            {
                URPConfigManager.Initialize();

                UpdateDXCIncludeState();
                return;
            }
            string unity = EditorApplication.applicationPath;
            string toolsDir = Path.Combine(Path.GetDirectoryName(unity), "Data", "Tools");
            string localPath = Path.GetFullPath("Packages/com.unity.render-pipelines.universal/Editor/DXCUpdate/DXC_Patch/dxc~");

            string unityDxcPath = Path.Combine(toolsDir, "dxcompiler.dll");
            // string unityDxilPath = Path.Combine(toolsDir, "dxil.dll");
            bool unityDXCExists = File.Exists(unityDxcPath) /* && File.Exists(unityDxilPath) */;
            FileVersionInfo installDXCVersion = unityDXCExists ? FileVersionInfo.GetVersionInfo(unityDxcPath) : default(FileVersionInfo);


            string localDxcPath = Path.Combine(localPath, "dxcompiler.dll");
            //string localDxilPath = Path.Combine(localPath, "dxil.dll");
            bool localDXCExists = File.Exists(localDxcPath) /* && File.Exists(localDxilPath) */;
            FileVersionInfo localDXCVersion = localDXCExists ? FileVersionInfo.GetVersionInfo(localDxcPath) : default(FileVersionInfo);

            int defaultNewDXCVersionMajor = 1;
            int defaultNewDXCVersionMinor = 7;
            bool needsUpdate = !unityDXCExists || !localDXCExists || (installDXCVersion.FileMajorPart < defaultNewDXCVersionMajor || installDXCVersion.FileMinorPart < defaultNewDXCVersionMinor);
            
            // for legal reasons, we can't auto update the compiler on end-user's machines. Auto-updater moved to internal package
            if (needsUpdate)
            {
                bool warnNonWin = EditorUtility.DisplayDialog($"DXC out of date",
                      $"The DirectX Shader Compiler (DXC) in the Unity Editor installation is too old to support Quest.\n" +
                      "Unity uses the DXC library at:\n" +
                      $"{toolsDir}\\dxcompiler.dll\n\n" +
                      "A Unity-compatible fork of DXC is included inside this package at:\n" +
                      $"{localPath}\\dxcompiler.dll\n\n" +
                      $"The legacy compiler will be used. Advanced shader features will be unavailable and compilation times will be longer.",
                      "Dismiss",
                      "Dismiss - Don't warn again for this machine"
                      );
                if (!warnNonWin)
                {
                    EditorPrefs.SetBool("SkipDXCUpdate", true);
                }
               

            }
            URPConfigManager.Initialize();
            UpdateDXCIncludeState(installDXCVersion);
            SessionState.SetBool("DXCChecked", true);
        }
    }
}

#endif