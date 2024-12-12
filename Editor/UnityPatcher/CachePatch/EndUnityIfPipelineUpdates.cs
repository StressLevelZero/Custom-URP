using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using UnityEditor.PackageManager;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

public static class EndUnityIfPipelineUpdates
{

    [InitializeOnLoadMethod]
    static void ThisAssemblyReload()
    {
        CheckOrDie();
        Events.registeringPackages -= RegisteringPackages;
        Events.registeringPackages += RegisteringPackages;
    }

    static void RegisteringPackages(PackageRegistrationEventArgs args)
    {
        Debug.Log("Ran RegisteringPackages");
        PackageInfo oldCore = null;
        PackageInfo newCore = null;

        PackageInfo oldURP = null;
        PackageInfo newURP = null;

        int numChanged = args.changedFrom.Count;
        Debug.Log("Num Changed: " + numChanged);
        for (int pkgIdx = 0; pkgIdx < numChanged; pkgIdx++)
        {
            //Debug.Log("Changed: " + args.changedFrom[pkgIdx].packageId);
            if (oldURP == null && args.changedFrom[pkgIdx].packageId.StartsWith("com.unity.render-pipelines.universal@"))
            {
                oldURP = args.changedFrom[pkgIdx];
                newURP = args.changedTo[pkgIdx];
            }
            else if (oldCore == null && args.changedFrom[pkgIdx].packageId.StartsWith("com.unity.render-pipelines.core@"))
            {
                oldCore = args.changedFrom[pkgIdx];
                newCore = args.changedTo[pkgIdx];
            }
        }
        if (oldCore != null || oldURP != null)
        {
            bool urpChanged = oldURP != null && !string.Equals(oldURP.version, newURP.version);
            bool coreChanged = oldCore != null && !string.Equals(oldCore.version, newCore.version);
            if (coreChanged || urpChanged)
            {
                Debug.Log($"URP Version - old: {oldURP?.version}, current: {newURP?.version},\nSRP Core Version - old: {oldCore?.version}, current: {newCore?.version}");
                Debug.LogError("PANIC - URP or Core pipelines updated while unity was open! Force closing unity!");
                Instagib();
            }
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

        if (!(noOldUrpVersion && noOldCoreVersion) && (!string.Equals(oldUrpVersion, currentUrpVersion) || !string.Equals(oldCoreVersion, currentCoreVersion)))
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
