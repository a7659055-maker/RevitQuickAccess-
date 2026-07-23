using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using RevitQuickAccess.Binds;

namespace RevitQuickAccess.UI
{
    /// <summary>Searchable picker for the curated Revit actions (RevitActions.All), grouped by section.</summary>
    public partial class ActionPickerWindow : Window
    {
        public RevitAction Selected { get; private set; }

        public ActionPickerWindow()
        {
            InitializeComponent();

            var view = new ListCollectionView(RevitActions.All.ToList());
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RevitAction.Group)));
            view.Filter = Match;
            list.ItemsSource = view;
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            txtSearch.Focus();
        }

        private string _query = "";

        private bool Match(object o)
        {
            if (string.IsNullOrWhiteSpace(_query)) return true;
            var a = (RevitAction)o;
            return a.Label.IndexOf(_query, System.StringComparison.OrdinalIgnoreCase) >= 0
                || a.Token.IndexOf(_query, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Search_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _query = txtSearch.Text.Trim();
            (list.ItemsSource as ListCollectionView)?.Refresh();
            if (list.SelectedItem == null && list.Items.Count > 0) list.SelectedIndex = 0;
        }

        private void List_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Confirm();

        private void Ok_Click(object sender, RoutedEventArgs e) => Confirm();

        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        private void Confirm()
        {
            Selected = list.SelectedItem as RevitAction;
            if (Selected == null) return;
            DialogResult = true;
            Close();
        }
    }
}
