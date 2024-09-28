using SLZ.SLZEditorTools;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace SLZ.EditorPatcher
{
    internal static class SetDXCIncludeState
    {
        static string packageName = "com.stresslevelzero.urpconfig";
        public static bool Set(bool patched, uint major, uint minor, uint patch, uint build)
        {
            Debug.Log($"Setting DXCUpdateState");
            URPConfigManager.Initialize();
            string includePath = Path.Combine(URPConfigManager.packagePath, "include", "DXCUpdateState.hlsl");
            Debug.Log($"DXCUpdateState path: {includePath}");
            if (!File.Exists(includePath))
            {
                Debug.LogError($"Critical shader include file is missing ({includePath})");
                return false;
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
                Debug.Log($"DXCUpdateState needs to be updated!");
                File.WriteAllText(includePath, file);
            }

            return true;
        }
    }
}
