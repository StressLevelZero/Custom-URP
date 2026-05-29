using SLZ.SLZEditorTools;
using System;
using System.IO;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SLZ.DXCUpdater
{
    public static class SetDXCIncludeState
    {
        const string dxcompilerName = "dxcompiler.dll";
        public static readonly string unityDxcPath = Path.Combine(Path.GetDirectoryName(EditorApplication.applicationPath), "Data", "Tools", dxcompilerName);
        public static readonly string slzDxcPath = Path.GetFullPath(Path.Combine("Packages","com.unity.render-pipelines.universal","Editor","DXCUpdate","DXC_Patch","dxc~", dxcompilerName));

        public static readonly string includePath = Path.Combine(URPConfigManager.packagePath, "include", "DXCUpdateState.hlsl");
        public static readonly string fullIncludePath = Path.GetFullPath(includePath);

        static readonly ulong MinSupportedVersion = FileVersionToLong(1, 7, 0);

        public static ulong FileVersionToLong(uint major, uint minor, uint build)
        {
            return (((ulong)major) << 48) | (((ulong)minor) << 32) | ((ulong)build);
        }

        public static ulong FileVersionToLong(FileVersionInfo version)
        {
            return FileVersionToLong((uint)version.FileMajorPart, (uint)version.FileMinorPart, (uint)version.FileBuildPart);
        }

        public static bool UpdateDXCIncludeState()
        {
            bool unityDXCExists = File.Exists(unityDxcPath) /* && File.Exists(unityDxilPath) */;
            FileVersionInfo installDXCVersion = unityDXCExists ? FileVersionInfo.GetVersionInfo(unityDxcPath) : null;
            return UpdateDXCIncludeState(installDXCVersion);
        }

        public static bool UpdateDXCIncludeState(FileVersionInfo versionInfo)
        {
            uint major = versionInfo != null ? (uint)versionInfo.FileMajorPart : 0;
            uint minor = versionInfo != null ? (uint)versionInfo.FileMinorPart : 0;
            uint build = versionInfo != null ? (uint)versionInfo.FileBuildPart : 0;
            uint priv = versionInfo != null ? (uint)versionInfo.FilePrivatePart : 0;
            bool isUpdated = FileVersionToLong(major, minor, build) >= MinSupportedVersion;
            URPConfigManager.Initialize();
            return SetDXCIncludeState.Set(isUpdated, major, minor, build, priv);
        }

     /*
        public static long GetLastModifiedTime()
        {
            DateTime dt = File.GetLastWriteTime(fullIncludePath);
            DateTimeOffset dto = new DateTimeOffset(dt.ToUniversalTime());
            long unixTimestamp = dto.ToUnixTimeSeconds();
            return unixTimestamp;
        }

        public static long GetStoredLastModifiedTime()
        {
            Vector2Int intTime = new Vector2Int()
            {
                x = SessionState.GetInt("SLZ.DXCUpdater.DXCIncModTime0", 0),
                y = SessionState.GetInt("SLZ.DXCUpdater.DXCIncModTime1", 0)
            };
            long unixTimestamp = UnsafeUtility.As<Vector2Int, long>(ref intTime);
            return unixTimestamp;
        }

        public static void UpdateLastModifiedTime()
        {
            long time = GetLastModifiedTime();
            Vector2Int intTime = UnsafeUtility.As<long, Vector2Int>(ref time);
            SessionState.SetInt("SLZ.DXCUpdater.DXCIncModTime0", intTime.x);
            SessionState.SetInt("SLZ.DXCUpdater.DXCIncModTime1", intTime.y);
        }

        static bool HasBeenModified()
        {
            long lastModified = GetLastModifiedTime();
            long stored = GetStoredLastModifiedTime();
            return lastModified != stored;
        }
        */
        //static string packageName = "com.stresslevelzero.urpconfig";
        public static bool Set(bool patched, uint major, uint minor, uint patch, uint build)
        {
            //Debug.Log($"Setting DXCUpdateState");
            URPConfigManager.Initialize();
            string includePath = Path.Combine(URPConfigManager.packagePath, "include", "DXCUpdateState.hlsl");
            //Debug.Log($"DXCUpdateState path: {includePath}");
            if (!File.Exists(includePath))
            {
                try
                {
                    File.Copy(URPConfigManager.dxcUpdateStateSrcPath, URPConfigManager.dxcUpdateStateInclPath, true);
                    File.Copy(URPConfigManager.dxcUpdateStateSrcPath + ".meta", URPConfigManager.dxcUpdateStateInclPath + ".meta", true);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Debug.LogError($"Critical shader include file is missing ({includePath})");
                    return false;
                }
            }

            string comment = patched ? "" : "//";
            string file =
                $"#ifndef SLZ_DXC_STATE\n" +
                $"\t#define SLZ_DXC_STATE\n" +
                $"\t{comment}#define SLZ_DXC_UPDATED\n" +
                $"\t{comment}#define SLZ_DXC_VERSION_MAJOR {major}\n" +
                $"\t{comment}#define SLZ_DXC_VERSION_MINOR {minor}\n" +
                $"\t{comment}#define SLZ_DXC_VERSION_PATCH {patch}\n" +
                $"\t{comment}#define SLZ_DXC_VERSION_BUILD {build}\n" +
                $"#endif";
            string original = File.ReadAllText(includePath);
            if (!string.Equals(original, file, System.StringComparison.InvariantCulture))
            {
                Debug.Log($"DXCUpdateState needs to be updated! {major}.{minor}.{patch}.{build}");
                File.WriteAllText(includePath, file);
            }
            if (Application.isBatchMode)
            {
                Debug.Log($"DXC Version: {major}.{minor}.{patch}.{build}");
                if (major <= 1 && minor < 7)
                {
                    Debug.LogError("DXC Out of Date!!!! Update DXC on this machine!");
                    return false;
                }
            }

            return true;
        }
    }
}
