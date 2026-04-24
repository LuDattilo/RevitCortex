using Newtonsoft.Json.Linq;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.Server;

public class ToolResponseShaperTests
{
    [Fact]
    public void ShapeAvailableFamilyTypesCompact_RemovesUniqueIds()
    {
        var payload = JArray.Parse("""
        [
          { "familyTypeId": 1, "uniqueId": "abc", "familyName": "Door", "typeName": "900x2100", "category": "Doors" }
        ]
        """);

        var shaped = ToolResponseShaper.Shape("get_available_family_types", payload, compact: true, summaryOnly: false);

        Assert.Equal(1, shaped["count"]!.Value<int>());
        Assert.Null(shaped["items"]![0]!["uniqueId"]);
    }

    [Fact]
    public void ShapeSchedulableFieldsSummaryOnly_ReturnsNamesOnly()
    {
        var payload = JObject.Parse("""
        {
          "category": "OST_Rooms",
          "scheduleType": "regular",
          "fieldCount": 2,
          "fields": [
            { "name": "Name", "fieldType": "Instance", "parameterId": 1 },
            { "name": "Number", "fieldType": "Instance", "parameterId": 2 }
          ]
        }
        """);

        var shaped = ToolResponseShaper.Shape("list_schedulable_fields", payload, compact: true, summaryOnly: true);

        Assert.Equal(new[] { "Name", "Number" }, shaped["fieldNames"]!.ToObject<string[]>());
        Assert.Null(shaped["fields"]);
    }

    [Fact]
    public void ShapeRoomOpeningsSummaryOnly_KeepsCountsAndDropsNestedArrays()
    {
        var payload = JObject.Parse("""
        {
          "totalRooms": 1,
          "totalDoors": 4,
          "totalWindows": 2,
          "rooms": [
            {
              "roomId": 100,
              "roomName": "Office",
              "roomNumber": "A-101",
              "doorCount": 4,
              "doors": [{ "elementId": 1 }],
              "windowCount": 2,
              "windows": [{ "elementId": 2 }]
            }
          ]
        }
        """);

        var shaped = ToolResponseShaper.Shape("get_room_openings", payload, compact: true, summaryOnly: true);

        Assert.Equal(4, shaped["rooms"]![0]!["doorCount"]!.Value<int>());
        Assert.Null(shaped["rooms"]![0]!["doors"]);
        Assert.Null(shaped["rooms"]![0]!["windows"]);
    }
}
