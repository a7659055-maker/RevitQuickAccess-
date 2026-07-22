using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitEx = Autodesk.Revit.Exceptions;

namespace RevitQuickAccess.Commands
{
    /// <summary>
    /// AutoCAD-style TRIM for detail/model lines and circles/arcs: click on the part of a curve to
    /// remove; it is cut at its intersections with any other curves in the view. One click per trim,
    /// repeats until Esc. Handles open curves (shorten / split) and closed curves (circle → arc).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TrimCommand : IExternalCommand
    {
        private const double Tol = 1e-6;
        private const double MinLen = 1e-4;   // ~0.03 mm — reject degenerate results (prevents crashes)

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;
            var doc = uidoc.Document;
            var view = uidoc.ActiveView;
            var filter = new CurveFilter();

            while (true)
            {
                Reference r;
                try { r = uidoc.Selection.PickObject(ObjectType.Element, filter, "Обрезка: кликни на удаляемый участок (Esc — выход)"); }
                catch (RevitEx.OperationCanceledException) { break; }

                var el = doc.GetElement(r) as CurveElement;
                Curve curve = el?.GeometryCurve;
                XYZ pick = r?.GlobalPoint;
                if (curve == null || pick == null) continue;

                try { TrimOne(doc, view, el, curve, pick); } catch { /* skip a bad trim, keep going */ }
            }
            return Result.Succeeded;
        }

        private static void TrimOne(Document doc, View view, CurveElement el, Curve curve, XYZ pick)
        {
            // all intersection parameters on this curve
            var raw = new List<double>();
            foreach (CurveElement other in new FilteredElementCollector(doc, view.Id).OfClass(typeof(CurveElement)))
            {
                if (other.Id == el.Id) continue;
                Curve oc = other.GeometryCurve;
                if (oc == null) continue;
                try
                {
                    if (curve.Intersect(oc, out IntersectionResultArray res) == SetComparisonResult.Overlap && res != null)
                        foreach (IntersectionResult ir in res) raw.Add(ir.UVPoint.U);
                }
                catch { }
            }

            double pp = curve.Project(pick).Parameter;

            if (curve.IsBound)
                TrimOpen(doc, view, el, curve, raw, pp);
            else
                TrimClosed(doc, el, curve, raw, pp);
        }

        private static void TrimOpen(Document doc, View view, CurveElement el, Curve curve, List<double> raw, double pp)
        {
            double p0 = curve.GetEndParameter(0), p1 = curve.GetEndParameter(1);
            var us = raw.Where(u => u > p0 + Tol && u < p1 - Tol).OrderBy(u => u).ToList();

            double a = p0, b = p1;
            foreach (double u in us) { if (u <= pp) a = u; else break; }
            foreach (double u in us) { if (u >= pp) { b = u; break; } }

            bool removeStart = a <= p0 + Tol, removeEnd = b >= p1 - Tol;

            using (var t = new Transaction(doc, "Quick Access — обрезка"))
            {
                t.Start();
                if (removeStart && removeEnd) doc.Delete(el.Id);
                else if (removeStart) SetGeom(el, Bound(curve, b, p1));
                else if (removeEnd) SetGeom(el, Bound(curve, p0, a));
                else
                {
                    Curve head = Bound(curve, p0, a), tail = Bound(curve, b, p1);
                    if (head != null && tail != null) { SetGeom(el, head); Duplicate(doc, view, el, tail); }
                }
                t.Commit();
            }
        }

        private static void TrimClosed(Document doc, CurveElement el, Curve curve, List<double> raw, double pp)
        {
            double period = curve.Period;
            if (period <= Tol) return;

            var us = raw.Select(u => Wrap(u, period)).OrderBy(x => x).ToList();
            // dedupe near-equal cut points
            var cuts = new List<double>();
            foreach (double u in us) if (cuts.Count == 0 || u - cuts[cuts.Count - 1] > 1e-4) cuts.Add(u);
            if (cuts.Count < 2) return;   // need at least two cuts to trim a closed curve

            double ppn = Wrap(pp, period);

            // find the arc gap [a,b] containing the pick
            double a = 0, b = 0; bool found = false;
            for (int i = 0; i < cuts.Count; i++)
            {
                double lo = cuts[i];
                double hi = (i + 1 < cuts.Count) ? cuts[i + 1] : cuts[0] + period;
                double q = ppn < lo ? ppn + period : ppn;
                if (q >= lo - Tol && q <= hi + Tol) { a = lo; b = hi; found = true; break; }
            }
            if (!found) return;

            Curve remaining = Bound(curve, b, a + period);   // keep the complement of the removed arc
            if (remaining == null) return;

            using (var t = new Transaction(doc, "Quick Access — обрезка"))
            {
                t.Start();
                SetGeom(el, remaining);
                t.Commit();
            }
        }

        private static double Wrap(double u, double period) => ((u % period) + period) % period;

        private static Curve Bound(Curve curve, double u0, double u1)
        {
            if (u1 - u0 <= Tol) return null;
            Curve c = curve.Clone();
            c.MakeBound(u0, u1);
            return c.Length >= MinLen ? c : null;
        }

        private static void SetGeom(CurveElement el, Curve c)
        {
            if (c != null) el.SetGeometryCurve(c, false);
        }

        private static void Duplicate(Document doc, View view, CurveElement source, Curve piece)
        {
            if (piece == null) return;
            if (source is DetailCurve)
            {
                var dc = doc.Create.NewDetailCurve(view, piece);
                try { dc.LineStyle = source.LineStyle; } catch { }
            }
            else if (source is ModelCurve mc)
            {
                var nc = doc.Create.NewModelCurve(piece, mc.SketchPlane);
                try { nc.LineStyle = source.LineStyle; } catch { }
            }
        }

        private class CurveFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is CurveElement;
            public bool AllowReference(Reference r, XYZ p) => false;
        }
    }
}
