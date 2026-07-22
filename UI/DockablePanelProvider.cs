using Autodesk.Revit.UI;

namespace RevitQuickAccess.UI
{
    /// <summary>
    /// Supplies the WPF panel to Revit's dockable-pane framework. The panel is created lazily on
    /// first show, so a problem building the panel can never stop the ribbon/tab from loading.
    /// </summary>
    public class DockablePanelProvider : IDockablePaneProvider
    {
        private MainPanel _panel;

        public MainPanel Panel => _panel ??= new MainPanel();

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = Panel;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind = DockablePanes.BuiltInDockablePanes.ProjectBrowser
            };
        }
    }
}
