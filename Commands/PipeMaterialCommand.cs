using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using RevitQuickAccess.UI;

namespace RevitQuickAccess.Commands
{
    /// <summary>
    /// «Материал трубы» — changes the material of a pipe type through its Pipe Segment.
    ///
    /// A PipeSegment's material is read-only in the API, so the material cannot be repainted in place:
    /// a new segment is created as «выбранный материал + Schedule/Type и типоразмеры segment-источника»
    /// and substituted into the type's routing preferences. Revit refuses a second segment with the same
    /// Material + Schedule/Type pair, so an existing one is reused when it is already there.
    ///
    /// Pipes already drawn do not always follow the routing preferences, so their instance Pipe Segment
    /// is switched separately.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PipeMaterialCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;
            var doc = uidoc.Document;

            var pipeTypes = new FilteredElementCollector(doc).OfClass(typeof(PipeType))
                .Cast<PipeType>().OrderBy(TypeToolUtil.Name).ToList();
            if (pipeTypes.Count == 0) { message = "В проекте нет типов трубопровода."; return Result.Failed; }

            var materials = new FilteredElementCollector(doc).OfClass(typeof(Material))
                .Cast<Material>().OrderBy(TypeToolUtil.Name).ToList();
            if (materials.Count == 0)
            {
                message = "В проекте нет материалов. Создай материал (Управление → Материалы) и запусти команду снова.";
                return Result.Failed;
            }

            var segments = new FilteredElementCollector(doc).OfClass(typeof(PipeSegment))
                .Cast<PipeSegment>().OrderBy(TypeToolUtil.Name).ToList();
            if (segments.Count == 0) { message = "В проекте нет ни одного сегмента трубы."; return Result.Failed; }

            var chType = new ToolChoice
            {
                Label = "Тип трубопровода",
                Items = pipeTypes.Select(pt => new ToolOption
                {
                    Name = TypeToolUtil.Name(pt),
                    Tag = pt,
                    Info = TypeInfo(doc, pt)
                }).ToList()
            };

            var chMat = new ToolChoice
            {
                Label = "Материал",
                Items = materials.Select(m => new ToolOption { Name = TypeToolUtil.Name(m), Tag = m }).ToList()
            };

            var chSeg = new ToolChoice
            {
                Label = "Откуда взять типоразмеры (сегмент-источник)",
                Tip = "Список диаметров и Schedule/Type нового сегмента берутся отсюда",
                Items = segments.Select(s => new ToolOption
                {
                    Name = TypeToolUtil.Name(s) + "  ·  размеров: " + TypeToolUtil.SegmentSizes(s).Count,
                    Tag = s,
                    Info = SegInfo(doc, s)
                }).ToList()
            };

            var fPipes = new ToolFlag
            {
                Text = "обновить уже нарисованные трубы этого типа",
                Tip = "Нарисованные трубы не всегда подхватывают настройки трассировки — им сегмент меняется отдельно",
                IsChecked = true
            };
            var fForce = new ToolFlag
            {
                Text = "переводить и трубы с посторонним сегментом",
                Tip = "Труба использует сегмент, которого нет в правилах типа — всё равно перевести её на новый",
                IsChecked = true
            };

            // preselect the segment the chosen type already uses, so the size list stays the same by default
            var first = TypeToolUtil.SegmentsOf(doc, pipeTypes[0]).FirstOrDefault();
            if (first != null)
                chSeg.Selected = chSeg.Items.FirstOrDefault(o => (o.Tag as PipeSegment)?.Id == first.Id);

            var dlg = new ToolDialog("Материал типа трубопровода",
                "Материал сегмента в Revit только читается, поэтому создаётся новый сегмент " +
                "с выбранным материалом и подставляется в трассировку типа.",
                new List<ToolChoice> { chType, chMat, chSeg }, new List<ToolFlag> { fPipes, fForce });

            if (dlg.ShowDialog() != true) return Result.Cancelled;

            var type = chType.Tag<PipeType>();
            var mat = chMat.Tag<Material>();
            var src = chSeg.Tag<PipeSegment>();
            if (type == null || mat == null || src == null) return Result.Cancelled;

            var rules = TypeToolUtil.SegmentsOf(doc, type);
            if (rules.Count == 0)
            {
                message = "У типа «" + TypeToolUtil.Name(type) + "» нет правил сегментов в настройках трассировки.";
                return Result.Failed;
            }
            if (TypeToolUtil.SegmentSizes(src).Count == 0)
            {
                message = "У сегмента-источника «" + TypeToolUtil.Name(src) + "» нет типоразмеров.";
                return Result.Failed;
            }

            var log = new List<string>();
            string summary;
            try
            {
                summary = Apply(doc, type, mat, src, fPipes.IsChecked, fForce.IsChecked, log);
            }
            catch (Exception ex)
            {
                message = "Не удалось сменить материал: " + ex.Message;
                return Result.Failed;
            }

            TypeToolUtil.Report("Материал типа трубопровода", summary, log);
            return Result.Succeeded;
        }

        // ---- dialog info ----

        private static string TypeInfo(Document doc, PipeType pt)
        {
            var segs = TypeToolUtil.SegmentsOf(doc, pt);
            if (segs.Count == 0) return "Тип: " + TypeToolUtil.Name(pt) + "\nВ трассировке нет сегментов — команда не сработает.";

            var lines = new List<string> { "Тип: " + TypeToolUtil.Name(pt), "Сегментов в трассировке: " + segs.Count };
            foreach (var s in segs.Take(4))
            {
                string mat = "<нет>";
                try { mat = TypeToolUtil.Name(doc.GetElement(s.MaterialId)); } catch { }
                lines.Add("• " + TypeToolUtil.Name(s) + " · сейчас: " + mat);
            }
            return string.Join("\n", lines);
        }

        private static string SegInfo(Document doc, PipeSegment s)
        {
            string mat = "<нет>", sch = "<нет>";
            try { mat = TypeToolUtil.Name(doc.GetElement(s.MaterialId)); } catch { }
            try { sch = TypeToolUtil.Name(doc.GetElement(s.ScheduleTypeId)); } catch { }
            var sizes = TypeToolUtil.SegmentSizes(s);
            return "Источник: " + TypeToolUtil.Name(s) +
                   "\nМатериал источника: " + mat + " (не используется — берётся выбранный выше)" +
                   "\nSchedule/Type: " + sch +
                   "\nДиаметры (" + sizes.Count + "): " + TypeToolUtil.SizesPreview(sizes);
        }

        // ---- work ----

        private static string Apply(Document doc, PipeType type, Material mat, PipeSegment src,
                                    bool updatePipes, bool force, List<string> log)
        {
            int replaced = 0;
            var map = new Dictionary<ElementId, ElementId>();   // old segment -> new segment
            ElementId fallback = ElementId.InvalidElementId;
            PipeStats stats = null;

            using (var t = new Transaction(doc, "Quick Access — материал типа трубопровода"))
            {
                t.Start();

                var pool = new FilteredElementCollector(doc).OfClass(typeof(PipeSegment)).Cast<PipeSegment>().ToList();
                var rpm = type.RoutingPreferenceManager;
                const RoutingPreferenceRuleGroupType group = RoutingPreferenceRuleGroupType.Segments;

                int count = rpm.GetNumberOfRules(group);
                for (int i = 0; i < count; i++)
                {
                    RoutingPreferenceRule rule;
                    PipeSegment oldSeg;
                    try
                    {
                        rule = rpm.GetRule(group, i);
                        oldSeg = doc.GetElement(rule?.MEPPartId) as PipeSegment;
                    }
                    catch { continue; }
                    if (rule == null || oldSeg == null) continue;

                    PipeSegment newSeg = Resolve(doc, oldSeg, src, mat.Id, pool, log);
                    if (newSeg == null) continue;

                    map[oldSeg.Id] = newSeg.Id;
                    if (!TypeToolUtil.Valid(fallback)) fallback = newSeg.Id;

                    if (newSeg.Id == oldSeg.Id) { log.Add("= " + TypeToolUtil.Name(oldSeg) + " — уже нужный материал и размеры"); continue; }

                    try
                    {
                        var copy = CopyRule(rule, newSeg.Id);
                        rpm.RemoveRule(group, i);
                        rpm.AddRule(group, copy, i);
                        replaced++;
                        log.Add("+ правило " + (i + 1) + ": " + TypeToolUtil.Name(oldSeg) + " → " + TypeToolUtil.Name(newSeg));
                    }
                    catch (Exception ex) { log.Add("! правило " + (i + 1) + ": " + ex.Message); }
                }

                try { doc.Regenerate(); } catch { }

                if (updatePipes)
                    stats = UpdatePipes(doc, type, map, fallback, mat.Id, force, log);

                try { doc.Regenerate(); } catch { }
                t.Commit();
            }

            string s = "Тип: " + TypeToolUtil.Name(type) +
                       "\nМатериал: " + TypeToolUtil.Name(mat) +
                       "\nТипоразмеры из: " + TypeToolUtil.Name(src) +
                       "\nЗаменено правил сегментов: " + replaced;
            if (stats != null)
                s += "\n\nСуществующих труб этого типа: " + stats.Found +
                     "\nПереведено на новый сегмент: " + stats.Changed +
                     (stats.AlreadyOk > 0 ? "\nУже были на нужном: " + stats.AlreadyOk : "") +
                     (stats.Failed > 0 ? "\nНе поддались: " + stats.Failed : "");
            return s;
        }

        /// <summary>Find or create «выбранный материал + Schedule/Type и размеры источника».</summary>
        private static PipeSegment Resolve(Document doc, PipeSegment oldSeg, PipeSegment src, ElementId matId,
                                           List<PipeSegment> pool, List<string> log)
        {
            var schedule = src.ScheduleTypeId;

            if (oldSeg.MaterialId == matId && oldSeg.ScheduleTypeId == schedule &&
                Same(TypeToolUtil.SegmentSizes(oldSeg), TypeToolUtil.SegmentSizes(src)))
                return oldSeg;

            // Revit allows only one segment per Material + Schedule/Type pair
            var twin = pool.FirstOrDefault(p => p.MaterialId == matId && p.ScheduleTypeId == schedule);
            if (twin != null)
            {
                if (!Same(TypeToolUtil.SegmentSizes(twin), TypeToolUtil.SegmentSizes(src)))
                    log.Add("! сегмент с такой парой «материал + Schedule/Type» уже есть (" + TypeToolUtil.Name(twin) +
                            "), но его список диаметров отличается от источника — Revit не даёт создать второй, использован существующий");
                return twin;
            }

            var sizes = new List<MEPSize>();
            try { foreach (MEPSize ms in src.GetSizes()) sizes.Add(ms); } catch { }
            if (sizes.Count == 0) { log.Add("! у источника не прочитались размеры"); return null; }

            try
            {
                var made = PipeSegment.Create(doc, matId, schedule, sizes);
                if (made != null) pool.Add(made);
                return made;
            }
            catch (Exception ex)
            {
                var again = pool.FirstOrDefault(p => p.MaterialId == matId && p.ScheduleTypeId == schedule);
                if (again != null) { log.Add("! создать сегмент не удалось, использован существующий: " + ex.Message); return again; }
                log.Add("! создать сегмент не удалось: " + ex.Message);
                return null;
            }
        }

        private static bool Same(List<double> a, List<double> b) =>
            a.Count == b.Count && !a.Where((v, i) => Math.Abs(v - b[i]) > 1e-9).Any();

        private static RoutingPreferenceRule CopyRule(RoutingPreferenceRule old, ElementId partId)
        {
            string desc = "";
            try { desc = old.Description ?? ""; } catch { }
            var res = new RoutingPreferenceRule(partId, desc);
            for (int i = 0; i < old.NumberOfCriteria; i++)
                res.AddCriterion(old.GetCriterion(i));
            return res;
        }

        // ---- already-drawn pipes ----

        private class PipeStats { public int Found, Changed, AlreadyOk, Failed; }

        private static PipeStats UpdatePipes(Document doc, PipeType type, Dictionary<ElementId, ElementId> map,
                                             ElementId fallback, ElementId matId, bool force, List<string> log)
        {
            var st = new PipeStats();
            var pipes = new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>()
                .Where(p => p.GetTypeId() == type.Id).ToList();
            st.Found = pipes.Count;

            foreach (var pipe in pipes)
            {
                Parameter segParam = TypeToolUtil.FindParam(pipe,
                    new[] { BuiltInParameter.RBS_PIPE_SEGMENT_PARAM },
                    new[] { "Pipe Segment", "Segment", "Сегмент трубы", "Сегмент" });

                ElementId current = TypeToolUtil.ParamId(segParam);
                ElementId want = ElementId.InvalidElementId;

                if (TypeToolUtil.Valid(current) && map.TryGetValue(current, out var mapped)) want = mapped;
                else if (force) want = fallback;

                if (!TypeToolUtil.Valid(want)) { st.Failed++; continue; }
                if (current == want) { st.AlreadyOk++; continue; }

                if (segParam != null && !segParam.IsReadOnly)
                {
                    try { segParam.Set(want); st.Changed++; continue; }
                    catch (Exception ex) { if (log.Count < 150) log.Add("! труба " + pipe.Id + ": " + ex.Message); }
                }

                // fallback: some pipes expose a writable Material parameter even when Segment is locked
                var mp = TypeToolUtil.MaterialParam(pipe);
                if (mp != null && !mp.IsReadOnly && mp.StorageType == StorageType.ElementId)
                {
                    try { mp.Set(matId); st.Changed++; continue; } catch { }
                }
                st.Failed++;
            }
            return st;
        }
    }
}
