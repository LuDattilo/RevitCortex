using System.Linq;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Server.Tools;

public static class ToolResponseShaper
{
    public static JToken Shape(string toolName, JToken payload, bool compact, bool summaryOnly)
    {
        if (!compact && !summaryOnly)
        {
            return payload;
        }

        return toolName switch
        {
            "get_available_family_types" => ShapeAvailableFamilyTypes(payload),
            "list_schedulable_fields" => ShapeSchedulableFields(payload, summaryOnly),
            "get_room_openings" => ShapeRoomOpenings(payload, summaryOnly),
            _ => payload
        };
    }

    private static JToken ShapeAvailableFamilyTypes(JToken payload)
    {
        var items = payload.Children<JObject>()
            .Select(item => new JObject
            {
                ["familyTypeId"] = item["familyTypeId"],
                ["familyName"] = item["familyName"],
                ["typeName"] = item["typeName"],
                ["category"] = item["category"]
            })
            .ToArray();

        return new JObject
        {
            ["count"] = items.Length,
            ["items"] = new JArray(items)
        };
    }

    private static JToken ShapeSchedulableFields(JToken payload, bool summaryOnly)
    {
        if (!summaryOnly)
        {
            return payload;
        }

        var fieldNames = payload["fields"]!
            .Children<JObject>()
            .Select(field => field["name"])
            .Where(name => name is not null)
            .ToArray();

        var fieldNameItems = fieldNames
            .Select(name => (object)name!)
            .ToArray();

        return new JObject
        {
            ["category"] = payload["category"],
            ["scheduleType"] = payload["scheduleType"],
            ["fieldCount"] = payload["fieldCount"],
            ["fieldNames"] = new JArray(fieldNameItems)
        };
    }

    private static JToken ShapeRoomOpenings(JToken payload, bool summaryOnly)
    {
        if (!summaryOnly)
        {
            return payload;
        }

        var rooms = payload["rooms"]!
            .Children<JObject>()
            .Select(room => new JObject
            {
                ["roomId"] = room["roomId"],
                ["roomName"] = room["roomName"],
                ["roomNumber"] = room["roomNumber"],
                ["doorCount"] = room["doorCount"],
                ["windowCount"] = room["windowCount"]
            })
            .ToArray();

        return new JObject
        {
            ["totalRooms"] = payload["totalRooms"],
            ["totalDoors"] = payload["totalDoors"],
            ["totalWindows"] = payload["totalWindows"],
            ["rooms"] = new JArray(rooms)
        };
    }
}
