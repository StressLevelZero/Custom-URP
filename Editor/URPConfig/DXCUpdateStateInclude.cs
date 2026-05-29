using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using SLZ.DXCUpdater;

namespace SLZ.SLZEditorTools
{
    [ScriptedImporter(0, new string[] { "dxcguard" }, null, -3000, AllowCaching = false)]
    public class DXCUpdateStateInclude : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            URPConfigManager.Initialize();
            SetDXCIncludeState.UpdateDXCIncludeState();
            ctx.DependsOnSourceAsset("Packages/com.stresslevelzero.urpconfig/include/DXCUpdateState.hlsl");
        }
    }
}
