using System.ComponentModel;

namespace RevitQuickAccess.Browser
{
    /// <summary>
    /// One editable row of the batch project-browser table. Represents an existing view/sheet
    /// (Id >= 0) or a pending new one (IsNew). Edits are held here until the user hits "Применить",
    /// then applied to the model in a single transaction.
    /// </summary>
    public sealed class BrowserRow : INotifyPropertyChanged
    {
        public bool IsSheet { get; set; }
        public bool IsNew { get; set; }

        /// <summary>ElementId value of the existing element, or -1 for a new one.</summary>
        public long Id { get; set; } = -1;

        /// <summary>For a new "duplicate view" row: the source view's ElementId value.</summary>
        public long SourceViewId { get; set; } = -1;

        /// <summary>Duplicate mode for a new duplicate row: 0=Duplicate, 1=WithDetailing, 2=AsDependent.</summary>
        public int DupOption { get; set; } = 0;

        // original values (for diffing on apply)
        public string OrigName { get; set; } = "";
        public string OrigNumber { get; set; } = "";
        public string OrigGroup { get; set; } = "";

        public string Kind { get; set; } = "";

        /// <summary>Category grouping order for the ТИП column (views): plans, elevations, sections, …</summary>
        public int KindRank { get; set; } = 100;

        /// <summary>Title block type actually placed on this sheet (empty for views / sheets without one).</summary>
        public long TitleBlockId { get; set; } = -1;
        public string TitleBlockName { get; set; } = "";

        private string _name = "";
        private string _sheetNumber = "";
        private string _group = "";
        private bool _toDelete;

        /// <summary>Marked for deletion on apply (deferred, like everything else in this tab).</summary>
        public bool ToDelete
        {
            get => _toDelete;
            set { _toDelete = value; OnChanged(nameof(ToDelete)); OnChanged(nameof(Status)); }
        }

        public string Name
        {
            get => _name;
            set { _name = value ?? ""; OnChanged(nameof(Name)); OnChanged(nameof(Status)); }
        }

        public string SheetNumber
        {
            get => _sheetNumber;
            set { _sheetNumber = value ?? ""; OnChanged(nameof(SheetNumber)); OnChanged(nameof(Status)); }
        }

        public string Group
        {
            get => _group;
            set { _group = value ?? ""; OnChanged(nameof(Group)); OnChanged(nameof(Status)); }
        }

        private string _error = "";

        /// <summary>Set when applying this row failed; shown in the status column.</summary>
        public string Error
        {
            get => _error;
            set { _error = value ?? ""; OnChanged(nameof(Error)); OnChanged(nameof(Status)); }
        }

        /// <summary>Human-readable change state for the last column.</summary>
        public string Status
        {
            get
            {
                if (!string.IsNullOrEmpty(_error)) return "ошибка";
                if (_toDelete) return "удалить";
                if (IsNew) return "новый";
                bool changed = _name != OrigName || _sheetNumber != OrigNumber || _group != OrigGroup;
                return changed ? "изменён" : "";
            }
        }

        /// <summary>Refresh the "original" snapshot from the current edited values (after a successful apply).</summary>
        public void Snapshot()
        {
            OrigName = _name;
            OrigNumber = _sheetNumber;
            OrigGroup = _group;
            Error = "";
            OnChanged(nameof(Status));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
