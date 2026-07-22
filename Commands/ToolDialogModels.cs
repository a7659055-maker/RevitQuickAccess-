using System.Collections.Generic;
using System.ComponentModel;

namespace RevitQuickAccess.Commands
{
    /// <summary>One entry in a <see cref="ToolChoice"/> dropdown.</summary>
    public class ToolOption
    {
        /// <summary>Text shown in the dropdown.</summary>
        public string Name { get; set; }

        /// <summary>Multi-line details shown in the info box when this entry is selected.</summary>
        public string Info { get; set; }

        /// <summary>Whatever the command needs to carry along (usually a Revit element).</summary>
        public object Tag { get; set; }

        public override string ToString() => Name;
    }

    /// <summary>A labelled dropdown in the generic tool dialog.</summary>
    public class ToolChoice : INotifyPropertyChanged
    {
        private ToolOption _selected;

        public string Label { get; set; }
        public string Tip { get; set; }
        public List<ToolOption> Items { get; set; } = new List<ToolOption>();

        public ToolOption Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                Raise(nameof(Selected));
                SelectionChanged?.Invoke(this);
            }
        }

        /// <summary>Convenience: the tag of the selected entry, already cast.</summary>
        public T Tag<T>() where T : class => _selected?.Tag as T;

        public event System.Action<ToolChoice> SelectionChanged;
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>A checkbox in the generic tool dialog.</summary>
    public class ToolFlag : INotifyPropertyChanged
    {
        private bool _isChecked;

        public string Text { get; set; }
        public string Tip { get; set; }

        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
