using System.Collections.Generic;
using System.Linq;
using System.Windows;
using RevitQuickAccess.Commands;

namespace RevitQuickAccess.UI
{
    /// <summary>
    /// Generic Slate-Amber dialog shared by the pipe-type tools: a few labelled dropdowns,
    /// an optional text field, a few checkboxes and a live "what will happen" box.
    /// Each command just fills in the lists — no per-tool XAML.
    /// </summary>
    public partial class ToolDialog : Window
    {
        public List<ToolChoice> Choices { get; }
        public List<ToolFlag> Flags { get; }

        /// <summary>Value of the optional free-text field (suffix, name, …).</summary>
        public string InputValue => txtInput.Text;

        public ToolDialog(string title, string subtitle, List<ToolChoice> choices, List<ToolFlag> flags,
                          string okText = "Применить", string inputLabel = null, string inputValue = "")
        {
            InitializeComponent();

            Choices = choices ?? new List<ToolChoice>();
            Flags = flags ?? new List<ToolFlag>();

            Title = lblTitle.Text = title;
            lblSubtitle.Text = subtitle ?? "";
            btnOk.Content = okText;

            icChoices.ItemsSource = Choices;
            icFlags.ItemsSource = Flags;

            if (inputLabel != null)
            {
                pnlInput.Visibility = Visibility.Visible;
                lblInput.Text = inputLabel;
                txtInput.Text = inputValue ?? "";
            }

            foreach (var c in Choices)
            {
                c.SelectionChanged += _ => RefreshInfo();
                if (c.Selected == null && c.Items.Count > 0) c.Selected = c.Items[0];
            }
            RefreshInfo();
        }

        private void RefreshInfo()
        {
            var parts = Choices
                .Select(c => c.Selected?.Info)
                .Where(s => !string.IsNullOrWhiteSpace(s));
            lblInfo.Text = string.Join("\n\n", parts);
        }

        private void Ok_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }

        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    }
}
