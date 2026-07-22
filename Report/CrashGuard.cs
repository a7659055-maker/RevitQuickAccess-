using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace RevitQuickAccess.Report
{
    /// <summary>
    /// Captures crashes for the bug reporter:
    ///   • catchable .NET exceptions (AppDomain / WPF Dispatcher / unobserved tasks) → written to a
    ///     pending report immediately;
    ///   • native crashes that kill the process instantly (can't be caught) → detected on the NEXT
    ///     launch via a "session lock" file that only gets removed on a clean shutdown.
    /// Pending reports are auto-sent on the next launch (if SMTP is configured) or offered in the dialog.
    /// </summary>
    public static class CrashGuard
    {
        private static bool _installed;

        private static string BaseDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
        private static string SessionLock => Path.Combine(BaseDir, "session.lock");

        public static void Install(Dispatcher uiDispatcher)
        {
            if (_installed) return;
            _installed = true;

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Write(e.ExceptionObject as Exception, "AppDomain.UnhandledException");

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Write(e.Exception, "UnobservedTaskException");
                e.SetObserved();
            };

            try
            {
                if (uiDispatcher != null)
                    uiDispatcher.UnhandledException += (s, e) =>
                    {
                        Write(e.Exception, "Dispatcher.UnhandledException");
                        // don't mark handled: let Revit deal with it too, we've logged it
                    };
            }
            catch { }
        }

        /// <summary>Call on startup: if the previous session didn't shut down cleanly, log a crash.</summary>
        public static void MarkSessionStart()
        {
            try
            {
                if (File.Exists(SessionLock))
                {
                    string prev = "";
                    try { prev = File.ReadAllText(SessionLock); } catch { }
                    WriteNote("Предыдущая сессия Revit завершилась некорректно (вероятно нативный краш).\n" + prev);
                }
                File.WriteAllText(SessionLock, "session started " + DateTime.Now + " pid " + Environment.ProcessId);
            }
            catch { }
        }

        /// <summary>Call on clean shutdown so the next launch doesn't treat it as a crash.</summary>
        public static void MarkSessionClean()
        {
            try { if (File.Exists(SessionLock)) File.Delete(SessionLock); } catch { }
        }

        // ---- write pending reports ----

        private static void Write(Exception ex, string source)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Источник: " + source);
            sb.AppendLine(ex?.ToString() ?? "(no exception object)");
            WriteNote(sb.ToString());
        }

        private static void WriteNote(string detail)
        {
            try
            {
                string body = BugReporter.Compose(detail, isCrash: true);
                string f = Path.Combine(BugReporter.PendingDir,
                    "crash_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
                File.WriteAllText(f, body);
            }
            catch { }
        }
    }
}
