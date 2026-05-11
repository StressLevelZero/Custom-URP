using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using SLZ.DXCUpdater;

namespace SLZ.SLZEditorTools
{
    [ScriptedImporter(0, "dxcguard")]
    public class DXCUpdateStateInclude : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            ctx.DependsOnSourceAsset("Packages/com.stresslevelzero.urpconfig/include/DXCUpdateState.hlsl");
            CheckDXCInstallExternal.UpdateDXCIncludeState();
        }
    }
}
