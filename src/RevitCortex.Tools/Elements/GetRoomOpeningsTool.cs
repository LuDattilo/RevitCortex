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
/// Retrieves doors/windows by room with dimensions and room association data.
/// Phase-aware lookup via FromRoom/ToRoom with type-level caching.
/// </summary>
[ToolSafety(true, false)]
public class GetRoomOpeningsTool : ICortexTool
{
    public string Name => "get_room_openings";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Retrieves doors/windows by room with dimensions and room association data. Phase-aware lookup via FromRoom/ToRoom with type-level caching.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var roomIds             = input["roomIds"]?.ToObject<List<long>>() ?? new List<long>();
        var roomNumbers         = input["roomNumbers"]?.ToObject<List<string>>() ?? new List<string>();
        var levelName           = input["levelName"]?.Value<string>() ?? "";
        var elementType         = input["elementType"]?.Value<string>() ?? "both";
        var includeRoomParams   = input["includeRoomParams"]?.Value<bool>() ?? false;
        var includeElementParams = input["includeElementParams"]?.Value<bool>() ?? false;
        var parameterNames      = input["parameterNames"]?.ToObject<List<string>>() ?? new List<string>();
        var maxElementsPerRoom  = input["maxElementsPerRoom"]?.Value<int>() ?? 100;

        try
        {
            Phase phase = doc.Phases.Cast<Phase>().Last();

            // Resolve target rooms
            List<Room> targetRooms;
            if (roomIds.Count > 0)
            {
                targetRooms = new List<Room>();
                foreach (var id in roomIds)
                {
#if REVIT2024_OR_GREATER
                    var elem = doc.GetElement(new ElementId(id)) as Room;
#else
                    var elem = doc.GetElement(new ElementId((int)id)) as Room;
#endif
                    if (elem != null && elem.Area > 0) targetRooms.Add(elem);
                }
            }
            else
            {
                IEnumerable<Room> allRooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0);

                if (roomNumbers.Count > 0)
                {
                    var numSet = new HashSet<string>(roomNumbers, StringComparer.OrdinalIgnoreCase);
                    allRooms = allRooms.Where(r =>
                        numSet.Contains(r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? ""));
                }

                if (!string.IsNullOrEmpty(levelName))
                {
                    allRooms = allRooms.Where(r =>
                        r.Level != null && r.Level.Name.IndexOf(levelName, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                targetRooms = allRooms.ToList();
            }

            if (targetRooms.Count == 0)
                return CortexResult<object>.Ok(new
                {
                    totalRooms = 0, totalDoors = 0, totalWindows = 0,
                    rooms = new List<object>()
                });

            var roomIdSet = new HashSet<long>(targetRooms.Select(r => GetElementIdValue(r.Id)));

            // Collect and map doors/windows
            bool includeDoors   = elementType == "doors" || elementType == "both";
            bool includeWindows = elementType == "windows" || elementType == "both";

            var roomDoors   = targetRooms.ToDictionary(r => GetElementIdValue(r.Id), _ => new List<FamilyInstance>());
            var roomWindows = targetRooms.ToDictionary(r => GetElementIdValue(r.Id), _ => new List<FamilyInstance>());

            if (includeDoors)
                MapOpeningsToRooms(doc, BuiltInCategory.OST_Doors, phase, roomIdSet, roomDoors);

            if (includeWindows)
                MapOpeningsToRooms(doc, BuiltInCategory.OST_Windows, phase, roomIdSet, roomWindows);

            // Build results
            var results = new List<object>();
            int totalDoors = 0, totalWindows = 0;
            var typeCache = new Dictionary<long, (string familyName, string typeName, string width, string height)>();

            foreach (var room in targetRooms)
            {
                var rid = GetElementIdValue(room.Id);
                var allDoors   = roomDoors[rid];
                var allWindows = roomWindows[rid];
                var doors   = allDoors.Take(maxElementsPerRoom).ToList();
                var windows = allWindows.Take(maxElementsPerRoom).ToList();

                totalDoors   += allDoors.Count;
                totalWindows += allWindows.Count;

                results.Add(new
                {
                    roomId     = rid,
                    roomName   = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "",
                    roomNumber = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "",
                    level      = room.Level?.Name ?? "",
                    area       = Math.Round(room.Area * 0.09290304, 2), // sq ft → sq m
                    roomParameters  = includeRoomParams ? ExtractParams(room, parameterNames) : null,
                    doorCount  = allDoors.Count,
                    doors      = includeDoors ? doors.Select(d => BuildOpeningInfo(d, phase, typeCache, includeElementParams, parameterNames)).ToList() : null,
                    windowCount = allWindows.Count,
                    windows    = includeWindows ? windows.Select(w => BuildOpeningInfo(w, phase, typeCache, includeElementParams, parameterNames)).ToList() : null
                });
            }

            return CortexResult<object>.Ok(new
            {
                totalRooms   = targetRooms.Count,
                totalDoors,
                totalWindows,
                rooms = results
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Get room openings failed: {ex.Message}");
        }
    }

    private static void MapOpeningsToRooms(Document doc, BuiltInCategory category, Phase phase,
        HashSet<long> roomIdSet, Dictionary<long, List<FamilyInstance>> roomMap)
    {
        foreach (FamilyInstance fi in new FilteredElementCollector(doc)
            .OfCategory(category)
            .WhereElementIsNotElementType())
        {
            long fromId = fi.get_FromRoom(phase) != null ? GetElementIdValue(fi.get_FromRoom(phase).Id) : -1;
            long toId   = fi.get_ToRoom(phase) != null ? GetElementIdValue(fi.get_ToRoom(phase).Id) : -1;

            if (fromId > 0 && roomIdSet.Contains(fromId) && roomMap.ContainsKey(fromId))
                roomMap[fromId].Add(fi);
            if (toId > 0 && toId != fromId && roomIdSet.Contains(toId) && roomMap.ContainsKey(toId))
                roomMap[toId].Add(fi);
        }
    }

    private static object BuildOpeningInfo(FamilyInstance fi, Phase phase,
        Dictionary<long, (string familyName, string typeName, string width, string height)> typeCache,
        bool includeElementParams, List<string> parameterNames)
    {
        var typeId = GetElementIdValue(fi.GetTypeId());
        if (!typeCache.ContainsKey(typeId))
        {
            var sym = fi.Symbol;
            var w = sym.get_Parameter(BuiltInParameter.DOOR_WIDTH)?.AsValueString()
                 ?? sym.get_Parameter(BuiltInParameter.WINDOW_WIDTH)?.AsValueString()
                 ?? sym.LookupParameter("Width")?.AsValueString()
                 ?? sym.LookupParameter("Rough Width")?.AsValueString() ?? "";
            var h = sym.get_Parameter(BuiltInParameter.DOOR_HEIGHT)?.AsValueString()
                 ?? sym.get_Parameter(BuiltInParameter.WINDOW_HEIGHT)?.AsValueString()
                 ?? sym.LookupParameter("Height")?.AsValueString()
                 ?? sym.LookupParameter("Rough Height")?.AsValueString() ?? "";
            typeCache[typeId] = (sym.FamilyName, sym.Name, w, h);
        }
        var cached = typeCache[typeId];

        var fromRoom = fi.get_FromRoom(phase);
        var toRoom   = fi.get_ToRoom(phase);

        return new
        {
            elementId      = GetElementIdValue(fi.Id),
            familyName     = cached.familyName,
            typeName       = cached.typeName,
            width          = cached.width,
            height         = cached.height,
            sillHeight     = fi.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM)?.AsValueString()
                          ?? fi.LookupParameter("Sill Height")?.AsValueString() ?? "",
            headHeight     = fi.LookupParameter("Head Height")?.AsValueString() ?? "",
            mark           = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsValueString() ?? "",
            level          = fi.LevelId != ElementId.InvalidElementId
                                 ? fi.Document.GetElement(fi.LevelId)?.Name ?? "" : "",
            fromRoomNumber = fromRoom?.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "",
            toRoomNumber   = toRoom?.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "",
            parameters     = includeElementParams ? ExtractParams(fi, parameterNames) : null
        };
    }

    private static object ExtractParams(Element elem, List<string> parameterNames)
    {
        var dict = new Dictionary<string, object>();
        if (parameterNames.Count > 0)
        {
            foreach (var name in parameterNames)
            {
                var param = elem.LookupParameter(name);
                if (param != null && param.HasValue)
                {
                    var val = GetParamValue(param);
                    if (val != null) dict[name] = val;
                }
            }
        }
        else
        {
            foreach (Parameter param in elem.Parameters)
            {
                if (!param.HasValue) continue;
                var val = GetParamValue(param);
                if (val != null && val.ToString() != "" && val.ToString() != "0")
                    dict[param.Definition.Name] = val;
            }
        }
        return dict;
    }

    private static object? GetParamValue(Parameter param)
    {
        return param.StorageType switch
        {
            StorageType.String    => param.AsString() ?? "",
            StorageType.Integer   => param.AsValueString() ?? param.AsInteger().ToString(),
            StorageType.Double    => param.AsValueString() ?? param.AsDouble().ToString("F4"),
            StorageType.ElementId => param.AsValueString() ?? GetElementIdValue(param.AsElementId()).ToString(),
            _                     => null
        };
    }

    private static long GetElementIdValue(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return (long)id.IntegerValue;
#endif
    }
}
