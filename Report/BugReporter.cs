using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace RevitQuickAccess.Report
{
    /// <summary>
    /// Builds and sends bug/crash reports to the developer address. Sending strategy:
    ///   • if a mail config file (RevitQuickAccess_mail.txt) with sender credentials exists → real
    ///     SMTP send (also used for silent auto-send of crash reports on the next launch);
    ///   • otherwise → open the user's mail client (mailto:) prefilled, and put the full report in a
    ///     file + the clipboard so nothing is lost.
    /// No credentials are ever embedded in the plugin — the user supplies them in the config if they
    /// want fully-automatic sending.
    /// </summary>
    public static class BugReporter
    {
        public const string DefaultTo = "du2look@mail.ru";

        private static string BaseDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
        private static string ReportsDir => EnsureDir(Path.Combine(BaseDir, "reports"));
        internal static string PendingDir => EnsureDir(Path.Combine(BaseDir, "pending_reports"));
        private static string MailConfigPath => Path.Combine(BaseDir, "RevitQuickAccess_mail.txt");

        public static bool SmtpConfigured => LoadMailConfig() != null;

        // ---- public API ----

        /// <summary>Compose + save + send a report from the panel's dialog.</summary>
        public static ReportResult Send(string description, bool isCrash = false)
        {
            string report = Compose(description, isCrash);
            string file = SaveReport(report);

            var cfg = LoadMailConfig();
            if (cfg != null && TrySmtp(cfg, isCrash ? "CRASH report" : "Bug report", report, out string err))
                return new ReportResult(true, "Отчёт отправлен на " + cfg.To + ".", file);

            // fallback: clipboard + mail client
            TryClipboard(report);
            bool opened = OpenMailto(cfg?.To ?? DefaultTo, isCrash ? "RevitQuickAccess CRASH" : "RevitQuickAccess bug report", report);
            string msg = opened
                ? "Открыт почтовый клиент — нажми «Отправить». Полный отчёт также в буфере обмена и в файле."
                : "Отчёт сохранён в файл и скопирован в буфер обмена (почтовый клиент не открылся).";
            return new ReportResult(false, msg, file);
        }

        /// <summary>Try to send everything queued by the crash guard (called on startup).</summary>
        public static int SendPending()
        {
            var cfg = LoadMailConfig();
            if (cfg == null) return 0;                 // without SMTP we can't send silently
            int sent = 0;
            foreach (var f in GetPendingFiles())
            {
                try
                {
                    string body = File.ReadAllText(f);
                    if (TrySmtp(cfg, "CRASH report (auto)", body, out _))
                    {
                        File.Delete(f);
                        sent++;
                    }
                }
                catch { }
            }
            return sent;
        }

        public static List<string> GetPendingFiles()
        {
            try { return new List<string>(Directory.GetFiles(PendingDir, "*.txt")); }
            catch { return new List<string>(); }
        }

        /// <summary>Read + merge any pending crash reports into a description (for the dialog to prefill).</summary>
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
            sb.AppendLine("Плагин: " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?"));
            sb.AppendLine(RevitInfo());
            sb.AppendLine("ОС: " + RuntimeInformation.OSDescription + " | .NET " + RuntimeInformation.FrameworkDescription);
            sb.AppendLine();
            sb.AppendLine("--- Описание ---");
            sb.AppendLine(string.IsNullOrWhiteSpace(description) ? "(не указано)" : description.Trim());
            sb.AppendLine();
            sb.AppendLine("--- Диагностика ---");
            sb.AppendLine("keylog (хвост):");
            sb.AppendLine(Tail(Path.Combine(BaseDir, "RevitQuickAccess_keylog.txt"), 1500));
            sb.AppendLine("binds:");
            sb.AppendLine(Tail(Path.Combine(BaseDir, "RevitQuickAccess_binds.txt"), 800));
            sb.AppendLine("quick:");
            sb.AppendLine(Tail(Path.Combine(BaseDir, "RevitQuickAccess_quick.txt"), 800));
            return sb.ToString();
        }

        private static string RevitInfo()
        {
            try
            {
                var app = App.UiApp?.Application;
                if (app != null)
                    return $"Revit: {app.VersionName} {app.VersionNumber} (build {app.VersionBuild})";
            }
            catch { }
            return "Revit: (недоступно)";
        }

        // ---- SMTP ----

        private sealed class MailConfig
        {
            public string Host = "smtp.mail.ru";
            public int Port = 587;
            public string From = "";
            public string Password = "";
            public string To = DefaultTo;
        }

        private static MailConfig LoadMailConfig()
        {
            try
            {
                if (!File.Exists(MailConfigPath)) return null;
                var c = new MailConfig();
                foreach (var raw in File.ReadAllLines(MailConfigPath))
                {
                    string line = raw?.Trim() ?? "";
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim().ToLowerInvariant();
                    string v = line.Substring(i + 1).Trim();
                    switch (k)
                    {
                        case "host": c.Host = v; break;
                        case "port": if (int.TryParse(v, out int p)) c.Port = p; break;
                        case "from": c.From = v; break;
                        case "password": c.Password = v; break;
                        case "to": c.To = v; break;
                    }
                }
                return string.IsNullOrWhiteSpace(c.From) || string.IsNullOrWhiteSpace(c.Password) ? null : c;
            }
            catch { return null; }
        }

        private static bool TrySmtp(MailConfig cfg, string subject, string body, out string err)
        {
            err = "";
            try
            {
                using var msg = new MailMessage(cfg.From, cfg.To)
                {
                    Subject = "RevitQuickAccess: " + subject,
                    Body = body
                };
                using var client = new SmtpClient(cfg.Host, cfg.Port)
                {
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Credentials = new NetworkCredential(cfg.From, cfg.Password),
                    Timeout = 15000
                };
                client.Send(msg);
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        // ---- mailto + clipboard + files ----

        private static bool OpenMailto(string to, string subject, string report)
        {
            try
            {
                // mailto body is length-limited; send a short note and point to the file/clipboard
                string shortBody = report.Length > 1500 ? report.Substring(0, 1500) + "\n…(полный отчёт в буфере обмена и в файле reports\\)" : report;
                string url = $"mailto:{to}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(shortBody)}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }
            catch { return false; }
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
