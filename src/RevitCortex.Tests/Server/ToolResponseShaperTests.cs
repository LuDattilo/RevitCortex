using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;
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

    // ---------------------------------------------------------------------
    // New shaper tests (one per tool added in this iteration)
    // ---------------------------------------------------------------------

    [Fact]
    public void ShapeSharedParametersCompact_KeepsCoreFieldsAndDropsExtras()
    {
        var payload = JObject.Parse("""
        {
          "parameterCount": 2,
          "parameters": [
            { "name": "WBS_Code", "guid": "1111", "parameterType": "Text", "parameterGroup": "Identity Data", "isShared": true, "isInstance": true, "categories": ["Doors"], "extraNoise": "drop me" },
            { "name": "FireRating", "guid": "2222", "parameterType": "Text", "parameterGroup": "Construction", "isShared": true, "isInstance": false, "categories": ["Walls"], "ownerSchema": "GUID-XYZ" }
          ]
        }
        """);

        var shaped = ToolResponseShaper.Shape("get_shared_parameters", payload, compact: true, summaryOnly: false);

        Assert.Equal(2, shaped["parameterCount"]!.Value<int>());
        var parameters = (JArray)shaped["parameters"]!;
        Assert.Equal(2, parameters.Count);

        Assert.Equal("WBS_Code", parameters[0]!["name"]!.Value<string>());
        Assert.Equal("1111", parameters[0]!["guid"]!.Value<string>());
        Assert.Equal("Text", parameters[0]!["parameterType"]!.Value<string>());
        Assert.Equal("Identity Data", parameters[0]!["parameterGroup"]!.Value<string>());
        Assert.True(parameters[0]!["isShared"]!.Value<bool>());
        Assert.True(parameters[0]!["isInstance"]!.Value<bool>());
        Assert.NotNull(parameters[0]!["categories"]);

        Assert.Null(parameters[0]!["extraNoise"]);
        Assert.Null(parameters[1]!["ownerSchema"]);
    }

    [Fact]
    public void ShapeLinkedFileInstancesCompact_DropsTransformsKeepsTopLevel()
    {
        var payload = JObject.Parse("""
        {
          "typeCount": 1,
          "totalInstances": 2,
          "linkedFiles": [
            {
              "typeId": 100,
              "typeName": "ARCH.rvt",
              "isLoaded": true,
              "path": "C:/links/ARCH.rvt",
              "instanceCount": 2,
              "instances": [
                { "instanceId": 11, "isPinned": true, "origin": [0,0,0], "basisX": [1,0,0], "basisY": [0,1,0] },
                { "instanceId": 12, "isPinned": false, "origin": [10,0,0], "basisX": [1,0,0], "basisY": [0,1,0] }
              ]
            }
          ]
        }
        """);

        var shaped = ToolResponseShaper.Shape("get_linked_file_instances", payload, compact: true, summaryOnly: false);

        Assert.Equal(1, shaped["typeCount"]!.Value<int>());
        Assert.Equal(2, shaped["totalInstances"]!.Value<int>());

        var files = (JArray)shaped["linkedFiles"]!;
        Assert.Single(files);
        var instances = (JArray)files[0]!["instances"]!;
        Assert.Equal(2, instances.Count);

        Assert.Equal(11, instances[0]!["instanceId"]!.Value<int>());
        Assert.True(instances[0]!["isPinned"]!.Value<bool>());
        Assert.Null(instances[0]!["origin"]);
        Assert.Null(instances[0]!["basisX"]);
        Assert.Null(instances[0]!["basisY"]);
    }

    [Fact]
    public void ShapeLinkedElements_PreservesUserRequestedParameters()
    {
        // get_linked_elements supports parameterNames=[...] which materializes as
        // dynamic top-level fields per element. The shaper MUST NOT strip these,
        // otherwise it silently destroys user-requested data.
        var payload = JObject.Parse("""
        {
          "linkCount": 1,
          "links": [
            {
              "linkName": "ARCH",
              "linkId": 999,
              "documentTitle": "ARCH.rvt",
              "elementCount": 2,
              "elements": [
                { "elementId": 1, "category": "Walls", "name": "Wall A", "Mark": "W1", "IfcType": "IfcWall" },
                { "elementId": 2, "category": "Walls", "name": "Wall B", "Mark": "W2", "IfcType": "IfcWall" }
              ]
            }
          ]
        }
        """);

        var shaped = ToolResponseShaper.Shape("get_linked_elements", payload, compact: true, summaryOnly: false);

        Assert.Equal(1, shaped["linkCount"]!.Value<int>());
        var elements = (JArray)shaped["links"]![0]!["elements"]!;
        Assert.Equal(2, elements.Count);
        Assert.Equal("W1", elements[0]!["Mark"]!.Value<string>());
        Assert.Equal("IfcWall", elements[0]!["IfcType"]!.Value<string>());
    }

    [Fact]
    public void ShapeElementsInSpatialVolumeCompact_KeepsCountersAndTrimsElements()
    {
        var payload = JObject.Parse("""
        {
          "totalElements": 2,
          "volumeCount": 1,
          "volumes": [
            {
              "volumeType": "Room",
              "volumeId": 500,
              "volumeName": "Office",
              "elementCount": 2,
              "totalElementCount": 2,
              "truncated": false,
              "elements": [
                { "elementId": 1, "name": "Chair", "category": "Furniture", "familyName": "Chair-Family", "typeName": "Chair-Std", "boundingBox": { "min": [0,0,0], "max": [1,1,1] } },
                { "elementId": 2, "name": "Desk",  "category": "Furniture", "familyName": "Desk-Family",  "typeName": "Desk-Std",  "boundingBox": { "min": [0,0,0], "max": [2,1,1] } }
              ]
            }
          ]
        }
        """);

        var shaped = ToolResponseShaper.Shape("get_elements_in_spatial_volume", payload, compact: true, summaryOnly: false);

        Assert.Equal(2, shaped["totalElements"]!.Value<int>());
        Assert.Equal(1, shaped["volumeCount"]!.Value<int>());
        var volumes = (JArray)shaped["volumes"]!;
        Assert.Single(volumes);
        var elements = (JArray)volumes[0]!["elements"]!;
        Assert.Equal(2, elements.Count);

        Assert.Equal(1, elements[0]!["elementId"]!.Value<int>());
        Assert.Equal("Chair", elements[0]!["name"]!.Value<string>());
        Assert.Equal("Furniture", elements[0]!["category"]!.Value<string>());
        Assert.Equal("Chair-Family", elements[0]!["familyName"]!.Value<string>());
        Assert.Equal("Chair-Std", elements[0]!["typeName"]!.Value<string>());
        Assert.Null(elements[0]!["boundingBox"]);
    }

    [Fact]
    public void ShapeMaterialsCompact_DropsNumericPropsButKeepsAssetFlags()
    {
        var payload = JObject.Parse("""
        {
          "materialCount": 2,
          "materials": [
            { "id": 1, "name": "Concrete", "materialClass": "Concrete", "materialCategory": "Structural", "color": "200,200,200", "transparency": 0, "shininess": 30, "smoothness": 50, "hasAppearanceAsset": true, "hasStructuralAsset": true, "hasThermalAsset": false },
            { "id": 2, "name": "Steel", "materialClass": "Metal", "materialCategory": "Structural", "color": "100,100,100", "transparency": 0, "shininess": 80, "smoothness": 90, "hasAppearanceAsset": true, "hasStructuralAsset": true, "hasThermalAsset": true }
          ]
        }
        """);

        var shaped = ToolResponseShaper.Shape("get_materials", payload, compact: true, summaryOnly: false);

        Assert.Equal(2, shaped["materialCount"]!.Value<int>());
        var materials = (JArray)shaped["materials"]!;
        Assert.Equal(2, materials.Count);

        Assert.Equal(1, materials[0]!["id"]!.Value<int>());
        Assert.Equal("Concrete", materials[0]!["name"]!.Value<string>());
        Assert.Equal("Concrete", materials[0]!["materialClass"]!.Value<string>());
        Assert.Equal("Structural", materials[0]!["materialCategory"]!.Value<string>());
        Assert.Equal("200,200,200", materials[0]!["color"]!.Value<string>());

        // Numeric props dropped — they are large noise.
        Assert.Null(materials[0]!["transparency"]);
        Assert.Null(materials[0]!["shininess"]);
        Assert.Null(materials[0]!["smoothness"]);

        // Asset flags PRESERVED — caller uses them to decide whether to call get_material_properties.
        Assert.True(materials[0]!["hasAppearanceAsset"]!.Value<bool>());
        Assert.True(materials[0]!["hasStructuralAsset"]!.Value<bool>());
        Assert.False(materials[0]!["hasThermalAsset"]!.Value<bool>());
    }

    [Fact]
    public void ShapeExportRoomDataCompact_KeepsEssentialsAndDropsDepartmentPerimeter()
    {
        var payload = JObject.Parse("""
        {
          "roomCount": 2,
          "rooms": [
            { "id": 100, "name": "Office", "number": "A-101", "level": "L1", "areaSqM": 12.5, "volumeCuM": 33.0, "department": "Ops", "perimeter": 14.0, "occupancy": "OPEN" },
            { "id": 101, "name": "Meeting", "number": "A-102", "level": "L1", "areaSqM": 18.0, "volumeCuM": 50.4, "department": "HR", "perimeter": 17.5 }
          ]
        }
        """);

        var shaped = ToolResponseShaper.Shape("export_room_data", payload, compact: true, summaryOnly: false);

        Assert.Equal(2, shaped["roomCount"]!.Value<int>());
        var rooms = (JArray)shaped["rooms"]!;
        Assert.Equal(2, rooms.Count);

        Assert.Equal(100, rooms[0]!["id"]!.Value<int>());
        Assert.Equal("Office", rooms[0]!["name"]!.Value<string>());
        Assert.Equal("A-101", rooms[0]!["number"]!.Value<string>());
        Assert.Equal("L1", rooms[0]!["level"]!.Value<string>());
        Assert.Equal(12.5, rooms[0]!["areaSqM"]!.Value<double>());
        Assert.Equal(33.0, rooms[0]!["volumeCuM"]!.Value<double>());

        Assert.Null(rooms[0]!["department"]);
        Assert.Null(rooms[0]!["perimeter"]);
        Assert.Null(rooms[0]!["occupancy"]);
    }

    [Fact]
    public void ShapeIfcExportConfigurationsCompact_KeepsNameAndIfcVersion()
    {
        var payload = JObject.Parse("""
        {
          "count": 2,
          "configurations": [
            { "name": "IFC2x3 Coordination", "ifcVersion": "IFC2x3", "description": "long blurb here", "fileType": "IFC", "spaceBoundaries": 1 },
            { "name": "IFC4 Reference View",   "ifcVersion": "IFC4",   "description": "another blurb",    "fileType": "IFC", "spaceBoundaries": 0 }
          ]
        }
        """);

        var shaped = ToolResponseShaper.Shape("ifc_list_export_configurations", payload, compact: true, summaryOnly: false);

        Assert.Equal(2, shaped["count"]!.Value<int>());
        var configs = (JArray)shaped["configurations"]!;
        Assert.Equal(2, configs.Count);

        Assert.Equal("IFC2x3 Coordination", configs[0]!["name"]!.Value<string>());
        Assert.Equal("IFC2x3", configs[0]!["ifcVersion"]!.Value<string>());
        Assert.Null(configs[0]!["description"]);
        Assert.Null(configs[0]!["fileType"]);
        Assert.Null(configs[0]!["spaceBoundaries"]);
    }

    [Fact]
    public void ShapeIfcAnalyzeRebuildabilityCompact_KeepsKeyFieldsAndDropsHeavyDetails()
    {
        var payload = JObject.Parse("""
        {
          "totalAnalyzed": 2,
          "totalInDocument": 50,
          "rebuildableCount": 1,
          "results": [
            { "elementId": 10, "name": "Wall-IFC-1", "category": "Walls", "ifcEntity": "IfcWallStandardCase", "geometryType": "Extrusion", "rebuildStrategy": "ifc_rebuild_walls", "rebuildConfidence": 0.92, "boundingBox": {"min":[0,0,0]}, "ifcGuid": "abc-def" },
            { "elementId": 11, "name": "Mass-1",    "category": "Mass",  "ifcEntity": "IfcBuildingElementProxy", "geometryType": "Brep", "rebuildStrategy": null, "rebuildConfidence": 0.10, "rawProperties": {} }
          ]
        }
        """);

        var shaped = ToolResponseShaper.Shape("ifc_analyze_rebuildability", payload, compact: true, summaryOnly: false);

        Assert.Equal(2, shaped["totalAnalyzed"]!.Value<int>());
        Assert.Equal(50, shaped["totalInDocument"]!.Value<int>());
        Assert.Equal(1, shaped["rebuildableCount"]!.Value<int>());

        var results = (JArray)shaped["results"]!;
        Assert.Equal(2, results.Count);

        Assert.Equal(10, results[0]!["elementId"]!.Value<int>());
        Assert.Equal("Wall-IFC-1", results[0]!["name"]!.Value<string>());
        Assert.Equal("Walls", results[0]!["category"]!.Value<string>());
        Assert.Equal("IfcWallStandardCase", results[0]!["ifcEntity"]!.Value<string>());
        Assert.Equal("Extrusion", results[0]!["geometryType"]!.Value<string>());
        Assert.Equal("ifc_rebuild_walls", results[0]!["rebuildStrategy"]!.Value<string>());
        Assert.Equal(0.92, results[0]!["rebuildConfidence"]!.Value<double>());

        Assert.Null(results[0]!["boundingBox"]);
        Assert.Null(results[0]!["ifcGuid"]);
        Assert.Null(results[1]!["rawProperties"]);
    }

    [Fact]
    public void ShapeIfcListRebuildCandidatesCompact_KeepsCoreFieldsAndDropsRest()
    {
        var payload = JObject.Parse("""
        {
          "count": 2,
          "minConfidence": 0.7,
          "candidates": [
            { "elementId": 21, "name": "Floor-IFC-1", "category": "Floors", "geometryType": "Extrusion", "rebuildConfidence": 0.95, "ifcEntity": "IfcSlab", "extraNoise": "drop" },
            { "elementId": 22, "name": "Roof-IFC-1",  "category": "Roofs",  "geometryType": "Extrusion", "rebuildConfidence": 0.80, "ifcEntity": "IfcRoof" }
          ]
        }
        """);

        var shaped = ToolResponseShaper.Shape("ifc_list_rebuild_candidates", payload, compact: true, summaryOnly: false);

        Assert.Equal(2, shaped["count"]!.Value<int>());
        Assert.Equal(0.7, shaped["minConfidence"]!.Value<double>());

        var candidates = (JArray)shaped["candidates"]!;
        Assert.Equal(2, candidates.Count);

        Assert.Equal(21, candidates[0]!["elementId"]!.Value<int>());
        Assert.Equal("Floor-IFC-1", candidates[0]!["name"]!.Value<string>());
        Assert.Equal("Floors", candidates[0]!["category"]!.Value<string>());
        Assert.Equal("Extrusion", candidates[0]!["geometryType"]!.Value<string>());
        Assert.Equal(0.95, candidates[0]!["rebuildConfidence"]!.Value<double>());

        Assert.Null(candidates[0]!["ifcEntity"]);
        Assert.Null(candidates[0]!["extraNoise"]);
    }

    [Fact]
    public void ShapeWorkflowModelAuditCompact_KeepsCountersAndTrimsArrays()
    {
        var payload = JObject.Parse("""
        {
          "healthScore": 82,
          "grade": "B",
          "warningCount": 2,
          "warnings": [
            { "description": "Highlighted lines overlap", "count": 5, "elementIds": [1,2,3,4,5], "severity": "warning" },
            { "description": "Room not enclosed",        "count": 2, "elementIds": [6,7],         "severity": "error"   }
          ],
          "inPlaceFamilies": [
            { "name": "InPlace-Mass-1", "category": "Mass",          "id": 100, "instanceCount": 1 },
            { "name": "InPlace-Wall-1", "category": "Walls",         "id": 101, "instanceCount": 2 }
          ],
          "unusedFamilies": [
            { "name": "Door-Old", "category": "Doors", "id": 200, "instanceCount": 0 }
          ]
        }
        """);

        var shaped = ToolResponseShaper.Shape("workflow_model_audit", payload, compact: true, summaryOnly: false);

        Assert.Equal(82, shaped["healthScore"]!.Value<int>());
        Assert.Equal("B", shaped["grade"]!.Value<string>());
        Assert.Equal(2, shaped["warningCount"]!.Value<int>());

        var warnings = (JArray)shaped["warnings"]!;
        Assert.Equal(2, warnings.Count);
        Assert.Equal("Highlighted lines overlap", warnings[0]!["description"]!.Value<string>());
        Assert.Equal(5, warnings[0]!["count"]!.Value<int>());
        Assert.Null(warnings[0]!["elementIds"]);
        Assert.Null(warnings[0]!["severity"]);

        var inPlace = (JArray)shaped["inPlaceFamilies"]!;
        Assert.Equal(2, inPlace.Count);
        Assert.Equal("InPlace-Mass-1", inPlace[0]!["name"]!.Value<string>());
        Assert.Equal("Mass", inPlace[0]!["category"]!.Value<string>());
        Assert.Null(inPlace[0]!["id"]);
        Assert.Null(inPlace[0]!["instanceCount"]);

        var unused = (JArray)shaped["unusedFamilies"]!;
        Assert.Single(unused);
        Assert.Equal("Door-Old", unused[0]!["name"]!.Value<string>());
        Assert.Equal("Doors", unused[0]!["category"]!.Value<string>());
        Assert.Null(unused[0]!["id"]);
    }
}
