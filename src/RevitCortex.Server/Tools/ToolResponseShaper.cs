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
            if (!payload.HasValues)
            {
                return payload;
            }

            var compactFields = payload["fields"]?
                .Children<JObject>()
                .Select(field => new JObject
                {
                    ["name"] = field["name"],
                    ["fieldType"] = field["fieldType"]
                })
                .ToArray();

            return new JObject
            {
                ["category"] = payload["category"],
                ["scheduleType"] = payload["scheduleType"],
                ["fieldCount"] = payload["fieldCount"],
                ["fields"] = compactFields is null ? null : new JArray(compactFields)
            };
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
            var compactRooms = payload["rooms"]?
                .Children<JObject>()
                .Select(room => new JObject
                {
                    ["roomId"] = room["roomId"],
                    ["roomName"] = room["roomName"],
                    ["roomNumber"] = room["roomNumber"],
                    ["level"] = room["level"],
                    ["area"] = room["area"],
                    ["doorCount"] = room["doorCount"],
                    ["doors"] = ShapeCompactOpenings(room["doors"]),
                    ["windowCount"] = room["windowCount"],
                    ["windows"] = ShapeCompactOpenings(room["windows"])
                })
                .ToArray();

            return new JObject
            {
                ["totalRooms"] = payload["totalRooms"],
                ["totalDoors"] = payload["totalDoors"],
                ["totalWindows"] = payload["totalWindows"],
                ["rooms"] = compactRooms is null ? null : new JArray(compactRooms)
            };
        }

        var summaryRooms = payload["rooms"]!
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
            ["rooms"] = new JArray(summaryRooms)
        };
    }

    private static JToken? ShapeCompactOpenings(JToken? openings)
    {
        if (openings == null || openings.Type == JTokenType.Null)
        {
            return openings;
        }

        var items = openings
            .Children<JObject>()
            .Select(opening => new JObject
            {
                ["elementId"] = opening["elementId"],
                ["familyName"] = opening["familyName"],
                ["typeName"] = opening["typeName"],
                ["width"] = opening["width"],
                ["height"] = opening["height"],
                ["mark"] = opening["mark"]
            })
            .ToArray();

        return new JArray(items);
    }
}
