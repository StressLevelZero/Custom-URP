using System.Linq;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace SLZ.URPModResources
{
   
    public static class InitializeProject
    {
#if !MARROW_PROJECT
        [InitializeOnLoadMethod]
        public static void InitProject()
        {
            Events.registeredPackages += CheckPkgForURPInstall;
        }

        public static void CheckPkgForURPInstall(PackageRegistrationEventArgs args)
        {
            //Debug.Log("Checking Package");
            PackageInfo urp = args.added.FirstOrDefault(pkg => string.Equals(pkg.name, "com.unity.render-pipelines.universal"));

            if (urp != null)
            {
                string version = urp.version;
                Debug.Log("SLZ URP version " + version + " installed");
                PlatformQualitySetter.OverrideQualitySettings(EditorUserBuildSettings.activeBuildTarget);
                ExtractAssets.ExtractShaders(true);
                URPModUpdateShaderUI.ShowWindow();
            }
            else
            {
                PackageInfo urp2 = args.changedTo.FirstOrDefault(pkg => string.Equals(pkg.name, "com.unity.render-pipelines.universal"));
                if (urp2 != null)
                {
                    string version = urp2.version;
                    Debug.Log("SLZ URP updated to version " + version);
                    PlatformQualitySetter.OverrideQualitySettings(EditorUserBuildSettings.activeBuildTarget);
                    URPModUpdateShaderUI.ShowWindow();
                }
            }
            //Events.registeredPackages -= CheckPkgForURPInstall;
        }

       // [MenuItem("Stress Level Zero/Add default shaders to project")]
        public static void CopyShadersToProject()
        {
            string projectPath = Path.GetDirectoryName(Application.dataPath);
            string packagePath = Path.Combine(projectPath, "Packages/com.unity.render-pipelines.universal/ModResources/SLZShaders~/");
            string assetsPath = Path.Combine(projectPath, "Assets/SLZShaders/");

            CopyDirectory(packagePath, assetsPath, true);
            AssetDatabase.Refresh();
        }

        static void CopyShadersIfMissing()
        {
            string projectPath = Path.GetDirectoryName(Application.dataPath);
            string assetsPath = Path.Combine(projectPath, "Assets/SLZShaders/");
            if (!Directory.Exists(assetsPath))
            {
                CopyShadersToProject();
            }
        }

        static bool DeleteOldShaderSamples()
        {
            string oldShaderPath = Path.Combine(Application.dataPath, "Samples/SLZ Universal RP/8148.0.2/SLZ Bonelab Shaders");
            if (Directory.Exists(oldShaderPath))
            {
                Directory.Delete(oldShaderPath, true);
                File.Delete(oldShaderPath + ".meta");
                return true;
            }
            else
            {
                return false;
            }
        }

        static bool DeleteOldAmplifySamples()
        {
            string oldShaderPath = Path.Combine(Application.dataPath, "Samples/SLZ Universal RP/8148.0.2/Amplify Shader Extentions");
            if (Directory.Exists(oldShaderPath))
            {
                Directory.Delete(oldShaderPath, true);
                File.Delete(oldShaderPath + ".meta");
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Copied from Microsoft docs (https://learn.microsoft.com/en-us/dotnet/standard/io/how-to-copy-directories), kinda silly that System.IO has no method for copying a folder.
        /// </summary>
        /// <param name="sourceDir"></param>
        /// <param name="destinationDir"></param>
        /// <param name="recursive"></param>
        /// <exception cref="DirectoryNotFoundException"></exception>
        static void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
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

            // If recursive and copying subdirectories, recursively call this method
            if (recursive)
            {
                foreach (DirectoryInfo subDir in dirs)
                {
                    string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                    CopyDirectory(subDir.FullName, newDestinationDir, true);
                }
            }
        }
#endif

#if SLZ_RENDERPIPELINE_DEV

#endif
    }
}
