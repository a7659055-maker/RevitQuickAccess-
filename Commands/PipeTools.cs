using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI.Selection;

namespace RevitQuickAccess.Commands
{
    /// <summary>
    /// Shared engine for the pipe-growing tools ("Стояк" and "По коннектору").
    /// Grows a pipe of a fixed length from the connector you clicked near:
    ///  • if the clicked pipe already runs along that direction → the pipe is EXTENDED;
    ///  • otherwise a new pipe is created and joined (elbow when turning, straight when in line);
    ///  • pipe type / system / level come from the actually connected piping — the network is walked
    ///    through the fittings until a real pipe is found, so a PPR run stays PPR.
    /// </summary>
    public static class PipeTools
    {
        private const double Par = 0.99;   // dot-product threshold for "parallel"

        public class MepFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) =>
                e is MEPCurve || (e is FamilyInstance fi && fi.MEPModel != null);
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        /// <summary>Grow a pipe from <paramref name="start"/> along <paramref name="dir"/>.
        /// Returns null on success, or an error message.</summary>
        public static string Grow(Document doc, View view, Element el, Connector start, XYZ dir, double lenFt, string txName)
        {
            if (dir == null || dir.GetLength() < 1e-9) return "Не удалось определить направление.";
            dir = dir.Normalize();

            // --- case 1: the clicked pipe already runs along this direction → just make it longer ---
            if (el is Pipe axisPipe && IsAxisParallel(axisPipe, dir))
            {
                using (var t = new Transaction(doc, txName))
                {
                    t.Start();
                    if (!ExtendPipe(axisPipe, start, Math.Abs(lenFt))) { t.RollBack(); return "Не удалось удлинить трубу."; }
                    t.Commit();
                }
                return null;
            }

            // --- case 2: create a new pipe and join it ---
            Pipe refPipe = FindReferencePipe(doc, el);
            ElementId pipeTypeId = refPipe?.GetTypeId() ?? FirstId(doc, typeof(PipeType));
            ElementId systemTypeId = refPipe?.MEPSystem?.GetTypeId()
                                     ?? start.MEPSystem?.GetTypeId()
                                     ?? FirstId(doc, typeof(PipingSystemType));
            ElementId levelId = ResolveLevel(doc, refPipe ?? el, view);

            if (pipeTypeId == ElementId.InvalidElementId || systemTypeId == ElementId.InvalidElementId)
                return "В проекте не найден тип трубы / тип системы.";

            XYZ p0 = start.Origin;
            XYZ p1 = p0 + dir * lenFt;
            double dia = start.Radius > 0 ? start.Radius * 2 : 0;

            using (var t = new Transaction(doc, txName))
            {
                t.Start();

                Pipe np = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, p0, p1);
                if (np == null) { t.RollBack(); return "Не удалось создать трубу."; }

                if (dia > 0) np.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(dia);
                doc.Regenerate();

                Connector nc = Nearest(np.ConnectorManager, p0);
                if (nc != null && !nc.IsConnectedTo(start))
                {
                    XYZ outDir = start.CoordinateSystem?.BasisZ;
                    bool straight = outDir != null && Math.Abs(outDir.Normalize().DotProduct(dir)) > Par;
                    try
                    {
                        if (straight) nc.ConnectTo(start);              // in line → no fitting needed
                        else doc.Create.NewElbowFitting(start, nc);     // turning → elbow
                    }
                    catch
                    {
                        try { nc.ConnectTo(start); } catch { }          // last resort: logical join
                    }
                }
                t.Commit();
            }
            return null;
        }

        /// <summary>
        /// Break the pipe at <paramref name="point"/>, grow a branch along <paramref name="dir"/> and
        /// join all three with a tee from the pipe type's routing preferences.
        /// Returns null on success, or an error message.
        /// </summary>
        public static string MakeTee(Document doc, View view, Pipe pipe, XYZ point, XYZ dir, double lenFt, string txName)
        {
            if (pipe == null) return "Нужна труба.";
            if (dir == null || dir.GetLength() < 1e-9) return "Не удалось определить направление ответвления.";
            dir = dir.Normalize();

            if (!(pipe.Location is LocationCurve lc) || !(lc.Curve is Line ln)) return "Труба не прямолинейна.";
            XYZ onAxis = ln.Project(point)?.XYZPoint ?? point;

            // don't break too close to an end — there must be room for the tee
            double d0 = onAxis.DistanceTo(ln.GetEndPoint(0)), d1 = onAxis.DistanceTo(ln.GetEndPoint(1));
            if (Math.Min(d0, d1) < 0.05) return "Точка слишком близко к концу трубы.";

            ElementId pipeTypeId = pipe.GetTypeId();
            ElementId systemTypeId = pipe.MEPSystem?.GetTypeId() ?? FirstId(doc, typeof(PipingSystemType));
            ElementId levelId = ResolveLevel(doc, pipe, view);
            double dia = pipe.Diameter > 0 ? pipe.Diameter : 0;

            using (var t = new Transaction(doc, txName))
            {
                t.Start();

                ElementId secondId = PlumbingUtils.BreakCurve(doc, pipe.Id, onAxis);
                doc.Regenerate();

                var p1 = doc.GetElement(pipe.Id) as Pipe;
                var p2 = doc.GetElement(secondId) as Pipe;
                if (p1 == null || p2 == null) { t.RollBack(); return "Не удалось разорвать трубу."; }

                Connector c1 = Nearest(p1.ConnectorManager, onAxis);
                Connector c2 = Nearest(p2.ConnectorManager, onAxis);
                if (c1 == null || c2 == null) { t.RollBack(); return "Не найдены коннекторы разрыва."; }

                Pipe branch = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, onAxis, onAxis + dir * lenFt);
                if (branch == null) { t.RollBack(); return "Не удалось создать ответвление."; }
                if (dia > 0) branch.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(dia);
                doc.Regenerate();

                Connector c3 = Nearest(branch.ConnectorManager, onAxis);
                if (c3 == null) { t.RollBack(); return "Не найден коннектор ответвления."; }

                try { doc.Create.NewTeeFitting(c1, c2, c3); }
                catch (Exception ex) { t.RollBack(); return "Тройник не установлен: " + ex.Message; }

                t.Commit();
            }
            return null;
        }

        // ---- geometry ----

        /// <summary>Branch direction for a tee: rotate the pipe axis, or go vertical.</summary>
        public static XYZ BranchDirection(Pipe pipe, string key)
        {
            XYZ axis = XYZ.BasisX;
            if (pipe.Location is LocationCurve lc && lc.Curve is Line ln)
            {
                XYZ d = ln.GetEndPoint(1) - ln.GetEndPoint(0);
                if (d.GetLength() > 1e-9) axis = d.Normalize();
            }
            // horizontal perpendicular to the pipe (falls back to X if the pipe is vertical)
            XYZ up = XYZ.BasisZ;
            XYZ left = axis.CrossProduct(up);
            if (left.GetLength() < 1e-6) left = XYZ.BasisX; else left = left.Normalize();
            XYZ right = left.Negate();

            switch (key)
            {
                case "up": return up;
                case "down": return up.Negate();
                case "left": return left;
                case "right": return right;
                case "up45": return (axis + up).Normalize();
                case "down45": return (axis - up).Normalize();
                case "left45": return (axis + left).Normalize();
                case "right45": return (axis + right).Normalize();
                default: return up;
            }
        }

        public static bool IsAxisParallel(Pipe p, XYZ dir)
        {
            if (!(p.Location is LocationCurve lc) || !(lc.Curve is Line ln)) return false;
            XYZ d = ln.GetEndPoint(1) - ln.GetEndPoint(0);
            if (d.GetLength() < 1e-9) return false;
            return Math.Abs(d.Normalize().DotProduct(dir)) > Par;
        }

        /// <summary>Lengthen the pipe outwards at the end where the connector sits.</summary>
        public static bool ExtendPipe(Pipe pipe, Connector conn, double len)
        {
            if (!(pipe.Location is LocationCurve lc) || !(lc.Curve is Line ln)) return false;
            XYZ a = ln.GetEndPoint(0), b = ln.GetEndPoint(1);
            bool atB = conn.Origin.DistanceTo(b) <= conn.Origin.DistanceTo(a);
            XYZ dir = atB ? b - a : a - b;
            if (dir.GetLength() < 1e-9) return false;
            dir = dir.Normalize();

            XYZ moved = (atB ? b : a) + dir * len;
            lc.Curve = atB ? Line.CreateBound(a, moved) : Line.CreateBound(moved, b);
            return true;
        }

        // ---- connectors / network ----

        public static Connector FindConnector(Element el, XYZ pick)
        {
            var mgr = GetManager(el);
            if (mgr == null) return null;

            Connector free = null, any = null;
            double freeD = double.MaxValue, anyD = double.MaxValue;
            foreach (Connector c in mgr.Connectors)
            {
                if (c.ConnectorType != ConnectorType.End) continue;
                double d = c.Origin.DistanceTo(pick);
                if (d < anyD) { anyD = d; any = c; }
                if (!c.IsConnected && d < freeD) { freeD = d; free = c; }
            }
            return free ?? any;   // prefer an open end (the draggable handle)
        }

        public static ConnectorManager GetManager(Element el)
        {
            if (el is MEPCurve mc) return mc.ConnectorManager;
            if (el is FamilyInstance fi) return fi.MEPModel?.ConnectorManager;
            return null;
        }

        public static Connector Nearest(ConnectorManager mgr, XYZ p)
        {
            Connector best = null; double bd = double.MaxValue;
            if (mgr == null) return null;
            foreach (Connector c in mgr.Connectors)
            {
                double d = c.Origin.DistanceTo(p);
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        /// <summary>Walk connected elements (through fittings) until a real Pipe is found.</summary>
        public static Pipe FindReferencePipe(Document doc, Element start)
        {
            var seen = new HashSet<ElementId> { start.Id };
            var q = new Queue<Element>();
            q.Enqueue(start);
            int guard = 0;

            while (q.Count > 0 && guard++ < 300)
            {
                var el = q.Dequeue();
                if (el is Pipe p) return p;

                var mgr = GetManager(el);
                if (mgr == null) continue;
                foreach (Connector c in mgr.Connectors)
                    foreach (Connector rc in c.AllRefs)
                    {
                        var owner = rc?.Owner;
                        if (owner == null || owner is MEPSystem || owner.Id == el.Id) continue;
                        if (seen.Add(owner.Id)) q.Enqueue(owner);
                    }
            }
            return null;
        }

        public static ElementId FirstId(Document doc, Type t)
        {
            var id = new FilteredElementCollector(doc).OfClass(t).FirstElementId();
            return id ?? ElementId.InvalidElementId;
        }

        public static ElementId ResolveLevel(Document doc, Element el, View view)
        {
            if (el is Pipe p && p.ReferenceLevel != null) return p.ReferenceLevel.Id;
            if (el?.LevelId != null && el.LevelId != ElementId.InvalidElementId) return el.LevelId;
            if (view?.GenLevel != null) return view.GenLevel.Id;
            return FirstId(doc, typeof(Level));
        }
    }
}
