using System.ComponentModel;
using System.Windows.Media;

namespace RevitQuickAccess.Quick
{
    /// <summary>
    /// A saved "quick command": a friendly name, the command string to run (a single Revit command
    /// id or a " ; " separated macro), an icon captured from the ribbon, and the tile width.
    /// Name / Command / IconFile / Width are persisted; MenuOpen / Editing / IconSource are transient UI state.
    /// </summary>
    public sealed class QuickCommand : INotifyPropertyChanged
    {
        public const double DefaultWidth = 96;
        public const double MinWidth = 64;
        public const double MaxWidth = 260;

        private string _name = "";
        private string _command = "";
        private string _iconFile = "";
        private double _width = DefaultWidth;
        private double _height = DefaultWidth;
        private bool _menuOpen;
        private bool _editing;
        private ImageSource _iconSource;
        private bool _iconLoaded;

        public string Name
        {
            get => _name;
            set { _name = value ?? ""; OnChanged(nameof(Name)); }
        }

        public string Command
        {
            get => _command;
            set { _command = value ?? ""; OnChanged(nameof(Command)); }
        }

        /// <summary>Icon file name (inside the icons folder), or empty for the placeholder.</summary>
        public string IconFile
        {
            get => _iconFile;
            set { _iconFile = value ?? ""; _iconLoaded = false; OnChanged(nameof(IconFile)); OnChanged(nameof(IconSource)); OnChanged(nameof(HasIcon)); }
        }

        public double Width
        {
            get => _width;
            set
            {
                double v = value;
                if (v < MinWidth) v = MinWidth;
                if (v > MaxWidth) v = MaxWidth;
                _width = v; OnChanged(nameof(Width));
            }
        }

        public double Height
        {
            get => _height;
            set
            {
                double v = value;
                if (v < MinWidth) v = MinWidth;
                if (v > MaxWidth) v = MaxWidth;
                _height = v; OnChanged(nameof(Height));
            }
        }

        public bool MenuOpen
        {
            get => _menuOpen;
            set { _menuOpen = value; OnChanged(nameof(MenuOpen)); }
        }

        public bool Editing
        {
            get => _editing;
            set { _editing = value; OnChanged(nameof(Editing)); }
        }

        /// <summary>Lazily loaded icon bitmap (null → show placeholder).</summary>
        public ImageSource IconSource
        {
            get
            {
                if (!_iconLoaded)
                {
                    _iconSource = IconStore.Load(_iconFile)          // captured PNG (new recordings)
                                  ?? RibbonIcons.GetLarge(_command);  // live ribbon icon by command id
                    // only cache once we actually found something; the ribbon may not be built yet
                    if (_iconSource != null) _iconLoaded = true;
                }
                return _iconSource;
            }
        }

        public bool HasIcon => IconSource != null;

        public QuickCommand() { }

        public QuickCommand(string name, string command, string iconFile = "", double width = DefaultWidth, double height = DefaultWidth)
        {
            _name = name ?? "";
            _command = command ?? "";
            _iconFile = iconFile ?? "";
            Width = width;
            Height = height;
        }

        public QuickCommand Clone() => new QuickCommand(Name, Command, IconFile, Width, Height);

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
