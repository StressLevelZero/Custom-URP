using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace SLZ.SLZEditorTools
{
    /// <summary>
    /// Stores notification provider settings for the VolBake pipeline.
    /// All values are EditorPrefs (per-machine, never committed to source control).
    ///
    /// To add a new provider:
    ///   1. Add pref keys + public accessors below
    ///   2. Add a UI section in OnGUI()
    ///   3. Add a Send-XxxNotification function in VolBakeLaunchMenu.BuildPS1Main()
    ///   4. Call it from Send-VolBakeNotification in the same method
    /// </summary>
    public static class VolBakeNotifier
    {
        // ── Discord ────────────────────────────────────────────────────────────
        // Webhook posts to any channel. For personal/DM-style push notifications,
        // create a private server with only yourself — messages there push to your
        // phone exactly like DMs. Get a webhook from:
        //   Server Settings → Integrations → Webhooks → New Webhook → Copy URL
        const string kDiscordEnabled    = "VolBake.Notify.Discord.Enabled";
        const string kDiscordWebhookUrl = "VolBake.Notify.Discord.WebhookUrl";
        const string kDiscordMention    = "VolBake.Notify.Discord.Mention";

        public static bool   DiscordEnabled    => EditorPrefs.GetBool(kDiscordEnabled, false);
        public static string DiscordWebhookUrl => EditorPrefs.GetString(kDiscordWebhookUrl, "");
        public static string DiscordMention    => EditorPrefs.GetString(kDiscordMention, "");

        // ── ntfy.sh ────────────────────────────────────────────────────────────
        // Free, open-source push notifications. No account needed.
        //   1. Install the ntfy app on Android or iOS
        //   2. Pick any unique topic name (treat it like a password — it's public)
        //   3. Subscribe to that topic in the app
        //   4. Paste the topic name here
        // Self-hosted ntfy server? Use a full URL as the topic: https://ntfy.myserver.com/mytopic
        const string kNtfyEnabled = "VolBake.Notify.Ntfy.Enabled";
        const string kNtfyTopic   = "VolBake.Notify.Ntfy.Topic";

        public static bool   NtfyEnabled => EditorPrefs.GetBool(kNtfyEnabled, false);
        public static string NtfyTopic   => EditorPrefs.GetString(kNtfyTopic, "");

        // ── Add future providers here ──────────────────────────────────────────
        // Follow the same pattern: pref keys, public accessors, UI block, PS1 function.


        // ── Settings UI ───────────────────────────────────────────────────────
        [SettingsProvider]
        static SettingsProvider CreateSettingsProvider() => new SettingsProvider(
            "Preferences/Stress Level Zero/Volumetrics Notifications", SettingsScope.User)
        {
            label = "VolBake Notifications",
            guiHandler = _ => OnGUI()
        };

        static void OnGUI()
        {
            EditorGUILayout.Space(8);
            var header = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            EditorGUILayout.LabelField("VolBake — Completion Notifications", header);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Notifications fire from the batch PS1 window after Unity exits, " +
                "so they work even if Unity crashes mid-bake.",
                MessageType.Info);

            // ── Discord ───────────────────────────────────────────────────────
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Discord", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            bool discordOn = EditorGUILayout.Toggle("Enabled", DiscordEnabled);
            EditorPrefs.SetBool(kDiscordEnabled, discordOn);

            EditorGUI.BeginDisabledGroup(!discordOn);

            string webhookUrl = EditorGUILayout.TextField("Webhook URL", DiscordWebhookUrl);
            EditorPrefs.SetString(kDiscordWebhookUrl, webhookUrl.Trim());

            string mention = EditorGUILayout.TextField(
                new GUIContent("Mention (optional)",
                    "Prepended to the message.\n" +
                    "Examples:\n" +
                    "  <@123456789>  — ping a specific user (enable Developer Mode, right-click name → Copy ID)\n" +
                    "  @here         — ping online members\n" +
                    "  @everyone     — ping all members"),
                DiscordMention);
            EditorPrefs.SetString(kDiscordMention, mention.Trim());

            EditorGUILayout.HelpBox(
                "Tip: for DM-style notifications, create a private Discord server with only yourself " +
                "and add the webhook to any channel there.",
                MessageType.None);

            EditorGUILayout.Space(2);
            if (GUILayout.Button("Send Test Notification", GUILayout.Width(200)))
                FireTestPS1("Discord", BuildDiscordTestPS1(webhookUrl.Trim(), mention.Trim()));

            EditorGUI.EndDisabledGroup();
            EditorGUI.indentLevel--;

            // ── ntfy.sh ───────────────────────────────────────────────────────
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("ntfy.sh", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            bool ntfyOn = EditorGUILayout.Toggle("Enabled", NtfyEnabled);
            EditorPrefs.SetBool(kNtfyEnabled, ntfyOn);

            EditorGUI.BeginDisabledGroup(!ntfyOn);

            string ntfyTopic = EditorGUILayout.TextField(
                new GUIContent("Topic",
                    "Any unique string. Subscribe to the same topic in the ntfy app.\n" +
                    "Treat it like a password — anyone who knows it can send you messages.\n\n" +
                    "Self-hosted: use the full URL, e.g. https://ntfy.myserver.com/mytopic"),
                NtfyTopic);
            EditorPrefs.SetString(kNtfyTopic, ntfyTopic.Trim());

            EditorGUILayout.HelpBox(
                "Install the free ntfy app on Android or iOS, then subscribe to your topic. " +
                "No account required.",
                MessageType.None);

            EditorGUILayout.Space(2);
            if (GUILayout.Button("Send Test Notification", GUILayout.Width(200)))
                FireTestPS1("ntfy", BuildNtfyTestPS1(ntfyTopic.Trim()));

            EditorGUI.EndDisabledGroup();
            EditorGUI.indentLevel--;

            // ── Future providers go here ──────────────────────────────────────
        }

        // ── Test helpers ──────────────────────────────────────────────────────

        static void FireTestPS1(string providerName, string ps1)
        {
            if (string.IsNullOrEmpty(ps1))
            {
                EditorUtility.DisplayDialog("VolBake", $"{providerName}: required fields are empty.", "OK");
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName         = "powershell.exe",
                Arguments        = $"-NoProfile -ExecutionPolicy Bypass -Command \"{ps1.Replace("\"", "\\\"")}\"",
                UseShellExecute  = true,
                WindowStyle      = ProcessWindowStyle.Normal
            });
        }

        static string BuildDiscordTestPS1(string url, string mention)
        {
            if (string.IsNullOrEmpty(url)) return null;
            string safeUrl     = url.Replace("'", "''");
            string safeMention = mention.Replace("'", "''");
            return
                $"$prefix = if ('{safeMention}') {{ '{safeMention} ' }} else {{ '' }}; " +
                $"$body = @{{ content = ($prefix + ':bell: VolBake test — webhooks are working.') }} | ConvertTo-Json; " +
                $"try {{ Invoke-RestMethod -Uri '{safeUrl}' -Method Post -Body $body -ContentType 'application/json'; " +
                $"Write-Host 'OK' }} catch {{ Write-Host ('FAILED: ' + $_.Exception.Message) -ForegroundColor Red; exit 1 }}";
        }

        static string BuildNtfyTestPS1(string topic)
        {
            if (string.IsNullOrEmpty(topic)) return null;
            // Support both plain topic names (ntfy.sh) and full self-hosted URLs
            string safeTopic = topic.Replace("'", "''");
            string uri = topic.StartsWith("http") ? safeTopic : $"https://ntfy.sh/{safeTopic}";
            return
                $"$headers = @{{ Title = 'VolBake Test'; Tags = 'bell'; Priority = 'default' }}; " +
                $"try {{ Invoke-RestMethod -Uri '{uri}' -Method Post -Body 'VolBake test — push is working.' -Headers $headers; " +
                $"Write-Host 'OK' }} catch {{ Write-Host ('FAILED: ' + $_.Exception.Message) -ForegroundColor Red; exit 1 }}";
        }
    }
}