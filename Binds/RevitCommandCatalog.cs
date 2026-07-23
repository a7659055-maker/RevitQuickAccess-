using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace RevitQuickAccess.Binds
{
    /// <summary>
    /// The full catalog of bindable Revit commands, read from Revit's own KeyboardShortcuts.xml.
    ///
    /// That file lists EVERY command Revit can bind a shortcut to — not just ribbon buttons, but
    /// context-menu items (hide/isolate/override), the view control bar, Project Browser actions, etc. —
    /// each with a command id (ID_…), a localized name and its menu path. So instead of a hand-made list
    /// we surface the real, complete, localized command set; the stored ID_ id is posted to replay it.
    ///
    /// Revit writes the file to %APPDATA%\Autodesk\Revit\Autodesk Revit 2026\ whenever the user opens the
    /// Keyboard Shortcuts dialog (Options → «Сочетания клавиш»). If it isn't there yet, the picker offers
    /// to open that dialog (PostableCommand.KeyboardShortcuts) so Revit creates it.
    /// </summary>
    public static class RevitCommandCatalog
    {
        /// <summary>Path to Revit's auto-saved KeyboardShortcuts.xml, or null if not found.</summary>
        public static string FindFile()
        {
            try
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Autodesk", "Revit");
                if (!Directory.Exists(root)) return null;

                // the folder is usually "Autodesk Revit 2026"; be lenient about the exact name
                foreach (var dir in Directory.GetDirectories(root, "*Revit 2026*")
                                             .Concat(Directory.GetDirectories(root)))
                {
                    string f = Path.Combine(dir, "KeyboardShortcuts.xml");
                    if (File.Exists(f)) return f;
                }
            }
            catch { }
            return null;
        }

        public static bool Available => FindFile() != null;

        private static Dictionary<string, string> _names;

        /// <summary>Localized command name for an ID_ token from the catalog, or null (cached after first read).</summary>
        public static string NameOf(string commandId)
        {
            if (string.IsNullOrWhiteSpace(commandId)) return null;
            try
            {
                _names ??= Load().GroupBy(a => a.Token, StringComparer.OrdinalIgnoreCase)
                                 .ToDictionary(g => g.Key, g => g.First().Label, StringComparer.OrdinalIgnoreCase);
                return _names.TryGetValue(commandId.Trim(), out var n) ? n : null;
            }
            catch { return null; }
        }

        /// <summary>Parse KeyboardShortcuts.xml into bindable actions, or an empty list if unavailable.</summary>
        public static List<RevitAction> Load()
        {
            var res = new List<RevitAction>();
            string file = FindFile();
            if (file == null) return res;

            try
            {
                var doc = XDocument.Load(file);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var el in doc.Descendants().Where(e =>
                             string.Equals(e.Name.LocalName, "ShortcutItem", StringComparison.OrdinalIgnoreCase)))
                {
                    string id = Attr(el, "CommandId");
                    string name = Attr(el, "CommandName");
                    string paths = Attr(el, "Paths");
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    if (!seen.Add(id)) continue;                       // one row per command
                    if (string.IsNullOrWhiteSpace(name)) name = Clean(id);

                    res.Add(new RevitAction(GroupOf(paths), name, id));
                }
            }
            catch { }

            return res
                .OrderBy(a => a.Group, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(a => a.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static string Attr(XElement el, string name) =>
            el.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value;

        /// <summary>Top menu of the first path, e.g. "Вид&gt;Скрыть&gt;Элементы" → "Вид".</summary>
        private static string GroupOf(string paths)
        {
            if (string.IsNullOrWhiteSpace(paths)) return "Прочее";
            string first = paths.Split(';')[0];
            string top = first.Split('>')[0].Trim();
            return string.IsNullOrWhiteSpace(top) ? "Прочее" : top;
        }

        private static string Clean(string id)
        {
            string s = id.StartsWith("ID_", StringComparison.OrdinalIgnoreCase) ? id.Substring(3) : id;
            return s.Replace('_', ' ').Trim();
        }
    }
}
