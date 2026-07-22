using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace RqaInstaller
{
    public partial class MainWindow : Window
    {
        private const string ProductName = "Revit Quick Access";
        private const string ProductVersion = "1.0";
        private const string RegKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\RevitQuickAccess";

        private static readonly string TargetDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         @"Autodesk\Revit\Addins\2026");

        private string DllPath => Path.Combine(TargetDir, "RevitQuickAccess.dll");
        private string AddinPath => Path.Combine(TargetDir, "RevitQuickAccess.addin");
        private string PdbPath => Path.Combine(TargetDir, "RevitQuickAccess.pdb");
        private string SetupCopyPath => Path.Combine(TargetDir, "RevitQuickAccess-Setup.exe");

        private bool _busy;

        public MainWindow(bool uninstallMode)
        {
            InitializeComponent();
            lblPath.Text = TargetDir;
            lblVersion.Text = "ВЕРСИЯ " + ProductVersion;
            RefreshState();

            if (uninstallMode)
            {
                Loaded += async (s, e) =>
                {
                    if (IsInstalled() && !IsRevitRunning())
                        await DoUninstall();
                };
            }
        }

        // Guarantee the process actually exits when the window closes (no lingering background process).
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Environment.Exit(0);
        }

        // ---- state ----

        private static bool IsRevitRunning() => Process.GetProcessesByName("Revit").Length > 0;
        private bool IsInstalled() => File.Exists(DllPath);

        private void RefreshState()
        {
            bool revit = IsRevitRunning();
            bool installed = IsInstalled();

            btnUninstall.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            btnInstall.Content = installed ? "Переустановить" : "Установить";

            if (revit)
            {
                revitBar.Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xF1, 0xDE));
                revitBar.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0x81, 0x1E));
                lblRevit.Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x5A, 0x12));
                lblRevit.Text = "Revit запущен — установить можно. Плагин подхватится после перезапуска Revit.";
                if (!_busy) { btnInstall.IsEnabled = true; btnUninstall.IsEnabled = true; }
            }
            else
            {
                revitBar.Background = new SolidColorBrush(Color.FromRgb(0xE9, 0xF3, 0xEC));
                revitBar.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x9D, 0x63));
                lblRevit.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x4F));
                lblRevit.Text = installed
                    ? "✓ Revit закрыт. Плагин установлен — можно переустановить или удалить."
                    : "✓ Revit закрыт — можно устанавливать.";
                if (!_busy) { btnInstall.IsEnabled = true; btnUninstall.IsEnabled = true; }
            }
        }

        // ---- buttons ----

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => RefreshState();

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(TargetDir);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + TargetDir + "\"") { UseShellExecute = true });
            }
            catch { }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private async void BtnInstall_Click(object sender, RoutedEventArgs e) => await DoInstall();

        private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Удалить плагин Revit Quick Access?\n(конфигурация биндов сохранится)",
                    "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                await DoUninstall();
        }

        // ---- operations ----

        private async Task DoInstall()
        {
            if (_busy) return;
            bool revitWasRunning = IsRevitRunning();
            _busy = true; SetButtons(false);

            try
            {
                await Step(10, "Создание папки...");
                Directory.CreateDirectory(TargetDir);
                CleanupOld();

                await Step(35, "Копирование плагина...");
                WriteResourceSafe("payload.RevitQuickAccess.dll", DllPath);

                await Step(60, "Копирование манифеста...");
                WriteResourceSafe("payload.RevitQuickAccess.addin", AddinPath);

                await Step(80, "Регистрация деинсталлятора...");
                TryCopySelf();
                RegisterUninstall();

                await Step(100, "Готово.");
                MessageBox.Show(
                    revitWasRunning
                        ? "Плагин установлен.\n\nRevit запущен — перезапусти его, чтобы плагин загрузился."
                        : "Плагин установлен.\n\nЗапусти Revit 2026 — на ленте появится вкладка «Quick Access».",
                    ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Ошибка установки:\n" + ex.Message +
                    (IsRevitRunning() ? "\n\nФайл занят запущенным Revit — закрой Revit и повтори." : ""),
                    ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _busy = false; SetButtons(true); RefreshState();
            }
        }

        private async Task DoUninstall()
        {
            if (_busy) return;
            bool revitWasRunning = IsRevitRunning();
            _busy = true; SetButtons(false);

            try
            {
                await Step(30, "Удаление файлов плагина...");
                // removing the .addin is what actually unloads it on the next Revit start
                TryRemove(AddinPath);
                TryRemove(DllPath);
                TryRemove(PdbPath);

                await Step(70, "Удаление записи деинсталляции...");
                UnregisterUninstall();

                await Step(100, "Удалено.");
                MessageBox.Show(
                    revitWasRunning
                        ? "Плагин удалён. Revit запущен — он выгрузится после перезапуска Revit."
                        : "Плагин удалён. Конфигурация биндов оставлена в папке Addins.",
                    ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка удаления:\n" + ex.Message, ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _busy = false; SetButtons(true); RefreshState();
            }
        }

        // ---- helpers ----

        private async Task Step(int pct, string text)
        {
            lblStatus.Text = text;
            progress.Value = pct;
            await Task.Delay(220);
        }

        private void SetButtons(bool enabled)
        {
            btnInstall.IsEnabled = enabled;
            btnUninstall.IsEnabled = enabled;
            btnRefresh.IsEnabled = enabled;
            btnClose.IsEnabled = enabled;
        }

        private static void WriteResource(string logicalName, string destPath)
        {
            var asm = Assembly.GetExecutingAssembly();
            using var src = asm.GetManifestResourceStream(logicalName);
            if (src == null) throw new FileNotFoundException("Не найден встроенный ресурс: " + logicalName);
            using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write);
            src.CopyTo(dst);
        }

        /// <summary>
        /// Write a payload file even when Revit is running. A first install isn't blocked at all
        /// (nothing holds the file yet); for a re-install the loaded DLL can't be overwritten, but on
        /// Windows it CAN be renamed — so we move it aside and drop the new one in its place. The
        /// leftover ".old_*" is deleted on the next install.
        /// </summary>
        private static void WriteResourceSafe(string logicalName, string destPath)
        {
            try { WriteResource(logicalName, destPath); return; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            string aside = destPath + ".old_" + DateTime.Now.Ticks;
            File.Move(destPath, aside);      // allowed for a loaded assembly
            WriteResource(logicalName, destPath);
        }

        /// <summary>Delete, or (if the file is locked by a running Revit) at least move it aside.</summary>
        private static void TryRemove(string path)
        {
            try { if (File.Exists(path)) { File.Delete(path); return; } } catch { }
            try { if (File.Exists(path)) File.Move(path, path + ".old_" + DateTime.Now.Ticks); } catch { }
        }

        private static void CleanupOld()
        {
            try
            {
                foreach (var f in Directory.GetFiles(TargetDir, "*.old_*"))
                    try { File.Delete(f); } catch { }
            }
            catch { }
        }

        private void TryCopySelf()
        {
            try
            {
                string self = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(self) &&
                    !string.Equals(Path.GetFullPath(self), Path.GetFullPath(SetupCopyPath), StringComparison.OrdinalIgnoreCase))
                    File.Copy(self, SetupCopyPath, overwrite: true);
            }
            catch { }
        }

        private void RegisterUninstall()
        {
            string uninstallExe = File.Exists(SetupCopyPath) ? SetupCopyPath : (Environment.ProcessPath ?? "");
            RegAdd("DisplayName", ProductName);
            RegAdd("DisplayVersion", ProductVersion);
            RegAdd("Publisher", "RQA");
            RegAdd("InstallLocation", TargetDir);
            RegAdd("DisplayIcon", uninstallExe);
            RegAdd("UninstallString", "\"" + uninstallExe + "\" --uninstall");
            RegAddDword("NoModify", 1);
            RegAddDword("NoRepair", 1);
        }

        private void UnregisterUninstall()
        {
            RunReg("delete", RegKey, "/f");
        }

        private void RegAdd(string name, string value)
            => RunReg("add", RegKey, "/v", name, "/t", "REG_SZ", "/d", value, "/f");

        private void RegAddDword(string name, int value)
            => RunReg("add", RegKey, "/v", name, "/t", "REG_DWORD", "/d", value.ToString(), "/f");

        private static void RunReg(params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo("reg.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                p?.WaitForExit(4000);
            }
            catch { }
        }
    }
}
