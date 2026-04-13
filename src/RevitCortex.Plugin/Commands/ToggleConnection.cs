using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace RevitCortex.Plugin.Commands;

[Transaction(TransactionMode.Manual)]
public class ToggleConnection : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var app = RevitCortexApp.Instance;
            if (app == null)
            {
                TaskDialog.Show("RevitCortex", "Plugin not initialized.");
                return Result.Failed;
            }

            if (app.IsServiceRunning)
            {
                app.StopService();
                TaskDialog.Show("RevitCortex", "Server stopped.");
            }
            else
            {
                // Pass active document so the session is initialized immediately
                var doc = commandData.Application.ActiveUIDocument?.Document;
                app.StartService(doc);
                TaskDialog.Show("RevitCortex", $"Server started on port {app.Port}.");
            }

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}
