using System;
using System.Reflection;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitQuickAccess.Binds;
using RevitQuickAccess.Browser;
using RevitQuickAccess.Quick;
using RevitQuickAccess.Report;
using RevitQuickAccess.UI;

namespace RevitQuickAccess
{
    /// <summary>
    /// Plugin entry point. Registers the ribbon button + dockable pane, loads the saved binds,
    /// and installs the key interceptor. First install requires one Revit restart (so the pane
    /// and ribbon get registered); afterwards everything is live.
    /// </summary>
    public class App : IExternalApplication
    {
        // Stable id for the dockable pane
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("1b2c3d4e-5f60-7182-93a4-b5c6d7e8f901"));

        /// <summary>Captured on the first Idling tick; used by CommandExecutor to post commands.</summary>
        public static UIApplication UiApp { get; private set; }

        private UIControlledApplication _ctrlApp;

        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                _ctrlApp = app;

                // Crash capture / bug reporter: detect a prior unclean shutdown, hook exceptions,
                // and auto-send any pending crash reports (if SMTP is configured in the mail config).
                CrashGuard.Install(System.Windows.Threading.Dispatcher.CurrentDispatcher);
                CrashGuard.MarkSessionStart();
                try { BugReporter.SendPending(); } catch { }

                Settings.PluginSettings.Load();
                BindsManager.Load();
                QuickCommandsManager.Load();

                // Ribbon: one button that shows the panel
                const string tabName = "Quick Access";
                try { app.CreateRibbonTab(tabName); } catch { /* tab may already exist */ }
                var panel = app.CreateRibbonPanel(tabName, "Панель");
                string asmPath = Assembly.GetExecutingAssembly().Location;
                var btnData = new PushButtonData(
                    "RQA_ShowPanel", "Quick\nAccess", asmPath,
                    "RevitQuickAccess.Commands.ShowPanelCommand")
                {
                    ToolTip = "Открыть панель Quick Access (бинды, быстрые команды, диспетчер)"
                };
                btnData.LargeImage = RibbonIcon.Make("Q", 32);
                btnData.Image = RibbonIcon.Make("Q", 16);
                panel.AddItem(btnData);

                // Tools panel — these buttons are also recordable (ribbon click) and bindable
                var tools = app.CreateRibbonPanel(tabName, "Инструменты");
                void Add(string id, string text, string cls, string glyph, string tip)
                {
                    var b = new PushButtonData(id, text, asmPath, "RevitQuickAccess.Commands." + cls)
                    {
                        ToolTip = tip,
                        LargeImage = RibbonIcon.Make(glyph, 32),
                        Image = RibbonIcon.Make(glyph, 16)
                    };
                    tools.AddItem(b);
                }
                Add("RQA_Trim", "Обрезка", "TrimCommand", "✂", "Обрезка линий по пересечениям, как TRIM в AutoCAD (кликни на удаляемый участок)");
                Add("RQA_VertPipe", "Стояк", "VerticalPipeCommand", "↕",
                    "Вертикальный трубопровод на фиксированную величину от коннектора (величина — во вкладке «Настройки»)");
                Add("RQA_ConnPipe", "По\nконнектору", "ConnectorPipeCommand", "→",
                    "Трубопровод в направлении самого коннектора: кликни фитинг у нужного выхода — труба вырастет туда (величина — в «Настройках»)");
                Add("RQA_TrCopy", "Копир.\nв коорд.", "TransferCopyCommand", "К", "Перенос: копировать выделенное");
                Add("RQA_TrPaste", "Вставить\nв коорд.", "TransferPasteCommand", "В", "Перенос: вставить в исходных координатах");
                Add("RQA_TrCopyBase", "Копир.\nопорн.", "TransferCopyBaseCommand", "Ко", "Перенос: копировать с опорной точкой");
                Add("RQA_TrPasteBase", "Вставить\nопорн.", "TransferPasteBaseCommand", "Во", "Перенос: вставить по опорной точке");
                Add("RQA_TrInspect", "Коорд.", "TransferInspectCommand", "И", "Перенос: показать координаты выделенного");
                Add("RQA_Coupling", "Авто\nмуфты", "CouplingCommand", "М",
                    "Автомуфты: разрезает выбранные трубы на сегменты и ставит муфты из трассировки типа");

                tools.AddItem(new PushButtonData("RQA_FlexPipe", "Гибкая\nтруба", asmPath,
                    "RevitQuickAccess.Commands.FlexPipeCommand")
                {
                    ToolTip = "Соединить гибким трубопроводом: кликни один коннектор, затем второй",
                    LargeImage = RibbonIcon.MakeFlex(32),
                    Image = RibbonIcon.MakeFlex(16)
                });

                // grouped tee buttons — click the main button or open the dropdown (like Revit's Wall tool)
                PushButtonData TeeBtn(string id, string text, string cls, string dir, string tip)
                    => new PushButtonData(id, text, asmPath, "RevitQuickAccess.Commands." + cls)
                    {
                        ToolTip = tip,
                        LargeImage = RibbonIcon.MakeTee(32, dir),
                        Image = RibbonIcon.MakeTee(16, dir)
                    };

                if (tools.AddItem(new SplitButtonData("RQA_TeeGroup", "Тройник")) is SplitButton tee)
                {
                    tee.AddPushButton(TeeBtn("RQA_TeeUp", "Тройник\nвверх", "TeeUpCommand", "up", "Врезать тройник вверх"));
                    tee.AddPushButton(TeeBtn("RQA_TeeDown", "Тройник\nвниз", "TeeDownCommand", "down", "Врезать тройник вниз"));
                    tee.AddPushButton(TeeBtn("RQA_TeeLeft", "Тройник\nвлево", "TeeLeftCommand", "left", "Врезать тройник влево"));
                    tee.AddPushButton(TeeBtn("RQA_TeeRight", "Тройник\nвправо", "TeeRightCommand", "right", "Врезать тройник вправо"));
                }

                if (tools.AddItem(new SplitButtonData("RQA_Tee45Group", "Тройник 45°")) is SplitButton tee45)
                {
                    tee45.AddPushButton(TeeBtn("RQA_Tee45Up", "45°\nвверх", "Tee45UpCommand", "up45", "Врезать тройник 45° вверх"));
                    tee45.AddPushButton(TeeBtn("RQA_Tee45Down", "45°\nвниз", "Tee45DownCommand", "down45", "Врезать тройник 45° вниз"));
                    tee45.AddPushButton(TeeBtn("RQA_Tee45Left", "45°\nвлево", "Tee45LeftCommand", "left45", "Врезать тройник 45° влево"));
                    tee45.AddPushButton(TeeBtn("RQA_Tee45Right", "45°\nвправо", "Tee45RightCommand", "right45", "Врезать тройник 45° вправо"));
                }

                // Dockable pane (like Properties / Project Browser) — panel built lazily on first show
                app.RegisterDockablePane(PaneId, "Quick Access", new DockablePanelProvider());

                // Cursor toast for bind on/off (works even when the Revit canvas is focused)
                ToastNotifier.Init();
                BindsManager.EnabledChanged += on =>
                    ToastNotifier.Show(on ? "Бинды включены" : "Бинды выключены", on);
                KeyInterceptor.SetSwitched += name => ToastNotifier.Show("Набор: " + name, true);

                // Key interceptor (runs on this UI thread)
                KeyInterceptor.Install();

                // Ribbon-click recorder for the "Быстрые команды" tab
                RibbonRecorder.Install();

                // ExternalEvent for the batch browser tab (model edits must run in API context)
                BrowserManager.Init();

                // ExternalEvent for the precise-transfer tab
                Transfer.TransferManager.Init();

                // Capture UIApplication on first idle, then unsubscribe
                app.Idling += OnFirstIdling;

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Quick Access", "Ошибка запуска плагина:\n" + ex);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            try { KeyInterceptor.Uninstall(); } catch { }
            try { RibbonRecorder.Uninstall(); } catch { }
            try { CrashGuard.MarkSessionClean(); } catch { }
            return Result.Succeeded;
        }

        private void OnFirstIdling(object sender, IdlingEventArgs e)
        {
            if (sender is UIApplication ui)
                UiApp = ui;

            if (_ctrlApp != null)
            {
                try { _ctrlApp.Idling -= OnFirstIdling; } catch { }
            }
        }
    }
}
