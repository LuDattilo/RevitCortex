using System;
using Autodesk.Revit.UI;

namespace RevitCortex.Plugin.UI;

public class CortexDockablePaneProvider : IDockablePaneProvider
{
    private CortexPanel? _panel;

    public static readonly DockablePaneId PaneId =
        new(new Guid("7A3C8D12-E4F5-4B67-9A01-BCDE23456789"));

    public CortexPanel? Panel => _panel;

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        _panel = new CortexPanel();
        data.FrameworkElement = _panel;
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Right
        };
        data.VisibleByDefault = false;
    }
}
