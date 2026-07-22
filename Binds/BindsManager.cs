using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RevitQuickAccess.Binds
{
    /// <summary>A named set (profile) of binds with an optional activation hotkey.</summary>
    public sealed class BindSet
    {
        public string Name = "";
        public string Activate = "";          // combo that switches to this set (e.g. "Ctrl+1")
        public List<KeyBindEntry> Binds = new List<KeyBindEntry>();
    }

    /// <summary>
    /// Central store + matcher for keybindings, organised into switchable SETS (profiles). Only the
    /// active set's binds are live; each set can have an activation hotkey. Matching logic (single /
    /// Ctrl+ / multi-tap "E*2" / chord "G+H+J" / "__TOGGLE__") is ported from the AutoCAD BINDS10 plugin.
    /// </summary>
    public static class BindsManager
    {
        public const int DoubleTapMs = 350;
        public const string ToggleCommand = "__TOGGLE__";

        private static readonly object _lock = new object();
        private static readonly List<BindSet> _sets = new List<BindSet> { new BindSet { Name = "По умолчанию" } };
        private static int _active = 0;
        private static bool _enabled = true;
        private static bool _hasChordBinds = false;

        public static event Action Changed;
        public static event Action<bool> EnabledChanged;

        private static List<KeyBindEntry> _binds => _sets[Math.Max(0, Math.Min(_active, _sets.Count - 1))].Binds;

        public static bool HasChordBinds { get { lock (_lock) { return _hasChordBinds; } } }

        public static bool Enabled
        {
            get { lock (_lock) { return _enabled; } }
            set
            {
                lock (_lock) { _enabled = value; }
                Save();
                SafeInvoke(() => EnabledChanged?.Invoke(value));
                SafeInvoke(() => Changed?.Invoke());
            }
        }

        // ---- sets ----

        public static List<string> GetSetNames()
        {
            lock (_lock) { return _sets.Select(s => s.Name).ToList(); }
        }

        public static int ActiveIndex { get { lock (_lock) { return _active; } } }
        public static string ActiveName { get { lock (_lock) { return _sets[_active].Name; } } }

        public static void SwitchTo(int index)
        {
            lock (_lock)
            {
                if (index < 0 || index >= _sets.Count || index == _active) return;
                _active = index;
                UpdateHasChordBinds();
            }
            Save();
            SafeInvoke(() => Changed?.Invoke());
        }

        public static void AddSet(string name)
        {
            lock (_lock)
            {
                _sets.Add(new BindSet { Name = string.IsNullOrWhiteSpace(name) ? "Набор " + (_sets.Count + 1) : name.Trim() });
                _active = _sets.Count - 1;
                UpdateHasChordBinds();
            }
            Save();
            SafeInvoke(() => Changed?.Invoke());
        }

        public static void RemoveSet(int index)
        {
            lock (_lock)
            {
                if (_sets.Count <= 1 || index < 0 || index >= _sets.Count) return;
                _sets.RemoveAt(index);
                if (_active >= _sets.Count) _active = _sets.Count - 1;
                UpdateHasChordBinds();
            }
            Save();
            SafeInvoke(() => Changed?.Invoke());
        }

        public static void RenameSet(int index, string name)
        {
            lock (_lock)
            {
                if (index < 0 || index >= _sets.Count || string.IsNullOrWhiteSpace(name)) return;
                _sets[index].Name = name.Trim();
            }
            Save();
            SafeInvoke(() => Changed?.Invoke());
        }

        public static void SetActivateCombo(int index, string combo)
        {
            lock (_lock)
            {
                if (index < 0 || index >= _sets.Count) return;
                _sets[index].Activate = combo?.Trim() ?? "";
            }
            Save();
            SafeInvoke(() => Changed?.Invoke());
        }

        public static string GetActivateCombo(int index)
        {
            lock (_lock) { return index >= 0 && index < _sets.Count ? _sets[index].Activate : ""; }
        }

        /// <summary>Index of a set whose activation hotkey matches the pressed combo/chord, or -1.</summary>
        public static int MatchActivate(string combo, string chord)
        {
            lock (_lock)
            {
                for (int i = 0; i < _sets.Count; i++)
                {
                    string a = _sets[i].Activate;
                    if (string.IsNullOrWhiteSpace(a)) continue;
                    string na = NormalizeForMatch(a);
                    if (IsChordFormat(na)) { if (!string.IsNullOrEmpty(chord) && MatchChord(chord, na)) return i; }
                    else if (MatchCombo(combo, na)) return i;
                }
            }
            return -1;
        }

        // ---- config path ----

        public static string GetConfigPath()
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            return Path.Combine(dir, "RevitQuickAccess_bindsets.txt");
        }

        private static string LegacyPath()
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            return Path.Combine(dir, "RevitQuickAccess_binds.txt");
        }

        // ---- CRUD (on the active set) ----

        public static List<KeyBindEntry> GetBindsCopy()
        {
            lock (_lock) { return _binds.Select(b => b.Clone()).ToList(); }
        }

        public static void SetBinds(IEnumerable<KeyBindEntry> entries)
        {
            lock (_lock)
            {
                _binds.Clear();
                if (entries != null) _binds.AddRange(entries.Select(e => e.Clone()));
                UpdateHasChordBinds();
            }
            Save();
            SafeInvoke(() => Changed?.Invoke());
        }

        public static void AddBind(KeyBindEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.KeyCombo) || string.IsNullOrWhiteSpace(entry.Command)) return;
            lock (_lock) { _binds.Add(entry.Clone()); UpdateHasChordBinds(); }
            Save();
            SafeInvoke(() => Changed?.Invoke());
        }

        public static void UpdateBind(int index, KeyBindEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.KeyCombo) || string.IsNullOrWhiteSpace(entry.Command)) return;
            lock (_lock)
            {
                if (index < 0 || index >= _binds.Count) return;
                _binds[index] = entry.Clone();
                UpdateHasChordBinds();
            }
            Save();
            SafeInvoke(() => Changed?.Invoke());
        }

        public static void RemoveBind(int index)
        {
            lock (_lock)
            {
                if (index < 0 || index >= _binds.Count) return;
                _binds.RemoveAt(index);
                UpdateHasChordBinds();
            }
            Save();
            SafeInvoke(() => Changed?.Invoke());
        }

        public static bool ContainsKeyCombo(string keyCombo, int excludeIndex)
        {
            if (string.IsNullOrWhiteSpace(keyCombo)) return false;
            lock (_lock)
            {
                for (int i = 0; i < _binds.Count; i++)
                    if (i != excludeIndex && MatchCombo(keyCombo, _binds[i].KeyCombo)) return true;
            }
            return false;
        }

        public static bool IsValidKeyCombo(string s) => !string.IsNullOrWhiteSpace(s);

        // ---- persistence ----

        public static void Load()
        {
            lock (_lock)
            {
                _sets.Clear();
                try
                {
                    string path = GetConfigPath();
                    if (File.Exists(path))
                    {
                        ParseSets(File.ReadAllLines(path));
                    }
                    else if (File.Exists(LegacyPath()))
                    {
                        // migrate the old single-set file into the default set
                        var set = new BindSet { Name = "По умолчанию" };
                        ParseBindLines(File.ReadAllLines(LegacyPath()), set.Binds, applyEnabled: true);
                        _sets.Add(set);
                    }
                }
                catch { }
                if (_sets.Count == 0) _sets.Add(new BindSet { Name = "По умолчанию" });
                if (_active >= _sets.Count) _active = _sets.Count - 1;
                if (_active < 0) _active = 0;
                UpdateHasChordBinds();
            }
            // write the new-format file if we migrated
            if (!File.Exists(GetConfigPath())) Save();
        }

        public static void Save()
        {
            try { WriteFile(GetConfigPath()); } catch { }
        }

        private static void WriteFile(string path)
        {
            var lines = new List<string>
            {
                "# RevitQuickAccess bind sets. Section: [Имя]|КлавишаАктивации, then combo<Tab>command lines.",
                "# enabled=" + (_enabled ? "1" : "0"),
                "# active=" + _active,
                ""
            };
            lock (_lock)
            {
                foreach (var set in _sets)
                {
                    lines.Add("[" + set.Name + "]" + (string.IsNullOrWhiteSpace(set.Activate) ? "" : "|" + set.Activate));
                    foreach (var b in set.Binds)
                        lines.Add(b.KeyCombo + "\t" + b.Command);
                    lines.Add("");
                }
            }
            File.WriteAllLines(path, lines);
        }

        private static void ParseSets(string[] rawLines)
        {
            BindSet cur = null;
            int activeIdx = 0;
            foreach (var raw in rawLines)
            {
                string line = raw?.Trim() ?? "";
                if (line.Length == 0) continue;
                if (line.StartsWith("# enabled=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(line.Substring("# enabled=".Length).Trim(), out int v)) _enabled = v != 0;
                    continue;
                }
                if (line.StartsWith("# active=", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(line.Substring("# active=".Length).Trim(), out activeIdx);
                    continue;
                }
                if (line.StartsWith("#")) continue;

                if (line.StartsWith("["))
                {
                    int close = line.IndexOf(']');
                    string name = close > 0 ? line.Substring(1, close - 1) : line.Substring(1);
                    string activate = "";
                    int bar = line.IndexOf("]|", StringComparison.Ordinal);
                    if (bar >= 0) activate = line.Substring(bar + 2).Trim();
                    cur = new BindSet { Name = name.Trim(), Activate = activate };
                    _sets.Add(cur);
                    continue;
                }

                if (cur == null) { cur = new BindSet { Name = "По умолчанию" }; _sets.Add(cur); }
                AddBindLine(line, cur.Binds);
            }
            _active = activeIdx;
        }

        private static void ParseBindLines(string[] rawLines, List<KeyBindEntry> target, bool applyEnabled)
        {
            foreach (var raw in rawLines)
            {
                string line = raw?.Trim() ?? "";
                if (line.Length == 0) continue;
                if (line.StartsWith("# enabled=", StringComparison.OrdinalIgnoreCase))
                {
                    if (applyEnabled && int.TryParse(line.Substring("# enabled=".Length).Trim(), out int v)) _enabled = v != 0;
                    continue;
                }
                if (line.StartsWith("#")) continue;
                AddBindLine(line, target);
            }
        }

        private static void AddBindLine(string line, List<KeyBindEntry> target)
        {
            int sep = line.IndexOf('\t');
            if (sep < 0) sep = line.IndexOf('|');
            if (sep < 0) return;
            string combo = line.Substring(0, sep).Trim();
            string cmd = line.Substring(sep + 1).Trim();
            if (combo.Length > 0 && cmd.Length > 0) target.Add(new KeyBindEntry(combo, cmd));
        }

        public static bool SaveToFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                var lines = new List<string> { "# RevitQuickAccess binds (single set export): combo<Tab>command", "" };
                lock (_lock) foreach (var b in _binds) lines.Add(b.KeyCombo + "\t" + b.Command);
                File.WriteAllLines(path, lines);
                return true;
            }
            catch { return false; }
        }

        public static void LoadFromFile(string path, bool replace)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            lock (_lock)
            {
                if (replace) _binds.Clear();
                try { ParseBindLines(File.ReadAllLines(path), _binds, applyEnabled: false); UpdateHasChordBinds(); }
                catch { }
            }
            Save();
            SafeInvoke(() => Changed?.Invoke());
        }

        // ---- matching helpers (ported from BINDS10) ----

        public static string NormalizeForMatch(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            return string.Join("+", s.Split(new[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0));
        }

        public static bool MatchCombo(string pressed, string bind)
        {
            if (string.IsNullOrEmpty(pressed) || string.IsNullOrEmpty(bind)) return false;
            return string.Equals(NormalizeForMatch(pressed), NormalizeForMatch(bind), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsMultiTapFormat(string nc)
        {
            int i = nc.IndexOf('*');
            if (i < 0) return false;
            return int.TryParse(nc.Substring(i + 1).Trim(), out int c) && c >= 2;
        }

        public static string GetMultiTapBase(string nc)
        {
            int i = nc.IndexOf('*');
            return i < 0 ? nc : nc.Substring(0, i).Trim();
        }

        public static int GetMultiTapCount(string nc)
        {
            int i = nc.IndexOf('*');
            if (i < 0) return 1;
            return int.TryParse(nc.Substring(i + 1).Trim(), out int c) ? c : 1;
        }

        public static bool IsChordFormat(string nc)
        {
            var keys = nc.Split('+').Select(x => x.Trim())
                .Where(x => x.Length > 0
                    && !string.Equals(x, "Ctrl", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(x, "Shift", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(x, "Alt", StringComparison.OrdinalIgnoreCase)).ToList();
            return keys.Count >= 2;
        }

        public static bool MatchChord(string pressedChord, string bindChord)
        {
            if (string.IsNullOrEmpty(pressedChord) || string.IsNullOrEmpty(bindChord)) return false;
            var a = NormalizeForMatch(pressedChord).Split('+').Select(x => x.ToUpperInvariant()).OrderBy(x => x).ToList();
            var b = NormalizeForMatch(bindChord).Split('+').Select(x => x.ToUpperInvariant()).OrderBy(x => x).ToList();
            return a.Count == b.Count && a.SequenceEqual(b);
        }

        public static bool HasMultiTapVariant(string singleCombo)
        {
            lock (_lock)
            {
                foreach (var b in _binds)
                {
                    string nc = NormalizeForMatch(b.KeyCombo);
                    if (IsMultiTapFormat(nc) && string.Equals(GetMultiTapBase(nc), singleCombo, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        public static KeyBindEntry GetMultiTapBindForCount(string baseCombo, int tapCount)
        {
            lock (_lock)
            {
                foreach (var b in _binds)
                {
                    string nc = NormalizeForMatch(b.KeyCombo);
                    if (IsMultiTapFormat(nc) && GetMultiTapCount(nc) == tapCount
                        && string.Equals(GetMultiTapBase(nc), baseCombo, StringComparison.OrdinalIgnoreCase))
                        return b.Clone();
                }
            }
            return null;
        }

        public static string GetToggleCombo()
        {
            lock (_lock)
            {
                foreach (var b in _binds)
                    if (string.Equals(b.Command, ToggleCommand, StringComparison.OrdinalIgnoreCase))
                        return NormalizeForMatch(b.KeyCombo);
            }
            return null;
        }

        public static List<KeyBindEntry> Snapshot()
        {
            lock (_lock) { return _binds.Select(b => b.Clone()).ToList(); }
        }

        internal static void SetEnabledInternal(bool value)
        {
            lock (_lock) { _enabled = value; }
            Save();
            SafeInvoke(() => EnabledChanged?.Invoke(value));
            SafeInvoke(() => Changed?.Invoke());
        }

        private static void UpdateHasChordBinds()
        {
            _hasChordBinds = _binds.Any(b => IsChordFormat(NormalizeForMatch(b.KeyCombo ?? "")));
        }

        private static void SafeInvoke(Action a) { try { a(); } catch { } }
    }
}
