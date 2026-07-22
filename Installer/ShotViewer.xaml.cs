using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RqaInstaller
{
    /// <summary>
    /// Full-size preview: the screenshots are ~1250 px wide, so in the installer's small box the UI
    /// on them is unreadable. Here they are shown as large as the screen allows.
    /// </summary>
    public partial class ShotViewer : Window
    {
        private readonly List<ImageSource> _shots;
        private int _i;

        public ShotViewer(List<ImageSource> shots, int start)
        {
            InitializeComponent();
            _shots = shots ?? new List<ImageSource>();
            Show(start);
        }

        private void Show(int i)
        {
            if (_shots.Count == 0) return;
            _i = ((i % _shots.Count) + _shots.Count) % _shots.Count;
            img.Source = _shots[_i];
            lblIndex.Text = $"{_i + 1} / {_shots.Count}";
        }

        private void Prev_Click(object sender, RoutedEventArgs e) => Show(_i - 1);

        private void Next_Click(object sender, RoutedEventArgs e) => Show(_i + 1);

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Left || e.Key == Key.Up) Show(_i - 1);
            else if (e.Key == Key.Right || e.Key == Key.Down || e.Key == Key.Space) Show(_i + 1);
        }
    }
}
