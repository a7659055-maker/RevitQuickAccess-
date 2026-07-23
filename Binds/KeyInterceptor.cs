using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace RevitQuickAccess.Binds
{
    /// <summary>
    /// Intercepts key presses with a low-level keyboard hook (WH_KEYBOARD_LL).
    ///
    /// Text-safety model (WHITELIST, not blacklist): a bind fires ONLY when keyboard focus is on the
    /// Revit drawing canvas (an MFC "AfxFrameOrView…" window). Every other focus — Project Browser,
    /// Properties, dialogs, family-editor fields, our own panel — is treated as "not the canvas", so
    /// binds can never leak into text entry, no matter how Revit draws that text field. This replaces
    /// the earlier per-field detection, which missed Revit's custom (non-Win32) edit controls.
    /// </summary>
    public static class KeyInterceptor
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int HC_ACTION = 0;
        private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;

        private static IntPtr _hook = IntPtr.Zero;
        private static LowLevelKeyboardProc _proc;   // keep alive
        private static uint _ownPid;

        // multi-tap state
        private static string _pendingCombo;
        private static int _pendingCount;
        private static DateTime _lastTapTime = DateTime.MinValue;
        private static DispatcherTimer _tapTimer;

        /// <summary>Set true while the panel is recording a key combo, so binds don't fire.</summary>
        public static bool Suspended { get; set; }

        /// <summary>Raised (with the new set name) when a set-activation hotkey switches the active set.</summary>
        public static event Action<string> SetSwitched;

        /// <summary>Writes a small focus log (class name + decision) on every bound-key press, for diagnostics.</summary>
        public static bool DebugLog { get; set; } = true;

        public static void Install()
        {
            if (_hook != IntPtr.Zero) return;
            _ownPid = (uint)Environment.ProcessId;
            _proc = HookCallback;
            IntPtr hMod = Marshal.GetHINSTANCE(typeof(KeyInterceptor).Module);
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);
        }

        public static void Uninstall()
        {
            if (_hook != IntPtr.Zero)
            {
                try { UnhookWindowsHookEx(_hook); } catch { }
                _hook = IntPtr.Zero;
            }
            _proc = null;
            StopTimer();
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode == HC_ACTION)
                {
                    int msg = (int)wParam;
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                        int vk = (int)data.vkCode;
                        if (vk != VK_SHIFT && vk != VK_CONTROL && vk != VK_MENU &&
                            IsRevitForeground() && Process(vk))
                            return (IntPtr)1;   // swallow: no beep, no default action
                    }
                }
            }
            catch { }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private static bool IsRevitForeground()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            return pid == _ownPid;
        }

        /// <summary>Core dispatch. Returns true if the key was consumed.</summary>
        private static bool Process(int vk)
        {
            var mods = WinForms.Control.ModifierKeys;
            string combo = FormatSingleCombo(vk, mods);
            string chordCombo = BindsManager.HasChordBinds ? FormatChordCombo(mods) : null;
            if (string.IsNullOrEmpty(combo) && string.IsNullOrEmpty(chordCombo)) return false;

            var binds = BindsManager.Snapshot();

            // 1. toggle enable/disable — allowed everywhere (F8 is never a text character).
            string toggle = BindsManager.GetToggleCombo();
            if (!string.IsNullOrEmpty(toggle) &&
                (BindsManager.MatchCombo(combo, toggle) || BindsManager.MatchCombo(chordCombo, toggle)))
            {
                FlushPending();
                BindsManager.SetEnabledInternal(!BindsManager.Enabled);
                return true;
            }

            if (Suspended) return false;
            if (!BindsManager.Enabled) return false;

            // 2. Is this key a set-activation hotkey, or bound to anything in the active set?
            int activateSet = BindsManager.MatchActivate(combo, chordCombo);
            if (activateSet < 0 && !KeyIsBound(combo, chordCombo, binds)) return false;

            // 3. WHITELIST: only fire on the Revit drawing canvas. Anything else = a place text may be typed.
            bool canvas = IsCanvasFocused(out string cls);
            Diag(combo, cls, canvas);
            if (!canvas) return false;

            // switch bind set
            if (activateSet >= 0)
            {
                FlushPending();
                BindsManager.SwitchTo(activateSet);
                try { SetSwitched?.Invoke(BindsManager.ActiveName); } catch { }
                return true;
            }

            // 4. dispatch — a pressed chord wins over the single key it ends on; both support multi-tap
            string effective = !string.IsNullOrEmpty(chordCombo) ? chordCombo : combo;

            // multi-tap (E*2, A+S*2, …): accumulate taps and let the timer resolve them
            if (BindsManager.HasMultiTapVariant(effective))
            {
                var now = DateTime.UtcNow;
                bool continuation = string.Equals(_pendingCombo, effective, StringComparison.OrdinalIgnoreCase)
                                    && (now - _lastTapTime).TotalMilliseconds < BindsManager.DoubleTapMs;
                if (continuation) _pendingCount++;
                else { FlushPending(); _pendingCombo = effective; _pendingCount = 1; }
                _lastTapTime = now;
                RestartTimer();
                return true;
            }

            // no multi-tap variant → fire the exact match right away (chord first, then single)
            var exact = BindsManager.GetExactBind(effective);
            if (exact != null) { FlushPending(); CommandExecutor.Execute(exact.Command); return true; }
            return false;
        }

        /// <summary>True if the pressed combo/chord matches any (non-toggle) bind's key.</summary>
        private static bool KeyIsBound(string combo, string chordCombo, List<KeyBindEntry> binds)
        {
            foreach (var b in binds)
            {
                if (string.Equals(b.Command, BindsManager.ToggleCommand, StringComparison.OrdinalIgnoreCase)) continue;
                string nc = BindsManager.NormalizeForMatch(b.KeyCombo);
                if (string.IsNullOrEmpty(nc)) continue;
                // strip a "*N" multi-tap suffix first, then decide chord vs single on the base
                string bas = BindsManager.IsMultiTapFormat(nc) ? BindsManager.GetMultiTapBase(nc) : nc;
                if (BindsManager.IsChordFormat(bas))
                {
                    if (!string.IsNullOrEmpty(chordCombo) && BindsManager.MatchChord(chordCombo, bas)) return true;
                }
                else if (BindsManager.MatchCombo(combo, bas)) return true;
            }
            return false;
        }

        // ---- canvas detection (the whitelist) ----

        private static bool IsCanvasFocused(out string cls)
        {
            cls = "";
            IntPtr hwnd = GetFocusedControl();
            if (hwnd == IntPtr.Zero) return false;
            var sb = new StringBuilder(256);
            if (GetClassName(hwnd, sb, sb.Capacity) > 0) cls = sb.ToString();

            // Revit's MDI drawing view: "AfxFrameOrView140u" (the version digits vary between builds).
            if (cls.IndexOf("FrameOrView", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            // Safe hedge: focus is on a top-level window itself, not a child control — text is always
            // typed into a child, so this is never a text-entry context. Also guards against the canvas
            // class differing on some build (binds keep working) without opening any text-leak path.
            return hwnd == GetForegroundWindow();
        }

        private static IntPtr GetFocusedControl()
        {
            var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            if (GetGUIThreadInfo(0, ref gti) && gti.hwndFocus != IntPtr.Zero)
                return gti.hwndFocus;
            return GetFocus();
        }

        private static void Diag(string combo, string cls, bool canvas)
        {
            if (!DebugLog) return;
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                string f = Path.Combine(dir, "RevitQuickAccess_keylog.txt");
                if (File.Exists(f) && new FileInfo(f).Length > 200_000) File.Delete(f);
                File.AppendAllText(f, $"{DateTime.Now:HH:mm:ss} key={combo} class=\"{cls}\" canvas={canvas} -> {(canvas ? "FIRE" : "suppressed")}{Environment.NewLine}");
            }
            catch { }
        }

        // ---- multi-tap timer (runs on the Revit UI thread) ----

        private static void RestartTimer()
        {
            StopTimer();
            _tapTimer = new DispatcherTimer(DispatcherPriority.Input)
            { Interval = TimeSpan.FromMilliseconds(BindsManager.DoubleTapMs) };
            _tapTimer.Tick += (s, e) => FlushPending();
            _tapTimer.Start();
        }

        private static void StopTimer()
        {
            if (_tapTimer != null) { _tapTimer.Stop(); _tapTimer = null; }
        }

        private static void FlushPending()
        {
            string combo = _pendingCombo;
            int count = _pendingCount;
            _pendingCombo = null;
            _pendingCount = 0;
            StopTimer();
            if (combo == null || count <= 0) return;

            var mt = BindsManager.GetMultiTapBindForCount(combo, count);
            if (mt != null) { CommandExecutor.Execute(mt.Command); return; }
            if (count == 1)
            {
                // a single tap of a combo that also has a multi-tap variant → the plain bind (key or chord)
                var exact = BindsManager.GetExactBind(combo);
                if (exact != null) CommandExecutor.Execute(exact.Command);
            }
        }

        // ---- combo formatting (WinForms.Keys names for BINDS10 config compatibility) ----

        public static string FormatSingleCombo(int vk, WinForms.Keys mods)
        {
            var parts = new List<string>();
            if ((mods & WinForms.Keys.Control) != 0) parts.Add("Ctrl");
            if ((mods & WinForms.Keys.Shift) != 0) parts.Add("Shift");
            if ((mods & WinForms.Keys.Alt) != 0) parts.Add("Alt");
            parts.Add(((WinForms.Keys)vk).ToString());
            return string.Join("+", parts);
        }

        private static string FormatChordCombo(WinForms.Keys mods)
        {
            var held = new List<int>();
            for (int i = 8; i <= 254; i++)
            {
                if (i == VK_SHIFT || i == VK_CONTROL || i == VK_MENU) continue;
                if (IsKeyDown(i)) held.Add(i);
            }
            if (held.Count < 2) return null;

            var parts = new List<string>();
            if ((mods & WinForms.Keys.Control) != 0) parts.Add("Ctrl");
            if ((mods & WinForms.Keys.Shift) != 0) parts.Add("Shift");
            if ((mods & WinForms.Keys.Alt) != 0) parts.Add("Alt");
            held.Sort();
            foreach (var k in held) parts.Add(((WinForms.Keys)k).ToString());
            return string.Join("+", parts);
        }

        private static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        // ---- P/Invoke ----

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern IntPtr GetFocus();
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    }
}
