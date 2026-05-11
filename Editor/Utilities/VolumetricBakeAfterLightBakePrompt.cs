using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SLZ.SLZEditorTools
{
    [InitializeOnLoad]
    public static class VolumetricBakeAfterLightBakePrompt 
    {
        // ── Prefs ─────────────────────────────────────────────────────────────
        const string PrefBehavior             = "SLZ.VolBakePrompt.Behavior";
        const string PrefPromptOnAutoGenerate = "SLZ.VolBakePrompt.AutoGenerate";
        const string SessionBakeStartedKey    = "SLZ.VolBakePrompt.BakeStarted";

        public const string SettingsPath = "Preferences/Stress Level Zero/Volumetric Bake";

        public enum AfterLightBakeBehavior
        {
            Disabled = 0,
            Prompt   = 1,
            AutoBake = 2,
        }

        public static AfterLightBakeBehavior Behavior
        {
            get => (AfterLightBakeBehavior)EditorPrefs.GetInt(PrefBehavior, (int)AfterLightBakeBehavior.Prompt);
            set => EditorPrefs.SetInt(PrefBehavior, (int)value);
        }

        public static bool PromptOnAutoGenerate
        {
            get => EditorPrefs.GetBool(PrefPromptOnAutoGenerate, false);
            set => EditorPrefs.SetBool(PrefPromptOnAutoGenerate, value);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        static VolumetricBakeAfterLightBakePrompt()
        {
            Lightmapping.bakeStarted   -= OnBakeStarted;
            //Lightmapping.bakeCompleted -= OnBakeCompleted;
            Lightmapping.bakeStarted   += OnBakeStarted;
            //Lightmapping.bakeCompleted += OnBakeCompleted;
            SortedPostBakeEvent.Unregister(VolOnBakeCompleted, PostBakeOrder.VolumetricBake);
            SortedPostBakeEvent.Register(VolOnBakeCompleted, PostBakeOrder.VolumetricBake);
        }

        // ── Menu item — pings the prefs page ──────────────────────────────────

        [MenuItem("Stress Level Zero/Volumetrics/After Light Bake \u2014 Settings \u2197", priority = 200)]
        static void OpenSettings()
        {
            SettingsService.OpenUserPreferences(SettingsPath);
        }

        // ── Settings provider ─────────────────────────────────────────────────

        [SettingsProvider]
        static SettingsProvider CreateSettingsProvider() => new SettingsProvider(SettingsPath, SettingsScope.User)
        {
            label      = "Volumetric Bake",
            guiHandler = _ => OnPreferencesGUI()
        };

        static void OnPreferencesGUI()
        {
            EditorGUILayout.Space(8);
            var headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            EditorGUILayout.LabelField("Volumetric Bake", headerStyle);
            EditorGUILayout.Space(8);

            // ── After Light Bake ──────────────────────────────────────────────
            EditorGUILayout.LabelField("After Light Bake", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            var newBehavior = (AfterLightBakeBehavior)EditorGUILayout.EnumPopup(
                new GUIContent("Behavior",
                    "Disabled  — do nothing after a light bake completes.\n\n" +
                    "Prompt    — show a dialog asking whether to launch a volumetric bake.\n\n" +
                    "Auto-Bake — silently save all scenes and launch the volumetric bake automatically."),
                Behavior);

            if (newBehavior != Behavior)
                Behavior = newBehavior;

            EditorGUI.BeginDisabledGroup(Behavior == AfterLightBakeBehavior.Disabled);

            bool newAutoGen = EditorGUILayout.Toggle(
                new GUIContent("React to Auto Generate",
                    "When enabled, the selected behavior also triggers during " +
                    "continuous Auto Generate light bakes. Disabled by default " +
                    "to avoid launching a bake on every incremental update."),
                PromptOnAutoGenerate);

            if (newAutoGen != PromptOnAutoGenerate)
                PromptOnAutoGenerate = newAutoGen;

            EditorGUI.EndDisabledGroup();

            // Contextual help blurb beneath the dropdown
            EditorGUILayout.Space(4);
            switch (Behavior)
            {
                case AfterLightBakeBehavior.Disabled:
                    EditorGUILayout.HelpBox(
                        "No action will be taken after a light bake.",
                        MessageType.None);
                    break;
                case AfterLightBakeBehavior.Prompt:
                    EditorGUILayout.HelpBox(
                        "A dialog will appear after each light bake asking whether to " +
                        "save and launch the volumetric bake.",
                        MessageType.None);
                    break;
                case AfterLightBakeBehavior.AutoBake:
                    EditorGUILayout.HelpBox(
                        "After each light bake, all open scenes and assets are saved " +
                        "automatically and the volumetric bake launches immediately — " +
                        "no confirmation required.",
                        MessageType.Warning);
                    break;
            }

            EditorGUI.indentLevel--;
        }

        // ── Bake event handlers ───────────────────────────────────────────────

        static void OnBakeStarted()
        {
            if (Application.isBatchMode) return;
            if (Behavior == AfterLightBakeBehavior.Disabled) return;

            bool isAutoGenerate = Lightmapping.giWorkflowMode == Lightmapping.GIWorkflowMode.Iterative;
            if (isAutoGenerate && !PromptOnAutoGenerate) return;

            SessionState.SetBool(SessionBakeStartedKey, true);
        }

        static void VolOnBakeCompleted()
        {
            if (Application.isBatchMode) return;
            if (Behavior == AfterLightBakeBehavior.Disabled) return;
            if (!SessionState.GetBool(SessionBakeStartedKey, false)) return;

            SessionState.SetBool(SessionBakeStartedKey, false);

            // Delay one tick so we're out of Unity's bake callback stack / progress scopes
            EditorApplication.delayCall += ShowPromptIfApplicable;
        }

        // ── Core logic ────────────────────────────────────────────────────────

        static void ShowPromptIfApplicable()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || string.IsNullOrEmpty(activeScene.path))
            {
                Debug.LogWarning("Volumetric post-bake prompt skipped: active scene is unsaved.");
                return;
            }

            int volAreaCount = 0;
            try { volAreaCount = VolumetricRegisters.volumetricAreas?.Count ?? 0; }
            catch { }

            if (volAreaCount <= 0)
            {
                Debug.Log("Volumetric post-bake prompt skipped: no volumetric areas found.");
                return;
            }

            switch (Behavior)
            {
                case AfterLightBakeBehavior.AutoBake:
                    Debug.Log($"[VolBake] Auto-bake triggered — saving and launching volumetric bake ({volAreaCount} area(s)).");
                    LaunchBake();
                    break;

                case AfterLightBakeBehavior.Prompt:
                    bool result = EditorUtility.DisplayDialog(
                        "Light Bake Complete",
                        $"Light baking finished.\n\nFound {volAreaCount} volumetric area(s) in the loaded scene.\n\nSave and launch volumetric bake now?",
                        "Save + Bake Volumetrics",
                        "Not Now"
                    );
                    if (result) LaunchBake();
                    break;
            }
        }

        static void LaunchBake()
        {
            try
            {
                EditorSceneManager.SaveOpenScenes();
                AssetDatabase.SaveAssets();
                VolBakeLaunchMenu.VolumetricBake_BatchAndRelaunch();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog(
                    "Volumetric Bake Launch Failed",
                    $"Failed to launch volumetric bake.\n\n{ex.Message}",
                    "OK"
                );
            }
        }
    }
}