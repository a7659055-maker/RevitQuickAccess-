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

            // set by an older installer that just self-updated and launched this newer one
            string updatedFrom = ArgValue(e.Args, "--updated-from");
            string replacePath = ArgValue(e.Args, "--replace");

            var win = new MainWindow(uninstall, updatedFrom, replacePath);
            win.Show();
        }

        private static string ArgValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
