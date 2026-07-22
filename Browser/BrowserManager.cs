using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitQuickAccess.Browser
{
    public enum BrowserMode { Load, Apply, AddDuplicate, OpenView, DuplicateNow }

    /// <summary>
    /// UI-facing bridge for the batch browser tab. The DataGrid binds to <see cref="Rows"/>.
    /// All model reads/writes go through an ExternalEvent so they run in a valid Revit API context.
    /// </summary>
    public static class BrowserManager
    {
        public static ObservableCollection<BrowserRow> Rows { get; } = new ObservableCollection<BrowserRow>();

        /// <summary>Name of the text parameter used for grouping (drives what Project Browser groups by).</summary>
        public static string GroupParam { get; set; } = "";

        /// <summary>Title block types available for new sheets, and the one currently chosen.</summary>
        public static ObservableCollection<TitleBlockOption> TitleBlocks { get; } = new ObservableCollection<TitleBlockOption>();
        public static ElementId TitleBlockId { get; set; } = ElementId.InvalidElementId;

        /// <summary>Raised (on UI thread) after a load/apply/add with a status message.</summary>
        public static event Action<string> Notified;

        private static ExternalEvent _event;
        private static BrowserActionHandler _handler;

        public static void Init()
        {
            if (_handler != null) return;
            _handler = new BrowserActionHandler();
            _event = ExternalEvent.Create(_handler);
        }

        /// <summary>ElementId value of the view/sheet to open (for BrowserMode.OpenView).</summary>
        public static long OpenTargetId { get; set; } = -1;

        public static void RequestLoad() => Raise(BrowserMode.Load);
        public static void RequestApply() => Raise(BrowserMode.Apply);
        public static void RequestAddDuplicate() => Raise(BrowserMode.AddDuplicate);
        public static void RequestOpen(long id) { OpenTargetId = id; Raise(BrowserMode.OpenView); }

        /// <summary>Element to duplicate immediately (BrowserMode.DuplicateNow).</summary>
        public static long DupTargetId { get; set; } = -1;
        public static bool DupIsSheet { get; set; }
        public static int DupOption { get; set; }

        public static void RequestDuplicate(long id, bool isSheet, int option)
        {
            DupTargetId = id; DupIsSheet = isSheet; DupOption = option;
            Raise(BrowserMode.DuplicateNow);
        }

        private static void Raise(BrowserMode mode)
        {
            if (_event == null) { Notify("Плагин ещё не инициализирован."); return; }
            _handler.Mode = mode;
            _event.Raise();
        }

        internal static void Notify(string msg)
        {
            try { Notified?.Invoke(msg); } catch { }
        }

        internal static void SetRows(IEnumerable<BrowserRow> rows)
        {
            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);
        }

        internal static void AddRow(BrowserRow r)
        {
            if (r != null) Rows.Add(r);
        }

        internal static void LoadTitleBlocks(Document doc)
        {
            TitleBlocks.Clear();
            foreach (var tb in BrowserService.GetTitleBlocks(doc)) TitleBlocks.Add(tb);
            if (TitleBlockId == ElementId.InvalidElementId || TitleBlocks.All(t => t.Id != TitleBlockId))
                TitleBlockId = BrowserService.MostUsedTitleBlock(doc);
        }
    }

    /// <summary>Runs the browser load/apply work inside Revit's API context.</summary>
    public class BrowserActionHandler : IExternalEventHandler
    {
        public BrowserMode Mode { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null) { BrowserManager.Notify("Нет открытого документа Revit."); return; }

                switch (Mode)
                {
                    case BrowserMode.Load:
                        BrowserManager.SetRows(BrowserService.Load(doc, BrowserManager.GroupParam));
                        BrowserManager.LoadTitleBlocks(doc);
                        BrowserManager.Notify("Загружено строк: " + BrowserManager.Rows.Count);
                        break;

                    case BrowserMode.Apply:
                        // rows are updated in place — no reload, so nothing the user typed is lost
                        string msg = BrowserService.Apply(doc, BrowserManager.Rows,
                                                          BrowserManager.GroupParam, BrowserManager.TitleBlockId);
                        BrowserManager.Notify(msg);
                        break;

                    case BrowserMode.AddDuplicate:
                        var row = BrowserService.BuildDuplicateOfActive(doc, uidoc.ActiveView);
                        if (row != null)
                        {
                            BrowserManager.AddRow(row);
                            BrowserManager.Notify("Добавлена копия активного вида — задай имя и «Применить».");
                        }
                        else BrowserManager.Notify("Активный вид нельзя дублировать (открой обычный вид).");
                        break;

                    case BrowserMode.DuplicateNow:
                    {
                        string res = BrowserService.DuplicateNow(doc, BrowserManager.DupTargetId,
                                                                 BrowserManager.DupIsSheet, BrowserManager.DupOption);
                        // refresh so the new view/sheet shows up right away
                        BrowserManager.SetRows(BrowserService.Load(doc, BrowserManager.GroupParam));
                        BrowserManager.LoadTitleBlocks(doc);
                        BrowserManager.Notify(res);
                        break;
                    }

                    case BrowserMode.OpenView:
                        try
                        {
                            var view = doc.GetElement(new ElementId(BrowserManager.OpenTargetId)) as View;
                            if (view != null) { uidoc.ActiveView = view; BrowserManager.Notify("Открыт: " + view.Name); }
                            else BrowserManager.Notify("Вид не найден.");
                        }
                        catch (Exception ex) { BrowserManager.Notify("Не удалось открыть: " + ex.Message); }
                        break;
                }
            }
            catch (Exception ex)
            {
                BrowserManager.Notify("Ошибка: " + ex.Message);
            }
        }

        public string GetName() => "RevitQuickAccess Browser Handler";
    }
}
