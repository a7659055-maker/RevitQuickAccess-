using System;
using System.Linq;
using System.Windows;

namespace RqaInstaller
{
    public partial class App : Application
    {
        private void App_OnStartup(object sender, StartupEventArgs e)
        {
            bool uninstall = e.Args.Any(a =>
                string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase));

            var win = new MainWindow(uninstall);
            win.Show();
        }
    }
}
