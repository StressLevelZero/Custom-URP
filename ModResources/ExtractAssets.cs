using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SLZ.URPModResources
{
    /// <summary>
    /// Extracts assets from hidden folders inside the URP package into the project. Made as a replacement for Unity's sample system
    /// that we were using before to address the issue that unity installs the assets to a path relative to the package version number.
    /// Shader includes rely on absolute paths, so the sample system is unsuitable for this usecase. Also the samples are buried in
    /// Unity's package manager so most users won't even know they exist.
    /// </summary>
    public static class ExtractAssets
    {
#if !MARROW_PROJECT
        static string pkgPathShader = "Packages/com.unity.render-pipelines.universal/ModResources/SLZShaders~/";
        static string pkgPathAmplify = "Packages/com.unity.render-pipelines.universal/ModResources/AmplifyExtensions~/";
        static string assetPathShader = "Assets/SLZShaders/";
        static string assetPathAmplify = "Assets/AmplifyShaderEditor/";

        /// <summary>
        /// Extract the bonelab shader files into the assets folder.
        /// </summary>
        /// <param name="cleanInstall">Do we delete all files in the project with the same GUID's those in this package? GUIDs come from .GUIDList.txt in the package folder</param>
        internal static void ExtractShaders(bool cleanInstall)
        {
            string projectPath = Path.GetDirectoryName(Application.dataPath);
            string packagePath = Path.Combine(projectPath, pkgPathShader);
            string assetsPath = Path.Combine(projectPath, assetPathShader);
            if (cleanInstall)
            {
                string GUIDListPath = packagePath + ".GUIDList.txt";
                if (File.Exists(GUIDListPath))
                {
                    NukeOldFiles(GUIDListPath);
                }
                else
                {
                    Debug.LogWarning("Missing GUID list, will not attempt to delete old versions of files. GUIDs may get mangled if imported assets have same GUIDs as other assets in the project!");
                }
            }
            CopyDirectory(packagePath, assetsPath);
            AssetDatabase.Refresh();
        }

        internal static void ExtractAmplify(bool cleanInstall)
        {
            string projectPath = Path.GetDirectoryName(Application.dataPath);
            string packagePath = Path.Combine(projectPath, pkgPathAmplify);
            string assetsPath = Path.Combine(projectPath, assetPathAmplify);
            string GUIDListPath = packagePath + ".GUIDList.txt";
            if (cleanInstall)
            {
                if (File.Exists(GUIDListPath))
                {
                    NukeOldFiles(GUIDListPath);
                }
                else
                {
                    Debug.LogWarning("Missing GUID list, will not attempt to delete old versions of files. GUIDs may get mangled if imported assets have same GUIDs as other assets in the project!");
                }
            }
            CopyDirectory(packagePath, assetsPath);
            AssetDatabase.Refresh();
        }

        static void NukeOldFiles(string GUIDListPath)
        {
            string projectPath = Path.GetDirectoryName(Application.dataPath);
            List<string> deletePaths = new List<string>(); 
            using (StreamReader sr = new StreamReader(GUIDListPath))
            {
                string guid;
                while ((guid = sr.ReadLine()) != null && guid.Length >= 32)
                {
                    guid = guid.Substring(0, 32);
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(assetPath) && File.Exists(Path.Combine(projectPath, assetPath)) && !assetPath.StartsWith("Packages"))
                    {
                        deletePaths.Add(assetPath);
                        //AssetDatabase.DeleteAsset(assetPath);
                        //File.Delete(Path.Combine(projectPath, assetPath));
                        //File.Delete(Path.Combine(projectPath, assetPath + ".meta"));
                    }
                }
            }
            List<string> failedPaths = new List<string>();
            AssetDatabase.DeleteAssets(deletePaths.ToArray(), failedPaths);
        }

        /// <summary>
        /// Copied from Microsoft docs (https://learn.microsoft.com/en-us/dotnet/standard/io/how-to-copy-directories), kinda silly that System.IO has no method for copying a folder.
        /// </summary>
        /// <param name="sourceDir">Directory to copy from</param>
        /// <param name="destinationDir">Directory to copy to</param>
        /// <exception cref="DirectoryNotFoundException"></exception>
        static void CopyDirectory(string sourceDir, string destinationDir)
        {
            // Get information about the source directory
            var dir = new DirectoryInfo(sourceDir);

            // Check if the source directory exists
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

            // Cache directories before we start copying
            DirectoryInfo[] dirs = dir.GetDirectories();

            // Create the destination directory
            Directory.CreateDirectory(destinationDir);

            // Get the files in the source directory and copy to the destination directory
            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, true);
            }


            foreach (DirectoryInfo subDir in dirs)
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestinationDir);
            }
        }
#endif
    }
}
