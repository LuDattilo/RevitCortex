using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Rebar;

/// <summary>
/// Creates a fabric area system on a host (auto-distributes fabric sheets). Boundary mode (no curves)
/// or explicit boundary (curves[] mm forming a closed loop). Major direction fixed at creation.
/// </summary>
[ToolSafety(false, false)]
public class CreateFabricAreaTool : ICortexTool
{
    public string Name => "create_fabric_area";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create a fabric area system on a host (wall/floor/foundation). Provide hostId, majorDirection{x,y,z}, fabricSheetTypeId|fabricSheetTypeName; optional curves[] (mm, must form a closed loop) and fabricAreaTypeId.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (host, herr) = RebarToolHelpers.RequireHost(doc!, input["hostId"]?.Value<long?>());
        if (herr != null) return herr;
        if (input["majorDirection"] == null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "majorDirection{x,y,z} is required");
        // H35: a zero vector would make XYZ.Normalize() throw and escape Execute().
        var majorRaw = RebarToolHelpers.ParseXyzMm(input["majorDirection"]!);
        if (majorRaw.IsZeroLength())
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "majorDirection must be a non-zero vector");
        var major = majorRaw.Normalize();

        var sheetType = doc!.GetElement(ToolHelpers.ToElementId(input["fabricSheetTypeId"]?.Value<long?>() ?? -1)) as FabricSheetType;
        if (sheetType == null)
        {
            var name = input["fabricSheetTypeName"]?.Value<string>();
            sheetType = new FilteredElementCollector(doc).OfClass(typeof(FabricSheetType)).Cast<FabricSheetType>()
                .FirstOrDefault(t => name == null || t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        if (sheetType == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No FabricSheetType in document",
            suggestion: "Use list_rebar_fabric_types to find a fabricSheetTypeId");

        // FabricAreaType: prefer an explicit id, then any existing type, then create the default.
        var areaType = doc.GetElement(ToolHelpers.ToElementId(input["fabricAreaTypeId"]?.Value<long?>() ?? -1)) as FabricAreaType
            ?? new FilteredElementCollector(doc).OfClass(typeof(FabricAreaType)).Cast<FabricAreaType>().FirstOrDefault();
        var areaTypeId = areaType?.Id ?? FabricAreaType.CreateDefaultFabricAreaType(doc);
        if (areaTypeId == null || areaTypeId == ElementId.InvalidElementId)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No FabricAreaType could be resolved or created in document");

        // Optional explicit boundary. Verified across R23-R27 the curves overload takes IList<CurveLoop>
        // plus an XYZ majorDirectionOrigin — NOT IList<Curve>. We assemble the parsed curves into a single
        // closed CurveLoop and use the first curve's start as the major-direction origin.
        IList<CurveLoop>? loops = null;
        XYZ majorOrigin = XYZ.Zero;
        if (input["curves"] is JArray ca)
        {
            var curves = RebarToolHelpers.ParseCurveSpecsMm(ca, out var cerr);
            if (cerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, cerr);
            CurveLoop loop;
            try { loop = CurveLoop.Create(curves); }
            catch (Exception ex)
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"curves[] must form a single continuous closed loop: {ex.Message}");
            }
            loops = new List<CurveLoop> { loop };
            majorOrigin = curves[0].GetEndPoint(0);
        }

        if (!session.RequestConfirmation("create fabric area", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Create Fabric Area");
        tx.Start();
        try
        {
            // Verified across R23-R27:
            //   FabricArea.Create(Document, Element host, XYZ majorDirection, ElementId areaTypeId, ElementId sheetTypeId)
            //   FabricArea.Create(Document, Element host, IList<CurveLoop> loops, XYZ majorDirection, XYZ majorDirectionOrigin, ElementId areaTypeId, ElementId sheetTypeId)
            FabricArea area = loops != null
                ? FabricArea.Create(doc, host!, loops, major, majorOrigin, areaTypeId, sheetType.Id)
                : FabricArea.Create(doc, host!, major, areaTypeId, sheetType.Id);
            if (area == null) { tx.RollBack(); return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Revit returned no fabric area"); }
            // Fabric sheet distribution is populated during regeneration; query after regen in the same tx.
            doc.Regenerate();
            var sheetIds = area.GetFabricSheetElementIds().Select(i => ToolHelpers.GetElementIdValue(i)).ToList();
            var id = ToolHelpers.GetElementIdValue(area);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created fabric area {id} with {sheetIds.Count} sheet(s)",
                fabricAreaId = id,
                hostId = ToolHelpers.GetElementIdValue(host!),
                sheetCount = sheetIds.Count,
                sheetIds
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to create fabric area: {ex.Message}");
        }
    }
}

/// <summary>
/// Creates a single fabric sheet in a host. Flat by default; a bendProfile[] (mm curves forming a
/// closed loop) produces a bent sheet.
/// </summary>
[ToolSafety(false, false)]
public class CreateFabricSheetTool : ICortexTool
{
    public string Name => "create_fabric_sheet";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Create a single fabric sheet in a host. Provide hostId, fabricSheetTypeId|fabricSheetTypeName; optional bendProfile[] (mm curves forming a closed loop) to create a bent sheet.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var (host, herr) = RebarToolHelpers.RequireHost(doc!, input["hostId"]?.Value<long?>());
        if (herr != null) return herr;

        var sheetType = doc!.GetElement(ToolHelpers.ToElementId(input["fabricSheetTypeId"]?.Value<long?>() ?? -1)) as FabricSheetType;
        if (sheetType == null)
        {
            var name = input["fabricSheetTypeName"]?.Value<string>();
            sheetType = new FilteredElementCollector(doc).OfClass(typeof(FabricSheetType)).Cast<FabricSheetType>()
                .FirstOrDefault(t => name == null || t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        if (sheetType == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No FabricSheetType in document",
            suggestion: "Use list_rebar_fabric_types to find a fabricSheetTypeId");

        CurveLoop? bendLoop = null;
        if (input["bendProfile"] is JArray ba)
        {
            var curves = RebarToolHelpers.ParseCurveSpecsMm(ba, out var cerr);
            if (cerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, cerr);
            try { bendLoop = CurveLoop.Create(curves); }
            catch (Exception ex)
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"bendProfile[] must form a single continuous closed loop: {ex.Message}");
            }
        }

        if (!session.RequestConfirmation("create fabric sheet", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Create Fabric Sheet");
        tx.Start();
        try
        {
            // Verified across R23-R27:
            //   FabricSheet.Create(Document, Element host, ElementId sheetTypeId)  (flat)
            //   FabricSheet.Create(Document, ElementId hostId, ElementId sheetTypeId, CurveLoop bendProfile)  (bent)
            FabricSheet sheet = bendLoop != null
                ? FabricSheet.Create(doc, host!.Id, sheetType.Id, bendLoop)
                : FabricSheet.Create(doc, host!, sheetType.Id);
            if (sheet == null) { tx.RollBack(); return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, "Revit returned no fabric sheet"); }
            var id = ToolHelpers.GetElementIdValue(sheet);
            var isBent = sheet.IsBent;
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Created fabric sheet {id}",
                fabricSheetId = id,
                hostId = ToolHelpers.GetElementIdValue(host!),
                isBent
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to create fabric sheet: {ex.Message}");
        }
    }
}

/// <summary>Positions an existing fabric sheet inside a host with an optional translation (mm).</summary>
[ToolSafety(false, false)]
public class PlaceFabricSheetTool : ICortexTool
{
    public string Name => "place_fabric_sheet";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Place an existing fabric sheet into a host. Provide fabricSheetId, hostId; optional transform{translation:{x,y,z}} in mm (default identity).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var sheetId = input["fabricSheetId"]?.Value<long?>();
        if (sheetId == null || sheetId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "fabricSheetId is required");
        var sheet = doc!.GetElement(ToolHelpers.ToElementId(sheetId.Value)) as FabricSheet;
        if (sheet == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No FabricSheet with id {sheetId}");
        var (host, herr) = RebarToolHelpers.RequireHost(doc, input["hostId"]?.Value<long?>());
        if (herr != null) return herr;

        Transform transform = Transform.Identity;
        var translationToken = input["transform"]?["translation"];
        if (translationToken != null)
            transform = Transform.CreateTranslation(RebarToolHelpers.ParseXyzMm(translationToken));

        if (!session.RequestConfirmation("place fabric sheet", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Place Fabric Sheet");
        tx.Start();
        try
        {
            // Verified across R23-R27: FabricSheet.PlaceInHost(Element hostElement, Transform transform).
            sheet.PlaceInHost(host!, transform);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Placed fabric sheet {sheetId} in host {ToolHelpers.GetElementIdValue(host!)}",
                fabricSheetId = sheetId.Value,
                hostId = ToolHelpers.GetElementIdValue(host!),
                placed = true
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to place fabric sheet: {ex.Message}");
        }
    }
}

/// <summary>Sets the bend profile (closed curve loop in mm) of an existing bent fabric sheet.</summary>
[ToolSafety(false, false)]
public class SetFabricSheetBendProfileTool : ICortexTool
{
    public string Name => "set_fabric_sheet_bend_profile";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set the bend profile of a bent fabric sheet. Provide fabricSheetId and bendProfile[] (mm curves forming a closed loop). Only valid when the sheet is bent.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var sheetId = input["fabricSheetId"]?.Value<long?>();
        if (sheetId == null || sheetId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "fabricSheetId is required");
        var sheet = doc!.GetElement(ToolHelpers.ToElementId(sheetId.Value)) as FabricSheet;
        if (sheet == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No FabricSheet with id {sheetId}");

        if (!(input["bendProfile"] is JArray ba))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "bendProfile[] (mm curves) is required");
        var curves = RebarToolHelpers.ParseCurveSpecsMm(ba, out var cerr);
        if (cerr != null) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, cerr);
        CurveLoop loop;
        try { loop = CurveLoop.Create(curves); }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"bendProfile[] must form a single continuous closed loop: {ex.Message}");
        }

        if (!sheet.IsBent)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Fabric sheet {sheetId} is not bent; a bend profile can only be set on a bent sheet",
                suggestion: "Create the sheet with a bendProfile via create_fabric_sheet to make it bent.");

        if (!session.RequestConfirmation("set fabric sheet bend profile", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Set Fabric Sheet Bend Profile");
        tx.Start();
        try
        {
            // Verified across R23-R27: FabricSheet.SetBendProfile(CurveLoop bendProfile).
            sheet.SetBendProfile(loop);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Set bend profile on fabric sheet {sheetId}",
                fabricSheetId = sheetId.Value,
                isBent = sheet.IsBent
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to set fabric sheet bend profile: {ex.Message}");
        }
    }
}

/// <summary>Removes a fabric area reinforcement system (DESTRUCTIVE).</summary>
[ToolSafety(false, true)]
public class RemoveFabricReinforcementSystemTool : ICortexTool
{
    public string Name => "remove_fabric_reinforcement_system";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Remove a fabric area reinforcement system (destructive). Provide fabricAreaId.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var areaId = input["fabricAreaId"]?.Value<long?>();
        if (areaId == null || areaId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "fabricAreaId is required");
        var area = doc!.GetElement(ToolHelpers.ToElementId(areaId.Value)) as FabricArea;
        if (area == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No FabricArea with id {areaId}");

        if (!session.RequestConfirmation("remove fabric area system", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Remove Fabric Reinforcement System");
        tx.Start();
        try
        {
            // Verified across R23-R27: STATIC FabricArea.RemoveFabricReinforcementSystem(Document, FabricArea)
            // returns IList<ElementId> of affected/remaining elements.
            IList<ElementId> remaining = FabricArea.RemoveFabricReinforcementSystem(doc, area);
            tx.Commit();
            return CortexResult<object>.Ok(new
            {
                message = $"Removed fabric reinforcement system {areaId}",
                fabricAreaId = areaId.Value,
                removed = true,
                remainingElementCount = remaining.Count
            });
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed, $"Failed to remove fabric reinforcement system: {ex.Message}");
        }
    }
}

/// <summary>Reads core data of a fabric area system (read-only).</summary>
[ToolSafety(true, false)]
public class GetFabricAreaDataTool : ICortexTool
{
    public string Name => "get_fabric_area_data";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read a fabric area system: type id/name, host id, sheet ids, sheet count, major direction (mm vector).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var areaId = input["fabricAreaId"]?.Value<long?>();
        if (areaId == null || areaId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "fabricAreaId is required");
        var area = doc!.GetElement(ToolHelpers.ToElementId(areaId.Value)) as FabricArea;
        if (area == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No FabricArea with id {areaId}");

        try
        {
            // Verified R23-R27: FabricArea.GetFabricSheetElementIds() (NOT GetFabricSheetIds); HostId and
            // FabricAreaType are PROPERTIES (no GetHostId() method); Direction is an XYZ property.
            var sheetIds = area.GetFabricSheetElementIds().Select(i => ToolHelpers.GetElementIdValue(i)).ToList();
            var type = area.FabricAreaType;
            JObject? direction = null;
            try { if (area.Direction != null) direction = RebarToolHelpers.XyzToDtoMm(area.Direction); }
            catch { /* direction not readable on some systems */ }
            return CortexResult<object>.Ok(new
            {
                fabricAreaId = ToolHelpers.GetElementIdValue(area),
                hostId = ToolHelpers.GetElementIdValue(area.HostId),
                typeId = ToolHelpers.GetElementIdValue(type),
                typeName = type?.Name,
                direction,
                sheetCount = sheetIds.Count,
                sheetIds
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read fabric area: {ex.Message}");
        }
    }
}

/// <summary>Reads core data of a fabric sheet (read-only).</summary>
[ToolSafety(true, false)]
public class GetFabricSheetDataTool : ICortexTool
{
    public string Name => "get_fabric_sheet_data";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read a fabric sheet: type id/name, isBent, fabricNumber, cut overall length and width (mm).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var sheetId = input["fabricSheetId"]?.Value<long?>();
        if (sheetId == null || sheetId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "fabricSheetId is required");
        var sheet = doc!.GetElement(ToolHelpers.ToElementId(sheetId.Value)) as FabricSheet;
        if (sheet == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No FabricSheet with id {sheetId}");

        try
        {
            var type = doc.GetElement(sheet.GetTypeId()) as FabricSheetType;
            // Verified R23-R27: FabricSheet exposes IsBent, FabricNumber, CutOverallLength, CutOverallWidth.
            return CortexResult<object>.Ok(new
            {
                fabricSheetId = ToolHelpers.GetElementIdValue(sheet),
                typeId = ToolHelpers.GetElementIdValue(type),
                typeName = type?.Name,
                isBent = sheet.IsBent,
                fabricNumber = sheet.FabricNumber,
                cutOverallLengthMm = RebarToolHelpers.ToMm(sheet.CutOverallLength),
                cutOverallWidthMm = RebarToolHelpers.ToMm(sheet.CutOverallWidth)
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read fabric sheet: {ex.Message}");
        }
    }
}

/// <summary>Reads the wire layout (diameter + distance, mm) of a fabric sheet in one direction (read-only).</summary>
[ToolSafety(true, false)]
public class GetFabricWireDataTool : ICortexTool
{
    public string Name => "get_fabric_wire_data";
    public string Category => "Rebar";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read the wire items of a fabric sheet in one direction. Provide fabricSheetId and direction (major|minor); optional maxWires (default 200). Returns per-wire diameter (mm), distance (mm), wire length (mm).";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;
        var sheetId = input["fabricSheetId"]?.Value<long?>();
        if (sheetId == null || sheetId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "fabricSheetId is required");
        var sheet = doc!.GetElement(ToolHelpers.ToElementId(sheetId.Value)) as FabricSheet;
        if (sheet == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"No FabricSheet with id {sheetId}");

        var dirStr = (input["direction"]?.Value<string>() ?? "").Trim().ToLowerInvariant();
        WireDistributionDirection direction;
        switch (dirStr)
        {
            // Verified enum members (R23-R27): WireDistributionDirection.Major / .Minor (NOT X / Y).
            case "major": direction = WireDistributionDirection.Major; break;
            case "minor": direction = WireDistributionDirection.Minor; break;
            default:
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unsupported direction '{input["direction"]?.Value<string>()}'. Valid: major, minor");
        }

        // Verified R23-R27: GetWireItem(int, WireDistributionDirection) lives on FabricSheetType, not FabricSheet.
        // The wire COUNT comes from MajorNumberOfWires/MinorNumberOfWires; the wire DIAMETER comes from the
        // Major/MinorDirectionWireType (a RebarBarType). FabricWireItem exposes Distance/WireLength/OffsetAlongWire.
        var type = doc.GetElement(sheet.GetTypeId()) as FabricSheetType;
        if (type == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
            $"Fabric sheet {sheetId} has no resolvable FabricSheetType");

        var maxWires = input["maxWires"]?.Value<int?>() ?? 200;
        if (maxWires <= 0) maxWires = 200;

        try
        {
            int total = direction == WireDistributionDirection.Major ? type.MajorNumberOfWires : type.MinorNumberOfWires;
            var wireTypeId = direction == WireDistributionDirection.Major ? type.MajorDirectionWireType : type.MinorDirectionWireType;
            var wireBarType = doc.GetElement(wireTypeId) as RebarBarType;
            double? diameterMm = wireBarType != null ? RebarToolHelpers.ToMm(wireBarType.BarModelDiameter) : (double?)null;
            double spacingMm = RebarToolHelpers.ToMm(direction == WireDistributionDirection.Major ? type.MajorSpacing : type.MinorSpacing);

            var truncated = total > maxWires;
            var limit = truncated ? maxWires : total;
            var wires = new List<JObject>();
            for (int i = 0; i < limit; i++)
            {
                FabricWireItem item;
                try { item = type.GetWireItem(i, direction); }
                catch { break; } // defensive: stop at the first index Revit refuses
                if (item == null) break;
                wires.Add(new JObject
                {
                    ["index"] = i,
                    ["diameterMm"] = diameterMm,
                    ["distanceMm"] = RebarToolHelpers.ToMm(item.Distance),
                    ["wireLengthMm"] = RebarToolHelpers.ToMm(item.WireLength),
                    ["offsetAlongWireMm"] = RebarToolHelpers.ToMm(item.OffsetAlongWire)
                });
            }

            return CortexResult<object>.Ok(new
            {
                fabricSheetId = ToolHelpers.GetElementIdValue(sheet),
                typeId = ToolHelpers.GetElementIdValue(type),
                direction = dirStr,
                wireTypeId = ToolHelpers.GetElementIdValue(wireTypeId),
                wireDiameterMm = diameterMm,
                spacingMm,
                totalWireCount = total,
                returnedWireCount = wires.Count,
                truncated,
                wires
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to read fabric wire data: {ex.Message}");
        }
    }
}
