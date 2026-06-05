using System.Linq;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

/// <summary>
/// Response-shaping rules — INVARIANTS (safety contract):
///   1) NEVER drop items from a list. Stripping happens at the FIELD level inside each item.
///      Counters returned by Revit (count, totalRooms, instanceCount, …) must remain truthful.
///   2) NEVER recompute a numeric value from the trimmed list. Always carry the original
///      scalar from the source payload through DeepClone or explicit copy.
///   3) NEVER alter user-facing identifiers (id/elementId/name/category). They are essential.
///   4) NEVER throw on malformed payload — return original payload unchanged when in doubt.
/// Violating any of these turns the shaper into a silent data corruptor — Claude reads
/// trimmed responses verbatim and CANNOT detect a wrong count.
/// </summary>
public static class ToolResponseShaper
{
    /// <summary>
    /// Per-call shaping. The caller passes explicit compact/summaryOnly inputs from the
    /// MCP request — when both are false the original payload is returned unchanged.
    /// </summary>
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
            "get_shared_parameters" => ShapeSharedParameters(payload),
            "get_linked_file_instances" => ShapeLinkedFileInstances(payload),
            "get_coordination_models" => ShapeCoordinationModels(payload),
            "get_elements_in_spatial_volume" => ShapeElementsInSpatialVolume(payload),
            "get_materials" => ShapeMaterials(payload),
            "export_room_data" => ShapeExportRoomData(payload),
            "ifc_list_export_configurations" => ShapeIfcExportConfigurations(payload),
            "ifc_analyze_rebuildability" => ShapeIfcAnalyzeRebuildability(payload),
            "ifc_list_rebuild_candidates" => ShapeIfcListRebuildCandidates(payload),
            "workflow_model_audit" => ShapeWorkflowModelAudit(payload),
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

        // H29: honor the "never throw on malformed payload" invariant — if the plugin
        // returned no 'fields' key (error response, empty category, zero schedulable
        // fields), return the payload unchanged instead of dereferencing null.
        var fieldsToken = payload["fields"];
        if (fieldsToken is null)
            return payload;

        var fieldNames = fieldsToken
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

    /// <summary>get_shared_parameters → keeps name, guid, parameterType, parameterGroup, isShared, isInstance, categories.</summary>
    private static JToken ShapeSharedParameters(JToken payload)
    {
        return StripFromArrayProperty(payload, "parameters",
            keep: new[] { "name", "guid", "parameterType", "parameterGroup", "isShared", "isInstance", "categories" });
    }

    /// <summary>get_linked_file_instances → drops origin/basisX/basisY transforms in nested instances; keeps file-level info.</summary>
    private static JToken ShapeLinkedFileInstances(JToken payload)
    {
        if (payload is not JObject obj) return payload;
        var result = (JObject)obj.DeepClone();
        if (result["linkedFiles"] is not JArray files) return result;

        var trimmed = files.Children<JObject>().Select(file =>
        {
            var slim = new JObject
            {
                ["typeId"] = file["typeId"],
                ["typeName"] = file["typeName"],
                ["isLoaded"] = file["isLoaded"],
                ["path"] = file["path"],
                ["instanceCount"] = file["instanceCount"]
            };
            if (file["instances"] is JArray instances)
            {
                slim["instances"] = new JArray(instances.Children<JObject>().Select(inst => new JObject
                {
                    ["instanceId"] = inst["instanceId"],
                    ["isPinned"] = inst["isPinned"]
                }));
            }
            return slim;
        }).ToArray();

        result["linkedFiles"] = new JArray(trimmed);
        return result;
    }

    /// <summary>
    /// get_coordination_models → keeps top-level counters (modelCount, totalInstances, matchedInstances, apiAvailable);
    /// drops verbose per-model fields (pathType, path) and per-instance fields (name, origin); preserves typeId,
    /// instanceCount, instanceId so the caller can still drill down.
    /// </summary>
    private static JToken ShapeCoordinationModels(JToken payload)
    {
        if (payload is not JObject obj) return payload;
        var result = (JObject)obj.DeepClone();
        if (result["models"] is not JArray models) return result;

        var trimmed = models.Children<JObject>().Select(model =>
        {
            var slim = new JObject
            {
                ["typeId"] = model["typeId"],
                ["typeName"] = model["typeName"],
                ["isCloud"] = model["isCloud"],
                ["instanceCount"] = model["instanceCount"]
            };
            if (model["instances"] is JArray instances)
            {
                slim["instances"] = new JArray(instances.Children<JObject>().Select(inst => new JObject
                {
                    ["instanceId"] = inst["instanceId"]
                }));
            }
            return slim;
        }).ToArray();

        result["models"] = new JArray(trimmed);
        return result;
    }

    /// <summary>
    /// get_linked_elements → keeps link wrappers; preserves ALL custom parameter fields per element.
    /// IMPORTANT: caller can request parameterNames=[...] which materialize as dynamic top-level
    /// fields on each element (e.g. "IfcType":"IfcWall"). Stripping by name list would silently
    /// erase user-requested data — instead we keep the entire element object as-is.
    /// </summary>
    private static JToken ShapeLinkedElements(JToken payload)
    {
        // No safe stripping possible without a whitelist of "removable metadata" — and the
        // current tool implementation only emits id/category/name + caller-driven keys, all
        // of which are essential. Return payload unchanged.
        return payload;
    }

    /// <summary>get_elements_in_spatial_volume → trims nested element items inside volume wrappers.</summary>
    private static JToken ShapeElementsInSpatialVolume(JToken payload)
    {
        if (payload is not JObject obj) return payload;
        var result = (JObject)obj.DeepClone();
        if (result["volumes"] is not JArray volumes) return result;

        var trimmed = volumes.Children<JObject>().Select(vol =>
        {
            var slim = new JObject
            {
                ["volumeType"] = vol["volumeType"],
                ["volumeId"] = vol["volumeId"],
                ["volumeName"] = vol["volumeName"],
                ["elementCount"] = vol["elementCount"],
                ["totalElementCount"] = vol["totalElementCount"],
                ["truncated"] = vol["truncated"]
            };
            if (vol["elements"] is JArray elements)
            {
                slim["elements"] = new JArray(elements.Children<JObject>().Select(el => new JObject
                {
                    ["elementId"] = el["elementId"],
                    ["name"] = el["name"],
                    ["category"] = el["category"],
                    ["familyName"] = el["familyName"],
                    ["typeName"] = el["typeName"]
                }));
            }
            return slim;
        }).ToArray();

        result["volumes"] = new JArray(trimmed);
        return result;
    }

    /// <summary>get_materials → drops only transparency/shininess/smoothness numbers; keeps has*Asset flags so the caller can decide whether to query get_material_properties.</summary>
    private static JToken ShapeMaterials(JToken payload)
    {
        return StripFromArrayProperty(payload, "materials",
            keep: new[] { "id", "name", "materialClass", "materialCategory", "color",
                          "hasAppearanceAsset", "hasStructuralAsset", "hasThermalAsset" });
    }

/// <summary>export_room_data → keeps room essentials, drops department/perimeter.</summary>
    private static JToken ShapeExportRoomData(JToken payload)
    {
        return StripFromArrayProperty(payload, "rooms",
            keep: new[] { "id", "name", "number", "level", "areaSqM", "volumeCuM" });
    }

    /// <summary>ifc_list_export_configurations → keeps name + ifcVersion (drops description text).</summary>
    private static JToken ShapeIfcExportConfigurations(JToken payload)
    {
        return StripFromArrayProperty(payload, "configurations",
            keep: new[] { "name", "ifcVersion" });
    }

    /// <summary>ifc_analyze_rebuildability → keeps id/category/strategy/confidence per result.</summary>
    private static JToken ShapeIfcAnalyzeRebuildability(JToken payload)
    {
        return StripFromArrayProperty(payload, "results",
            keep: new[] { "elementId", "name", "category", "ifcEntity", "geometryType", "rebuildStrategy", "rebuildConfidence" });
    }

    /// <summary>ifc_list_rebuild_candidates → keeps elementId/category/geometry/confidence.</summary>
    private static JToken ShapeIfcListRebuildCandidates(JToken payload)
    {
        return StripFromArrayProperty(payload, "candidates",
            keep: new[] { "elementId", "name", "category", "geometryType", "rebuildConfidence" });
    }

    /// <summary>workflow_model_audit → trims warnings/inPlaceFamilies/unusedFamilies arrays. Top-level counters preserved by DeepClone.</summary>
    private static JToken ShapeWorkflowModelAudit(JToken payload)
    {
        if (payload is not JObject obj) return payload;
        var result = (JObject)obj.DeepClone();
        if (result["warnings"] is JArray warnings)
        {
            result["warnings"] = new JArray(warnings.Children<JObject>().Select(w => new JObject
            {
                ["description"] = w["description"],
                ["count"] = w["count"]
            }));
        }
        if (result["inPlaceFamilies"] is JArray inPlace)
        {
            result["inPlaceFamilies"] = new JArray(inPlace.Children<JObject>().Select(f => new JObject
            {
                ["name"] = f["name"],
                ["category"] = f["category"]
            }));
        }
        if (result["unusedFamilies"] is JArray unused)
        {
            result["unusedFamilies"] = new JArray(unused.Children<JObject>().Select(f => new JObject
            {
                ["name"] = f["name"],
                ["category"] = f["category"]
            }));
        }
        return result;
    }

    /// <summary>
    /// Helper: rewrite an array-typed property of a JObject so that each item only retains
    /// the listed keys. Top-level scalars (counts, totals, etc.) are preserved.
    /// </summary>
    private static JToken StripFromArrayProperty(JToken payload, string arrayProperty, string[] keep)
    {
        if (payload is not JObject obj) return payload;
        var result = (JObject)obj.DeepClone();
        if (result[arrayProperty] is not JArray array) return result;

        var trimmed = array.Children<JObject>().Select(item =>
        {
            var slim = new JObject();
            foreach (var k in keep)
            {
                if (item[k] != null) slim[k] = item[k];
            }
            return slim;
        }).ToArray();

        result[arrayProperty] = new JArray(trimmed);
        return result;
    }
}
