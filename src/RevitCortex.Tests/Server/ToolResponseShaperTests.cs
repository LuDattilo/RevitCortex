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
    public void ShapeSchedulableFieldsCompact_StripsParameterIds()
    {
        var payload = JObject.Parse("""
        {
          "category": "OST_Rooms",
          "scheduleType": "regular",
          "fieldCount": 1,
          "fields": [
            { "name": "Name", "fieldType": "Instance", "parameterId": 1 }
          ]
        }
        """);

        var shaped = ToolResponseShaper.Shape("list_schedulable_fields", payload, compact: true, summaryOnly: false);

        Assert.Equal("Name", shaped["fields"]![0]!["name"]!.Value<string>());
        Assert.Equal("Instance", shaped["fields"]![0]!["fieldType"]!.Value<string>());
        Assert.Null(shaped["fields"]![0]!["parameterId"]);
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

    [Fact]
    public void ShapeGetElementParametersCompact_KeepsNameValueAndDropsEmptyParams()
    {
        var payload = JObject.Parse("""
        {
          "message": "Retrieved parameters for 1 elements",
          "elements": [
            {
              "elementId": 12345,
              "elementName": "Door 900",
              "category": "Doors",
              "parameters": [
                { "name": "Comments", "value": "entry", "hasValue": true, "isReadOnly": false, "isShared": false, "storageType": "String", "groupName": "autodesk.revit.group.text" },
                { "name": "Mark", "value": null, "hasValue": false, "isReadOnly": false, "isShared": false, "storageType": "String", "groupName": "autodesk.revit.group.identityData" },
                { "name": "[Type] Width", "value": 900.0, "hasValue": true, "isReadOnly": true, "isShared": false, "storageType": "Double", "groupName": "autodesk.revit.group.dimensions" }
              ]
            }
          ]
        }
        """);

        var shaped = ToolResponseShaper.Shape("get_element_parameters", payload, compact: true, summaryOnly: false);

        var element = shaped["elements"]![0]!;
        Assert.Equal(12345, element["elementId"]!.Value<long>());
        Assert.Equal("Doors", element["category"]!.Value<string>());

        var parameters = (JArray)element["parameters"]!;
        Assert.Equal(2, parameters.Count);

        Assert.Equal("Comments", parameters[0]!["name"]!.Value<string>());
        Assert.Equal("entry", parameters[0]!["value"]!.Value<string>());
        Assert.Null(parameters[0]!["hasValue"]);
        Assert.Null(parameters[0]!["isReadOnly"]);
        Assert.Null(parameters[0]!["isShared"]);
        Assert.Null(parameters[0]!["storageType"]);
        Assert.Null(parameters[0]!["groupName"]);

        Assert.Equal("[Type] Width", parameters[1]!["name"]!.Value<string>());
    }

    [Fact]
    public void ShapeAuditFamiliesCompact_KeepsCoreFieldsAndDropsAuditBooleans()
    {
        var payload = JObject.Parse("""
        {
          "totalFamilies": 2,
          "loadableCount": 2,
          "systemCount": 0,
          "families": [
            { "id": 1, "name": "Door1", "category": "Doors", "kind": "loadable", "isInPlace": false, "isEditable": true, "instanceCount": 5, "typeCount": 3, "isUnused": false },
            { "id": 2, "name": "Door2", "category": "Doors", "kind": "loadable", "isInPlace": true, "isEditable": true, "instanceCount": 0, "typeCount": 1, "isUnused": true }
          ]
        }
        """);

        var shaped = ToolResponseShaper.Shape("audit_families", payload, compact: true, summaryOnly: false);

        Assert.Equal(2, shaped["totalFamilies"]!.Value<int>());

        var families = (JArray)shaped["families"]!;
        Assert.Equal(2, families.Count);

        Assert.Equal("Door1", families[0]!["name"]!.Value<string>());
        Assert.Equal("Doors", families[0]!["category"]!.Value<string>());
        Assert.Equal(5, families[0]!["instanceCount"]!.Value<int>());
        Assert.Equal(3, families[0]!["typeCount"]!.Value<int>());

        Assert.Null(families[0]!["isInPlace"]);
        Assert.Null(families[0]!["isEditable"]);
        Assert.Null(families[0]!["isUnused"]);
        Assert.Null(families[0]!["kind"]);
    }

    [Fact]
    public void ShapeRoomOpeningsCompact_StripsHeavyOpeningDetails()
    {
        var payload = JObject.Parse("""
        {
          "totalRooms": 1,
          "totalDoors": 1,
          "totalWindows": 0,
          "rooms": [
            {
              "roomId": 100,
              "roomName": "Office",
              "roomNumber": "A-101",
              "level": "L1",
              "area": 12.5,
              "roomParameters": { "Department": "Ops" },
              "doorCount": 1,
              "doors": [
                {
                  "elementId": 1,
                  "familyName": "Single Door",
                  "typeName": "900x2100",
                  "width": "900",
                  "height": "2100",
                  "mark": "D1",
                  "parameters": { "Fire Rating": "60" },
                  "headHeight": "2400"
                }
              ],
              "windowCount": 0,
              "windows": []
            }
          ]
        }
        """);

        var shaped = ToolResponseShaper.Shape("get_room_openings", payload, compact: true, summaryOnly: false);

        Assert.Equal("Office", shaped["rooms"]![0]!["roomName"]!.Value<string>());
        Assert.Null(shaped["rooms"]![0]!["roomParameters"]);
        Assert.Equal("Single Door", shaped["rooms"]![0]!["doors"]![0]!["familyName"]!.Value<string>());
        Assert.Null(shaped["rooms"]![0]!["doors"]![0]!["parameters"]);
        Assert.Null(shaped["rooms"]![0]!["doors"]![0]!["headHeight"]);
    }
}
