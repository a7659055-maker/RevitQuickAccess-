using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace RevitQuickAccess.Browser
{
    /// <summary>A title block type available for new sheets.</summary>
    public sealed class TitleBlockOption
    {
        public ElementId Id { get; set; }
        public string Name { get; set; } = "";
        public override string ToString() => Name;
    }

    /// <summary>Numeric-aware ("natural") string comparison: 1, 1.2, 2, 10 — not 1, 10, 2.</summary>
    public sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new NaturalComparer();

        public int Compare(string a, string b)
        {
            a = a ?? ""; b = b ?? "";
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string na = a.Substring(si, i - si).TrimStart('0');
                    string nb = b.Substring(sj, j - sj).TrimStart('0');
                    if (na.Length != nb.Length) return na.Length - nb.Length;
                    int cd = string.CompareOrdinal(na, nb);
                    if (cd != 0) return cd;
                }
                else
                {
                    int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                    if (c != 0) return c;
                    i++; j++;
                }
            }
            return (a.Length - i) - (b.Length - j);
        }
    }

    /// <summary>All Revit-API work for the «Диспетчер» tab (runs inside the ExternalEvent handler).</summary>
    public static class BrowserService
    {
        private static readonly HashSet<ViewType> Excluded = new HashSet<ViewType>
        {
            ViewType.Internal, ViewType.ProjectBrowser, ViewType.SystemBrowser,
            ViewType.DrawingSheet, ViewType.Undefined
        };

        // ---- load ----

        public static List<BrowserRow> Load(Document doc, string groupParam)
        {
            var sheets = new List<BrowserRow>();
            var views = new List<BrowserRow>();
            if (doc == null) return sheets;

            // title block actually placed on each sheet (one pass, then look up per sheet)
            var tbBySheet = new Dictionary<ElementId, KeyValuePair<ElementId, string>>();
            foreach (FamilyInstance tb in new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks).OfClass(typeof(FamilyInstance)))
            {
                var owner = tb.OwnerViewId;
                if (owner == ElementId.InvalidElementId || tbBySheet.ContainsKey(owner)) continue;
                var tt = doc.GetElement(tb.GetTypeId());
                string nm = tt is FamilySymbol fsym ? (fsym.Family?.Name ?? "") + " : " + fsym.Name : (tt?.Name ?? "");
                tbBySheet[owner] = new KeyValuePair<ElementId, string>(tb.GetTypeId(), nm);
            }

            foreach (var vs in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
            {
                tbBySheet.TryGetValue(vs.Id, out var tbInfo);
                sheets.Add(Snapshot(new BrowserRow
                {
                    Kind = "Лист",
                    IsSheet = true,
                    Id = vs.Id.Value,
                    Name = vs.Name,
                    SheetNumber = vs.SheetNumber,
                    Group = ReadParam(vs, groupParam),
                    TitleBlockId = tbInfo.Key?.Value ?? -1,
                    TitleBlockName = tbInfo.Value ?? ""
                }));
            }

            foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
            {
                if (v is ViewSheet || v.IsTemplate || Excluded.Contains(v.ViewType)) continue;
                views.Add(Snapshot(new BrowserRow
                {
                    Kind = "Вид",
                    IsSheet = false,
                    Id = v.Id.Value,
                    Name = v.Name,
                    SheetNumber = "",
                    Group = ReadParam(v, groupParam)
                }));
            }

            sheets.Sort((x, y) => NaturalComparer.Instance.Compare(x.SheetNumber, y.SheetNumber));
            views.Sort((x, y) => NaturalComparer.Instance.Compare(x.Name, y.Name));

            var all = new List<BrowserRow>(sheets.Count + views.Count);
            all.AddRange(sheets);
            all.AddRange(views);
            return all;
        }

        public static List<TitleBlockOption> GetTitleBlocks(Document doc)
        {
            var res = new List<TitleBlockOption>();
            if (doc == null) return res;
            foreach (FamilySymbol fs in new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks).OfClass(typeof(FamilySymbol)))
                res.Add(new TitleBlockOption { Id = fs.Id, Name = (fs.Family?.Name ?? "") + " : " + fs.Name });
            return res.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Title block used by most existing sheets (a sane default for new ones).</summary>
        public static ElementId MostUsedTitleBlock(Document doc)
        {
            var counts = new Dictionary<ElementId, int>();
            foreach (FamilyInstance tb in new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks).OfClass(typeof(FamilyInstance)))
            {
                var tid = tb.GetTypeId();
                counts[tid] = counts.TryGetValue(tid, out int c) ? c + 1 : 1;
            }
            return counts.Count > 0
                ? counts.OrderByDescending(k => k.Value).First().Key
                : (new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .OfClass(typeof(FamilySymbol)).FirstElementId() ?? ElementId.InvalidElementId);
        }

        /// <summary>
        /// Duplicate a view or a sheet right away. Duplication is done immediately rather than queued
        /// with the other edits — it needs no batching and this way you see the result at once.
        /// </summary>
        public static string DuplicateNow(Document doc, long elementId, bool isSheet, int option)
        {
            var el = doc.GetElement(new ElementId(elementId));
            if (el == null) return "Элемент не найден — нажми «Загрузить из модели».";

            using (var t = new Transaction(doc, "Quick Access — дублировать"))
            {
                t.Start();
                try
                {
                    if (isSheet)
                    {
                        if (!(el is ViewSheet vs)) { t.RollBack(); return "Выбранная строка — не лист."; }
                        var opt = (SheetDuplicateOption)Math.Max(0, Math.Min(3, option));
                        if (!vs.CanBeDuplicated(opt)) { t.RollBack(); return "Этот лист нельзя дублировать в таком режиме."; }
                        var id = vs.Duplicate(opt);
                        t.Commit();
                        return "Лист продублирован: " + (doc.GetElement(id)?.Name ?? "");
                    }
                    else
                    {
                        if (!(el is View v) || el is ViewSheet) { t.RollBack(); return "Выбранная строка — не вид."; }
                        var vopt = option switch
                        {
                            1 => ViewDuplicateOption.WithDetailing,
                            2 => ViewDuplicateOption.AsDependent,
                            _ => ViewDuplicateOption.Duplicate
                        };
                        if (!v.CanViewBeDuplicated(vopt)) { t.RollBack(); return "Этот вид нельзя дублировать в таком режиме."; }
                        var id = v.Duplicate(vopt);
                        t.Commit();
                        return "Вид продублирован: " + (doc.GetElement(id)?.Name ?? "");
                    }
                }
                catch (Exception ex) { t.RollBack(); return "Не удалось продублировать: " + ex.Message; }
            }
        }

        public static BrowserRow BuildDuplicateOfActive(Document doc, View active)
        {
            if (active == null || active is ViewSheet) return null;
            if (!active.CanViewBeDuplicated(ViewDuplicateOption.Duplicate)) return null;
            return new BrowserRow
            {
                Kind = "Вид (копия)",
                IsSheet = false,
                IsNew = true,
                SourceViewId = active.Id.Value,
                Name = active.Name + " Копия"
            };
        }

        // ---- apply ----

        /// <summary>
        /// Apply every pending change in ONE transaction. Renames/renumbers run in two passes
        /// (park to temporary values, then set the finals) so that swapping or shifting numbers
        /// never trips "Sheet number is already in use". Rows are updated in place: successful ones
        /// are re-snapshotted, failed ones keep the user's values and get an error.
        /// </summary>
        public static string Apply(Document doc, IList<BrowserRow> rows, string groupParam, ElementId titleBlockId)
        {
            if (doc == null) return "Нет открытого документа.";
            if (rows == null || rows.Count == 0) return "Нет строк для применения.";

            int renamed = 0, created = 0, deleted = 0, errors = 0;
            var errText = new StringBuilder();
            var remove = new List<BrowserRow>();

            using (var t = new Transaction(doc, "Quick Access — применить диспетчер"))
            {
                t.Start();

                // 1) deletions
                foreach (var row in rows.Where(r => r.ToDelete).ToList())
                {
                    if (row.IsNew) { remove.Add(row); continue; }
                    try
                    {
                        var id = new ElementId(row.Id);
                        if (doc.GetElement(id) != null && doc.Delete(id)?.Count > 0) { deleted++; remove.Add(row); }
                    }
                    catch (Exception ex) { errors++; row.Error = ex.Message; errText.AppendLine($"• удалить «{row.Name}»: {ex.Message}"); }
                }

                // 2) creations
                foreach (var row in rows.Where(r => r.IsNew && !r.ToDelete).ToList())
                {
                    try
                    {
                        ElementId newId = Create(doc, row, titleBlockId, groupParam);
                        if (newId != ElementId.InvalidElementId)
                        {
                            row.Id = newId.Value;
                            row.IsNew = false;
                            row.SourceViewId = -1;
                            row.Snapshot();
                            created++;
                        }
                        else { errors++; row.Error = "не создано"; errText.AppendLine($"• создать «{row.Name}»: не удалось"); }
                    }
                    catch (Exception ex) { errors++; row.Error = ex.Message; errText.AppendLine($"• создать «{row.Name}»: {ex.Message}"); }
                }

                // 3) renames / renumbers / groups — two passes to dodge unique-name collisions
                var changed = rows.Where(r => !r.IsNew && !r.ToDelete && HasEdits(r)).ToList();

                // Pass A: park every element whose UNIQUE field is changing (sheet number, view name)
                // to a temporary value, so the real numbers/names are all vacated before anyone claims
                // them — this is what lets two sheets swap 6↔7 without "Sheet number is already in use".
                //
                // The temp MUST use only characters Revit allows in a sheet number — the previous
                // "~RQA…" was silently invalid ('~', like \ : { } [ ] | ; < > ? `, is a prohibited
                // sheet-number character), so every park threw, was swallowed, and nothing was parked
                // at all; then pass B collided on every swap. "RQATMP" is plain letters + digits.
                //
                // Building it from the element's own ElementId makes it unique three ways at once:
                // no two elements ever get the same temp, it never clashes with a real number, and a
                // leftover "RQATMP<id>" from an interrupted apply belongs to that very element (so
                // re-parking it is a harmless no-op).
                foreach (var row in changed)          // pass A: park the values that must stay unique
                {
                    var el = doc.GetElement(new ElementId(row.Id));
                    if (el == null) continue;
                    string park = "RQATMP" + row.Id;
                    try
                    {
                        if (el is ViewSheet vs && SheetNumberChanging(row))
                            vs.SheetNumber = park;
                        else if (!(el is ViewSheet) && el is View v && NameChanging(row))
                            v.Name = park;
                    }
                    catch (Exception ex)
                    {
                        // parking really shouldn't fail with an id-based temp; if it somehow does,
                        // record it so the row isn't silently left half-applied
                        row.Error = ex.Message;
                    }
                }

                try { doc.Regenerate(); } catch { }   // make the vacated numbers/names visible to pass B

                foreach (var row in changed)          // pass B: set the finals
                {
                    var el = doc.GetElement(new ElementId(row.Id));
                    if (el == null) { row.Error = "элемент не найден"; errors++; continue; }
                    try
                    {
                        if (el is ViewSheet vs)
                        {
                            if (SheetNumberChanging(row)) vs.SheetNumber = row.SheetNumber;
                            if (NameChanging(row)) vs.Name = row.Name;
                        }
                        else if (el is View v && NameChanging(row))
                        {
                            v.Name = row.Name;
                        }
                        if (row.Group != row.OrigGroup) SetParam(el, groupParam, row.Group);

                        row.Snapshot();
                        renamed++;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        row.Error = ex.Message;
                        errText.AppendLine($"• «{row.Name}»: {ex.Message}");
                        Restore(el, row);            // never leave a parked "~RQA…" value behind
                    }
                }

                t.Commit();
            }

            foreach (var r in remove) rows.Remove(r);

            var sb = new StringBuilder();
            sb.Append($"Готово. Изменено: {renamed}, создано: {created}, удалено: {deleted}");
            if (errors > 0)
            {
                sb.Append($", ошибок: {errors}");
                sb.Append("\n").Append(string.Join("\n", errText.ToString()
                    .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Take(5)));
            }
            return sb.ToString();
        }

        // ---- helpers ----

        private static bool HasEdits(BrowserRow r) =>
            r.Name != r.OrigName || r.SheetNumber != r.OrigNumber || r.Group != r.OrigGroup;

        private static bool SheetNumberChanging(BrowserRow r) =>
            r.SheetNumber != r.OrigNumber && !string.IsNullOrWhiteSpace(r.SheetNumber);

        private static bool NameChanging(BrowserRow r) =>
            r.Name != r.OrigName && !string.IsNullOrWhiteSpace(r.Name);

        private static void Restore(Element el, BrowserRow row)
        {
            try
            {
                if (el is ViewSheet vs)
                {
                    if (!string.IsNullOrWhiteSpace(row.OrigNumber)) vs.SheetNumber = row.OrigNumber;
                    if (!string.IsNullOrWhiteSpace(row.OrigName)) vs.Name = row.OrigName;
                }
                else if (el is View v && !string.IsNullOrWhiteSpace(row.OrigName)) v.Name = row.OrigName;
            }
            catch { }
        }

        private static ElementId Create(Document doc, BrowserRow row, ElementId titleBlockId, string groupParam)
        {
            if (row.IsSheet)
            {
                ViewSheet sheet;
                if (row.SourceViewId > 0 && doc.GetElement(new ElementId(row.SourceViewId)) is ViewSheet src)
                {
                    var opt = (SheetDuplicateOption)Math.Max(0, Math.Min(3, row.DupOption));
                    if (!src.CanBeDuplicated(opt)) return ElementId.InvalidElementId;
                    sheet = doc.GetElement(src.Duplicate(opt)) as ViewSheet;
                }
                else
                {
                    sheet = ViewSheet.Create(doc, titleBlockId ?? ElementId.InvalidElementId);
                }
                if (sheet == null) return ElementId.InvalidElementId;

                if (!string.IsNullOrWhiteSpace(row.SheetNumber)) TrySet(() => sheet.SheetNumber = row.SheetNumber);
                if (!string.IsNullOrWhiteSpace(row.Name)) TrySet(() => sheet.Name = row.Name);
                SetParam(sheet, groupParam, row.Group);
                return sheet.Id;
            }

            // duplicate a view
            if (!(doc.GetElement(new ElementId(row.SourceViewId)) is View srcView)) return ElementId.InvalidElementId;
            var vopt = row.DupOption switch
            {
                1 => ViewDuplicateOption.WithDetailing,
                2 => ViewDuplicateOption.AsDependent,
                _ => ViewDuplicateOption.Duplicate
            };
            if (!srcView.CanViewBeDuplicated(vopt)) return ElementId.InvalidElementId;
            var nv = doc.GetElement(srcView.Duplicate(vopt)) as View;
            if (nv == null) return ElementId.InvalidElementId;
            if (vopt != ViewDuplicateOption.AsDependent && !string.IsNullOrWhiteSpace(row.Name))
                TrySet(() => nv.Name = row.Name);
            SetParam(nv, groupParam, row.Group);
            return nv.Id;
        }

        private static string ReadParam(Element e, string paramName)
        {
            if (string.IsNullOrWhiteSpace(paramName)) return "";
            var p = e.LookupParameter(paramName);
            return p != null && p.StorageType == StorageType.String ? (p.AsString() ?? "") : "";
        }

        private static void SetParam(Element e, string paramName, string value)
        {
            if (string.IsNullOrWhiteSpace(paramName)) return;
            var p = e.LookupParameter(paramName);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String) p.Set(value ?? "");
        }

        private static void TrySet(Action a) { a(); }

        private static BrowserRow Snapshot(BrowserRow r) { r.Snapshot(); return r; }
    }
}
