using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Connection;

namespace RiveTT.Server.Tools;

/// <summary>
/// Small, explicit architectural surface for agents. These wrappers intentionally
/// avoid the ambiguous generic creation schemas inherited from RiveTT.
/// </summary>
[McpServerToolType]
public static class ArchitectureTools
{
    [McpServerTool(Name = "create_wall"), Description("Create one native Revit wall. wallTypeId and baseLevelId are required. Set topLevelId to constrain the wall to a level; topOffset is in mm and may be negative for partitions below a slab. ELEVATION: locationLine z values are IGNORED - baseLevelId plus baseOffset set the elevation. This differs from create_door/create_window, where locationPoint.z is an absolute project elevation unless zMode=relativeToLevel.")]
    public static async Task<string> CreateWall(
        RevitConnectionManager revit,
        [Description("Wall type element ID")] long wallTypeId,
        [Description("Base level element ID")] long baseLevelId,
        [Description("Line JSON: {p0:{x,y,z},p1:{x,y,z},pMid?:{x,y,z}} in mm")] string locationLine,
        [Description("Top constraint level element ID. Omit for an unconnected wall")] long? topLevelId = null,
        [Description("Unconnected height in mm. Used only when topLevelId is omitted. Default: 3000")] double? height = null,
        [Description("Base offset in mm. Default: 0")] double? baseOffset = null,
        [Description("Top offset in mm. Default: 0")] double? topOffset = null,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var wall = new JObject
        {
            ["category"] = "OST_Walls",
            ["typeId"] = wallTypeId,
            ["baseLevelId"] = baseLevelId,
            ["locationLine"] = JObject.Parse(locationLine),
            ["strictType"] = true
        };
        if (topLevelId != null) wall["topLevelId"] = topLevelId;
        if (height != null) wall["height"] = height;
        if (baseOffset != null) wall["baseOffset"] = baseOffset;
        if (topOffset != null) wall["topOffset"] = topOffset;
        return (await revit.ExecuteAsync("create_line_based_element", new JObject
        {
            ["data"] = new JArray(wall),
            ["dryRun"] = dryRun
        }, ct, publicToolName: "create_wall")).ToString();
    }

    [McpServerTool(Name = "create_door"), Description("Place a door family type in a host wall. ELEVATION: locationPoint.z is an ABSOLUTE project elevation by default - pass zMode=relativeToLevel to give z relative to levelId (z=0 = floor level), which is usually what you want. A point outside the host wall vertical range is refused with the valid range in mm, instead of Revit's 'instances do not cut anything'.")]
    public static Task<string> CreateDoor(
        RevitConnectionManager revit,
        [Description("Door family type element ID")] long typeId,
        [Description("Host wall element ID")] long hostWallId,
        [Description("Insertion point JSON {x,y,z} in mm")] string locationPoint,
        [Description("Level element ID")] long levelId,
        [Description("Flip the exterior/interior facing direction. Default false")] bool facingFlipped = false,
        [Description("Flip the door hand. Default false")] bool handFlipped = false,
        [Description("z semantics: absolute (default) | relativeToLevel")] string? zMode = null,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
        => CreateHostedOpening(revit, "OST_Doors", typeId, hostWallId, locationPoint, levelId,
            facingFlipped, handFlipped, zMode, dryRun, ct, publicToolName: "create_door");

    [McpServerTool(Name = "create_window"), Description("Place a window family type in a host wall. ELEVATION: locationPoint.z is an ABSOLUTE project elevation by default - pass zMode=relativeToLevel to give z as a sill height above levelId. A point outside the host wall vertical range, or a type wider than the wall, is refused with the numbers in mm.")]
    public static Task<string> CreateWindow(
        RevitConnectionManager revit,
        [Description("Window family type element ID")] long typeId,
        [Description("Host wall element ID")] long hostWallId,
        [Description("Insertion point JSON {x,y,z} in mm")] string locationPoint,
        [Description("Level element ID")] long levelId,
        [Description("Flip the exterior/interior facing direction. Default false")] bool facingFlipped = false,
        [Description("z semantics: absolute (default) | relativeToLevel")] string? zMode = null,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
        => CreateHostedOpening(revit, "OST_Windows", typeId, hostWallId, locationPoint, levelId,
            facingFlipped, false, zMode, dryRun, ct, publicToolName: "create_window");

    [McpServerTool(Name = "create_railing"), Description("Create a native Revit guardrail from a connected horizontal path. The path JSON is [{x,y,z}, ...] in mm.")]
    public static async Task<string> CreateRailing(
        RevitConnectionManager revit,
        [Description("Railing type element ID")] long railingTypeId,
        [Description("Base level element ID")] long baseLevelId,
        [Description("Connected path JSON [{x,y,z}, ...] in mm")] string path,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        return (await revit.ExecuteAsync("create_railing", new JObject
        {
            ["railingTypeId"] = railingTypeId,
            ["baseLevelId"] = baseLevelId,
            ["path"] = JArray.Parse(path),
            ["dryRun"] = dryRun
        }, ct)).ToString();
    }

    [McpServerTool(Name = "set_wall_host"), Description("Revit 2027: associate a lining or façade wall with a host wall. Set hostWallId to 0 to detach it. offsetFromHost is in mm.")]
    public static async Task<string> SetWallHost(
        RevitConnectionManager revit,
        [Description("Wall to host")] long wallId,
        [Description("Host wall ID, or 0 to detach")] long hostWallId,
        [Description("Offset from host in mm. Default 0")] double? offsetFromHost = null,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var request = new JObject { ["wallId"] = wallId, ["hostWallId"] = hostWallId };
        if (offsetFromHost != null) request["offsetFromHost"] = offsetFromHost;
        request["dryRun"] = dryRun;
        return (await revit.ExecuteAsync("set_wall_host", request, ct)).ToString();
    }

    private static async Task<string> CreateHostedOpening(
        RevitConnectionManager revit, string category, long typeId, long hostWallId,
        string locationPoint, long levelId, bool facingFlipped, bool handFlipped,
        string? zMode, bool dryRun, CancellationToken ct, string publicToolName)
    {
        var spec = new JObject
        {
            ["category"] = category,
            ["typeId"] = typeId,
            ["hostWallId"] = hostWallId,
            ["levelId"] = levelId,
            ["locationPoint"] = JObject.Parse(locationPoint),
            ["strictType"] = true
        };
        spec["facingFlipped"] = facingFlipped;
        spec["handFlipped"] = handFlipped;
        if (zMode != null) spec["zMode"] = zMode;
        return (await revit.ExecuteAsync("create_point_based_element", new JObject
        {
            ["data"] = new JArray(spec),
            ["dryRun"] = dryRun
        }, ct, publicToolName: publicToolName)).ToString();
    }
}
