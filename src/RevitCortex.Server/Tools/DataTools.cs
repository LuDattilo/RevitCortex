using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class DataTools
{
    [McpServerTool(Name = "store_project_data"), Description("Store project data in local RevitCortex database.")]
    public static async Task<string> StoreProjectData(
        RevitConnectionManager revit,
        [Description("JSON parameters (e.g. {\"projectName\":\"MyProject\", \"metadata\":{...}})")] string data,
        CancellationToken ct = default)
    {
        var p = JObject.Parse(data);
        var result = await revit.ExecuteAsync("store_project_data", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "store_room_data"), Description("Store room data for a project in local database.")]
    public static async Task<string> StoreRoomData(
        RevitConnectionManager revit,
        [Description("JSON parameters (e.g. {\"projectId\":\"abc\", \"rooms\":[...]})")] string data,
        CancellationToken ct = default)
    {
        var p = JObject.Parse(data);
        var result = await revit.ExecuteAsync("store_room_data", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "query_stored_data"), Description("Query projects and rooms from local database.")]
    public static async Task<string> QueryStoredData(
        RevitConnectionManager revit,
        [Description("JSON parameters (e.g. {\"query\":\"rooms\", \"projectId\":\"abc\", \"filters\":{...}})")] string data,
        CancellationToken ct = default)
    {
        var p = JObject.Parse(data);
        var result = await revit.ExecuteAsync("query_stored_data", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "import_table"), Description("Import CSV/TSV file as formatted table in a view.")]
    public static async Task<string> ImportTable(
        RevitConnectionManager revit,
        [Description("JSON parameters (e.g. {\"filePath\":\"C:\\\\data.csv\", \"viewId\":123, \"delimiter\":\"comma\"})")] string data,
        CancellationToken ct = default)
    {
        var p = JObject.Parse(data);
        var result = await revit.ExecuteAsync("import_table", p, ct);
        return result.ToString();
    }
}
