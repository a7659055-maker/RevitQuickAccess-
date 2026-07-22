using System.Collections.Generic;

namespace RevitQuickAccess.Binds
{
    /// <summary>
    /// One keybinding: a key combo (e.g. "E", "Ctrl+E", "E*2", "G+H+J") mapped to a command.
    /// A command is one or more steps executed in sequence (macro). A single Revit command id,
    /// a recorded ribbon action, or an internal action all serialize into a Command string;
    /// sequences are separated by " ; " (see BindCommand for parsing).
    /// Config format is 1:1 compatible with the AutoCAD BINDS10 plugin: "combo\tcommand".
    /// </summary>
    public sealed class KeyBindEntry
    {
        public string KeyCombo { get; set; } = "";

        /// <summary>Raw command string as stored in the config (may contain a " ; " separated sequence).</summary>
        public string Command { get; set; } = "";

        public KeyBindEntry() { }

        public KeyBindEntry(string keyCombo, string command)
        {
            KeyCombo = keyCombo ?? "";
            Command = command ?? "";
        }

        public KeyBindEntry Clone() => new KeyBindEntry(KeyCombo, Command);

        /// <summary>Split the command into its ordered steps (a single-command bind returns one step).</summary>
        public List<string> Steps()
        {
            var steps = new List<string>();
            if (string.IsNullOrWhiteSpace(Command)) return steps;
            foreach (var part in Command.Split(new[] { " ; " }, System.StringSplitOptions.None))
            {
                var s = part.Trim();
                if (s.Length > 0) steps.Add(s);
            }
            return steps;
        }
    }
}
