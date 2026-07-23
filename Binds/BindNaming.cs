using System;
using System.Linq;
using RevitQuickAccess.Quick;

namespace RevitQuickAccess.Binds
{
    /// <summary>Turns a stored bind command into a short human label for the "bind used" toast.</summary>
    public static class BindNaming
    {
        public static string Describe(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return "";
            var steps = command.Split(new[] { " ; " }, StringSplitOptions.None)
                               .Select(s => s.Trim()).Where(s => s.Length > 0)
                               .Select(DescribeStep);
            return string.Join(" → ", steps);
        }

        private static string DescribeStep(string step)
        {
            if (string.Equals(step, BindsManager.ToggleCommand, StringComparison.OrdinalIgnoreCase))
                return "Вкл/выкл биндов";

            if (NudgeService.TryParse(step, out var mm))
                return "Сдвиг " + Mm(mm);

            // a curated action from the "Действия" picker → its Russian label
            var act = RevitActions.All.FirstOrDefault(a =>
                string.Equals(a.Token, step, StringComparison.OrdinalIgnoreCase));
            if (act != null) return act.Label;

            // a quick-command tile referencing the same command → its name
            try
            {
                var q = QuickCommandsManager.Items.FirstOrDefault(i =>
                    string.Equals(i.Command, step, StringComparison.OrdinalIgnoreCase));
                if (q != null && !string.IsNullOrWhiteSpace(q.Name)) return q.Name;
            }
            catch { }

            // a command id picked from the full Revit catalog → its localized name
            var catalog = RevitCommandCatalog.NameOf(step);
            if (!string.IsNullOrWhiteSpace(catalog)) return catalog;

            return Clean(step);
        }

        private static string Mm(Autodesk.Revit.DB.XYZ ft)
        {
            const double f = 304.8;
            string P(double v) => Math.Round(v * f).ToString("0");
            return $"{P(ft.X)};{P(ft.Y)};{P(ft.Z)} мм";
        }

        /// <summary>Make a raw command id a bit more readable ("ID_OBJECTS_WALL" → "Objects Wall").</summary>
        private static string Clean(string step)
        {
            string s = step;
            if (s.StartsWith("ID_", StringComparison.OrdinalIgnoreCase)) s = s.Substring(3);
            s = s.Replace('_', ' ').Trim();
            return s.Length > 40 ? s.Substring(0, 40) + "…" : s;
        }
    }
}
