using System;
using Autodesk.Revit.UI;

namespace RevitQuickAccess.Transfer
{
    /// <summary>UI bridge for the «Перенос» tab. All model reads/writes and point-picking go through
    /// an ExternalEvent so they run in a valid Revit API context.</summary>
    public static class TransferManager
    {
        public static TransferClipboard Clip { get; } = new TransferClipboard();

        // parameters set by the UI before raising
        public static CoordBasis Basis { get; set; } = CoordBasis.Shared;
        public static bool WholeSystem { get; set; }
        public static bool WithDir { get; set; }
        public static bool Tag { get; set; }
        public static string TagParam { get; set; } = "";
        public static double Mx { get; set; }
        public static double My { get; set; }
        public static double Mz { get; set; }
        public static bool MoveShared { get; set; }
        public static string CsvPath { get; set; } = "";

        // last coordinates read by the inspector (mm), for the "Вставить координаты" button
        public static double[] LastInternalMm { get; set; }
        public static double[] LastSharedMm { get; set; }

        public static event Action<string> Notified;

        private static ExternalEvent _event;
        private static TransferHandler _handler;

        public static void Init()
        {
            if (_handler != null) return;
            _handler = new TransferHandler();
            _event = ExternalEvent.Create(_handler);
        }

        public static void RequestCopy() => Raise(TransferMode.Copy);
        public static void RequestCopyBase() => Raise(TransferMode.CopyBase);
        public static void RequestPasteExact() => Raise(TransferMode.PasteExact);
        public static void RequestPasteBase() => Raise(TransferMode.PasteBase);
        public static void RequestInspect() => Raise(TransferMode.Inspect);
        public static void RequestMoveXyz() => Raise(TransferMode.MoveXyz);
        public static void RequestPasteCsv() => Raise(TransferMode.PasteCsv);

        private static void Raise(TransferMode mode)
        {
            if (_event == null) { Notify("Плагин ещё не готов."); return; }
            _handler.Mode = mode;
            _event.Raise();
        }

        internal static void Notify(string msg)
        {
            try { Notified?.Invoke(msg); } catch { }
        }
    }

    public class TransferHandler : IExternalEventHandler
    {
        public TransferMode Mode { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null) { TransferManager.Notify("Нет открытого документа."); return; }
                var clip = TransferManager.Clip;
                string msg;
                switch (Mode)
                {
                    case TransferMode.Copy:
                        msg = TransferService.Copy(uidoc, clip, TransferManager.Basis, TransferManager.WholeSystem); break;
                    case TransferMode.CopyBase:
                        msg = TransferService.CopyBase(uidoc, clip, TransferManager.WholeSystem, TransferManager.WithDir); break;
                    case TransferMode.PasteExact:
                        msg = TransferService.PasteExact(uidoc, clip, TransferManager.Tag, TransferManager.TagParam); break;
                    case TransferMode.PasteBase:
                        msg = TransferService.PasteBase(uidoc, clip, TransferManager.Tag, TransferManager.TagParam); break;
                    case TransferMode.Inspect:
                        msg = TransferService.Inspect(uidoc); break;
                    case TransferMode.MoveXyz:
                        msg = TransferService.MoveXyz(uidoc, TransferManager.Mx, TransferManager.My, TransferManager.Mz, TransferManager.MoveShared); break;
                    case TransferMode.PasteCsv:
                        msg = TransferService.PasteCsv(uidoc, clip, TransferManager.CsvPath, TransferManager.Tag, TransferManager.TagParam); break;
                    default: msg = ""; break;
                }
                TransferManager.Notify(msg);
            }
            catch (Exception ex)
            {
                TransferManager.Notify("Ошибка: " + ex.Message);
            }
        }

        public string GetName() => "RevitQuickAccess Transfer Handler";
    }
}
