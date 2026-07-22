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
    /// «Гибкий тип» — makes a FlexPipeType out of a normal PipeType (name + suffix, «_Гибкий» by default).
    ///
    /// Revit treats PipeType and FlexPipeType as different classes, so a pipe type cannot simply be
    /// duplicated into a flexible one: an existing FlexPipeType is duplicated as a template and then
    /// every writable same-named parameter is copied over. The material comes from the pipe type's
    /// parameter, and if it has none — from the first PipeSegment in its routing preferences.
    ///
    /// A FlexPipeType has no routing preferences, so the segment size catalogue cannot be transferred
    /// one-to-one; only the first diameter is written, and the whole list is shown in the report.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FlexTypeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;
            var doc = uidoc.Document;

            var pipeTypes = new FilteredElementCollector(doc).OfClass(typeof(PipeType))
                .Cast<PipeType>().OrderBy(TypeToolUtil.Name).ToList();
            if (pipeTypes.Count == 0) { message = "В проекте нет типов трубопровода."; return Result.Failed; }

            var flexTypes = new FilteredElementCollector(doc).OfClass(typeof(FlexPipeType))
                .Cast<FlexPipeType>().OrderBy(TypeToolUtil.Name).ToList();
            if (flexTypes.Count == 0)
            {
                message = "В проекте нет ни одного типа гибкого трубопровода.\n\n" +
                          "Revit API не умеет создавать его с нуля — нужен образец. Создай любой тип гибкой трубы " +
                          "вручную и запусти команду снова.";
                return Result.Failed;
            }

            var chType = new ToolChoice
            {
                Label = "Обычный тип трубопровода — источник",
                Tip = "Из него будут взяты параметры, материал и диаметры",
                Items = pipeTypes.Select(pt => new ToolOption
                {
                    Name = TypeToolUtil.Name(pt),
                    Tag = pt,
                    Info = SourceInfo(doc, pt)
                }).ToList()
            };

            var chTemplate = new ToolChoice
            {
                Label = "Образец гибкого типа (с него снимается копия)",
                Tip = "Новый тип создаётся дублированием этого гибкого типа",
                Items = flexTypes.Select(ft => new ToolOption { Name = TypeToolUtil.Name(ft), Tag = ft }).ToList()
            };

            var fUpdate = new ToolFlag
            {
                Text = "обновить тип, если он уже существует",
                Tip = "Снято — существующий тип с таким именем останется нетронутым",
                IsChecked = true
            };

            var dlg = new ToolDialog("Гибкий тип трубопровода",
                "Создаёт тип гибкой трубы по образцу обычного типа: параметры, материал и первый диаметр.",
                new List<ToolChoice> { chType, chTemplate },
                new List<ToolFlag> { fUpdate },
                "Создать", "Суффикс имени:", "_Гибкий");

            if (dlg.ShowDialog() != true) return Result.Cancelled;

            var source = chType.Tag<PipeType>();
            var template = chTemplate.Tag<FlexPipeType>();
            string suffix = string.IsNullOrWhiteSpace(dlg.InputValue) ? "_Гибкий" : dlg.InputValue.Trim();
            if (source == null || template == null) return Result.Cancelled;

            string target = TargetName(source, suffix);
            var existing = flexTypes.FirstOrDefault(f => TypeToolUtil.Name(f) == target);
            if (existing != null && !fUpdate.IsChecked)
            {
                TypeToolUtil.Report("Гибкий тип", "Тип «" + target + "» уже существует, обновление выключено.", null);
                return Result.Cancelled;
            }

            var log = new List<string>();
            string summary;
            try
            {
                summary = Build(doc, source, template, existing, target, log);
            }
            catch (Exception ex)
            {
                message = "Не удалось создать гибкий тип: " + ex.Message;
                return Result.Failed;
            }

            TypeToolUtil.Report("Гибкий тип", summary, log);
            return Result.Succeeded;
        }

        // ---- info for the dialog ----

        private static string TargetName(PipeType pt, string suffix)
        {
            string b = TypeToolUtil.Name(pt);
            return b.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? b : b + suffix;
        }

        private static string SourceInfo(Document doc, PipeType pt)
        {
            var segs = TypeToolUtil.SegmentsOf(doc, pt);
            var lines = new List<string> { "Тип: " + TypeToolUtil.Name(pt) };

            if (segs.Count == 0)
                lines.Add("В трассировке типа нет сегментов — будут скопированы только параметры.");
            else
            {
                foreach (var s in segs.Take(4))
                {
                    string mat = "<нет>";
                    try { mat = TypeToolUtil.Name(doc.GetElement(s.MaterialId)); } catch { }
                    lines.Add("• " + TypeToolUtil.Name(s) + " · материал: " + mat +
                              " · диаметры: " + (TypeToolUtil.SizesPreview(TypeToolUtil.SegmentSizes(s), 8)));
                }
                if (segs.Count > 4) lines.Add("…");
            }
            return string.Join("\n", lines);
        }

        // ---- work ----

        private static string Build(Document doc, PipeType source, FlexPipeType template,
                                    FlexPipeType existing, string targetName, List<string> log)
        {
            FlexPipeType target = existing;
            bool created = false;

            using (var t = new Transaction(doc, "Quick Access — гибкий тип трубопровода"))
            {
                t.Start();

                if (target == null)
                {
                    target = template.Duplicate(targetName) as FlexPipeType;
                    if (target == null) { t.RollBack(); throw new Exception("не удалось скопировать образец " + TypeToolUtil.Name(template)); }
                    created = true;
                }

                int copied = CopyParams(source, target, log);

                // material: the type's own parameter first, then the first routing-preference segment
                ElementId matId = TypeToolUtil.ParamId(TypeToolUtil.MaterialParam(source));
                string matFrom = "параметр типа";
                if (!TypeToolUtil.Valid(matId))
                {
                    var seg = TypeToolUtil.SegmentsOf(doc, source).FirstOrDefault();
                    if (seg != null) { matId = seg.MaterialId; matFrom = "сегмент " + TypeToolUtil.Name(seg); }
                }

                bool matOk = false;
                if (TypeToolUtil.Valid(matId))
                {
                    var mp = TypeToolUtil.MaterialParam(target);
                    if (mp != null && !mp.IsReadOnly && mp.StorageType == StorageType.ElementId)
                    {
                        try { mp.Set(matId); matOk = true; }
                        catch (Exception ex) { log.Add("! материал: " + ex.Message); }
                    }
                    else log.Add("! материал: у гибкого типа нет записываемого параметра материала");
                }
                else log.Add("! материал у источника не найден");

                // diameter: a flex type has no size catalogue, so only the first value is written
                var sizes = TypeToolUtil.SegmentsOf(doc, source).SelectMany(TypeToolUtil.SegmentSizes)
                                        .Distinct().OrderBy(v => v).ToList();
                string diaNote = "диаметры источника не прочитаны";
                if (sizes.Count > 0)
                {
                    var dp = TypeToolUtil.DiameterParam(target);
                    if (dp != null && !dp.IsReadOnly && dp.StorageType == StorageType.Double)
                    {
                        try { dp.Set(sizes[0]); diaNote = "записан " + TypeToolUtil.Mm(sizes[0]) + " мм"; }
                        catch (Exception ex) { diaNote = "не записан: " + ex.Message; }
                    }
                    else diaNote = "у гибкого типа нет записываемого параметра диаметра";
                }

                try { doc.Regenerate(); } catch { }
                t.Commit();

                log.Insert(0, "Диаметры источника: " + (TypeToolUtil.SizesPreview(sizes) ?? "—"));
                log.Insert(1, "Первый диаметр: " + diaNote);
                log.Insert(2, "Материал (" + matFrom + "): " + (matOk ? "записан" : "не записан"));
                log.Insert(3, "Скопировано одноимённых параметров: " + copied);
                log.Insert(4, "");

                return (created ? "Создан тип «" : "Обновлён тип «") + TypeToolUtil.Name(target) + "»." +
                       "\n\nУ гибкого типа нет настроек трассировки, поэтому каталог типоразмеров " +
                       "не переносится один-в-один — копируется всё, что Revit разрешает записать.";
            }
        }

        /// <summary>Type-level parameters that must never be copied (identity / classification).</summary>
        private static readonly HashSet<string> Skip = new HashSet<string>
        {
            "Имя типа", "Type Name", "Название типа",
            "Семейство", "Family", "Family Name", "Имя семейства",
            "Изображение типоразмера", "Type Image",
            "URL", "Keynote", "Ключевая пометка",
            "Описание сборки", "Assembly Description",
            "Код по классификатору", "Assembly Code",
            "OmniClass Number", "OmniClass Title",
            "IFC Predefined Type", "IfcExportAs", "IfcExportType"
        };

        private static int CopyParams(Element source, Element target, List<string> log)
        {
            int copied = 0;
            foreach (Parameter sp in source.Parameters)
            {
                string name;
                try { name = sp.Definition?.Name; } catch { continue; }
                if (string.IsNullOrWhiteSpace(name) || Skip.Contains(name)) continue;

                Parameter tp = null;
                try
                {
                    tp = target.Parameters.Cast<Parameter>().FirstOrDefault(p =>
                    {
                        try { return p.Definition?.Name == name && p.StorageType == sp.StorageType && !p.IsReadOnly; }
                        catch { return false; }
                    });
                }
                catch { }
                if (tp == null) continue;

                try
                {
                    switch (sp.StorageType)
                    {
                        case StorageType.String: tp.Set(sp.AsString() ?? ""); break;
                        case StorageType.Integer: tp.Set(sp.AsInteger()); break;
                        case StorageType.Double: tp.Set(sp.AsDouble()); break;
                        case StorageType.ElementId: tp.Set(sp.AsElementId()); break;
                        default: continue;
                    }
                    copied++;
                }
                catch (Exception ex) { log.Add("! " + name + " — " + ex.Message); }
            }
            return copied;
        }
    }
}
