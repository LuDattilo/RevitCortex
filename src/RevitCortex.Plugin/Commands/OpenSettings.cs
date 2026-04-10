using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCortex.Plugin.UI;

namespace RevitCortex.Plugin.Commands;

[Transaction(TransactionMode.Manual)]
public class OpenSettings : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var window = new SettingsWindow();
        _ = new System.Windows.Interop.WindowInteropHelper(window)
        {
            Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle
        };
        window.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
        window.Show();

        return Result.Succeeded;
    }
}
