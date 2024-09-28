using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SLZ.SLZEditorTools
{
    internal static class URPConfigManager
    {

        public static readonly string packageName = "com.stresslevelzero.urpconfig";
        static string m_pkgPath;
        static bool m_initialized = false;
        public static string packagePath
        {
            get
            {
                if (string.IsNullOrEmpty(m_pkgPath))
                {
                    m_pkgPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Packages", packageName);
                }
                return m_pkgPath;
            }
        }

        [InitializeOnLoadMethod]
        public static void Initialize()
        {
            if (m_initialized || SessionState.GetBool("URPCfgInit", false))
            {
                //Debug.Log("Early Exit from URP Config Manager Init");
                return;
            }
            Debug.Log("Running URP Config Manager Init");

            if (!Directory.Exists(packagePath))
            {
                try
                {
                    string localPackage = Path.GetFullPath("Packages/com.unity.render-pipelines.universal/Editor/URPConfig/package~/com.stresslevelzero.urpconfig");
                    DirectoryInfo localPkgInfo = new DirectoryInfo(localPackage);
                    DirectoryInfo realPkgInfo = new DirectoryInfo(packagePath);
                    Debug.Log($"Cloning:\n{localPkgInfo.FullName}\n{realPkgInfo.FullName}");
                    realPkgInfo.Create();

                    CopyDirectory(localPkgInfo, realPkgInfo);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to clone urpconfig package: {ex.Message}");
                }
            }
            m_initialized = true;
            SessionState.SetBool("URPCfgInit", true);
        }

        // Why is this not a built-in function of System.IO?
        private static void CopyDirectory(DirectoryInfo src, DirectoryInfo dest)
        {

            foreach (FileInfo srcfile in src.EnumerateFiles())
            {
                FileInfo destFile = new FileInfo(Path.Combine(dest.FullName, srcfile.Name));
                srcfile.CopyTo(destFile.FullName, true);
            }

            foreach (DirectoryInfo srcChild in src.EnumerateDirectories())
            {
                DirectoryInfo destChild = new DirectoryInfo(Path.Combine(dest.FullName, srcChild.Name));
                destChild.Create();
                CopyDirectory(srcChild, destChild);
            }
        }
    }
}