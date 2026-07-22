using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using RevitQuickAccess.Settings;
using RevitQuickAccess.UI;

namespace RevitQuickAccess.Update
{
    /// <summary>
    /// Silent self-update from GitHub Releases.
    ///
    /// Runs in the background at Revit startup so it never blocks loading. If the latest release is
    /// newer than the running assembly, the new DLL is downloaded and swapped in place — the loaded
    /// file can't be overwritten, but on Windows it CAN be renamed, so the old one is moved aside and
    /// the new one takes its name. Revit picks it up on the next launch.
    ///
    /// Public repo ⇒ no tokens, no secrets. Nothing is sent anywhere; only a GET to the GitHub API.
    /// </summary>
    public static class UpdateService
    {
        private const string AssetName = "RevitQuickAccess.dll";

        public static void CheckInBackground()
        {
            if (!PluginSettings.AutoUpdate) return;
            if (string.IsNullOrWhiteSpace(PluginSettings.UpdateRepo)) return;

            Task.Run(async () =>
            {
                try { await CheckAsync(); }
                catch (Exception ex) { Log("ошибка: " + ex.Message); }
            });
        }

        private static async Task CheckAsync()
        {
            Version current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("RevitQuickAccess-Updater");

            string url = $"https://api.github.com/repos/{PluginSettings.UpdateRepo.Trim()}/releases/latest";
            string json = await http.GetStringAsync(url);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            Version latest = ParseVersion(tag);
            if (latest == null) { Log($"не разобрал версию релиза «{tag}»"); return; }
            if (latest <= current) { Log($"актуально: {current} (релиз {tag})"); return; }

            string dllUrl = null;
            if (root.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                    if (a.TryGetProperty("name", out var n) &&
                        string.Equals(n.GetString(), AssetName, StringComparison.OrdinalIgnoreCase) &&
                        a.TryGetProperty("browser_download_url", out var u))
                        dllUrl = u.GetString();

            if (string.IsNullOrEmpty(dllUrl)) { Log($"в релизе {tag} нет файла {AssetName}"); return; }

            byte[] data = await http.GetByteArrayAsync(dllUrl);
            if (data == null || data.Length < 1024) { Log("скачанный файл подозрительно мал — пропуск"); return; }

            Stage(data);
            Log($"обновлено {current} → {latest}");
            ToastNotifier.Show($"Обновление {latest} загружено — применится после перезапуска Revit", true);
        }

        /// <summary>Put the new DLL in place of the running one (rename-aside trick).</summary>
        private static void Stage(byte[] dll)
        {
            string dir = BaseDir();
            string target = Path.Combine(dir, AssetName);

            foreach (var f in SafeFiles(dir, "*.old_*")) { try { File.Delete(f); } catch { } }

            try { if (File.Exists(target)) File.Move(target, target + ".old_" + DateTime.Now.Ticks); } catch { }
            File.WriteAllBytes(target, dll);
        }

        private static Version ParseVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            string s = tag.Trim().TrimStart('v', 'V');
            return Version.TryParse(s, out var v) ? v : null;
        }

        private static string BaseDir() => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";

        private static string[] SafeFiles(string dir, string pattern)
        {
            try { return Directory.GetFiles(dir, pattern); } catch { return Array.Empty<string>(); }
        }

        private static void Log(string msg)
        {
            try
            {
                File.AppendAllText(Path.Combine(BaseDir(), "RevitQuickAccess_update.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
