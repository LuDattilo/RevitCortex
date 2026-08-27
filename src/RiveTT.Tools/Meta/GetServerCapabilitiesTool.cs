using System.Linq;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Hosting;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;

namespace RiveTT.Tools.Meta;

[ToolSafety(true, false)]
public sealed class GetServerCapabilitiesTool : ICortexTool
{
    public string Name => "get_server_capabilities";
    public string Category => "Meta";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Report the effective RiveTT execution, safety, response, and document capability contract.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var caps = session.Capabilities;
        return CortexResult<object>.Ok(new
        {
            connector = "RiveTT",
            // Read from the active document rather than a literal: this connector's
            // plugin/tools DLLs can be built for either Revit 2026 (-p:RevitVersion=2026,
            // targeting the 2026.5 .NET 10 update) or 2027, and a hardcoded number here
            // would lie about whichever one this session is actually running inside.
            revitVersion = GetActiveRevitVersion(session),
            runtime = ".NET 10 / Windows x64",
            transport = "named_pipe_current_user",
            executionMode = "automatic",
            confirmationRequired = false,
            // Two different facts, and conflating them cost a whole session:
            // execution.toolReadOnly classifies the tool that answered, while
            // writesAllowed is the state of the ribbon write lock for the whole
            // session. The field used to be named "readOnly" and hard-coded true.
            writesAllowed = session.WriteAccess.WritesAllowed,
            readOnlyModeExists = true,
            readOnlyMode = new
            {
                active = !session.WriteAccess.WritesAllowed,
                defaultAtStartup = "read-only",
                scope = "the Revit session; surviving document open/close/save-as",
                blocks = "every tool whose execution.toolReadOnly is false, refused with " +
                         "PermissionDenied before execution — the model is never touched",
                stillAllowed = "all read tools, and dryRun previews are NOT an exception: " +
                               "a write tool stays refused even with dryRun=true",
                unlockFrom = "Revit ribbon, Add-Ins tab, RiveTT panel, 'Écriture' button",
                toolsCanUnlock = false,
                changedBy = session.WriteAccess.ChangedBy,
                changedUtc = session.WriteAccess.ChangedUtc.ToString("o")
            },
            executionFields = new
            {
                toolReadOnly = "classification of the tool that produced this response",
                toolDestructive = "the tool can delete or overwrite model data",
                writesAllowed = "session-wide state of the ribbon write lock; when false, " +
                                "every tool with toolReadOnly=false is refused",
                cached = "the response was served from the tool result cache"
            },
            dryRunDefault = true,
            unitPolicy = new
            {
                inputs = "lengths in mm, angles in degrees",
                outputs = "project display units, with an explicit unit and the Revit internal value " +
                          "(internalValue, in ft/ft2/ft3) on numeric parameters",
                elevation = "create_wall ignores locationLine.z (baseLevelId governs); " +
                            "create_door/create_window locationPoint.z is an absolute project elevation " +
                            "unless zMode=relativeToLevel"
            },
            parameterNames = new
            {
                localization = "parameter and schedule-field names resolve in English or in the document " +
                               "language (Mark/Repere, Level/Niveau, Width/Largeur, Type Name/Nom du type)",
                unresolved = "reported in unresolvedParameterNames with suggestions, never as an empty column"
            },
            auditLogPath = CortexEnvironment.Current.AuditLogPath,
            responseModes = new[] { "summary", "idsOnly", "details" },
            selectionScopes = new[]
            {
                "elementIds", "selectionToken", "savedSelectionName",
                "selection", "last_filter", "active_view", "whole_model"
            },
            document = new
            {
                locale = session.DetectedLocale,
                hasWorksets = caps.HasWorksets,
                hasPhases = caps.HasPhases,
                hasDesignOptions = caps.HasDesignOptions,
                hasLinkedModels = caps.HasLinkedModels,
                enabledDynamicTools = caps.EnabledTools.OrderBy(name => name).ToArray()
            },
            // Stated limitations save an agent a fruitless search of the whole tool
            // catalogue. Everything listed here was looked for, and not found, during
            // real sessions.
            lifecycleLimitations = new[]
            {
                "RiveTT starts every Revit session in READ-ONLY mode. Write tools are refused with PermissionDenied until a human presses Écriture in the RiveTT ribbon panel (Add-Ins tab). No tool can lift it, dryRun included.",
                "edit_family edits type parameter VALUES in the background (dimensions, materials, yes/no, text) on EXISTING types — not new types, not geometry. For geometry or new types, open_family opens the .rfa visually in Revit; close_document closes it afterward (closing the ACTIVE document requires another open document to switch to first — Document.Close(false) refuses the active document, a real Revit API constraint, not a bug).",
                "Adding a member to a group cannot be done in place: edit_group_members ungroups and recreates the type, and the other instances keep the old definition. REMOVING a member is different — it is Revit's exclusion, applied to that instance only, with the type untouched.",
                "There is no API to restore an excluded group member: do it from the Revit ribbon (Restore Excluded Members) after selecting the instance.",
                "Stairs are created by component (straight runs + automatic landings) through create_stair. Sketched stairs, spiral runs and winders are not exposed."
            },
            discoveryHints = new[]
            {
                "A blank project comes from create_document(templatePath, targetPath) — save_as_document duplicates the OPEN model instead.",
                "open_document switches the active document; every later call targets it and all caches are flushed.",
                "Vertical circulation: create_stair between two levels, then a railing via its railingTypeId.",
                "System types (walls, floors, ceilings, roofs, railings, stairs, title blocks) are NOT loadable families: enumerate them with list_system_types, duplicate them with duplicate_system_type.",
                "There is no 'create similar' tool: copy_elements with an offset re-hosts the copy and does the same job, including across levels (level constraints are recomputed).",
                "To split a room without a physical wall, use create_room_separation_line.",
                "Instances of ONE group type may legitimately differ: an excluded member, or a grouped wall whose height follows its own level constraints. manage_model_groups reports memberCount and hasExcludedMembers per instance — never assume the first instance holds the full definition.",
                "Each group instance owns its OWN copies of the members, with their own element ids: ids read from one instance mean nothing in another.",
                "Category labels are localized and sometimes ambiguous (French Revit names the viewport category 'Fenetres ', like windows): prefer the OST_* codes returned as categoryBic."
            }
        });
    }

    private static string GetActiveRevitVersion(CortexSession session)
    {
        try
        {
            return (session.Store.Get<object>("activeDocument") as Autodesk.Revit.DB.Document)
                ?.Application?.VersionNumber ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
