using System.Collections.Generic;
using System.Linq;
using System.Windows;
using RevitQuickAccess.Commands;

namespace RevitQuickAccess.UI
{
    public partial class CouplingWindow : Window
    {
        public List<CouplingGroup> Groups { get; }

        /// <summary>True when the user asked to pick the pipes again.</summary>
        public bool Reselect { get; private set; }

        public CouplingWindow(List<CouplingGroup> groups)
        {
            InitializeComponent();
            Groups = groups ?? new List<CouplingGroup>();
            icGroups.ItemsSource = Groups;
            lblTotal.Text = $"Выбрано труб: {Groups.Sum(g => g.PipeCount)} · типов трубопровода: {Groups.Count}";
        }

        private void Apply_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }

        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        private void Reselect_Click(object sender, RoutedEventArgs e)
        {
            Reselect = true;
            DialogResult = false;
            Close();
        }
    }
}
