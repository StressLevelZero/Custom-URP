using SLZ.SLZEditorTools;
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SLZ.DXCUpdater
{
    public static class SetDXCIncludeState
    {
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
