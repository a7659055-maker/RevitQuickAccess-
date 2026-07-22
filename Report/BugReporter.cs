using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RevitQuickAccess.Settings;

namespace RevitQuickAccess.Report
{
    /// <summary>
    /// Builds bug/crash reports and posts them to a self-hosted relay endpoint (a Cloudflare Worker).
    ///
    /// Why a relay: the plugin is open source, so it must ship no credentials. The Worker holds the
    /// secret (Telegram token, webhook, mailbox — whatever you wire up on its side) and the plugin
    /// only knows a public HTTPS URL. That also makes crash reports genuinely automatic.
    ///
    /// If no endpoint is configured (or it is unreachable) the report is still saved to a file and
    /// copied to the clipboard, so nothing is ever lost.
    /// </summary>
    public static class BugReporter
    {
        private static string BaseDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
        private static string ReportsDir => EnsureDir(Path.Combine(BaseDir, "reports"));
        internal static string PendingDir => EnsureDir(Path.Combine(BaseDir, "pending_reports"));

        public static bool EndpointConfigured => !string.IsNullOrWhiteSpace(PluginSettings.ReportEndpoint);

        // ---- public API ----

        public static ReportResult Send(string description, bool isCrash = false)
        {
            string report = Compose(description, isCrash);
            string file = SaveReport(report);

            if (Post(isCrash ? "crash" : "bug", report, out string err))
                return new ReportResult(true, "Отчёт отправлен. Спасибо!", file);

            TryClipboard(report);
            string why = EndpointConfigured
                ? "Не удалось отправить (" + err + ")."
                : "Приём отчётов не настроен.";
            return new ReportResult(false, why + " Отчёт сохранён в файл и скопирован в буфер обмена.", file);
        }

        /// <summary>Fire-and-forget delivery of crash reports queued by the crash guard.</summary>
        public static void SendPendingInBackground()
        {
            if (!EndpointConfigured) return;
            Task.Run(() =>
            {
                foreach (var f in GetPendingFiles())
                {
                    try { if (Post("crash", File.ReadAllText(f), out _)) File.Delete(f); }
                    catch { }
                }
            });
        }

        public static List<string> GetPendingFiles()
        {
            try { return new List<string>(Directory.GetFiles(PendingDir, "*.txt")); }
            catch { return new List<string>(); }
        }

        public static string ReadPendingSummary()
        {
            var files = GetPendingFiles();
            if (files.Count == 0) return null;
            var sb = new StringBuilder();
            sb.AppendLine($"[Автоматически: обнаружено отчётов о сбоях — {files.Count}]");
            foreach (var f in files)
            {
                try { sb.AppendLine(File.ReadAllText(f)); sb.AppendLine("----"); } catch { }
            }
            return sb.ToString();
        }

        public static void ClearPending()
        {
            foreach (var f in GetPendingFiles()) { try { File.Delete(f); } catch { } }
        }

        // ---- report composition ----

        public static string Compose(string description, bool isCrash)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== RevitQuickAccess " + (isCrash ? "CRASH" : "BUG") + " REPORT ===");
            sb.AppendLine("Время: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Плагин: " + Ver());
            sb.AppendLine(RevitInfo());
            sb.AppendLine("ОС: " + RuntimeInformation.OSDescription + " | .NET " + RuntimeInformation.FrameworkDescription);
            sb.AppendLine();
            sb.AppendLine("--- Описание ---");
            sb.AppendLine(string.IsNullOrWhiteSpace(description) ? "(не указано)" : description.Trim());
            sb.AppendLine();
            sb.AppendLine("--- Диагностика ---");
            sb.AppendLine("keylog (хвост):");
            sb.AppendLine(Tail(Path.Combine(BaseDir, "RevitQuickAccess_keylog.txt"), 1500));
            sb.AppendLine("bind sets:");
            sb.AppendLine(Tail(Path.Combine(BaseDir, "RevitQuickAccess_bindsets.txt"), 800));
            sb.AppendLine("quick:");
            sb.AppendLine(Tail(Path.Combine(BaseDir, "RevitQuickAccess_quick.txt"), 800));
            sb.AppendLine("update log:");
            sb.AppendLine(Tail(Path.Combine(BaseDir, "RevitQuickAccess_update.log"), 500));
            return sb.ToString();
        }

        private static string Ver() =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";

        private static string RevitInfo()
        {
            try
            {
                var app = App.UiApp?.Application;
                if (app != null) return $"Revit: {app.VersionName} {app.VersionNumber} (build {app.VersionBuild})";
            }
            catch { }
            return "Revit: (недоступно)";
        }

        // ---- transport ----

        private static bool Post(string kind, string report, out string err)
        {
            err = "";
            string ep = (PluginSettings.ReportEndpoint ?? "").Trim();
            if (string.IsNullOrEmpty(ep)) { err = "эндпоинт не задан"; return false; }

            try
            {
                string payload = JsonSerializer.Serialize(new
                {
                    kind,
                    version = Ver(),
                    revit = RevitInfo(),
                    os = RuntimeInformation.OSDescription,
                    report
                });

                // Task.Run keeps us off the UI SynchronizationContext, so waiting can't deadlock.
                return Task.Run(async () =>
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("RevitQuickAccess");
                    using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                    var resp = await http.PostAsync(ep, content);
                    return resp.IsSuccessStatusCode;
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        private static void TryClipboard(string text)
        {
            try { System.Windows.Clipboard.SetText(text); } catch { }
        }

        private static string SaveReport(string report)
        {
            try
            {
                string f = Path.Combine(ReportsDir, "report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                File.WriteAllText(f, report);
                return f;
            }
            catch { return ""; }
        }

        // ---- helpers ----

        private static string Tail(string path, int maxChars)
        {
            try
            {
                if (!File.Exists(path)) return "(нет)";
                string s = File.ReadAllText(path);
                return s.Length > maxChars ? s.Substring(s.Length - maxChars) : s;
            }
            catch { return "(ошибка чтения)"; }
        }

        private static string EnsureDir(string d)
        {
            try { Directory.CreateDirectory(d); } catch { }
            return d;
        }
    }

    public sealed class ReportResult
    {
        public bool Sent { get; }
        public string Message { get; }
        public string File { get; }
        public ReportResult(bool sent, string message, string file) { Sent = sent; Message = message; File = file; }
    }
}
