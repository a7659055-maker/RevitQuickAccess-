using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitQuickAccess.Binds
{
    /// <summary>
    /// Internal bind action «__MOVE:dx,dy,dz__» — shifts the current selection by a fixed distance
    /// in millimetres along the model axes (Revit's own arrow-key nudge depends on the zoom level,
    /// so there is no built-in command that moves by an exact amount).
    ///
    /// The keyboard hook runs outside Revit's API context, so the actual move goes through an
    /// ExternalEvent. Presses are queued rather than merged: each one becomes its own transaction,
    /// so a single Ctrl+Z undoes a single nudge even when the key was held down.
    /// </summary>
    public static class NudgeService
    {
        public const string Prefix = "__MOVE:";
        private const double MmToFeet = 1.0 / 304.8;

        private static readonly Queue<XYZ> _pending = new Queue<XYZ>();
        private static ExternalEvent _event;
        private static NudgeHandler _handler;

        public static void Init()
        {
            if (_handler != null) return;
            _handler = new NudgeHandler();
            _event = ExternalEvent.Create(_handler);
        }

        /// <summary>True if the step is a «__MOVE:…__» action (and not a Revit command id).</summary>
        public static bool IsMoveStep(string step) =>
            !string.IsNullOrWhiteSpace(step) &&
            step.Trim().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>Parse «__MOVE:0,20,0__» into an offset in feet. Returns false if malformed.</summary>
        public static bool TryParse(string step, out XYZ offsetFeet)
        {
            offsetFeet = null;
            if (!IsMoveStep(step)) return false;

            string body = step.Trim().Substring(Prefix.Length).TrimEnd('_', ' ');
            var parts = body.Split(',');
            if (parts.Length != 3) return false;

            var mm = new double[3];
            for (int i = 0; i < 3; i++)
            {
                if (!double.TryParse(parts[i].Trim().Replace(',', '.'), NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out mm[i])) return false;
            }
            offsetFeet = new XYZ(mm[0] * MmToFeet, mm[1] * MmToFeet, mm[2] * MmToFeet);
            return true;
        }

        /// <summary>Queue one nudge and ask Revit for an API context.</summary>
        public static void Request(string step)
        {
            if (!TryParse(step, out XYZ offset)) return;
            if (_event == null) return;
            lock (_pending) _pending.Enqueue(offset);
            try { _event.Raise(); } catch { }
        }

        private class NudgeHandler : IExternalEventHandler
        {
            public void Execute(UIApplication app)
            {
                var uidoc = app?.ActiveUIDocument;
                if (uidoc == null) { lock (_pending) _pending.Clear(); return; }
                var doc = uidoc.Document;

                while (true)
                {
                    XYZ offset;
                    lock (_pending)
                    {
                        if (_pending.Count == 0) return;
                        offset = _pending.Dequeue();
                    }

                    // read the selection fresh each time — it can change between raises
                    var ids = uidoc.Selection.GetElementIds()
                                   .Where(id => doc.GetElement(id) != null).ToList();
                    if (ids.Count == 0) continue;

                    try
                    {
                        using var t = new Transaction(doc, "Quick Access — сдвиг выделенного");
                        t.Start();
                        ElementTransformUtils.MoveElements(doc, ids, offset);
                        t.Commit();
                    }
                    catch
                    {
                        // pinned / non-movable / hosted elements — silently skip, a bind must never
                        // pop a dialog in the middle of drawing
                    }
                }
            }

            public string GetName() => "Quick Access nudge";
        }
    }
}
