using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitEx = Autodesk.Revit.Exceptions;

namespace RevitQuickAccess.Transfer
{
    /// <summary>All Revit-API work for the «Перенос» tab. Runs inside a valid API context (the
    /// ExternalEvent handler). Point-picking uses uidoc.Selection.PickPoint.</summary>
    public static class TransferService
    {
        public const double FtToMm = 304.8;

        // ---- capture ----

        public static string Copy(UIDocument uidoc, TransferClipboard clip, CoordBasis basis, bool wholeSystem)
        {
            var ids = GetSelection(uidoc, wholeSystem);
            if (ids.Count == 0) return "Ничего не выделено.";
            clip.SourceDoc = uidoc.Document;
            clip.Ids = ids;
            clip.Basis = basis;
            clip.BasePoint = null;
            clip.BaseDir = null;
            clip.SourceTitle = uidoc.Document.Title;
            clip.SourcePath = uidoc.Document.PathName;
            return $"Скопировано элементов: {ids.Count} (база: {BasisName(basis)}). Открой цель и «Вставить».";
        }

        public static string CopyBase(UIDocument uidoc, TransferClipboard clip, bool wholeSystem, bool withDir)
        {
            var ids = GetSelection(uidoc, wholeSystem);
            if (ids.Count == 0) return "Сначала выдели объекты, потом «Копировать с опорной».";
            try
            {
                XYZ basePt = uidoc.Selection.PickPoint("Опорная точка (привязка к углу/краю)");
                XYZ dir = null;
                if (withDir) dir = uidoc.Selection.PickPoint("Точка направления");
                clip.SourceDoc = uidoc.Document;
                clip.Ids = ids;
                clip.Basis = CoordBasis.BasePoint;
                clip.BasePoint = basePt;
                clip.BaseDir = dir;
                clip.SourceTitle = uidoc.Document.Title;
                clip.SourcePath = uidoc.Document.PathName;
                return $"Скопировано с опорной точкой: {ids.Count} эл.{(withDir ? " (+направление)" : "")}. Теперь «Вставить по опорной».";
            }
            catch (RevitEx.OperationCanceledException) { return "Отменено."; }
        }

        // ---- paste ----

        public static string PasteExact(UIDocument uidoc, TransferClipboard clip, bool tag, string param)
        {
            if (!clip.HasElements) return "Буфер пуст — сначала «Копировать».";
            if (clip.SourceDoc == null || !clip.SourceDoc.IsValidObject)
                return "Исходный документ закрыт. Открой проект-источник и скопируй заново.";

            var dest = uidoc.Document;
            Transform t = BasisTransform(clip.SourceDoc, dest, clip.Basis);
            var newIds = DoCopy(clip.SourceDoc, clip.Ids, dest, t);
            if (tag) TagProvenance(dest, newIds, param, clip);
            return $"Вставлено: {newIds.Count} эл. в «{dest.Title}» (база: {BasisName(clip.Basis)}).";
        }

        public static string PasteBase(UIDocument uidoc, TransferClipboard clip, bool tag, string param)
        {
            if (!clip.HasElements || !clip.IsBase)
                return "Сначала «Копировать с опорной точкой».";
            if (clip.SourceDoc == null || !clip.SourceDoc.IsValidObject)
                return "Исходный документ закрыт.";

            var dest = uidoc.Document;
            try
            {
                XYZ target = uidoc.Selection.PickPoint("Куда поставить опорную точку");
                double angle = 0;
                if (clip.BaseDir != null)
                {
                    XYZ tdir = uidoc.Selection.PickPoint("Направление в цели");
                    angle = AngleZ(target, tdir) - AngleZ(clip.BasePoint, clip.BaseDir);
                }
                Transform t = BaseTransform(clip.BasePoint, target, angle);
                var newIds = DoCopy(clip.SourceDoc, clip.Ids, dest, t, sameDocTranslation: target - clip.BasePoint, sameDocAxisPoint: target, sameDocAngle: angle);
                if (tag) TagProvenance(dest, newIds, param, clip);
                return $"Вставлено по опорной точке: {newIds.Count} эл." + (clip.BaseDir != null ? " (с поворотом)" : "");
            }
            catch (RevitEx.OperationCanceledException) { return "Отменено."; }
        }

        public static string PasteCsv(UIDocument uidoc, TransferClipboard clip, string csvPath, bool tag, string param)
        {
            if (!clip.HasElements) return "Буфер пуст.";
            if (clip.SourceDoc == null || !clip.SourceDoc.IsValidObject) return "Исходный документ закрыт.";
            if (!File.Exists(csvPath)) return "CSV не найден: " + csvPath;
            XYZ basePt = clip.BasePoint ?? LocationOf(clip.SourceDoc.GetElement(clip.Ids[0]));

            var points = ReadCsvPoints(csvPath);
            if (points.Count == 0) return "В CSV нет точек (формат: X;Y;Z в мм, по строке на точку).";

            int done = 0;
            var dest = uidoc.Document;
            foreach (var p in points)
            {
                try
                {
                    Transform t = BaseTransform(basePt, p, 0);
                    var newIds = DoCopy(clip.SourceDoc, clip.Ids, dest, t, sameDocTranslation: p - basePt, sameDocAxisPoint: p, sameDocAngle: 0);
                    if (tag) TagProvenance(dest, newIds, param, clip);
                    done++;
                }
                catch { }
            }
            return $"Вставлено по CSV: {done} из {points.Count} точек.";
        }

        // ---- inspect / move ----

        public static string Inspect(UIDocument uidoc)
        {
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) return "Выдели один объект.";
            var el = uidoc.Document.GetElement(ids.First());
            XYZ p = LocationOf(el);
            Transform srcT = uidoc.Document.ActiveProjectLocation.GetTotalTransform();
            XYZ shared = srcT.Inverse.OfPoint(p);
            TransferManager.LastInternalMm = new[] { p.X * FtToMm, p.Y * FtToMm, p.Z * FtToMm };
            TransferManager.LastSharedMm = new[] { shared.X * FtToMm, shared.Y * FtToMm, shared.Z * FtToMm };
            string s =
                $"«{el.Name}»\n" +
                $"Внутренние (мм): X={Mm(p.X)}  Y={Mm(p.Y)}  Z={Mm(p.Z)}\n" +
                $"Общие/съёмка (мм): X={Mm(shared.X)}  Y={Mm(shared.Y)}  Z={Mm(shared.Z)}";
            try { System.Windows.Clipboard.SetText(s); } catch { }
            return s + "\n(скопировано в буфер)";
        }

        public static string MoveXyz(UIDocument uidoc, double xMm, double yMm, double zMm, bool shared)
        {
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) return "Выдели объект.";
            var doc = uidoc.Document;
            var el = doc.GetElement(ids.First());
            XYZ cur = LocationOf(el);
            XYZ targetInternal = new XYZ(xMm / FtToMm, yMm / FtToMm, zMm / FtToMm);
            if (shared)
            {
                Transform srcT = doc.ActiveProjectLocation.GetTotalTransform();
                targetInternal = srcT.OfPoint(targetInternal);   // shared → internal
            }
            XYZ delta = targetInternal - cur;
            using (var t = new Transaction(doc, "Quick Access — переместить в XYZ"))
            {
                t.Start();
                ElementTransformUtils.MoveElement(doc, el.Id, delta);
                t.Commit();
            }
            return $"Перемещено «{el.Name}» в точку.";
        }

        // ---- helpers ----

        private static List<ElementId> GetSelection(UIDocument uidoc, bool wholeSystem)
        {
            var doc = uidoc.Document;
            var result = new HashSet<ElementId>();
            foreach (var id in uidoc.Selection.GetElementIds())
            {
                result.Add(id);
                if (wholeSystem)
                    foreach (var sysEl in SystemElements(doc.GetElement(id)))
                        result.Add(sysEl);
            }
            return result.ToList();
        }

        private static IEnumerable<ElementId> SystemElements(Element el)
        {
            var systems = new List<MEPSystem>();
            if (el is MEPCurve mc && mc.MEPSystem is MEPSystem s1) systems.Add(s1);
            if (el is FamilyInstance fi && fi.MEPModel?.ConnectorManager != null)
            {
                foreach (Connector c in fi.MEPModel.ConnectorManager.Connectors)
                    if (c.MEPSystem is MEPSystem s2) systems.Add(s2);
            }
            foreach (var sys in systems)
                foreach (Element e in sys.Elements)
                    yield return e.Id;
        }

        private static Transform BasisTransform(Document src, Document dst, CoordBasis basis)
        {
            if (basis == CoordBasis.Internal) return Transform.Identity;
            // Shared: keep the same real-world/shared coordinate across the two projects.
            Transform srcT = src.ActiveProjectLocation.GetTotalTransform();   // shared → src internal
            Transform dstT = dst.ActiveProjectLocation.GetTotalTransform();   // shared → dst internal
            return dstT.Multiply(srcT.Inverse);                              // src internal → dst internal
        }

        private static Transform BaseTransform(XYZ basePt, XYZ target, double angle)
        {
            Transform rot = Transform.CreateRotation(XYZ.BasisZ, angle);
            return Transform.CreateTranslation(target)
                   .Multiply(rot)
                   .Multiply(Transform.CreateTranslation(basePt.Negate()));
        }

        private static ICollection<ElementId> DoCopy(Document src, ICollection<ElementId> ids, Document dst,
            Transform crossDocTransform, XYZ sameDocTranslation = null, XYZ sameDocAxisPoint = null, double sameDocAngle = 0)
        {
            var opts = new CopyPasteOptions();
            opts.SetDuplicateTypeNamesHandler(new KeepDestTypesHandler());

            if (!src.Equals(dst))
            {
                // Cross-document copy. Normally it manages its own transaction, but in some Revit 2026
                // contexts it demands one on the destination — retry inside a transaction if so.
                try
                {
                    return ElementTransformUtils.CopyElements(src, ids, dst, crossDocTransform, opts);
                }
                catch (RevitEx.ModificationOutsideTransactionException)
                {
                    using var t = new Transaction(dst, "Quick Access — вставка между проектами");
                    t.Start();
                    var r = ElementTransformUtils.CopyElements(src, ids, dst, crossDocTransform, opts);
                    t.Commit();
                    return r;
                }
            }

            // same document
            using (var t = new Transaction(dst, "Quick Access — вставка"))
            {
                t.Start();
                XYZ tr = sameDocTranslation ?? crossDocTransform.OfPoint(XYZ.Zero);
                var newIds = ElementTransformUtils.CopyElements(dst, ids, tr);
                if (Math.Abs(sameDocAngle) > 1e-9 && sameDocAxisPoint != null)
                {
                    Line axis = Line.CreateBound(sameDocAxisPoint, sameDocAxisPoint + XYZ.BasisZ);
                    ElementTransformUtils.RotateElements(dst, newIds, axis, sameDocAngle);
                }
                t.Commit();
                return newIds;
            }
        }

        private static void TagProvenance(Document dst, ICollection<ElementId> ids, string param, TransferClipboard clip)
        {
            if (string.IsNullOrWhiteSpace(param) || ids == null) return;
            string val = $"из {clip.SourceTitle} {DateTime.Now:yyyy-MM-dd}";
            using (var t = new Transaction(dst, "Quick Access — метка происхождения"))
            {
                t.Start();
                foreach (var id in ids)
                {
                    var p = dst.GetElement(id)?.LookupParameter(param);
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                        p.Set(val);
                }
                t.Commit();
            }
        }

        private static XYZ LocationOf(Element el)
        {
            if (el?.Location is LocationPoint lp) return lp.Point;
            if (el?.Location is LocationCurve lc) return lc.Curve.GetEndPoint(0);
            var bb = el?.get_BoundingBox(null);
            return bb != null ? (bb.Min + bb.Max) * 0.5 : XYZ.Zero;
        }

        private static double AngleZ(XYZ a, XYZ b) => Math.Atan2(b.Y - a.Y, b.X - a.X);

        private static List<XYZ> ReadCsvPoints(string path)
        {
            var pts = new List<XYZ>();
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw?.Trim() ?? "";
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split(new[] { ';', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (D(parts[0], out double x) && D(parts[1], out double y))
                {
                    double z = parts.Length > 2 && D(parts[2], out double zz) ? zz : 0;
                    pts.Add(new XYZ(x / FtToMm, y / FtToMm, z / FtToMm));
                }
            }
            return pts;
        }

        private static bool D(string s, out double v) =>
            double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        private static string Mm(double ft) => (ft * FtToMm).ToString("0.0", CultureInfo.InvariantCulture);

        private static string BasisName(CoordBasis b) => b switch
        {
            CoordBasis.Internal => "внутренние",
            CoordBasis.Shared => "общие/съёмка",
            _ => "опорная точка"
        };
    }
}
