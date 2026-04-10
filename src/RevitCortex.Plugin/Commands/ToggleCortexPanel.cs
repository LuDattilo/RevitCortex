using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCortex.Plugin.UI;

namespace RevitCortex.Plugin.Commands;

[Transaction(TransactionMode.Manual)]
public class ToggleCortexPanel : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var pane = commandData.Application.GetDockablePane(CortexDockablePaneProvider.PaneId);
            if (pane != null)
            {
                if (pane.IsShown())
                    pane.Hide();
                else
                    pane.Show();
            }
        }
        catch
        {
            TaskDialog.Show("RevitCortex", "Chat panel is not available. Please restart Revit.");
        }

        return Result.Succeeded;
    }
}
