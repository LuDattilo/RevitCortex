using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Exports room data from the current project (name, number, level, area, volume, etc.).
/// </summary>
[ToolSafety(true, false)]
public class ExportRoomDataTool : ICortexTool
{
    public string Name => "export_room_data";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Exports room data from the current project (name, number, level, area, volume, etc.).";
    private const double SqFtToSqM = 0.092903;
    private const double CuFtToCuM = 0.0283168;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var includeUnplaced = input["includeUnplacedRooms"]?.Value<bool>() ?? false;
        var includeNotEnclosed = input["includeNotEnclosedRooms"]?.Value<bool>() ?? false;
        var maxResults = input["maxResults"]?.Value<int>() ?? 100;
        // Filtering rooms by level had to be done client-side on the full list:
        // 138 rooms came back to keep the 22 of one storey.
        var levelName = input["levelName"]?.Value<string>();
        var levelId = input["levelId"]?.Value<long>() ?? 0;
        var nameFilter = input["nameFilter"]?.Value<string>();

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

            if (levelId > 0)
            {
                var wantedLevel = ToolHelpers.ToElementId(levelId);
                rooms = rooms.Where(r => r.LevelId == wantedLevel).ToList();
            }

            if (!string.IsNullOrWhiteSpace(levelName))
            {
                var wanted = NameMatching.Normalize(levelName!);
                rooms = rooms
                    .Where(r => NameMatching.Normalize(r.Level?.Name ?? "") == wanted)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(nameFilter))
            {
                var needle = NameMatching.Normalize(nameFilter!);
                rooms = rooms.Where(r =>
                {
                    var name = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                    var number = r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                    return NameMatching.Normalize(name).Contains(needle, StringComparison.Ordinal) ||
                           NameMatching.Normalize(number).Contains(needle, StringComparison.Ordinal);
                }).ToList();
            }

            var matchedCount = rooms.Count;
            var result = rooms.Take(maxResults).Select(r =>
            {
                var area = r.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0;
                var volume = r.get_Parameter(BuiltInParameter.ROOM_VOLUME)?.AsDouble() ?? 0;
                var perimeter = r.get_Parameter(BuiltInParameter.ROOM_PERIMETER)?.AsDouble() ?? 0;

                return new
                {
                    id = ToolHelpers.GetElementIdValue(r.Id),
                    name = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "",
                    number = r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "",
                    level = r.Level?.Name ?? "",
                    department = r.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "",
                    areaSqM = Math.Round(area * SqFtToSqM, 2),
                    volumeCuM = Math.Round(volume * CuFtToCuM, 2),
                    perimeterMm = Math.Round(perimeter * MmPerFoot, 0)
                };
            }).ToList();

            return CortexResult<object>.Ok(new
            {
                roomCount = result.Count,
                matchedCount,
                truncated = matchedCount > result.Count,
                levelName,
                levelId = levelId > 0 ? (long?)levelId : null,
                rooms = result
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
