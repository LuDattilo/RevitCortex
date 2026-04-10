using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Exports room data from the current project (name, number, level, area, volume, etc.).
/// </summary>
public class ExportRoomDataTool : ICortexTool
{
    public string Name => "export_room_data";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Exports room data from the current project (name, number, level, area, volume, etc.).";
    private const double SqFtToSqM = 0.092903;
    private const double CuFtToCuM = 0.0283168;
    private const double FtToMm = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var includeUnplaced = input["includeUnplacedRooms"]?.Value<bool>() ?? false;
        var includeNotEnclosed = input["includeNotEnclosedRooms"]?.Value<bool>() ?? false;
        var maxResults = input["maxResults"]?.Value<int>() ?? 100;

        try
        {
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .ToList();

            if (!includeUnplaced)
                rooms = rooms.Where(r => r.Area > 0).ToList();

            if (!includeNotEnclosed)
                rooms = rooms.Where(r =>
                {
                    try { return r.get_BoundingBox(null) != null; }
                    catch { return false; }
                }).ToList();

            var result = rooms.Take(maxResults).Select(r =>
            {
                var area = r.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0;
                var volume = r.get_Parameter(BuiltInParameter.ROOM_VOLUME)?.AsDouble() ?? 0;
                var perimeter = r.get_Parameter(BuiltInParameter.ROOM_PERIMETER)?.AsDouble() ?? 0;

                return new
                {
                    id = GetIdLong(r.Id),
                    name = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "",
                    number = r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "",
                    level = r.Level?.Name ?? "",
                    department = r.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "",
                    areaSqM = Math.Round(area * SqFtToSqM, 2),
                    volumeCuM = Math.Round(volume * CuFtToCuM, 2),
                    perimeterMm = Math.Round(perimeter * FtToMm, 0)
                };
            }).ToList();

            return CortexResult<object>.Ok(new { roomCount = result.Count, rooms = result });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static long GetIdLong(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}
