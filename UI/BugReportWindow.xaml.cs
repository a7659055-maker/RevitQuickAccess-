using System.Windows;
using RevitQuickAccess.Report;

namespace RevitQuickAccess.UI
{
    public partial class BugReportWindow : Window
    {
        private readonly bool _isCrash;

        public BugReportWindow(string prefill = null, bool isCrash = false)
        {
            InitializeComponent();
            _isCrash = isCrash;
            if (!string.IsNullOrEmpty(prefill)) tbDesc.Text = prefill;
            lblHint.Text = BugReporter.EndpointConfigured
                ? "Отчёт уйдёт разработчику автоматически. Копия сохранится в папку reports."
                : "Приём отчётов пока не настроен — отчёт сохранится в папку reports и в буфер обмена.";
            Loaded += (s, e) => { tbDesc.Focus(); tbDesc.CaretIndex = tbDesc.Text.Length; };
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            btnSend.IsEnabled = false;
            var result = BugReporter.Send(tbDesc.Text, _isCrash);
            BugReporter.ClearPending();   // reported now (either sent or handed to the mail client)
            MessageBox.Show(this, result.Message, "Отчёт об ошибке", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
