using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows.Media;
using Autodesk.Windows;

namespace RevitQuickAccess.Quick
{
    /// <summary>
    /// Resolves a Revit command id (e.g. "ID_OBJECTS_WALL" or a ModPlus "CustomCtrl_%...") to the
    /// icon Revit itself shows on the ribbon. Built once by walking ComponentManager.Ribbon, so both
    /// old tiles (recorded before icons existed) and binds get real icons without re-recording.
    /// </summary>
    public static class RibbonIcons
    {
        private static readonly object _lock = new object();
        private static Dictionary<string, ImageSource> _small;
        private static Dictionary<string, ImageSource> _large;

        public static ImageSource GetSmall(string commandId) => Lookup(commandId, small: true);
        public static ImageSource GetLarge(string commandId) => Lookup(commandId, small: false);

        /// <summary>Force a rebuild next time (e.g. after add-ins finished loading their ribbons).</summary>
        public static void Invalidate()
        {
            lock (_lock) { _small = null; _large = null; }
        }

        private static ImageSource Lookup(string command, bool small)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            string id = FirstToken(command);
            EnsureBuilt();
            lock (_lock)
            {
                var dict = small ? _small : _large;
                if (dict == null) return null;
                if (dict.TryGetValue(id, out var img)) return img;
                // fall back to the other size if only one is available
                var other = small ? _large : _small;
                return other != null && other.TryGetValue(id, out var img2) ? img2 : null;
            }
        }

        private static string FirstToken(string command)
        {
            int i = command.IndexOf(" ; ", StringComparison.Ordinal);
            return (i < 0 ? command : command.Substring(0, i)).Trim();
        }

        private static void EnsureBuilt()
        {
            lock (_lock)
            {
                if (_small != null) return;
                _small = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
                _large = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var ribbon = ComponentManager.Ribbon;
                    if (ribbon == null) return;
                    foreach (RibbonTab tab in ribbon.Tabs)
                        foreach (RibbonPanel panel in tab.Panels)
                            if (panel.Source != null)
                                foreach (RibbonItem item in panel.Source.Items)
                                    Scan(item, 0);
                }
                catch { }
            }
        }

        private static void Scan(object item, int depth)
        {
            if (item == null || depth > 6) return;
            var t = item.GetType();
            try
            {
                string id = t.GetProperty("Id")?.GetValue(item) as string;
                if (!string.IsNullOrEmpty(id))
                {
                    if (t.GetProperty("LargeImage")?.GetValue(item) is ImageSource lg && !_large.ContainsKey(id))
                        _large[id] = lg;
                    if (t.GetProperty("Image")?.GetValue(item) is ImageSource sm && !_small.ContainsKey(id))
                        _small[id] = sm;
                }

                // recurse into container items (RibbonRowPanel, RibbonSplitButton, RibbonListButton, ...)
                if (t.GetProperty("Items")?.GetValue(item) is IEnumerable children)
                    foreach (var sub in children)
                        Scan(sub, depth + 1);
            }
            catch { }
        }
    }
}
