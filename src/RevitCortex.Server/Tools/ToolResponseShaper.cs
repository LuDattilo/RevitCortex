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
            "get_element_parameters" => ShapeGetElementParameters(payload),
            "audit_families" => ShapeAuditFamilies(payload),
            _ => payload
        };
    }

    private static JToken ShapeGetElementParameters(JToken payload)
    {
        var elements = payload["elements"]?.Children<JObject>().Select(el =>
        {
            var parameters = el["parameters"]?.Children<JObject>()
                .Where(p => p["hasValue"]?.Value<bool>() == true)
                .Select(p => new JObject
                {
                    ["name"] = p["name"],
                    ["value"] = p["value"]
                })
                .ToArray() ?? System.Array.Empty<JObject>();

            return new JObject
            {
                ["elementId"] = el["elementId"],
                ["elementName"] = el["elementName"],
                ["category"] = el["category"],
                ["parameters"] = new JArray(parameters)
            };
        }).ToArray() ?? System.Array.Empty<JObject>();

        return new JObject
        {
            ["message"] = payload["message"],
            ["elements"] = new JArray(elements)
        };
    }

    private static JToken ShapeAuditFamilies(JToken payload)
    {
        var families = payload["families"]?.Children<JObject>().Select(f => new JObject
        {
            ["id"] = f["id"],
            ["name"] = f["name"],
            ["category"] = f["category"],
            ["instanceCount"] = f["instanceCount"],
            ["typeCount"] = f["typeCount"]
        }).ToArray() ?? System.Array.Empty<JObject>();

        var result = (JObject)payload.DeepClone();
        result["families"] = new JArray(families);
        return result;
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
