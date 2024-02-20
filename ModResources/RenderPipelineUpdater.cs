using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json;
using UnityEngine;
using UnityEditor;
using Debug = UnityEngine.Debug;
using System.Text;
using UnityEditor.SceneManagement;
using System.IO;
using Newtonsoft.Json.Linq;

namespace SLZ.URPModResources
{
    public class RenderPipelineUpdater
    {
        static string coreBranch = "Bonelab";
        static string urpBranch = "Bonelab";
        /// <summary>
        /// Updates the Core and Universal RP for the user. Gets the latest commit hashes from git,
        /// replaces the hashes in the packages-lock.json, and closes unity before it has a chance
        /// to corrupt shaders by importing them out of order with their dependencies.
        /// </summary>
        [MenuItem("Stress Level Zero/Check for Render Pipeline Updates", priority = 0)]
        public static void UpdateRenderPipelines()
        {
            // Ask the user if they want to update, as doing so will close unity
            bool consent = EditorUtility.DisplayDialog("SLZ RP Updater", "Check for Render Pipeline Updates? This will save your open scenes and restart unity if updates are found", "Update and Restart Unity", "Cancel");
            if (!consent)
            {
                return;
            }



            // Use git to get the hashes of the lastest commits 
            Process git = new Process();
            git.StartInfo.UseShellExecute = false;
            git.StartInfo.RedirectStandardOutput = true;
            git.StartInfo.RedirectStandardError = true;
            git.StartInfo.FileName = "git";
            git.StartInfo.Arguments = "ls-remote https://github.com/StressLevelZero/Custom-RenderPipelineCore refs/heads/" + coreBranch;
            var sbOut = new StringBuilder();
            var sbErr = new StringBuilder();

            git.OutputDataReceived += (sender, args) => sbOut.AppendLine(args.Data);
            git.ErrorDataReceived += (sender, args) => sbErr.AppendLine(args.Data);

            git.Start();
            git.BeginOutputReadLine();
            git.BeginErrorReadLine();
            git.WaitForExit();
            git.CancelErrorRead();
            git.CancelOutputRead();

            string coreHash = sbOut.ToString();
            string coreError = sbErr.ToString();
            if (!string.IsNullOrWhiteSpace(coreError) && coreError.Length > 2) // stderr is always at least 2 characters on windows (CRLF)
            {
                Debug.LogError("SLZ RP Updater - Unable to fetch latest Core RP version, git failed with message:\n " + coreError);
                EditorUtility.DisplayDialog("SLZ RP Updater", "Unable to fetch latest Core RP version, check console for error message", "Ok");
                return;
            }
            if (coreHash.Length < 40) // git hash length is 40 characters, so if its less than that then most likely there are no commits on the branch we just checked.
            {
                Debug.LogError("SLZ RP Updater - Unable to fetch latest Core RP version, no commits found on " + coreBranch + " branch");
                EditorUtility.DisplayDialog("SLZ RP Updater", "Unable to fetch latest Core RP version, no commits found on " + coreBranch + " branch. Check online to see if the Core Render Pipeline has moved.", "Ok");
                return;
            }
            coreHash = coreHash.Substring(0, 40);
            sbErr.Clear();
            sbOut.Clear();

            git.StartInfo.Arguments = "ls-remote https://github.com/StressLevelZero/Custom-URP refs/heads/" + urpBranch;
            git.Start();
            git.BeginOutputReadLine();
            git.BeginErrorReadLine();
            git.WaitForExit();

            string urpHash = sbOut.ToString();
            string urpError = sbErr.ToString();
            if (!string.IsNullOrWhiteSpace(urpError) && urpError.Length > 2)
            {

                Debug.LogError("SLZ RP Updater - Unable to fetch latest Universal RP version, git failed with message:\n " + urpError);
                EditorUtility.DisplayDialog("SLZ RP Updater", "Unable to fetch latest Universal RP version, check console for error message", "Ok");
                return;
            }
            if (urpHash.Length < 40)
            {
                Debug.LogError("SLZ RP Updater - Unable to fetch latest Universal RP version, no commits found on " + urpBranch + " branch");
                EditorUtility.DisplayDialog("SLZ RP Updater", "Unable to fetch latest Universal RP version, no commits found on " + urpBranch + " branch. Check online to see if the Custom URP has moved.", "Ok");
                return;
            }
            urpHash = urpHash.Substring(0, 40);


            string projectPath = Path.GetDirectoryName(Application.dataPath);
            string packLockPath = Path.Combine(projectPath, "Packages", "packages-lock.json");
            if (!File.Exists(packLockPath))
            {
                Debug.LogError("SLZ RP Updater - Could not find packages-lock.json at:\n " + packLockPath);
                EditorUtility.DisplayDialog("SLZ RP Updater", "Canceling update: Could not find packages-lock.json at " + packLockPath, "Ok");
            }
            string packLockContents = File.ReadAllText(packLockPath);
            dynamic packLockObj = JObject.Parse(packLockContents);

            bool coreUpdated = UpdatePackage(ref packLockObj, "com.unity.render-pipelines.core", coreHash, "Core RP");
            bool urpUpdated = UpdatePackage(ref packLockObj, "com.unity.render-pipelines.universal", urpHash, "SLZ Universal RP");

            bool anyUpdate = coreUpdated || urpUpdated;
            
            if (!anyUpdate)
            {
                EditorUtility.DisplayDialog("SLZ RP Updater", "No updates found", "Ok");
                return;
            }

            // Try to save the open scenes. If the saving fails for some reason, present the user with the choice of continuing or canceling again.
            bool scenesSaved = EditorSceneManager.SaveOpenScenes();
            if (!scenesSaved)
            {
                bool failSaveContinue = EditorUtility.DisplayDialog("SLZ RP Updater", "Failed to save open scenes, continue anyway?", "Continue", "Cancel");
                if (!failSaveContinue)
                {
                    return;
                }
            }

            // Backup packages-lock and overwrite it with the modified version
            string json = JsonConvert.SerializeObject(packLockObj, Formatting.Indented);
            File.Copy(packLockPath, packLockPath + ".bak", true);
            File.WriteAllText(packLockPath, json);

            // Open the project in another unity instance before we close 
            Process unity = new Process();
            unity.StartInfo.FileName = EditorApplication.applicationPath;
            unity.StartInfo.Arguments = "-projectPath \"" + projectPath + "\"";
            unity.Start();
            EditorApplication.Exit(0);
        }

        static bool UpdatePackage(ref dynamic json, string packageName, string hash, string friendlyName)
        {
            string pkgSource = json.dependencies[packageName].source;
            bool pkgFromGit = true;         
            if (!string.Equals(pkgSource, "git"))
            {
                Debug.LogError("SLZ RP Updater - " + friendlyName + " not installed from unity package manager, cannot update package\n ");
                EditorUtility.DisplayDialog("SLZ RP Updater", friendlyName + " not installed from unity package manager, cannot update package", "Ok");
                pkgFromGit = false;
            }
            if (pkgFromGit)
            {
                bool pkgUpdate = !string.Equals(json.dependencies[packageName].hash.ToString(), hash);
                if (pkgUpdate)
                {
                    json.dependencies[packageName].hash = hash;
                    return true;
                }
            }
            return false;
        }
    }
}
