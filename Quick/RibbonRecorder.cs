using System;
using System.Windows.Media;
using Autodesk.Windows;
using Autodesk.Internal.Windows;

namespace RevitQuickAccess.Quick
{
    /// <summary>
    /// Listens to Revit's ribbon and, while recording, captures the command id of any button the
    /// user clicks (via Autodesk.Windows.ComponentManager.ItemExecuted). This is how the
    /// "Быстрые команды" tab learns command ids without the user knowing them.
    /// </summary>
    public static class RibbonRecorder
    {
        private static bool _installed;

        /// <summary>True while capturing clicks.</summary>
        public static bool Recording { get; set; }

        /// <summary>Raised on each captured ribbon click: (commandId, label, icon).</summary>
        public static event Action<string, string, ImageSource> Captured;

        public static void Install()
        {
            if (_installed) return;
            try
            {
                ComponentManager.ItemExecuted += OnItemExecuted;
                _installed = true;
            }
            catch { }
        }

        public static void Uninstall()
        {
            if (!_installed) return;
            try { ComponentManager.ItemExecuted -= OnItemExecuted; } catch { }
            _installed = false;
        }

        private static void OnItemExecuted(object sender, RibbonItemExecutedEventArgs e)
        {
            if (!Recording) return;
            try
            {
                var item = e?.Item;
                if (item == null) return;
                string id = item.Id;
                if (string.IsNullOrWhiteSpace(id)) return;
                string label = CleanLabel(item.Text);
                ImageSource icon = item.LargeImage ?? item.Image;
                Captured?.Invoke(id, label, icon);
            }
            catch { }
        }

        private static string CleanLabel(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            return s.Replace("\r", " ").Replace("\n", " ").Replace("&", "").Trim();
        }
    }
}
