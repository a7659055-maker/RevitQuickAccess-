using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using RevitQuickAccess.UI;

namespace RevitQuickAccess.Commands
{
    /// <summary>
    /// «Тип системы» — changes the piping/duct system type of the selection.
    ///
    /// The catch that makes the obvious approach fail: on a connected valve, fitting or fixture the
    /// «Тип системы» parameter is computed, not stored — it is read from the MEPSystem the element is
    /// connected to, so writing it does nothing. Therefore the type is changed on the MEPSystem itself
    /// (its Type/Тип parameter; ChangeTypeId usually answers "This Element cannot have type assigned"),
    /// and only then, as a second pass, on the individual pipes/ducts via SetSystemType — that covers
    /// isolated runs which have no system yet.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SystemTypeCommand : IExternalCommand
    {
        private const int MaxNetwork = 8000;

        private enum Mode { Network = 0, SelectedCurves = 1, SelectedDirect = 2, WholeProject = 3, Diagnose = 4 }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;
            var doc = uidoc.Document;

            var selIds = uidoc.Selection.GetElementIds().ToList();

            var options = new List<ToolOption>();
            foreach (var t in new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType))
                                  .Cast<PipingSystemType>().OrderBy(TypeToolUtil.Name))
                options.Add(new ToolOption { Name = "[Трубы] " + TypeToolUtil.Name(t), Tag = t });
            foreach (var t in new FilteredElementCollector(doc).OfClass(typeof(MechanicalSystemType))
                                  .Cast<MechanicalSystemType>().OrderBy(TypeToolUtil.Name))
                options.Add(new ToolOption { Name = "[Воздуховоды] " + TypeToolUtil.Name(t), Tag = t });

            if (options.Count == 0) { message = "В проекте нет типов систем труб или воздуховодов."; return Result.Failed; }

            var chType = new ToolChoice
            {
                Label = "Новый тип системы",
                Tip = "Трубный тип не применится к воздуховодам и наоборот",
                Items = options
            };

            var chMode = new ToolChoice
            {
                Label = "Подход",
                Items = new List<ToolOption>
                {
                    new ToolOption { Name = "1. Вся подключённая сеть (рекомендуется)", Tag = Mode.Network,
                        Info = "Меняется тип у самих MEP-систем, к которым подключено выделенное, " +
                               "и у всех труб этой сети.\n\nЭто единственный способ, который работает для " +
                               "подключённых кранов и арматуры: у них параметр «Тип системы» только читается." },
                    new ToolOption { Name = "2. Только выделенные трубы (SetSystemType)", Tag = Mode.SelectedCurves,
                        Info = "Трогаются только выделенные трубы/воздуховоды. Фитинги и арматура пропускаются." },
                    new ToolOption { Name = "3. Только выделенное — напрямую", Tag = Mode.SelectedDirect,
                        Info = "Пытается записать тип в каждый выделенный элемент. У подключённой арматуры " +
                               "параметр обычно read-only, поэтому вариант чаще всего ничего не даст." },
                    new ToolOption { Name = "4. Весь проект", Tag = Mode.WholeProject,
                        Info = "Все системы и элементы выбранного домена во всём проекте. Выделение игнорируется." },
                    new ToolOption { Name = "5. Диагностика без изменений", Tag = Mode.Diagnose,
                        Info = "Ничего не меняет: показывает, что выделено, какие MEP-системы найдены " +
                               "и какой тип у них сейчас." }
                }
            };

            var fExpand = new ToolFlag
            {
                Text = "расширять выделение до всей подключённой сети",
                Tip = "Для подхода 1: пройти по коннекторам и собрать всю непрерывную сеть",
                IsChecked = true
            };
            var fRollback = new ToolFlag
            {
                Text = "откатить, если выделенное так и не получило нужный тип",
                Tip = "Снято — частичный результат останется в модели",
                IsChecked = true
            };

            string sub = selIds.Count > 0
                ? "Выделено элементов: " + selIds.Count
                : "Ничего не выделено — доступен только подход «Весь проект».";

            var dlg = new ToolDialog("Тип системы", sub,
                new List<ToolChoice> { chType, chMode }, new List<ToolFlag> { fExpand, fRollback });

            if (dlg.ShowDialog() != true) return Result.Cancelled;

            var target = chType.Selected?.Tag as ElementType;
            var mode = (Mode)(chMode.Selected?.Tag ?? Mode.Network);
            if (target == null) return Result.Cancelled;
            bool pipe = target is PipingSystemType;

            if (mode != Mode.WholeProject && selIds.Count == 0)
            {
                message = "Ничего не выделено. Выдели элементы или выбери подход «Весь проект».";
                return Result.Failed;
            }

            var selected = selIds.Select(doc.GetElement).Where(e => e != null).ToList();
            var log = new List<string>();

            if (mode == Mode.Diagnose)
            {
                Diagnose(doc, selected, pipe, fExpand.IsChecked, target, log);
                TypeToolUtil.Report("Тип системы — диагностика", "Модель не изменялась.", log);
                return Result.Succeeded;
            }

            string summary;
            using (var t = new Transaction(doc, "Quick Access — тип системы"))
            {
                t.Start();
                bool ok = Apply(doc, selected, pipe, mode, fExpand.IsChecked, target, log, out summary);

                if (!ok && fRollback.IsChecked)
                {
                    t.RollBack();
                    summary = "Не удалось — изменения откачены.\n\n" + summary;
                }
                else
                {
                    try { doc.Regenerate(); } catch { }
                    t.Commit();
                    if (!ok) summary = "Получилось не для всего выделенного (откат выключен).\n\n" + summary;
                }
            }

            TypeToolUtil.Report("Тип системы", summary, log);
            return Result.Succeeded;
        }

        // ---- main pass ----

        private bool Apply(Document doc, List<Element> selected, bool pipe, Mode mode, bool expand,
                           ElementType target, List<string> log, out string summary)
        {
            List<Element> work;
            List<MEPSystem> systems;

            if (mode == Mode.WholeProject)
            {
                work = ProjectElements(doc, pipe);
                systems = ProjectSystems(doc, pipe);
            }
            else if (mode == Mode.Network && expand)
            {
                work = Network(selected, pipe);
                systems = SystemsOf(work.Concat(selected), pipe);
            }
            else
            {
                work = selected.Where(e => HasDomain(e, pipe)).ToList();
                systems = mode == Mode.Network ? SystemsOf(selected, pipe) : new List<MEPSystem>();
            }

            int sysOk = 0, curveOk = 0;

            // pass 1 — the systems themselves (this is what unlocks connected valves/fittings)
            foreach (var sy in systems)
            {
                if (SetSystemTypeOnSystem(sy, target, out string msg)) { sysOk++; log.Add("система " + TypeToolUtil.Name(sy) + ": " + msg); }
                else if (msg != null) log.Add("система " + TypeToolUtil.Name(sy) + ": пропуск — " + msg);
            }
            try { doc.Regenerate(); } catch { }

            // pass 2 — individual curves (covers isolated runs with no system)
            if (mode != Mode.SelectedDirect)
            {
                foreach (var el in work.Where(IsCurve))
                {
                    if (CurrentTypeId(el, pipe) == target.Id) continue;
                    if (SetSystemTypeOnCurve(el, target, out string msg)) curveOk++;
                    else if (log.Count < 200) log.Add(Line(el) + ": " + msg);
                }
            }
            else
            {
                foreach (var el in work)
                {
                    if (CurrentTypeId(el, pipe) == target.Id) continue;
                    if (SetSystemTypeOnCurve(el, target, out string msg)) curveOk++;
                    else if (log.Count < 200) log.Add(Line(el) + ": " + msg);
                }
            }
            try { doc.Regenerate(); } catch { }

            int hit = selected.Count(e => HasDomain(e, pipe) && CurrentTypeId(e, pipe) == target.Id);
            int domain = selected.Count(e => HasDomain(e, pipe));

            summary =
                "Тип: " + TypeToolUtil.Name(target) + "\n" +
                "MEP-систем найдено: " + systems.Count + ", изменено: " + sysOk + "\n" +
                "Элементов в обработке: " + work.Count + ", труб/воздуховодов изменено: " + curveOk;

            if (mode != Mode.WholeProject)
            {
                summary += "\nИз выделенного получило нужный тип: " + hit + " из " + domain;
                if (domain == 0)
                    summary += "\n\nНи один выделенный элемент не относится к выбранному домену — " +
                               "похоже, выбран трубный тип для воздуховодов или наоборот.";
                return domain > 0 && hit == domain;
            }
            return sysOk + curveOk > 0;
        }

        private void Diagnose(Document doc, List<Element> selected, bool pipe, bool expand, ElementType target, List<string> log)
        {
            var work = expand ? Network(selected, pipe) : selected.Where(e => HasDomain(e, pipe)).ToList();
            var systems = SystemsOf(work.Concat(selected), pipe);

            log.Add("Целевой тип: " + TypeToolUtil.Name(target) + " (ID " + target.Id + ")");
            log.Add("Выделено: " + selected.Count + ", в сети: " + work.Count + ", MEP-систем: " + systems.Count);
            log.Add("");
            log.Add("Выделенные элементы:");
            foreach (var el in selected.Take(60))
                log.Add("  " + Line(el) + " — тип системы: " + NameOfId(doc, CurrentTypeId(el, pipe)));
            log.Add("");
            log.Add("Системы:");
            foreach (var sy in systems.Take(60))
                log.Add("  " + Line(sy) + " — тип: " + NameOfId(doc, SystemTypeId(sy)));
        }

        // ---- writing ----

        private static readonly BuiltInParameter[] SysTypeBips =
        {
            BuiltInParameter.ELEM_TYPE_PARAM,
            BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM,
            BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM
        };

        private static readonly string[] SysTypeNames = { "Type", "Тип", "System Type", "Тип системы" };

        private static bool SetSystemTypeOnSystem(MEPSystem sy, ElementType target, out string msg)
        {
            msg = null;
            if (sy == null) { msg = "нет системы"; return false; }
            if (SystemTypeId(sy) == target.Id) { msg = null; return false; }   // already there — not an error

            // the Type/Тип parameter of the MEPSystem is what actually drives connected fittings
            foreach (var bip in SysTypeBips)
            {
                Parameter p = null;
                try { p = sy.get_Parameter(bip); } catch { }
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.ElementId) continue;
                try { p.Set(target.Id); msg = "параметр " + bip; return true; } catch (Exception ex) { msg = ex.Message; }
            }
            foreach (var n in SysTypeNames)
            {
                Parameter p = null;
                try { p = sy.LookupParameter(n); } catch { }
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.ElementId) continue;
                try { p.Set(target.Id); msg = "параметр «" + n + "»"; return true; } catch (Exception ex) { msg = ex.Message; }
            }
            // last resort — usually throws "This Element cannot have type assigned"
            try { sy.ChangeTypeId(target.Id); msg = "ChangeTypeId"; return true; }
            catch (Exception ex) { msg = (msg == null ? "" : msg + "; ") + "ChangeTypeId: " + ex.Message; return false; }
        }

        private static bool SetSystemTypeOnCurve(Element el, ElementType target, out string msg)
        {
            msg = null;
            try
            {
                if (el is Pipe p) { p.SetSystemType(target.Id); return true; }
                if (el is Duct d) { d.SetSystemType(target.Id); return true; }
                msg = "нет метода SetSystemType";
            }
            catch (Exception ex) { msg = "SetSystemType: " + ex.Message; }

            var par = el is Pipe || el is FlexPipe
                ? SafeParam(el, BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)
                : SafeParam(el, BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM);
            if (par == null) return false;
            if (par.IsReadOnly) { msg += "; параметр «Тип системы» только для чтения"; return false; }
            try { par.Set(target.Id); return true; }
            catch (Exception ex) { msg += "; " + ex.Message; return false; }
        }

        // ---- reading ----

        private static Parameter SafeParam(Element el, BuiltInParameter b)
        {
            try { return el.get_Parameter(b); } catch { return null; }
        }

        private static ElementId SystemTypeId(MEPSystem sy)
        {
            foreach (var bip in SysTypeBips)
            {
                var id = TypeToolUtil.ParamId(SafeParam(sy, bip));
                if (TypeToolUtil.Valid(id)) return id;
            }
            try { var t = sy.GetTypeId(); if (TypeToolUtil.Valid(t)) return t; } catch { }
            return ElementId.InvalidElementId;
        }

        private static ElementId CurrentTypeId(Element el, bool pipe)
        {
            var id = TypeToolUtil.ParamId(SafeParam(el, pipe
                ? BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM
                : BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM));
            if (TypeToolUtil.Valid(id)) return id;

            foreach (var sy in SystemsOf(new[] { el }, pipe))
            {
                var s = SystemTypeId(sy);
                if (TypeToolUtil.Valid(s)) return s;
            }
            return ElementId.InvalidElementId;
        }

        private static string NameOfId(Document doc, ElementId id) =>
            TypeToolUtil.Valid(id) ? TypeToolUtil.Name(doc.GetElement(id)) : "—";

        private static string Line(Element el)
        {
            string cat = "";
            try { cat = el.Category?.Name ?? ""; } catch { }
            return el.Id + " · " + cat + " · " + TypeToolUtil.Name(el);
        }

        // ---- network walking ----

        private static bool IsCurve(Element el) => el is Pipe || el is FlexPipe || el is Duct || el is FlexDuct;

        private static bool IsPipeConnector(Connector c, bool pipe)
        {
            try { return c.Domain == (pipe ? Domain.DomainPiping : Domain.DomainHvac); }
            catch { return false; }
        }

        private static IEnumerable<Connector> Connectors(Element el, bool pipe)
        {
            var mgr = PipeTools.GetManager(el);
            if (mgr == null) yield break;
            foreach (Connector c in mgr.Connectors)
                if (IsPipeConnector(c, pipe)) yield return c;
        }

        private static bool HasDomain(Element el, bool pipe)
        {
            if (el == null) return false;
            if (Connectors(el, pipe).Any()) return true;
            if (pipe) return el is Pipe || el is FlexPipe;
            return el is Duct || el is FlexDuct;
        }

        private static List<Element> Network(IEnumerable<Element> seeds, bool pipe)
        {
            var res = new Dictionary<ElementId, Element>();
            var q = new Queue<Element>();
            foreach (var el in seeds.Where(e => HasDomain(e, pipe)))
                if (res.TryAdd(el.Id, el)) q.Enqueue(el);

            while (q.Count > 0 && res.Count < MaxNetwork)
            {
                var el = q.Dequeue();
                foreach (var c in Connectors(el, pipe))
                {
                    ConnectorSet refs;
                    try { refs = c.AllRefs; } catch { continue; }
                    foreach (Connector rc in refs)
                    {
                        Element owner;
                        try { owner = rc?.Owner; } catch { continue; }
                        if (owner == null || owner is MEPSystem || owner.Id == el.Id) continue;
                        if (!HasDomain(owner, pipe)) continue;
                        if (res.TryAdd(owner.Id, owner)) q.Enqueue(owner);
                    }
                }
            }
            return res.Values.ToList();
        }

        private static List<MEPSystem> SystemsOf(IEnumerable<Element> els, bool pipe)
        {
            var res = new Dictionary<ElementId, MEPSystem>();
            foreach (var el in els)
            {
                if (el is MEPSystem self && Matches(self, pipe)) { res.TryAdd(self.Id, self); continue; }
                try
                {
                    if (el is MEPCurve mc && mc.MEPSystem is MEPSystem s0 && Matches(s0, pipe)) res.TryAdd(s0.Id, s0);
                }
                catch { }
                foreach (var c in Connectors(el, pipe))
                {
                    try { if (c.MEPSystem is MEPSystem s && Matches(s, pipe)) res.TryAdd(s.Id, s); } catch { }
                }
            }
            return res.Values.ToList();
        }

        private static bool Matches(MEPSystem sy, bool pipe) => pipe ? sy is PipingSystem : sy is MechanicalSystem;

        private static List<MEPSystem> ProjectSystems(Document doc, bool pipe) =>
            new FilteredElementCollector(doc).OfClass(pipe ? typeof(PipingSystem) : typeof(MechanicalSystem))
                .Cast<MEPSystem>().ToList();

        private static List<Element> ProjectElements(Document doc, bool pipe)
        {
            var cats = pipe
                ? new[] { BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_FlexPipeCurves,
                          BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PipeAccessory,
                          BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_MechanicalEquipment }
                : new[] { BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_FlexDuctCurves,
                          BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_DuctAccessory,
                          BuiltInCategory.OST_MechanicalEquipment };

            var res = new Dictionary<ElementId, Element>();
            foreach (var c in cats)
                foreach (var el in new FilteredElementCollector(doc).OfCategory(c).WhereElementIsNotElementType())
                    if (HasDomain(el, pipe)) res.TryAdd(el.Id, el);
            return res.Values.ToList();
        }
    }
}
