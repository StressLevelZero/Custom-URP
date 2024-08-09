using SLZ.SLZEditorTools;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SLZ.EditorPatcher
{
    internal static class SetDXCIncludeState
    {
        static string packageName = "com.stresslevelzero.urpconfig";
        public static bool Set(bool patched)
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
                $"#ifndef SLZ_DXC_STATE\n\t#define SLZ_DXC_STATE\n\t{comment}#define SLZ_DXC_UPDATED\n#endif";
            File.WriteAllText(includePath, file);

            return true;
        }
    }
}
