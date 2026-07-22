using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace RevitQuickAccess.Binds
{
    /// <summary>
    /// Turns a stored command string into an actual Revit action.
    ///
    /// A step is resolved, in order, as:
    ///   1. a raw Revit command id  ("ID_OBJECTS_WALL", "ID_BUTTON_COPY", ...)
    ///   2. a PostableCommand enum name ("WallByFace", "Copy", ...)
    ///
    /// Single command → posted immediately. Macro (steps joined by " ; ") → driven by Revit's Idling
    /// event (which fires reliably even while an interactive tool is up, unlike a WPF timer). Each next
    /// step is preceded by an Escape that ends the previous step's tool, spaced by a short gap — this
    /// is what stops Revit from hard-crashing when a command is posted on top of an active one.
    ///
    /// Note: two INTERACTIVE drawing tools (e.g. Floor then Line) still cannot truly "chain" — that is
    /// a Revit limitation; the macro ends with the last tool active. Macros are meant for sequences of
    /// commands that complete on their own.
    /// </summary>
    public static class CommandExecutor
    {
        private const int VK_ESCAPE = 0x1B;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const int StepGapMs = 300;   // pause between macro steps

        private static readonly Queue<string> _queue = new Queue<string>();
        private static bool _hooked;
        private static DateTime _last = DateTime.MinValue;

        /// <summary>Execute a stored command string (single step or " ; " separated macro).</summary>
        public static void Execute(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            var steps = new KeyBindEntry("x", command).Steps();
            if (steps.Count == 0) return;

            Stop();

            if (steps.Count == 1) { Post(steps[0]); return; }   // single command: just post, no Escape

            // macro: ONE Escape at the very start (clears any current tool), then steps spaced by a
            // 0.3 s pause — no Escape between steps and none at the end.
            SendEscape();
            Post(steps[0]);
            for (int i = 1; i < steps.Count; i++) _queue.Enqueue(steps[i]);
            _last = DateTime.UtcNow;
            Hook();
        }

        private static void Hook()
        {
            var uiapp = App.UiApp;
            if (uiapp == null || _hooked) return;
            uiapp.Idling += OnIdling;
            _hooked = true;
        }

        private static void Unhook()
        {
            var uiapp = App.UiApp;
            if (_hooked && uiapp != null) uiapp.Idling -= OnIdling;
            _hooked = false;
        }

        private static void Stop()
        {
            Unhook();
            _queue.Clear();
        }

        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            bool moreWork;
            try
            {
                if (_queue.Count > 0 && (DateTime.UtcNow - _last).TotalMilliseconds >= StepGapMs)
                {
                    Post(_queue.Dequeue());          // just post the next step after the pause — no Escape
                    _last = DateTime.UtcNow;
                }
                moreWork = _queue.Count > 0;
            }
            catch { Stop(); return; }

            if (moreWork) e.SetRaiseWithoutDelay();   // keep Idling firing until the macro is done
            else Unhook();
        }

        private static void Post(string step)
        {
            var uiapp = App.UiApp;
            if (uiapp == null) return;
            RevitCommandId id = Resolve(step);
            if (id == null) return;
            try { uiapp.PostCommand(id); }
            catch { /* never let a bad step crash Revit */ }
        }

        private static void SendEscape()
        {
            try
            {
                keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
                keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            catch { }
        }

        /// <summary>Resolve a single step string into a RevitCommandId, or null if unknown.</summary>
        public static RevitCommandId Resolve(string step)
        {
            if (string.IsNullOrWhiteSpace(step)) return null;
            step = step.Trim();

            if (step.StartsWith("ID_", StringComparison.OrdinalIgnoreCase))
            {
                try { var id = RevitCommandId.LookupCommandId(step); if (id != null) return id; }
                catch { }
            }

            if (Enum.TryParse(step, ignoreCase: true, out PostableCommand pc))
            {
                try { var id = RevitCommandId.LookupPostableCommandId(pc); if (id != null) return id; }
                catch { }
            }

            try { var id = RevitCommandId.LookupCommandId(step); if (id != null) return id; }
            catch { }

            return null;
        }

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    }
}
