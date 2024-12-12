using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

public static class EndUnityIfPipelineUpdates
{

    [InitializeOnLoadMethod]
    static void ThisAssemblyReload()
    {
        CheckOrDie();
        AssetDatabase.importPackageStarted -= OnImportPackageStarted;
        AssetDatabase.importPackageStarted += OnImportPackageStarted;
    }

    static void OnImportPackageStarted(string packageName)
    {
        if (packageName.Equals("com.unity.render-pipelines.universal") || packageName.Equals("com.unity.render-pipelines.core"))
        {
            CheckOrDie();
        }
    }

    static void CheckOrDie()
    {

        var urpPkgInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.unity.render-pipelines.universal");
        var corePkgInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.unity.render-pipelines.core");

        string currentUrpVersion = urpPkgInfo.version != null ? urpPkgInfo.version : "0";
        string currentCoreVersion = corePkgInfo.version != null ? corePkgInfo.version : "0";


        string oldUrpVersion = SessionState.GetString("URPHash", string.Empty);
        string oldCoreVersion = SessionState.GetString("SRPCoreHash", string.Empty);
        bool noOldUrpVersion = string.IsNullOrEmpty(oldUrpVersion);
        bool noOldCoreVersion = string.IsNullOrEmpty(oldCoreVersion);

        if (noOldUrpVersion || noOldCoreVersion)
        {
            Debug.Log($"URP Version - old: {oldUrpVersion}, current: {currentUrpVersion},\nSRP Core Version - old: {oldCoreVersion}, current: {currentCoreVersion}");
        }

        if (noOldUrpVersion)
        {
            SessionState.SetString("URPHash", currentUrpVersion);
            oldUrpVersion = currentUrpVersion;
        }
        if (noOldCoreVersion)
        {
            SessionState.SetString("SRPCoreHash", currentCoreVersion);
            oldCoreVersion = currentCoreVersion;
        }  

        if (!string.Equals(oldUrpVersion, currentUrpVersion) || !string.Equals(oldCoreVersion, currentCoreVersion))
        {
            Debug.Log($"URP Version - old: {oldUrpVersion}, current: {currentUrpVersion},\nSRP Core Version - old: {oldCoreVersion}, current: {currentCoreVersion}");
            Debug.LogError("PANIC - URP or Core pipelines updated while unity was open! Force closing unity!");
            Instagib();
        }
    }

    static void Instagib()
    {
        Debug.LogWarning("Killing Unity immediately");
        int procID = Process.GetCurrentProcess().Id;
        Process cmd = new Process();
        cmd.StartInfo.FileName = "taskkill";
        cmd.StartInfo.Arguments = $"/F /PID {procID}";
        cmd.StartInfo.UseShellExecute = false;
        cmd.Start();
    }
}
