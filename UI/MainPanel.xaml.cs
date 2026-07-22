using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using RevitQuickAccess.Binds;
using RevitQuickAccess.Browser;
using RevitQuickAccess.Quick;
using RevitQuickAccess.Report;
using RevitQuickAccess.Settings;
using RevitQuickAccess.Transfer;
using WinForms = System.Windows.Forms;

namespace RevitQuickAccess.UI
{
    public partial class MainPanel : UserControl
    {
        private enum RecordTarget { None, Quick, BindCommand }

        private readonly ObservableCollection<KeyBindEntry> _rows = new ObservableCollection<KeyBindEntry>();
        private int _editingIndex = -1;
        private bool _syncingCheckbox;
        private bool _syncingSets;

        // key recording (binds tab)
        private bool _recordingKey;
        private readonly List<int> _recordedVks = new List<int>();
        private DispatcherTimer _recordTimer;

        // ribbon recording (shared)
        private RecordTarget _recordTarget = RecordTarget.None;
        private bool _suppressToggle;
        private readonly List<string> _seqIds = new List<string>();
        private readonly List<string> _seqLabels = new List<string>();
        private string _seqIconFile = "";

        // last command recorded into the bind command field (for "В БЫСТРЫЕ →")
        private string _lastBindCmdId = "";
        private string _lastBindCmdLabel = "";
        private ImageSource _lastBindCmdIcon;

        // macro (chain) recording for a bind command
        private bool _bindCmdRecording;
        private readonly List<string> _bindSeqIds = new List<string>();
        private readonly List<string> _bindSeqLabels = new List<string>();

        // tile drag/resize
        private QuickCommand _dragItem;
        private FrameworkElement _dragElement;
        private Point _dragStart;
        private bool _dragMoved;
        private bool _tilesDirty;

        public MainPanel()
        {
            InitializeComponent();
            lvBinds.ItemsSource = _rows;

            BindsManager.Changed += OnBindsChanged;
            BindsManager.EnabledChanged += OnEnabledChanged;

            icQuick.ItemsSource = QuickCommandsManager.Items;
            RibbonRecorder.Captured += OnRibbonCaptured;

            dgBrowser.ItemsSource = BrowserManager.Rows;
            cbTitleBlock.ItemsSource = BrowserManager.TitleBlocks;
            BrowserManager.Notified += OnBrowserNotified;

            // transfer (tab 4)
            TransferManager.Notified += OnTransferNotified;

            RefreshList();
            SyncEnabled();
            UpdateToggleLabel();
            RefreshSets();
            RefreshSettings();

            // if the previous session crashed, offer to send the report as soon as the panel opens
            Loaded += OnPanelLoaded;
        }

        private void OnPanelLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnPanelLoaded;

            // keep the fill handle glued to its cell while the grid scrolls
            var sv = FindChild<ScrollViewer>(dgBrowser);
            if (sv != null) sv.ScrollChanged += (s, a) => UpdateFillHandle();

            string pending = BugReporter.ReadPendingSummary();
            if (pending != null)
                Dispatcher.BeginInvoke(new Action(() => OpenReport(pending, isCrash: true)),
                    System.Windows.Threading.DispatcherPriority.Background);
        }

        private void BtnReport_Click(object sender, RoutedEventArgs e) => OpenReport(null, false);

        private void OpenReport(string prefill, bool isCrash)
        {
            try
            {
                var win = new BugReportWindow(prefill, isCrash) { Owner = Window.GetWindow(this) };
                win.ShowDialog();
            }
            catch { }
        }

        // ================= TAB 1: binds =================

        private void OnBindsChanged() => Dispatcher.Invoke(() => { RefreshList(); SyncEnabled(); UpdateToggleLabel(); RefreshSets(); });
        private void OnEnabledChanged(bool enabled) => Dispatcher.Invoke(SyncEnabled);

        private void RefreshList()
        {
            _rows.Clear();
            foreach (var b in BindsManager.GetBindsCopy()) _rows.Add(b);
        }

        private void SyncEnabled()
        {
            _syncingCheckbox = true;
            chkEnabled.IsChecked = BindsManager.Enabled;
            _syncingCheckbox = false;
        }

        private void UpdateToggleLabel()
        {
            string t = BindsManager.GetToggleCombo();
            lblToggle.Text = string.IsNullOrEmpty(t) ? " · F8 вкл/выкл" : " · " + t + " вкл/выкл";
        }

        // ---- bind sets (profiles) ----

        private void RefreshSets()
        {
            _syncingSets = true;
            cbSets.Items.Clear();
            foreach (var name in BindsManager.GetSetNames()) cbSets.Items.Add(name);
            cbSets.SelectedIndex = BindsManager.ActiveIndex;
            tbSetKey.Text = BindsManager.GetActivateCombo(BindsManager.ActiveIndex);
            _syncingSets = false;
        }

        private void CbSets_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingSets) return;
            int i = cbSets.SelectedIndex;
            if (i >= 0) BindsManager.SwitchTo(i);
        }

        private void BtnSetAdd_Click(object sender, RoutedEventArgs e)
        {
            string name = Prompt("Новый набор биндов", "Набор");
            if (!string.IsNullOrWhiteSpace(name)) BindsManager.AddSet(name);
        }

        private void BtnSetRename_Click(object sender, RoutedEventArgs e)
        {
            int i = cbSets.SelectedIndex;
            if (i < 0) return;
            string name = Prompt("Переименовать набор", BindsManager.ActiveName);
            if (!string.IsNullOrWhiteSpace(name)) BindsManager.RenameSet(i, name);
        }

        private void BtnSetDelete_Click(object sender, RoutedEventArgs e)
        {
            int i = cbSets.SelectedIndex;
            if (i < 0) return;
            if (MessageBox.Show($"Удалить набор «{BindsManager.ActiveName}» со всеми его биндами?", "Наборы",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                BindsManager.RemoveSet(i);
        }

        private void BtnSetKeyApply_Click(object sender, RoutedEventArgs e)
        {
            BindsManager.SetActivateCombo(BindsManager.ActiveIndex, tbSetKey.Text.Trim());
            ShowStatus("Клавиша активации набора задана.", false);
        }

        /// <summary>Minimal modal text prompt (no extra XAML file).</summary>
        private string Prompt(string title, string initial)
        {
            var tb = new TextBox { Text = initial ?? "", Margin = new Thickness(12, 12, 12, 6), Padding = new Thickness(4) };
            var ok = new Button { Content = "OK", Width = 74, Margin = new Thickness(4), IsDefault = true };
            var cancel = new Button { Content = "Отмена", Width = 74, Margin = new Thickness(4), IsCancel = true };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 0, 8, 8) };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            var root = new StackPanel();
            root.Children.Add(tb); root.Children.Add(buttons);
            var win = new Window
            {
                Title = title, Content = root, Width = 320, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
                Owner = Window.GetWindow(this)
            };
            ok.Click += (s, ev) => { win.DialogResult = true; win.Close(); };
            win.Loaded += (s, ev) => { tb.Focus(); tb.SelectAll(); };
            return win.ShowDialog() == true ? tb.Text.Trim() : null;
        }

        private void ShowStatus(string msg, bool isError)
        {
            lblStatus.Text = msg ?? "";
            lblStatus.Foreground = isError
                ? new SolidColorBrush(Color.FromRgb(194, 68, 54))
                : new SolidColorBrush(Color.FromRgb(46, 130, 90));
        }

        private void ChkEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_syncingCheckbox) return;
            BindsManager.Enabled = chkEnabled.IsChecked == true;
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Binds (*.txt)|*.txt|Все файлы (*.*)|*.*",
                DefaultExt = "txt",
                FileName = "RevitQuickAccess_binds.txt"
            };
            if (dlg.ShowDialog() == true)
            {
                bool ok = BindsManager.SaveToFile(dlg.FileName);
                ShowStatus(ok ? "Сохранено." : "Ошибка.", !ok);
            }
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Binds (*.txt)|*.txt|Все файлы (*.*)|*.*" };
            if (dlg.ShowDialog() != true) return;
            var res = MessageBox.Show("Заменить текущие бинды?\n\nДа — заменить\nНет — добавить\nОтмена — ничего",
                "Импорт", MessageBoxButton.YesNoCancel);
            if (res == MessageBoxResult.Yes) BindsManager.LoadFromFile(dlg.FileName, true);
            else if (res == MessageBoxResult.No) BindsManager.LoadFromFile(dlg.FileName, false);
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

        private void LvBinds_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => btnDelete.IsEnabled = lvBinds.SelectedIndex >= 0;

        private void LvBinds_DoubleClick(object sender, MouseButtonEventArgs e) => EditSelected();

        private void EditSelected()
        {
            int i = lvBinds.SelectedIndex;
            var list = BindsManager.GetBindsCopy();
            if (i < 0 || i >= list.Count) return;
            tbKey.Text = list[i].KeyCombo;
            tbCommand.Text = list[i].Command;
            _editingIndex = i;
            btnAdd.Content = "СОХРАНИТЬ";
            ShowStatus("Редактирование", false);
        }

        private void DeleteSelected()
        {
            int i = lvBinds.SelectedIndex;
            if (i < 0) return;
            BindsManager.RemoveBind(i);
            ResetEdit();
            ShowStatus("Удалено.", false);
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e) => AddOrUpdate();

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_recordingKey) { CancelKeyRecording(); return; }
            if (_recordTarget == RecordTarget.BindCommand) { StopRibbonRecording(); }
            ResetEdit();
            tbKey.Clear();
            tbCommand.Clear();
            ShowStatus("", false);
        }

        private void AddOrUpdate()
        {
            string key = tbKey.Text.Trim();
            string cmd = tbCommand.Text.Trim();
            ShowStatus("", false);
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(cmd)) { ShowStatus("Укажи клавишу и команду.", true); return; }
            if (!BindsManager.IsValidKeyCombo(key)) { ShowStatus("Некорректная клавиша.", true); return; }
            if (BindsManager.ContainsKeyCombo(key, _editingIndex) &&
                MessageBox.Show("Такое сочетание уже есть. Заменить?", "Quick Access",
                                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            if (_editingIndex >= 0) { BindsManager.UpdateBind(_editingIndex, new KeyBindEntry(key, cmd)); ShowStatus("Сохранено.", false); }
            else { BindsManager.AddBind(new KeyBindEntry(key, cmd)); ShowStatus("Добавлено.", false); }
            ResetEdit();
            tbKey.Clear();
            tbCommand.Clear();
        }

        private void ResetEdit()
        {
            _editingIndex = -1;
            btnAdd.Content = "ДОБАВИТЬ";
        }

        /// <summary>Add the command from the Команда field as a tile on the "Быстрые команды" tab.</summary>
        private void BtnBindToQuick_Click(object sender, RoutedEventArgs e)
        {
            string cmd = tbCommand.Text.Trim();
            if (string.IsNullOrEmpty(cmd)) { ShowStatus("Нет команды.", true); return; }

            // reuse the label/icon if this command was just recorded from the ribbon
            bool sameAsRecorded = string.Equals(cmd, _lastBindCmdId, StringComparison.Ordinal);
            string name = sameAsRecorded && !string.IsNullOrWhiteSpace(_lastBindCmdLabel) ? _lastBindCmdLabel : cmd;
            string iconFile = sameAsRecorded ? IconStore.Save(_lastBindCmdIcon) : "";

            QuickCommandsManager.Add(new QuickCommand(name, cmd, iconFile));
            ShowStatus("Добавлено в «Быстрые команды».", false);
        }

        // --- key recording ---

        private TextBox _keyBox;   // where recorded keys are written (bind key or set-activation key)

        private void BtnRecordKey_Click(object sender, RoutedEventArgs e) => StartKeyRecording(tbKey);
        private void BtnRecordSetKey_Click(object sender, RoutedEventArgs e) => StartKeyRecording(tbSetKey);

        private void StartKeyRecording(TextBox box)
        {
            _keyBox = box;
            box.Text = "нажми клавиши…";
            _recordedVks.Clear();
            _recordingKey = true;
            KeyInterceptor.Suspended = true;
            box.Focus();
        }

        private void KeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_recordingKey) return;
            e.Handled = true;
            var box = sender as TextBox ?? _keyBox;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { CancelKeyRecording(); if (box != null) box.Text = ""; return; }
            if (IsModifier(key)) { if (box != null) box.Text = BuildCombo(); return; }
            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk != 0 && !_recordedVks.Contains(vk)) _recordedVks.Add(vk);
            if (box != null) box.Text = BuildCombo();
            RestartRecordTimer();
        }

        private static bool IsModifier(Key k) =>
            k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.System;

        private string BuildCombo()
        {
            var parts = new List<string>();
            var mods = Keyboard.Modifiers;
            if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
            var keys = new List<int>(_recordedVks); keys.Sort();
            foreach (var vk in keys) parts.Add(((WinForms.Keys)vk).ToString());
            return string.Join("+", parts);
        }

        private void RestartRecordTimer()
        {
            _recordTimer?.Stop();
            _recordTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _recordTimer.Tick += (s, e) => FinishKeyRecording();
            _recordTimer.Start();
        }

        private void FinishKeyRecording()
        {
            _recordTimer?.Stop(); _recordTimer = null;
            _recordingKey = false; KeyInterceptor.Suspended = false;
        }

        private void CancelKeyRecording()
        {
            _recordTimer?.Stop(); _recordTimer = null;
            _recordingKey = false; KeyInterceptor.Suspended = false;
            _recordedVks.Clear();
        }

        // --- command recording (binds tab) — same mechanic as quick tab ---

        private void BtnRecordCmd_Click(object sender, RoutedEventArgs e)
        {
            if (_bindCmdRecording)   // second click = STOP (used to finish a chain/macro)
            {
                RibbonRecorder.Recording = false;
                _recordTarget = RecordTarget.None;
                _bindCmdRecording = false;
                txtRecCmd.Text = "ЗАПИСЬ";
                if (chkSequenceBind.IsChecked == true && _bindSeqIds.Count > 0)
                {
                    tbCommand.Text = string.Join(" ; ", _bindSeqIds);
                    ShowStatus($"Записан макрос из {_bindSeqIds.Count} шагов.", false);
                }
                return;
            }

            if (RibbonRecorder.Recording) return;
            _bindSeqIds.Clear();
            _bindSeqLabels.Clear();
            _recordTarget = RecordTarget.BindCommand;
            RibbonRecorder.Recording = true;
            _bindCmdRecording = true;
            txtRecCmd.Text = "СТОП";
            tbCommand.Text = chkSequenceBind.IsChecked == true
                ? "кликай кнопки ленты, потом СТОП…"
                : "кликни кнопку на ленте…";
        }

        // ================= TAB 2: quick command tiles =================

        private void OnRibbonCaptured(string id, string label, ImageSource icon)
        {
            Dispatcher.Invoke(() =>
            {
                if (_recordTarget == RecordTarget.BindCommand)
                {
                    if (chkSequenceBind.IsChecked == true)
                    {
                        _bindSeqIds.Add(id);
                        _bindSeqLabels.Add(string.IsNullOrWhiteSpace(label) ? id : label);
                        tbCommand.Text = string.Join(" ; ", _bindSeqIds);
                        if (_bindSeqIds.Count == 1) _lastBindCmdIcon = icon;
                        _lastBindCmdId = tbCommand.Text;
                        _lastBindCmdLabel = string.Join(" + ", _bindSeqLabels);
                        ShowStatus($"Шаг {_bindSeqIds.Count} записан — жми СТОП когда всё.", false);
                        // keep recording for more steps
                    }
                    else
                    {
                        tbCommand.Text = id;
                        _lastBindCmdId = id;
                        _lastBindCmdLabel = string.IsNullOrWhiteSpace(label) ? id : label;
                        _lastBindCmdIcon = icon;
                        _bindCmdRecording = false;
                        txtRecCmd.Text = "ЗАПИСЬ";
                        StopRibbonRecording();
                        ShowStatus("Команда записана.", false);
                    }
                    return;
                }

                // quick tab
                if (chkSequence.IsChecked == true)
                {
                    if (_seqIds.Count == 0) _seqIconFile = IconStore.Save(icon);
                    _seqIds.Add(id);
                    _seqLabels.Add(string.IsNullOrWhiteSpace(label) ? id : label);
                    lblQuickStatus.Text = $"Цепочка ({_seqIds.Count}): {string.Join(" ; ", _seqLabels)}";
                }
                else
                {
                    string iconFile = IconStore.Save(icon);
                    string name = string.IsNullOrWhiteSpace(label) ? id : label;
                    QuickCommandsManager.Add(new QuickCommand(name, id, iconFile));
                    lblQuickStatus.Text = "Записано: " + name;
                    _suppressToggle = true; btnRecord2.IsChecked = false; _suppressToggle = false;
                    RibbonRecorder.Recording = false;
                    _recordTarget = RecordTarget.None;
                }
            });
        }

        private void StopRibbonRecording()
        {
            RibbonRecorder.Recording = false;
            _recordTarget = RecordTarget.None;
        }

        private void BtnRecord2_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressToggle) return;
            _seqIds.Clear(); _seqLabels.Clear(); _seqIconFile = "";
            _recordTarget = RecordTarget.Quick;
            RibbonRecorder.Recording = true;
            lblQuickStatus.Text = chkSequence.IsChecked == true
                ? "Запись цепочки — кликай кнопки ленты, затем сними «Запись»."
                : "Запись — кликни кнопку на ленте Revit.";
        }

        private void BtnRecord2_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressToggle) return;
            RibbonRecorder.Recording = false;
            _recordTarget = RecordTarget.None;
            if (_seqIds.Count > 0)
            {
                string name = string.Join(" + ", _seqLabels);
                if (name.Length > 40) name = name.Substring(0, 40) + "…";
                QuickCommandsManager.Add(new QuickCommand(name, string.Join(" ; ", _seqIds), _seqIconFile));
                lblQuickStatus.Text = "Цепочка из " + _seqIds.Count + " шагов записана.";
                _seqIds.Clear(); _seqLabels.Clear(); _seqIconFile = "";
            }
        }

        private void BtnAddTile_Click(object sender, RoutedEventArgs e)
        {
            if (!RibbonRecorder.Recording) btnRecord2.IsChecked = true;
        }

        // --- tile menu ---

        private static QuickCommand Ctx(object sender) => (sender as FrameworkElement)?.DataContext as QuickCommand;

        private void TileMenu_Click(object sender, RoutedEventArgs e)
        {
            var qc = Ctx(sender);
            if (qc == null) return;
            foreach (var q in QuickCommandsManager.Items) if (q != qc) q.MenuOpen = false;
            qc.MenuOpen = !qc.MenuOpen;
        }

        private void TileToBind_Click(object sender, RoutedEventArgs e)
        {
            var qc = Ctx(sender); if (qc == null) return;
            qc.MenuOpen = false;
            tabs.SelectedIndex = 0;                 // switch to Binds
            tbCommand.Text = qc.Command;            // carry the command over
            _lastBindCmdId = qc.Command;
            _lastBindCmdLabel = qc.Name;
            _lastBindCmdIcon = qc.IconSource;
            ShowStatus("Нажми клавишу для «" + qc.Name + "» — идёт запись.", false);
            BtnRecordKey_Click(sender, e);          // auto-start key recording → user just presses the key
        }

        private void TileRenameStart_Click(object sender, RoutedEventArgs e)
        {
            var qc = Ctx(sender); if (qc == null) return;
            qc.MenuOpen = false;
            qc.Editing = true;
        }

        private void TileDup_Click(object sender, RoutedEventArgs e)
        {
            var qc = Ctx(sender); if (qc == null) return;
            qc.MenuOpen = false;
            QuickCommandsManager.Add(new QuickCommand(qc.Name + " копия", qc.Command, qc.IconFile, qc.Width));
        }

        private void TileDelete_Click(object sender, RoutedEventArgs e)
        {
            var qc = Ctx(sender); if (qc == null) return;
            qc.MenuOpen = false;
            QuickCommandsManager.Remove(qc);
        }

        private void TileRename_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (sender is TextBox tb) tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            var qc = Ctx(sender); if (qc != null) qc.Editing = false;
            QuickCommandsManager.Save();
            e.Handled = true;
        }

        private void TileRename_LostFocus(object sender, RoutedEventArgs e)
        {
            var qc = Ctx(sender); if (qc != null) qc.Editing = false;
            QuickCommandsManager.Save();
        }

        // --- tile drag (reorder) + resize ---

        private void Tile_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInteractiveChild(e.OriginalSource as DependencyObject)) return; // grip / hamburger / menu
            _dragElement = sender as FrameworkElement;
            _dragItem = _dragElement?.DataContext as QuickCommand;
            _dragStart = e.GetPosition(icQuick);
            _dragMoved = false;
            AnimatePress(_dragElement);   // visible "press" feedback on click
        }

        private void Tiles_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (Mouse.Captured is Thumb) return;           // resizing, not reordering
            if (_dragItem == null) return;

            var pos = e.GetPosition(icQuick);
            if (!_dragMoved &&
                Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            if (!_dragMoved) { _dragMoved = true; AnimatePickup(_dragElement); }

            var target = TileUnder(pos);
            if (target != null && target != _dragItem)
            {
                int from = QuickCommandsManager.Items.IndexOf(_dragItem);
                int to = QuickCommandsManager.Items.IndexOf(target);
                if (from >= 0 && to >= 0)
                {
                    // ObservableCollection.Move preserves the container, so the pickup animation
                    // stays on the same element as it slides to its new slot.
                    QuickCommandsManager.Items.Move(from, to);
                    _tilesDirty = true;
                }
            }
        }

        private void Tiles_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragItem != null && !_dragMoved)
            {
                AnimateClickRelease(_dragElement);            // spring back = "released"
                CommandExecutor.Execute(_dragItem.Command);   // plain click = run
            }
            else
            {
                AnimateDrop(_dragElement);
            }

            if (_tilesDirty) { QuickCommandsManager.Save(); _tilesDirty = false; }
            _dragItem = null;
            _dragElement = null;
            _dragMoved = false;
        }

        private static ScaleTransform EnsureScale(FrameworkElement el)
        {
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            if (el.RenderTransform is ScaleTransform s) return s;
            var st = new ScaleTransform(1, 1);
            el.RenderTransform = st;
            return st;
        }

        private static void Scale(ScaleTransform st, double to, TimeSpan dur, IEasingFunction ease = null)
        {
            var a = new DoubleAnimation(to, dur) { EasingFunction = ease };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        private static void AnimatePress(FrameworkElement el)
        {
            if (el == null) return;
            Scale(EnsureScale(el), 0.90, TimeSpan.FromMilliseconds(70), new CubicEase { EasingMode = EasingMode.EaseOut });
        }

        private static void AnimateClickRelease(FrameworkElement el)
        {
            if (el == null) return;
            // bounce back past 1.0 for a clear, satisfying "click"
            Scale(EnsureScale(el), 1.0, TimeSpan.FromMilliseconds(260),
                  new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 });
        }

        private static void AnimatePickup(FrameworkElement el)
        {
            if (el == null) return;
            Scale(EnsureScale(el), 1.06, TimeSpan.FromMilliseconds(120), new CubicEase { EasingMode = EasingMode.EaseOut });
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.82, TimeSpan.FromMilliseconds(120)));
        }

        private static void AnimateDrop(FrameworkElement el)
        {
            if (el == null) return;
            Scale(EnsureScale(el), 1.0, TimeSpan.FromMilliseconds(180), new CubicEase { EasingMode = EasingMode.EaseOut });
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        }

        private void TileResizeW_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (Ctx(sender) is QuickCommand qc) { qc.Width += e.HorizontalChange; _tilesDirty = true; }
        }

        private void TileResizeH_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (Ctx(sender) is QuickCommand qc) { qc.Height += e.VerticalChange; _tilesDirty = true; }
        }

        private QuickCommand TileUnder(Point pt)
        {
            var hit = icQuick.InputHitTest(pt) as DependencyObject;
            while (hit != null)
            {
                if (hit is FrameworkElement fe && fe.DataContext is QuickCommand qc &&
                    QuickCommandsManager.Items.Contains(qc))
                    return qc;
                hit = VisualTreeHelper.GetParent(hit);
            }
            return null;
        }

        private static bool IsInteractiveChild(DependencyObject d)
        {
            while (d != null)
            {
                if (d is Thumb || d is ButtonBase || d is TextBox) return true;
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        // ================= TAB 3: browser =================

        private bool _syncingTb;

        private void OnBrowserNotified(string msg) => Dispatcher.Invoke(() =>
        {
            lblBrowserStatus.Text = msg;
            SyncTitleBlock();
        });

        private void SyncTitleBlock()
        {
            _syncingTb = true;
            cbTitleBlock.SelectedItem = BrowserManager.TitleBlocks
                .FirstOrDefault(t => t.Id == BrowserManager.TitleBlockId);
            _syncingTb = false;
        }

        private void CbTitleBlock_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingTb) return;
            if (cbTitleBlock.SelectedItem is TitleBlockOption tb) BrowserManager.TitleBlockId = tb.Id;
        }

        // --- sheet duplication (deferred, like everything else here) ---

        private void CtxDupSheetEmpty_Click(object sender, RoutedEventArgs e) => AddSheetDuplicate(0, "копия");
        private void CtxDupSheetDet_Click(object sender, RoutedEventArgs e) => AddSheetDuplicate(1, "копия с детализацией");
        private void CtxDupSheetViews_Click(object sender, RoutedEventArgs e) => AddSheetDuplicate(3, "копия с видами");

        private void AddSheetDuplicate(int option, string suffix)
        {
            var r = SelectedRow;
            if (r == null || r.IsNew || !r.IsSheet)
            {
                lblBrowserStatus.Text = "Дублировать можно существующий ЛИСТ (выдели строку листа).";
                return;
            }
            BrowserManager.AddRow(new BrowserRow
            {
                Kind = "Лист (копия)",
                IsSheet = true,
                IsNew = true,
                SourceViewId = r.Id,
                DupOption = option,
                Name = r.Name + " " + suffix,
                SheetNumber = ""
            });
            lblBrowserStatus.Text = "Копия листа добавлена — «Применить», чтобы создать.";
        }

        private void BtnBrowserLoad_Click(object sender, RoutedEventArgs e)
        {
            lblBrowserStatus.Text = "Загрузка…";
            BrowserManager.RequestLoad();
        }

        private void BtnBrowserApply_Click(object sender, RoutedEventArgs e)
        {
            dgBrowser.CommitEdit(DataGridEditingUnit.Row, true);
            lblBrowserStatus.Text = "Применение (Revit может подумать)…";
            BrowserManager.RequestApply();
        }

        // --- sorting: sheets ALWAYS first, natural (numeric-aware) order, with arrow indicators ---

        private void DgBrowser_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;   // we sort ourselves so sheets stay on top

            var dir = e.Column.SortDirection != ListSortDirection.Ascending
                ? ListSortDirection.Ascending : ListSortDirection.Descending;
            foreach (var c in dgBrowser.Columns) c.SortDirection = null;
            e.Column.SortDirection = dir;              // this is what draws the arrow

            SortRows(e.Column.SortMemberPath, dir);
        }

        private void SortRows(string field, ListSortDirection dir)
        {
            var rows = BrowserManager.Rows.ToList();
            int sign = dir == ListSortDirection.Ascending ? 1 : -1;

            rows.Sort((a, b) =>
            {
                if (a.IsSheet != b.IsSheet) return a.IsSheet ? -1 : 1;   // sheets first, always
                string va = Field(a, field), vb = Field(b, field);
                return sign * NaturalComparer.Instance.Compare(va, vb);
            });

            BrowserManager.Rows.Clear();
            foreach (var r in rows) BrowserManager.Rows.Add(r);
        }

        private static string Field(BrowserRow r, string field)
        {
            switch (field)
            {
                case "Kind": return r.Kind;
                case "SheetNumber": return r.SheetNumber;
                case "Status": return r.Status;
                default: return r.Name;
            }
        }

        // --- Excel-style fill handle: drag the little square down to auto-number ---

        private BrowserRow _fillFrom;
        private DataGridColumn _fillColumn;
        private int _fillStart = -1;
        private bool _filling;

        private void DgBrowser_CurrentCellChanged(object sender, EventArgs e) => UpdateFillHandle();

        private void UpdateFillHandle()
        {
            var info = dgBrowser.CurrentCell;
            if (!info.IsValid || info.Column == null || !(info.Item is BrowserRow) ||
                (info.Column != colName && info.Column != colSheetNo))
            { fillHandle.Visibility = Visibility.Collapsed; return; }

            var cell = GetCellContainer(info);
            if (cell == null) { fillHandle.Visibility = Visibility.Collapsed; return; }
            try
            {
                var r = cell.TransformToVisual(fillLayer)
                            .TransformBounds(new Rect(0, 0, cell.ActualWidth, cell.ActualHeight));
                Canvas.SetLeft(fillHandle, r.Right - 5);
                Canvas.SetTop(fillHandle, r.Bottom - 5);
                fillHandle.Visibility = Visibility.Visible;
            }
            catch { fillHandle.Visibility = Visibility.Collapsed; }
        }

        private DataGridCell GetCellContainer(DataGridCellInfo info)
        {
            if (!(dgBrowser.ItemContainerGenerator.ContainerFromItem(info.Item) is DataGridRow row)) return null;
            var presenter = FindChild<DataGridCellsPresenter>(row);
            return presenter?.ItemContainerGenerator.ContainerFromIndex(info.Column.DisplayIndex) as DataGridCell;
        }

        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int n = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(parent, i);
                if (c is T t) return t;
                var d = FindChild<T>(c);
                if (d != null) return d;
            }
            return null;
        }

        private void FillHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var info = dgBrowser.CurrentCell;
            _fillFrom = info.Item as BrowserRow;
            _fillColumn = info.Column;
            _fillStart = _fillFrom != null ? BrowserManager.Rows.IndexOf(_fillFrom) : -1;
            if (_fillStart < 0) return;
            _filling = true;
            fillHandle.CaptureMouse();
            e.Handled = true;
        }

        private void FillHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_filling) e.Handled = true;   // fill is applied on release
        }

        private void FillHandle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_filling) return;
            _filling = false;
            fillHandle.ReleaseMouseCapture();
            e.Handled = true;

            int target = RowIndexAt(e.GetPosition(dgBrowser));
            if (target > _fillStart) ApplyFill(_fillStart, target);
            UpdateFillHandle();
        }

        private int RowIndexAt(Point p)
        {
            var hit = dgBrowser.InputHitTest(p) as DependencyObject;
            while (hit != null && !(hit is DataGridRow)) hit = VisualTreeHelper.GetParent(hit);
            if (hit is DataGridRow row && row.Item is BrowserRow br) return BrowserManager.Rows.IndexOf(br);
            return -1;
        }

        private void ApplyFill(int from, int to)
        {
            if (from < 0 || from >= BrowserManager.Rows.Count) return;
            var src = BrowserManager.Rows[from];
            bool numberCol = _fillColumn == colSheetNo;
            string baseVal = numberCol ? src.SheetNumber : src.Name;
            if (string.IsNullOrEmpty(baseVal)) return;

            int step = 1, filled = 0;
            for (int i = from + 1; i <= to && i < BrowserManager.Rows.Count; i++)
            {
                var r = BrowserManager.Rows[i];
                if (numberCol && r.IsSheet != src.IsSheet) continue;   // sheet numbers only for sheets
                string v = Bump(baseVal, step++);
                if (numberCol) r.SheetNumber = v; else r.Name = v;
                filled++;
            }
            lblBrowserStatus.Text = $"Автозаполнение: заполнено строк — {filled}.";
        }

        /// <summary>"1"→2,3… · "1.2"→1.3,1.4… · "ВК-01"→ВК-02… · без числа — просто копия.</summary>
        private static string Bump(string src, int step)
        {
            int i = src.Length;
            while (i > 0 && char.IsDigit(src[i - 1])) i--;
            if (i == src.Length) return src;
            string prefix = src.Substring(0, i), num = src.Substring(i);
            if (!long.TryParse(num, out long v)) return src;
            string s = (v + step).ToString();
            if (num.Length > s.Length && num[0] == '0') s = s.PadLeft(num.Length, '0');
            return prefix + s;
        }

        private void BtnAddSheet_Click(object sender, RoutedEventArgs e)
        {
            BrowserManager.AddRow(new BrowserRow { Kind = "Лист", IsSheet = true, IsNew = true, Name = "Новый лист", SheetNumber = "" });
            lblBrowserStatus.Text = "Добавлен лист — задай № и имя, затем «Применить».";
        }

        private void BtnAddDup_Click(object sender, RoutedEventArgs e)
        {
            lblBrowserStatus.Text = "Добавление копии активного вида…";
            BrowserManager.RequestAddDuplicate();
        }

        private void BtnRemoveRow_Click(object sender, RoutedEventArgs e)
        {
            if (dgBrowser.SelectedItem is BrowserRow r)
            {
                BrowserManager.Rows.Remove(r);
                lblBrowserStatus.Text = r.IsNew ? "Строка убрана." : "Убрано из таблицы (в модели не удаляется).";
            }
        }

        // --- context menu (mirrors Revit's Project Browser; edits are deferred until «Применить») ---

        private BrowserRow SelectedRow => dgBrowser.SelectedItem as BrowserRow;

        private void DgBrowser_RightClick(object sender, MouseButtonEventArgs e)
        {
            // select the row under the cursor so the menu acts on it
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not DataGridRow) dep = VisualTreeHelper.GetParent(dep);
            if (dep is DataGridRow row) { row.IsSelected = true; dgBrowser.SelectedItem = row.Item; }
        }

        private void CtxOpen_Click(object sender, RoutedEventArgs e)
        {
            var r = SelectedRow;
            if (r == null || r.IsNew) { lblBrowserStatus.Text = "Открыть можно существующий вид/лист."; return; }
            lblBrowserStatus.Text = "Открываю…";
            BrowserManager.RequestOpen(r.Id);
        }

        private void CtxRename_Click(object sender, RoutedEventArgs e)
        {
            var r = SelectedRow;
            if (r == null) return;
            dgBrowser.CurrentCell = new DataGridCellInfo(r, colName);
            dgBrowser.BeginEdit();
        }

        private void CtxDup_Click(object sender, RoutedEventArgs e) => AddDuplicateFromSelected(0, "копия");
        private void CtxDupDetail_Click(object sender, RoutedEventArgs e) => AddDuplicateFromSelected(1, "с детализацией");
        private void CtxDupDependent_Click(object sender, RoutedEventArgs e) => AddDuplicateFromSelected(2, "зависимый");

        private void AddDuplicateFromSelected(int option, string suffix)
        {
            var r = SelectedRow;
            if (r == null || r.IsNew || r.IsSheet)
            {
                lblBrowserStatus.Text = "Дублировать можно существующий вид (не лист).";
                return;
            }
            BrowserManager.AddRow(new BrowserRow
            {
                Kind = "Вид (копия)",
                IsSheet = false,
                IsNew = true,
                SourceViewId = r.Id,
                DupOption = option,
                Name = r.Name + " " + suffix
            });
            lblBrowserStatus.Text = "Копия добавлена — «Применить», чтобы создать.";
        }

        private void CtxDelete_Click(object sender, RoutedEventArgs e)
        {
            var r = SelectedRow;
            if (r == null) return;
            if (r.IsNew) { BrowserManager.Rows.Remove(r); lblBrowserStatus.Text = "Новая строка убрана."; return; }
            r.ToDelete = !r.ToDelete;
            lblBrowserStatus.Text = r.ToDelete
                ? "Помечено на удаление — применится по «Применить»."
                : "Пометка удаления снята.";
        }

        // ================= TAB 4: transfer =================

        private string _csvPath = "";

        private void OnTransferNotified(string msg) => Dispatcher.Invoke(() => lblTransferStatus.Text = msg);

        private void PushTransferOptions()
        {
            TransferManager.Basis = cbBasis.SelectedIndex == 1 ? CoordBasis.Internal : CoordBasis.Shared;
            TransferManager.WholeSystem = chkWholeSystem.IsChecked == true;
            TransferManager.WithDir = chkWithDir.IsChecked == true;
            TransferManager.Tag = chkTag.IsChecked == true;
            TransferManager.TagParam = tbTagParam.Text.Trim();
        }

        private void TrCopy_Click(object sender, RoutedEventArgs e)
        {
            PushTransferOptions();
            lblTransferStatus.Text = "Копирование…";
            TransferManager.RequestCopy();
        }

        private void TrPasteExact_Click(object sender, RoutedEventArgs e)
        {
            PushTransferOptions();
            lblTransferStatus.Text = "Вставка…";
            TransferManager.RequestPasteExact();
        }

        private void TrCopyBase_Click(object sender, RoutedEventArgs e)
        {
            PushTransferOptions();
            lblTransferStatus.Text = "Укажи опорную точку в модели…";
            TransferManager.RequestCopyBase();
        }

        private void TrPasteBase_Click(object sender, RoutedEventArgs e)
        {
            PushTransferOptions();
            lblTransferStatus.Text = "Укажи целевую точку в модели…";
            TransferManager.RequestPasteBase();
        }

        private void TrInspect_Click(object sender, RoutedEventArgs e)
        {
            lblTransferStatus.Text = "Чтение координат…";
            TransferManager.RequestInspect();
        }

        // ================= TAB 5: settings =================

        private bool _syncingSettings;

        private void RefreshSettings()
        {
            _syncingSettings = true;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            tbVertMm.Text = PluginSettings.VerticalPipeMm.ToString(ci);
            tbConnMm.Text = PluginSettings.ConnectorPipeMm.ToString(ci);
            tbTeeMm.Text = PluginSettings.TeeBranchMm.ToString(ci);
            tbCoupMm.Text = PluginSettings.CouplingStepMm.ToString(ci);
            chkVertUp.IsChecked = PluginSettings.VerticalPipeUp;
            _syncingSettings = false;
        }

        private static bool ParseMm(string s, out double mm) =>
            double.TryParse((s ?? "").Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out mm) && mm > 0;

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            if (!ParseMm(tbVertMm.Text, out double vert)) { lblSettingsStatus.Text = "«Стояк»: нужно положительное число (мм)."; return; }
            if (!ParseMm(tbConnMm.Text, out double conn)) { lblSettingsStatus.Text = "«По коннектору»: нужно положительное число (мм)."; return; }
            if (!ParseMm(tbTeeMm.Text, out double tee)) { lblSettingsStatus.Text = "«Ответвление тройника»: нужно положительное число (мм)."; return; }
            if (!ParseMm(tbCoupMm.Text, out double coup)) { lblSettingsStatus.Text = "«Шаг муфт»: нужно положительное число (мм)."; return; }

            PluginSettings.VerticalPipeMm = vert;
            PluginSettings.ConnectorPipeMm = conn;
            PluginSettings.TeeBranchMm = tee;
            PluginSettings.CouplingStepMm = coup;
            PluginSettings.VerticalPipeUp = chkVertUp.IsChecked == true;
            PluginSettings.Save();
            lblSettingsStatus.Text = $"Сохранено: стояк {vert:0.#} · коннектор {conn:0.#} · тройник {tee:0.#} · муфты {coup:0.#} мм.";
        }

        private void VertDir_Changed(object sender, RoutedEventArgs e)
        {
            if (_syncingSettings) return;
            PluginSettings.VerticalPipeUp = chkVertUp.IsChecked == true;
            PluginSettings.Save();
            lblSettingsStatus.Text = "Направление: " + (PluginSettings.VerticalPipeUp ? "вверх" : "вниз");
        }

        private void TrPasteCoords_Click(object sender, RoutedEventArgs e)
        {
            var mm = chkMoveShared.IsChecked == true ? TransferManager.LastSharedMm : TransferManager.LastInternalMm;
            if (mm == null || mm.Length < 3) { lblTransferStatus.Text = "Сначала «Инспектор» на объекте-образце."; return; }
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            tbMoveX.Text = mm[0].ToString("0.0", ci);
            tbMoveY.Text = mm[1].ToString("0.0", ci);
            tbMoveZ.Text = mm[2].ToString("0.0", ci);
            lblTransferStatus.Text = "Координаты подставлены — жми «→», чтобы переместить выделенное сюда.";
        }

        private void TrMove_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(tbMoveX.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double x) ||
                !double.TryParse(tbMoveY.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double y) ||
                !double.TryParse(tbMoveZ.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double z))
            {
                lblTransferStatus.Text = "Впиши числовые X, Y, Z (мм).";
                return;
            }
            TransferManager.Mx = x; TransferManager.My = y; TransferManager.Mz = z;
            TransferManager.MoveShared = chkMoveShared.IsChecked == true;
            lblTransferStatus.Text = "Перемещение…";
            TransferManager.RequestMoveXyz();
        }

        private void TrCsvPick_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "CSV (*.csv;*.txt)|*.csv;*.txt|Все файлы (*.*)|*.*" };
            if (dlg.ShowDialog() == true) { _csvPath = dlg.FileName; lblTransferStatus.Text = "CSV: " + _csvPath; }
        }

        private void TrPasteCsv_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_csvPath)) { lblTransferStatus.Text = "Сначала выбери CSV."; return; }
            PushTransferOptions();
            TransferManager.CsvPath = _csvPath;
            lblTransferStatus.Text = "Вставка по CSV…";
            TransferManager.RequestPasteCsv();
        }
    }
}
