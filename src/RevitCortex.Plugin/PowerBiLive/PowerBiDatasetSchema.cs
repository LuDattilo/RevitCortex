using System.Collections.Generic;
using Newtonsoft.Json;

namespace RevitCortex.Plugin.PowerBiLive;

/// <summary>
/// Defines the fixed, versioned schema for RevitCortex Live push datasets.
/// Schema changes require a new version suffix in the dataset name to avoid
/// invisible breaking changes on existing reports.
/// </summary>
public static class PowerBiDatasetSchema
{
    public const string CurrentVersion = "1.0";

    // ─── Table names ────────────────────────────────────────────────────────
    public const string TableMetadata          = "Metadata";
    public const string TableElements          = "Elements";
    public const string TableSchedules         = "Schedules";
    public const string TableElementParameters = "ElementParameters";
    public const string TableSelection         = "Selection";

    /// <summary>
    /// Builds the full Power BI REST API dataset creation body for the requested tables.
    /// </summary>
    public static object BuildCreateDatasetBody(
        string datasetName,
        IEnumerable<string> tables)
    {
        var tableDefs = new List<object>();
        foreach (var t in tables)
        {
            var def = GetTableDefinition(t);
            if (def != null) tableDefs.Add(def);
        }

        return new
        {
            name = datasetName,
            defaultMode = "Push",
            tables = tableDefs
        };
    }

    private static object? GetTableDefinition(string tableName)
    {
        return tableName switch
        {
            TableMetadata          => MetadataTable(),
            TableElements          => ElementsTable(),
            TableSchedules         => SchedulesTable(),
            TableElementParameters => ElementParametersTable(),
            TableSelection         => SelectionTable(),
            _                      => null
        };
    }

    // ─── Table definitions ───────────────────────────────────────────────────

    private static object MetadataTable() => new
    {
        name = TableMetadata,
        columns = new[]
        {
            Col("_SchemaVersion", "String"),
            Col("Key",            "String"),
            Col("Value",          "String"),
            Col("UpdatedAtUtc",   "DateTime")
        }
    };

    private static object ElementsTable() => new
    {
        name = TableElements,
        columns = new[]
        {
            Col("_SchemaVersion",   "String"),
            Col("ExportRunId",      "String"),
            Col("ExportedAtUtc",    "DateTime"),
            Col("ProjectId",        "String"),
            Col("ProjectName",      "String"),
            Col("DocumentGuid",     "String"),
            Col("ElementId",        "Int64"),
            Col("UniqueId",         "String"),
            Col("Category",         "String"),
            Col("OstCode",          "String"),
            Col("CategoryType",     "String"),
            Col("FamilyName",       "String"),
            Col("TypeName",         "String"),
            Col("Level",            "String"),
            Col("Workset",          "String"),
            Col("PhaseCreated",     "String"),
            Col("PhaseDemolished",  "String"),
            Col("Name",             "String"),
            Col("Mark",             "String"),
            Col("Comments",         "String"),
            Col("Volume",           "Double"),
            Col("Area",             "Double"),
            Col("Length",           "Double"),
            Col("BoundingBoxMinX",  "Double"),
            Col("BoundingBoxMinY",  "Double"),
            Col("BoundingBoxMinZ",  "Double"),
            Col("BoundingBoxMaxX",  "Double"),
            Col("BoundingBoxMaxY",  "Double"),
            Col("BoundingBoxMaxZ",  "Double")
        }
    };

    private static object SchedulesTable() => new
    {
        name = TableSchedules,
        columns = new[]
        {
            Col("_SchemaVersion", "String"),
            Col("ExportRunId",    "String"),
            Col("ExportedAtUtc",  "DateTime"),
            Col("ProjectId",      "String"),
            Col("DocumentGuid",   "String"),
            Col("ScheduleId",     "Int64"),
            Col("ScheduleName",   "String"),
            Col("RowIndex",       "Int64"),
            Col("ColumnName",     "String"),
            Col("ValueString",    "String"),
            Col("ValueNumber",    "Double")
        }
    };

    private static object ElementParametersTable() => new
    {
        name = TableElementParameters,
        columns = new[]
        {
            Col("_SchemaVersion",  "String"),
            Col("ExportRunId",     "String"),
            Col("ExportedAtUtc",   "DateTime"),
            Col("ProjectId",       "String"),
            Col("DocumentGuid",    "String"),
            Col("ElementId",       "Int64"),
            Col("UniqueId",        "String"),
            Col("ParameterName",   "String"),
            Col("ParameterScope",  "String"),
            Col("StorageType",     "String"),
            Col("ValueString",     "String"),
            Col("ValueNumber",     "Double"),
            Col("Unit",            "String"),
            Col("IsReadOnly",      "Boolean")
        }
    };

    private static object SelectionTable() => new
    {
        name = TableSelection,
        columns = new[]
        {
            Col("_SchemaVersion",  "String"),
            Col("UpdatedAtUtc",    "DateTime"),
            Col("ProjectId",       "String"),
            Col("DocumentGuid",    "String"),
            Col("ElementId",       "Int64"),
            Col("UniqueId",        "String"),
            Col("SelectionSetId",  "String")
        }
    };

    private static object Col(string name, string dataType) =>
        new { name, dataType };

    /// <summary>All supported table names in definition order.</summary>
    public static readonly string[] AllTables = new[]
    {
        TableMetadata,
        TableElements,
        TableSchedules,
        TableElementParameters,
        TableSelection
    };
}
