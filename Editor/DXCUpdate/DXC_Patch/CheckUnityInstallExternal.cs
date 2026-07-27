//#define SIMULATE_EXTERNAL
//#define SIMULATE_OLD_DXC

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


#if (!SLZ_RP_INTERNAL && !MARROW_INTERNAL) || SIMULATE_EXTERNAL

        [InitializeOnLoadMethod()]
        static void CheckDXC()
        {
            // avoid running this method every domain reload
            if (SessionState.GetBool("DXCChecked", false))
            {
                //Debug.Log("Early Exit from CheckDXCInstall");
                return;
            }

            List<string> args = new List<string>(Environment.GetCommandLineArgs());
            if (args.Contains("-skip-dxc-upgrade"))
            {
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "External DXC Update check: process launched with -skip-dxc-upgrade, skipping DXC check");
                SessionState.SetBool("DXCChecked", true);
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
            #if !SIMULATE_OLD_DXC
            EditorApplication.update -= CheckDXCExternal;
            if (EditorPrefs.GetBool("SkipDXCUpdate", false))
            {
                URPConfigManager.Initialize();

                SetDXCIncludeState.UpdateDXCIncludeState();
                return;
            }
            #endif
            
            GetDXCVersions(SetDXCIncludeState.unityDxcPath, SetDXCIncludeState.slzDxcPath, out bool unityDXCExists, out FileVersionInfo installDXCVersion, out bool localDXCExists, out FileVersionInfo localDXCVersion);

            //Debug.LogError($"{SetDXCIncludeState.unityDxcPath} : {unityDXCExists}\n{slzDxcPath} : {localDXCExists}");

            int defaultNewDXCVersionMajor = 1;
            int defaultNewDXCVersionMinor = 7;
            bool needsUpdate = !unityDXCExists || !localDXCExists || (installDXCVersion.FileMajorPart <= defaultNewDXCVersionMajor && installDXCVersion.FileMinorPart < defaultNewDXCVersionMinor);
            bool optionalUpdate = (installDXCVersion.FileMajorPart <= localDXCVersion.FileMajorPart && installDXCVersion.FileMinorPart < localDXCVersion.FileMinorPart);
            #if SIMULATE_OLD_DXC
            needsUpdate = false;
            #endif

            DXCWarningWindow[] warnWindows = Resources.FindObjectsOfTypeAll<DXCWarningWindow>();
            foreach (var warnWindow in warnWindows)
            {
                warnWindow.Close();
                EditorWindow.DestroyImmediate(warnWindow);
            }

            // for legal reasons, we can't auto update the compiler on end-user's machines. Auto-updater moved to internal package
            if (needsUpdate || optionalUpdate)
            {
                EditorPrefs.DeleteKey(typeof(DXCWarningWindow).ToString() + "x");
                EditorPrefs.DeleteKey(typeof(DXCWarningWindow).ToString() + "y");
                EditorPrefs.SetFloat(typeof(DXCWarningWindow).ToString() + "w", 800);
                EditorPrefs.SetFloat(typeof(DXCWarningWindow).ToString() + "h", 240);
                DXCWarningWindow.unityDXCInfo = $"{installDXCVersion.FileMajorPart}.{installDXCVersion.FileMinorPart}.{installDXCVersion.FileBuildPart}.{installDXCVersion.FilePrivatePart}";
                DXCWarningWindow.slzDXCInfo =   $"{localDXCVersion.FileMajorPart}.{localDXCVersion.FileMinorPart}.{localDXCVersion.FileBuildPart}.{localDXCVersion.FilePrivatePart}";
                DXCWarningWindow.unityDXCPath = SetDXCIncludeState.unityDxcPath;
                DXCWarningWindow.slzDXCPath = SetDXCIncludeState.slzDxcPath;
                DXCWarningWindow.updateOptional = !needsUpdate;
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
            #if !SIMULATE_OLD_DXC
            URPConfigManager.Initialize();
            SetDXCIncludeState.UpdateDXCIncludeState(installDXCVersion);
            SessionState.SetBool("DXCChecked", true);
            #endif
        }

#endif // !SLZ_RP_INTERNAL || SIMULATE_EXTERNAL
    }
}

