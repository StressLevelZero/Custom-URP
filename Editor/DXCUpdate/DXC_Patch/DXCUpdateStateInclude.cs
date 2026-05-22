using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using SLZ.DXCUpdater;

namespace SLZ.SLZEditorTools
{
    [ScriptedImporter(0, "dxcguard", -3000)]
    public class DXCUpdateStateInclude : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            URPConfigManager.Initialize();
            CheckDXCInstallExternal.UpdateDXCIncludeState();
            ctx.DependsOnSourceAsset("Packages/com.stresslevelzero.urpconfig/include/DXCUpdateState.hlsl");
        }
    }
}
