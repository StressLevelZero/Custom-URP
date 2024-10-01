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
    static void CheckOrDie()
    {
        var urpPkgInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.unity.render-pipelines.universal");
        var corePkgInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.unity.render-pipelines.core");

        string currentUrpHash = urpPkgInfo.version != null ? urpPkgInfo.version : "0";
        string currentCoreHash = corePkgInfo.version != null ? corePkgInfo.version : "0";
        //Debug.Log($"URP Git Hash: {currentUrpHash}");
        //Debug.Log($"SRP Core Git Hash: {currentCoreHash}");
        string oldUrpHash = SessionState.GetString("URPHash", string.Empty);
        string oldCoreHash = SessionState.GetString("SRPCoreHash", string.Empty);

        if (string.IsNullOrEmpty(oldUrpHash))
        {
            Debug.Log($"URP Version: {currentUrpHash}"); 
            SessionState.SetString("URPHash", currentUrpHash);
            oldUrpHash = currentUrpHash;
        }
        if (string.IsNullOrEmpty(oldCoreHash))
        {
            Debug.Log($"SRP Core version: {currentCoreHash}");
            SessionState.SetString("SRPCoreHash", currentCoreHash);
            oldCoreHash = currentCoreHash;
        }

        if (!string.Equals(oldUrpHash, currentUrpHash) || !string.Equals(oldCoreHash, currentCoreHash))
        {
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
