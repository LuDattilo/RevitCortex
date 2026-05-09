namespace RevitCortex.Plugin.PowerBi;

/// <summary>
/// Lightweight projection of a Revit schedule (ViewSchedule) for the export
/// wizard UI. The schedule's column layout and row count are read once when
/// the user opens the wizard so we can show meaningful information without
/// re-walking the model every keystroke.
/// </summary>
public class ScheduleInfo
{
    public long ScheduleId { get; set; }
    public string Name { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public int ColumnCount { get; set; }
    public int RowCount { get; set; }
}
