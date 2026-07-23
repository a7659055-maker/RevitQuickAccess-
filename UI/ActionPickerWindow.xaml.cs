using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using RevitQuickAccess.Binds;

namespace RevitQuickAccess.UI
{
    /// <summary>
    /// Searchable picker for bindable Revit actions. Prefers the FULL command catalog read from Revit's
    /// KeyboardShortcuts.xml (every menu / context-menu / Project Browser command); falls back to the
    /// small curated list if that file doesn't exist yet.
    /// </summary>
    public partial class ActionPickerWindow : Window
    {
        public RevitAction Selected { get; private set; }

        /// <summary>Set when the user asked to open Revit's Keyboard Shortcuts dialog (to generate the file).</summary>
        public bool RequestOpenShortcuts { get; private set; }

        public ActionPickerWindow()
        {
            InitializeComponent();

            var full = RevitCommandCatalog.Load();
            bool haveFull = full.Count > 0;
            var items = haveFull ? full : RevitActions.All.ToList();

            lblHeader.Text = haveFull
                ? $"Полный список команд Revit ({items.Count}) — лента, контекстное меню, панель вида, диспетчер проекта. " +
                  "Найди нужную, выбери и назначь клавишу. Команда выполняется над выделенным."
                : "Частые действия, которых нет на ленте (скрыть элемент/категорию, переопределить графику, " +
                  "показать скрытые). Чтобы биндить ЛЮБУЮ команду Revit (в т.ч. диспетчера проекта) — жми ссылку ниже.";
            btnFullList.Visibility = haveFull ? Visibility.Collapsed : Visibility.Visible;

            var view = new ListCollectionView(items);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RevitAction.Group)));
            view.Filter = Match;
            list.ItemsSource = view;
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            txtSearch.Focus();
        }

        private void FullList_Click(object sender, RoutedEventArgs e)
        {
            RequestOpenShortcuts = true;
            DialogResult = false;
            Close();
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
