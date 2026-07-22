using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace RevitQuickAccess.Quick
{
    /// <summary>Stores the user's quick commands (tiles) and persists them next to the plugin DLL.</summary>
    public static class QuickCommandsManager
    {
        /// <summary>Live collection the tiles bind to. Mutate on the UI thread, then call <see cref="Save"/>.</summary>
        public static ObservableCollection<QuickCommand> Items { get; } = new ObservableCollection<QuickCommand>();

        public static string GetConfigPath()
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            return Path.Combine(dir, "RevitQuickAccess_quick.txt");
        }

        public static void Add(QuickCommand item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Command)) return;
            if (string.IsNullOrWhiteSpace(item.Name)) item.Name = item.Command;
            Items.Add(item);
            Save();
        }

        public static void Remove(QuickCommand item)
        {
            if (item != null && Items.Remove(item)) Save();
        }

        public static void Load()
        {
            Items.Clear();
            try
            {
                string path = GetConfigPath();
                if (!File.Exists(path)) return;
                foreach (var raw in File.ReadAllLines(path))
                {
                    string line = raw ?? "";
                    if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;
                    var parts = line.Split('\t');
                    if (parts.Length < 2) continue;
                    string name = parts[0].Trim();
                    string cmd = parts[1].Trim();
                    string icon = parts.Length > 2 ? parts[2].Trim() : "";
                    double width = QuickCommand.DefaultWidth, height = QuickCommand.DefaultWidth;
                    if (parts.Length > 3)
                        double.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out width);
                    if (parts.Length > 4)
                        double.TryParse(parts[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out height);
                    else height = width; // old configs: square
                    if (cmd.Length > 0) Items.Add(new QuickCommand(name, cmd, icon, width, height));
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                var lines = new System.Collections.Generic.List<string>
                {
                    "# RevitQuickAccess quick commands: name<Tab>command<Tab>iconFile<Tab>width<Tab>height"
                };
                foreach (var i in Items)
                    lines.Add(string.Join("\t", i.Name, i.Command, i.IconFile,
                        i.Width.ToString(CultureInfo.InvariantCulture),
                        i.Height.ToString(CultureInfo.InvariantCulture)));
                File.WriteAllLines(GetConfigPath(), lines);
            }
            catch { }
        }
    }
}
