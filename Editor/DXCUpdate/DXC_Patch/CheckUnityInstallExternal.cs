//#define SIMULATE_ADMIN_NECESSARY


using SLZ.SLZEditorTools;
using System;
using System.Diagnostics;
using System.IO;

using UnityEditor;
using UnityEditor.Experimental;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SLZ.DXCUpdater
{
    /// <summary>
    /// Checks the state of Unity's DXC compiler dlls to determine if they're up-to-date
    /// </summary>
    public static class CheckDXCInstallExternal
    {
        static string dxcompilerName = "dxcompiler.dll";
        public static string UnityDxcPath() { return Path.Combine(Path.GetDirectoryName(EditorApplication.applicationPath), "Data", "Tools", dxcompilerName); }
        public static string SlzDxcPath() { return Path.GetFullPath(Path.Combine("Packages","com.unity.render-pipelines.universal","Editor","DXCUpdate","DXC_Patch","dxc~", dxcompilerName)); }

        static ulong FileVersionToLong(uint major, uint minor, uint build)
        {
            return (((ulong)major) << 48) | (((ulong)minor) << 32) | ((ulong)build);
        }

        static ulong FileVersionToLong(FileVersionInfo version)
        {
            return FileVersionToLong((uint)version.FileMajorPart, (uint)version.FileMinorPart, (uint)version.FileBuildPart);
        }

        static ulong MinSupportedVersion { get => FileVersionToLong(1, 7, 0); }

        public static void UpdateDXCIncludeState()
        {
            string unityDxcPath = UnityDxcPath();
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

        static string FileVersionToStr(FileVersionInfo vi)
        {
            return $"{vi.FileMajorPart}.{vi.FileMinorPart}.{vi.FileBuildPart}.{vi.FilePrivatePart}";
        }

#if !SLZ_RP_INTERNAL || SIMULATE_EXTERNAL

        [InitializeOnLoadMethod()]
        static void CheckDXC()
        {
            // avoid running this method every domain reload
            if (SessionState.GetBool("DXCChecked", false))
            {
                //Debug.Log("Early Exit from CheckDXCInstall");
                return;
            }

#if !SKIP_DXC_UPGRADE
            EditorApplication.update += CheckDXCExternal;
#else
            UpdateDXCIncludeState();
#endif
            SessionState.SetBool("DXCChecked", true);
        }

 


        [MenuItem("Stress Level Zero/Graphics/DXC Updater/Enable DXC Check")]
        static void EnableDXCCheck()
        {
            EditorPrefs.SetBool("SkipDXCUpdate", false);
            EditorUtility.DisplayDialog("DXC Update Check Enabled", "DXC update check enabled, restart editor to update", "Ok");
        }

        internal static void GetDXCVersions(string unityDxcPath, string slzDxcPath, out bool unityDXCExists, out FileVersionInfo unityDXCVersion, out bool slzDXCExists, out FileVersionInfo SlzDXCVersion)
        {
            unityDXCExists = File.Exists(unityDxcPath);

            unityDXCVersion = unityDXCExists ? FileVersionInfo.GetVersionInfo(unityDxcPath) : default(FileVersionInfo);
#if SIMULATE_EXTERNAL && SIMULATE_OLD_DXC
            unityDXCVersion = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(FileVersionInfo)) as FileVersionInfo;
#endif

            slzDXCExists = File.Exists(slzDxcPath);
            SlzDXCVersion = slzDXCExists ? FileVersionInfo.GetVersionInfo(slzDxcPath) : default(FileVersionInfo);
        }

#if SIMULATE_EXTERNAL
        [MenuItem("TEST/Show DXC Warning")]
#endif
        static void CheckDXCExternal()
        {
            EditorApplication.update -= CheckDXCExternal;
            if (EditorPrefs.GetBool("SkipDXCUpdate", false))
            {
                URPConfigManager.Initialize();

                UpdateDXCIncludeState();
                return;
            }
            string slzDxcPath = SlzDxcPath();
            string unityDxcPath = UnityDxcPath();
            GetDXCVersions(unityDxcPath, slzDxcPath, out bool unityDXCExists, out FileVersionInfo installDXCVersion, out bool localDXCExists, out FileVersionInfo localDXCVersion);

            //Debug.LogError($"{unityDxcPath} : {unityDXCExists}\n{slzDxcPath} : {localDXCExists}");

            int defaultNewDXCVersionMajor = 1;
            int defaultNewDXCVersionMinor = 7;
            bool needsUpdate = !unityDXCExists || !localDXCExists || (installDXCVersion.FileMajorPart < defaultNewDXCVersionMajor || installDXCVersion.FileMinorPart < defaultNewDXCVersionMinor);

            DXCWarningWindow[] warnWindows = Resources.FindObjectsOfTypeAll<DXCWarningWindow>();
            foreach (var warnWindow in warnWindows)
            {
                warnWindow.Close();
                EditorWindow.DestroyImmediate(warnWindow);
            }

            // for legal reasons, we can't auto update the compiler on end-user's machines. Auto-updater moved to internal package
            if (needsUpdate)
            {
                EditorPrefs.DeleteKey(typeof(DXCWarningWindow).ToString() + "x");
                EditorPrefs.DeleteKey(typeof(DXCWarningWindow).ToString() + "y");
                EditorPrefs.SetFloat(typeof(DXCWarningWindow).ToString() + "w", 800);
                EditorPrefs.SetFloat(typeof(DXCWarningWindow).ToString() + "h", 240);
                DXCWarningWindow.unityDXCInfo = FileVersionToStr(installDXCVersion);
                DXCWarningWindow.slzDXCInfo = FileVersionToStr(localDXCVersion);
                DXCWarningWindow.unityDXCPath = unityDxcPath;
                DXCWarningWindow.slzDXCPath = slzDxcPath;
                DXCWarningWindow warnWindow = EditorWindow.GetWindow<DXCWarningWindow>( true, "DXC Shader Compiler Out Of Date", true);
                DXCWarningWindow.unityDXCInfo  = null;
                DXCWarningWindow.slzDXCInfo    = null;
                DXCWarningWindow.unityDXCPath  = null;
                DXCWarningWindow.slzDXCPath    = null;
                //warnWindow.position = ContainerWindowBridge.ParentBorderSize(warnWindow, new Rect(new Vector2(0,0), new Vector2(800, 240)));
                warnWindow.ShowUtility();
                Debug.Log(warnWindow.position);
            }
            else
            {
                DXCWarningWindow[] windows = Resources.FindObjectsOfTypeAll<DXCWarningWindow>();
                foreach (DXCWarningWindow window in windows)
                {
                    window.Close();
                    UnityEngine.Object.DestroyImmediate(window);
                }
            }
            URPConfigManager.Initialize();
            UpdateDXCIncludeState(installDXCVersion);
            SessionState.SetBool("DXCChecked", true);
        }

#endif // !SLZ_RP_INTERNAL || SIMULATE_EXTERNAL
    }
}

