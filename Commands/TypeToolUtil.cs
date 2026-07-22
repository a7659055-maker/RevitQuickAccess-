using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitQuickAccess.Commands
{
    /// <summary>
    /// Small shared helpers for the type-level MEP tools (flexible pipe type, system type, pipe material).
    /// Mostly defensive reading of names, parameters and sizes — Revit throws from a lot of these
    /// depending on the element, so everything here swallows and falls back.
    /// </summary>
    public static class TypeToolUtil
    {
        public const double FeetToMm = 304.8;

        /// <summary>Element name that also works for types, materials and systems.</summary>
        public static string Name(Element el)
        {
            if (el == null) return "<нет>";
            foreach (var bip in new[] { BuiltInParameter.SYMBOL_NAME_PARAM, BuiltInParameter.ALL_MODEL_TYPE_NAME })
            {
                try
                {
                    var p = el.get_Parameter(bip);
                    string s = p?.AsString();
                    if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                }
                catch { }
            }
            try { if (!string.IsNullOrWhiteSpace(el.Name)) return el.Name.Trim(); } catch { }
            return "ID " + el.Id;
        }

        public static bool Valid(ElementId id) => id != null && id != ElementId.InvalidElementId;

        /// <summary>First parameter found by BuiltInParameter, then by localized name.</summary>
        public static Parameter FindParam(Element el, BuiltInParameter[] bips, string[] names)
        {
            if (el == null) return null;
            foreach (var b in bips)
            {
                try { var p = el.get_Parameter(b); if (p != null) return p; } catch { }
            }
            foreach (var n in names)
            {
                try { var p = el.LookupParameter(n); if (p != null) return p; } catch { }
            }
            return null;
        }

        public static Parameter MaterialParam(Element el) => FindParam(el,
            new[] { BuiltInParameter.RBS_PIPE_MATERIAL_PARAM, BuiltInParameter.MATERIAL_ID_PARAM },
            new[] { "Материал", "Material", "Материал трубы", "Pipe Material" });

        public static Parameter DiameterParam(Element el) => FindParam(el,
            new[] { BuiltInParameter.RBS_PIPE_DIAMETER_PARAM, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM },
            new[] { "Диаметр", "Diameter", "Номинальный диаметр", "Nominal Diameter" });

        public static ElementId ParamId(Parameter p)
        {
            try
            {
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var id = p.AsElementId();
                    if (Valid(id)) return id;
                }
            }
            catch { }
            return ElementId.InvalidElementId;
        }

        /// <summary>Nominal diameters (feet) of a pipe segment, sorted and de-duplicated.</summary>
        public static List<double> SegmentSizes(Autodesk.Revit.DB.Plumbing.PipeSegment seg)
        {
            var res = new List<double>();
            if (seg == null) return res;
            try
            {
                foreach (MEPSize s in seg.GetSizes())
                {
                    try { res.Add(s.NominalDiameter); } catch { }
                }
            }
            catch { }
            return res.Distinct().OrderBy(v => v).ToList();
        }

        public static string Mm(double feet)
        {
            double mm = feet * FeetToMm;
            return Math.Abs(mm - Math.Round(mm)) < 0.01
                ? ((int)Math.Round(mm)).ToString(CultureInfo.InvariantCulture)
                : Math.Round(mm, 2).ToString(CultureInfo.InvariantCulture);
        }

        public static string SizesPreview(IEnumerable<double> feet, int max = 14)
        {
            var list = feet?.Distinct().OrderBy(v => v).ToList() ?? new List<double>();
            if (list.Count == 0) return "";
            string txt = string.Join(", ", list.Take(max).Select(Mm));
            if (list.Count > max) txt += ", …";
            return txt + " мм";
        }

        /// <summary>Unique pipe segments referenced by the type's routing preferences.</summary>
        public static List<Autodesk.Revit.DB.Plumbing.PipeSegment> SegmentsOf(Document doc, MEPCurveType type)
        {
            var res = new List<Autodesk.Revit.DB.Plumbing.PipeSegment>();
            var seen = new HashSet<ElementId>();
            try
            {
                var rpm = type?.RoutingPreferenceManager;
                if (rpm == null) return res;
                int n = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments);
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        var id = rpm.GetRule(RoutingPreferenceRuleGroupType.Segments, i)?.MEPPartId;
                        if (!Valid(id) || !seen.Add(id)) continue;
                        if (doc.GetElement(id) is Autodesk.Revit.DB.Plumbing.PipeSegment seg) res.Add(seg);
                    }
                    catch { }
                }
            }
            catch { }
            return res;
        }

        /// <summary>Result dialog: short summary up front, full log behind "Подробности".</summary>
        public static void Report(string title, string summary, IEnumerable<string> log)
        {
            var lines = (log ?? Enumerable.Empty<string>()).Where(s => s != null).ToList();
            var td = new TaskDialog("Quick Access — " + title)
            {
                MainInstruction = title,
                MainContent = summary
            };
            if (lines.Count > 0)
            {
                td.ExpandedContent = string.Join("\n", lines.Take(120));
                td.VerificationText = null;
            }
            td.CommonButtons = TaskDialogCommonButtons.Close;
            try { td.Show(); }
            catch { TaskDialog.Show("Quick Access — " + title, summary); }
        }
    }
}
