using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitQuickAccess.Settings;
using RevitQuickAccess.UI;
using RevitEx = Autodesk.Revit.Exceptions;

namespace RevitQuickAccess.Commands
{
    /// <summary>
    /// «Автомуфты» — cuts the selected pipes into segments of a given length and inserts a coupling
    /// (Union fitting from the pipe type's routing preferences) at every cut. If the type has no
    /// coupling configured, the dialog warns and lets you pick a family — either just for this run
    /// (temporarily injected into the routing preferences) or assigned to the type permanently.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CouplingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;
            var doc = uidoc.Document;

            List<ElementId> pipeIds = uidoc.Selection.GetElementIds()
                .Where(id => doc.GetElement(id) is Pipe).ToList();

            var unions = GetUnionSymbols(doc);

            while (true)
            {
                if (pipeIds.Count == 0)
                {
                    try
                    {
                        var refs = uidoc.Selection.PickObjects(ObjectType.Element, new PipeOnly(),
                            "Выбери трубы для муфт, затем Enter");
                        pipeIds = refs.Select(r => r.ElementId).Where(id => doc.GetElement(id) is Pipe).ToList();
                    }
                    catch (RevitEx.OperationCanceledException) { return Result.Cancelled; }
                }
                if (pipeIds.Count == 0) { message = "Не выбрано ни одной трубы."; return Result.Failed; }

                var groups = BuildGroups(doc, pipeIds, unions);
                var win = new CouplingWindow(groups);
                bool? ok = win.ShowDialog();

                if (win.Reselect) { pipeIds.Clear(); continue; }   // pick again
                if (ok != true) return Result.Cancelled;

                string err = Apply(doc, groups);
                if (err != null) { message = err; return Result.Failed; }
                return Result.Succeeded;
            }
        }

        // ---- build dialog data ----

        private static List<CouplingGroup> BuildGroups(Document doc, List<ElementId> pipeIds, List<UnionOption> unions)
        {
            var byType = new Dictionary<ElementId, CouplingGroup>();
            foreach (var id in pipeIds)
            {
                if (!(doc.GetElement(id) is Pipe p)) continue;
                var tid = p.GetTypeId();
                if (!byType.TryGetValue(tid, out var g))
                {
                    var pt = doc.GetElement(tid) as PipeType;
                    ElementId currentUnion = CurrentUnion(pt);
                    g = new CouplingGroup
                    {
                        TypeId = tid,
                        TypeName = pt?.Name ?? "Тип трубы",
                        HasUnionRule = currentUnion != ElementId.InvalidElementId,
                        Unions = unions,
                        StepMm = PluginSettings.CouplingStepMm.ToString(CultureInfo.InvariantCulture)
                    };
                    g.Selected = unions.FirstOrDefault(u => u.SymbolId == currentUnion) ?? unions.FirstOrDefault();
                    byType[tid] = g;
                }
                g.PipeIds.Add(id);
            }
            return byType.Values.ToList();
        }

        private static ElementId CurrentUnion(PipeType pt)
        {
            try
            {
                var rpm = pt?.RoutingPreferenceManager;
                if (rpm == null) return ElementId.InvalidElementId;
                if (rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Unions) == 0) return ElementId.InvalidElementId;
                return rpm.GetRule(RoutingPreferenceRuleGroupType.Unions, 0)?.MEPPartId ?? ElementId.InvalidElementId;
            }
            catch { return ElementId.InvalidElementId; }
        }

        private static List<UnionOption> GetUnionSymbols(Document doc)
        {
            var res = new List<UnionOption>();
            foreach (FamilySymbol fs in new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_PipeFitting))
            {
                try
                {
                    var pp = fs.Family?.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE);
                    if (pp == null) continue;
                    var part = (PartType)pp.AsInteger();
                    if (part != PartType.Union && part != PartType.PipeMechanicalCoupling) continue;
                    res.Add(new UnionOption
                    {
                        SymbolId = fs.Id,
                        Name = (fs.Family?.Name ?? "") + " : " + fs.Name,
                        Preview = Preview(fs)
                    });
                }
                catch { }
            }
            return res.OrderBy(u => u.Name).ToList();
        }

        private static ImageSource Preview(ElementType et)
        {
            try
            {
                var bmp = et.GetPreviewImage(new System.Drawing.Size(48, 48));
                if (bmp == null) return null;
                using var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        // ---- apply ----

        private static string Apply(Document doc, List<CouplingGroup> groups)
        {
            int made = 0;
            var errors = new List<string>();

            using (var t = new Transaction(doc, "Quick Access — автомуфты"))
            {
                t.Start();
                foreach (var g in groups)
                {
                    if (!double.TryParse((g.StepMm ?? "").Replace(',', '.'), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double stepMm) || stepMm <= 0)
                    { errors.Add($"{g.TypeName}: некорректное расстояние."); continue; }

                    var pt = doc.GetElement(g.TypeId) as PipeType;
                    bool temporary = false;

                    // make sure a Union rule exists (permanently or just for this run)
                    if (g.Selected != null && pt?.RoutingPreferenceManager != null)
                    {
                        if (doc.GetElement(g.Selected.SymbolId) is FamilySymbol sym && !sym.IsActive) sym.Activate();
                        ElementId cur = CurrentUnion(pt);
                        if (g.AssignToType && cur != g.Selected.SymbolId)
                        {
                            AddUnionRule(pt, g.Selected.SymbolId);
                        }
                        else if (!g.AssignToType && cur != g.Selected.SymbolId)
                        {
                            AddUnionRule(pt, g.Selected.SymbolId);
                            temporary = true;
                        }
                    }

                    double stepFt = stepMm / 304.8;
                    foreach (var id in g.PipeIds)
                    {
                        try { made += SplitWithUnions(doc, id, stepFt); }
                        catch (Exception ex) { errors.Add($"{g.TypeName}: {ex.Message}"); }
                    }

                    if (temporary) RemoveTopUnionRule(pt);
                }
                t.Commit();
            }

            if (made == 0)
                return "Муфты не расставлены." + (errors.Count > 0 ? "\n" + string.Join("\n", errors.Take(3)) : "");
            return null;
        }

        private static void AddUnionRule(PipeType pt, ElementId symbolId)
        {
            try { pt.RoutingPreferenceManager.AddRule(RoutingPreferenceRuleGroupType.Unions,
                    new RoutingPreferenceRule(symbolId, "RQA"), 0); }
            catch { }
        }

        private static void RemoveTopUnionRule(PipeType pt)
        {
            try { pt.RoutingPreferenceManager.RemoveRule(RoutingPreferenceRuleGroupType.Unions, 0); } catch { }
        }

        /// <summary>Cut one pipe into segments of stepFt and put a union at every cut.</summary>
        private static int SplitWithUnions(Document doc, ElementId pipeId, double stepFt)
        {
            if (!(doc.GetElement(pipeId) is Pipe pipe)) return 0;
            if (!(pipe.Location is LocationCurve lc) || !(lc.Curve is Line ln)) return 0;

            double total = ln.Length;
            if (stepFt < 0.01 || total <= stepFt + 0.01) return 0;

            XYZ p0 = ln.GetEndPoint(0);
            XYZ dir = (ln.GetEndPoint(1) - p0).Normalize();

            ElementId cur = pipeId;
            int made = 0;
            for (double at = stepFt; at < total - 0.01; at += stepFt)
            {
                XYZ pt = p0 + dir * at;
                ElementId nid;
                try { nid = PlumbingUtils.BreakCurve(doc, cur, pt); }
                catch { break; }
                if (nid == ElementId.InvalidElementId) break;
                doc.Regenerate();

                var a = doc.GetElement(cur) as Pipe;
                var b = doc.GetElement(nid) as Pipe;
                var ca = PipeTools.Nearest(a?.ConnectorManager, pt);
                var cb = PipeTools.Nearest(b?.ConnectorManager, pt);
                if (ca != null && cb != null && !ca.IsConnectedTo(cb))
                {
                    try { doc.Create.NewUnionFitting(ca, cb); made++; } catch { }
                }
                cur = nid;
            }
            return made;
        }

        private class PipeOnly : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is Pipe;
            public bool AllowReference(Reference r, XYZ p) => false;
        }
    }
}
