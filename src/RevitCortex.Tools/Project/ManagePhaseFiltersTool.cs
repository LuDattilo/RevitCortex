using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

/// <summary>
/// List, set, or create Revit Phase Filters.
///
/// A PhaseFilter has four per-phase settings — how elements New, Demolished,
/// Existing, and Temporary (relative to the current phase) are displayed:
/// None, ByCategory, Overridden, NotDisplayed.
///
/// Without this tool the only way to fix a phase filter is Manage &gt; Phasing &gt;
/// Phase Filters in the Revit UI, or a send_code_to_revit script — the latter
/// being fragile with some third-party add-ins.
///
/// Presentation values map 1:1 to the Revit API enum PhaseStatusPresentation:
///   ShowByCategory (also accepted: ByCategory)
///   ShowOverriden  (also accepted: Overridden, Overriden)
///   DontShow       (also accepted: NotDisplayed, None, Hidden)
/// </summary>
public class ManagePhaseFiltersTool : ICortexTool
{
    public string Name => "manage_phase_filters";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "List, set, or create Revit Phase Filters. Actions: list | set | create. The 'set' action changes one presentation for one filter (preserves the other three). For each phase status (New|Demolished|Existing|Temporary), the presentation is None | ByCategory | Overridden | NotDisplayed.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var action = (input["action"]?.Value<string>() ?? "list").ToLowerInvariant();

        try
        {
            return action switch
            {
                "list"   => ListFilters(doc),
                "set"    => SetFilter(doc, input, session),
                "create" => CreateFilter(doc, input, session),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action: {action}",
                    suggestion: "Use one of: list, set, create")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"manage_phase_filters failed: {ex.Message}");
        }
    }

    // ── LIST ────────────────────────────────────────────────────────────────
    private static CortexResult<object> ListFilters(Document doc)
    {
        var filters = new FilteredElementCollector(doc)
            .OfClass(typeof(PhaseFilter))
            .Cast<PhaseFilter>()
            .OrderBy(f => f.Name)
            .ToList();

        var rows = filters.Select(f => new
        {
            id = GetElementIdLong(f.Id),
            name = f.Name,
            presentations = new
            {
                New        = f.GetPhaseStatusPresentation(ElementOnPhaseStatus.New).ToString(),
                Existing   = f.GetPhaseStatusPresentation(ElementOnPhaseStatus.Existing).ToString(),
                Demolished = f.GetPhaseStatusPresentation(ElementOnPhaseStatus.Demolished).ToString(),
                Temporary  = f.GetPhaseStatusPresentation(ElementOnPhaseStatus.Temporary).ToString(),
            }
        }).Cast<object>().ToList();

        return CortexResult<object>.Ok(new
        {
            action = "list",
            filterCount = rows.Count,
            filters = rows
        });
    }

    // ── SET ─────────────────────────────────────────────────────────────────
    private static CortexResult<object> SetFilter(Document doc, JObject input, CortexSession session)
    {
        var filterName = input["filterName"]?.Value<string>();
        var filterId   = input["filterId"]?.Value<long>();
        var status     = input["status"]?.Value<string>();
        var presentation = input["presentation"]?.Value<string>();

        if (string.IsNullOrWhiteSpace(filterName) && (filterId == null || filterId == 0))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "filterName or filterId is required");
        if (string.IsNullOrWhiteSpace(status))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "status is required (New | Demolished | Existing | Temporary)");
        if (string.IsNullOrWhiteSpace(presentation))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "presentation is required (None | ByCategory | Overridden | NotDisplayed)");

        if (!Enum.TryParse<ElementOnPhaseStatus>(status, ignoreCase: true, out var statusEnum))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unknown status '{status}'", suggestion: "Use: New, Demolished, Existing, Temporary");
        var presentationEnumNullable = ResolvePresentation(presentation);
        if (presentationEnumNullable == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unknown presentation '{presentation}'",
                suggestion: "Use: ByCategory (ShowByCategory), Overridden (ShowOverriden), NotDisplayed (DontShow)");
        var presentationEnum = presentationEnumNullable.Value;

        var filter = FindFilter(doc, filterName, filterId);
        if (filter == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Phase filter not found (name='{filterName}', id={filterId}).");

        if (!session.RequestConfirmation("modify phase filter", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Modify Phase Filter");
        tx.Start();
        filter.SetPhaseStatusPresentation(statusEnum, presentationEnum);
        tx.Commit();

        return CortexResult<object>.Ok(new
        {
            action = "set",
            filterName = filter.Name,
            filterId = GetElementIdLong(filter.Id),
            status = statusEnum.ToString(),
            presentation = presentationEnum.ToString(),
            presentations = new
            {
                New        = filter.GetPhaseStatusPresentation(ElementOnPhaseStatus.New).ToString(),
                Existing   = filter.GetPhaseStatusPresentation(ElementOnPhaseStatus.Existing).ToString(),
                Demolished = filter.GetPhaseStatusPresentation(ElementOnPhaseStatus.Demolished).ToString(),
                Temporary  = filter.GetPhaseStatusPresentation(ElementOnPhaseStatus.Temporary).ToString(),
            }
        });
    }

    // ── CREATE ──────────────────────────────────────────────────────────────
    private static CortexResult<object> CreateFilter(Document doc, JObject input, CortexSession session)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "name is required for create action");

        // Reject duplicates proactively (PhaseFilter.Create throws on duplicate, but the
        // Revit message is cryptic).
        var existing = new FilteredElementCollector(doc).OfClass(typeof(PhaseFilter))
            .Cast<PhaseFilter>().FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"A phase filter named '{name}' already exists (id={GetElementIdLong(existing.Id)}). Use action='set' to modify it.");

        // Defaults match Revit's "Show All" filter: everything ByCategory.
        var newP  = ParsePresentation(input["newStatus"]?.Value<string>(), PhaseStatusPresentation.ShowByCategory);
        var exP   = ParsePresentation(input["existingStatus"]?.Value<string>(), PhaseStatusPresentation.ShowByCategory);
        var demP  = ParsePresentation(input["demolishedStatus"]?.Value<string>(), PhaseStatusPresentation.ShowByCategory);
        var tempP = ParsePresentation(input["temporaryStatus"]?.Value<string>(), PhaseStatusPresentation.ShowByCategory);

        if (!session.RequestConfirmation("create phase filter", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Create Phase Filter");
        tx.Start();
        var created = PhaseFilter.Create(doc, name!);
        created.SetPhaseStatusPresentation(ElementOnPhaseStatus.New,        newP);
        created.SetPhaseStatusPresentation(ElementOnPhaseStatus.Existing,   exP);
        created.SetPhaseStatusPresentation(ElementOnPhaseStatus.Demolished, demP);
        created.SetPhaseStatusPresentation(ElementOnPhaseStatus.Temporary,  tempP);
        tx.Commit();

        return CortexResult<object>.Ok(new
        {
            action = "create",
            filterName = created.Name,
            filterId = GetElementIdLong(created.Id),
            presentations = new
            {
                New        = newP.ToString(),
                Existing   = exP.ToString(),
                Demolished = demP.ToString(),
                Temporary  = tempP.ToString(),
            }
        });
    }

    // ── Helpers ─────────────────────────────────────────────────────────────
    private static PhaseFilter? FindFilter(Document doc, string? name, long? id)
    {
        var collector = new FilteredElementCollector(doc).OfClass(typeof(PhaseFilter)).Cast<PhaseFilter>();
        if (id != null && id != 0)
        {
#if REVIT2024_OR_GREATER
            var target = new ElementId((long)id);
#else
            var target = new ElementId((int)id);
#endif
            var byId = collector.FirstOrDefault(f => f.Id.Equals(target));
            if (byId != null) return byId;
        }
        if (!string.IsNullOrWhiteSpace(name))
            return collector.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        return null;
    }

    /// <summary>Parse a presentation name with user-friendly aliases.</summary>
    private static PhaseStatusPresentation? ResolvePresentation(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var n = raw!.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
        return n switch
        {
            "showbycategory" or "bycategory" or "category"    => PhaseStatusPresentation.ShowByCategory,
            "showoverriden" or "showoverridden" or
                "overriden" or "overridden" or "override"     => PhaseStatusPresentation.ShowOverriden,
            "dontshow" or "notdisplayed" or "hidden" or
                "none" or "donotshow"                         => PhaseStatusPresentation.DontShow,
            _ => null
        };
    }

    private static PhaseStatusPresentation ParsePresentation(string? raw, PhaseStatusPresentation fallback)
        => ResolvePresentation(raw) ?? fallback;

    private static long GetElementIdLong(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}
