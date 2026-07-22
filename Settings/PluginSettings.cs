using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace RevitQuickAccess.Settings
{
    /// <summary>
    /// Build-time defaults. Both can be overridden per machine in RevitQuickAccess_settings.txt,
    /// so nothing secret ever has to live in the source.
    /// </summary>
    public static class UpdateDefaults
    {
        /// <summary>GitHub repository the plugin updates itself from ("owner/repo").</summary>
        public const string Repo = "a7659055-maker/RevitQuickAccess-";

        /// <summary>Public relay endpoint that receives bug reports (Cloudflare Worker URL).</summary>
        public const string ReportEndpoint = ""; // TODO: set to the deployed Worker URL
    }


    /// <summary>Simple key=value settings stored next to the plugin DLL.</summary>
    public static class PluginSettings
    {
        /// <summary>Length of the vertical pipe drop/riser, in mm.</summary>
        public static double VerticalPipeMm { get; set; } = 250;

        /// <summary>True = build upwards, false = downwards.</summary>
        public static bool VerticalPipeUp { get; set; } = true;

        /// <summary>Length of the pipe grown along a connector's own direction, in mm.</summary>
        public static double ConnectorPipeMm { get; set; } = 200;

        /// <summary>Length of the tee branch pipe, in mm.</summary>
        public static double TeeBranchMm { get; set; } = 200;

        /// <summary>Default distance between couplings (pipe segment length), in mm.</summary>
        public static double CouplingStepMm { get; set; } = 4000;

        /// <summary>GitHub repo to auto-update from, as "owner/repo". Empty = updates off.</summary>
        public static string UpdateRepo { get; set; } = UpdateDefaults.Repo;

        /// <summary>Check GitHub for a newer release on every Revit start.</summary>
        public static bool AutoUpdate { get; set; } = true;

        /// <summary>HTTPS endpoint that receives bug/crash reports (your Cloudflare Worker).</summary>
        public static string ReportEndpoint { get; set; } = UpdateDefaults.ReportEndpoint;

        private static string PathFile
        {
            get
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                return Path.Combine(dir, "RevitQuickAccess_settings.txt");
            }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(PathFile)) return;
                foreach (var raw in File.ReadAllLines(PathFile))
                {
                    string line = raw?.Trim() ?? "";
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim().ToLowerInvariant();
                    string v = line.Substring(i + 1).Trim();
                    switch (k)
                    {
                        case "verticalpipemm":
                            if (double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d > 0)
                                VerticalPipeMm = d;
                            break;
                        case "verticalpipeup":
                            VerticalPipeUp = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "connectorpipemm":
                            if (double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double c) && c > 0)
                                ConnectorPipeMm = c;
                            break;
                        case "teebranchmm":
                            if (double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double tb) && tb > 0)
                                TeeBranchMm = tb;
                            break;
                        case "couplingstepmm":
                            if (double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double cs) && cs > 0)
                                CouplingStepMm = cs;
                            break;
                        case "updaterepo": UpdateRepo = v; break;
                        case "reportendpoint": ReportEndpoint = v; break;
                        case "autoupdate":
                            AutoUpdate = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
                            break;
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                var lines = new List<string>
                {
                    "# RevitQuickAccess settings",
                    "verticalPipeMm=" + VerticalPipeMm.ToString(CultureInfo.InvariantCulture),
                    "verticalPipeUp=" + (VerticalPipeUp ? "1" : "0"),
                    "connectorPipeMm=" + ConnectorPipeMm.ToString(CultureInfo.InvariantCulture),
                    "teeBranchMm=" + TeeBranchMm.ToString(CultureInfo.InvariantCulture),
                    "couplingStepMm=" + CouplingStepMm.ToString(CultureInfo.InvariantCulture),
                    "updateRepo=" + UpdateRepo,
                    "autoUpdate=" + (AutoUpdate ? "1" : "0"),
                    "reportEndpoint=" + ReportEndpoint
                };
                File.WriteAllLines(PathFile, lines);
            }
            catch { }
        }
    }
}
