using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Project;

/// <summary>
/// Lists existing design option sets and options, and reports which one an element
/// belongs to. Creating a DesignOptionSet/DesignOption has no public Revit API
/// (confirmed: even Rhino.Inside.Revit's own documentation calls API support for
/// design options "very limited") — there is nothing to build here, only to read.
/// </summary>
[ToolSafety(true, false)]
public class ListDesignOptionsTool : ICortexTool
{
    public string Name => "list_design_options";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Lists existing design option sets and their options, and (with elementId) reports which option an " +
        "element belongs to. Creating a design option set/option from scratch has no public Revit API " +
        "(confirmed unsupported) — use Revit's own Design Options dialog to create them, then read them here.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var elementIdLong = input["elementId"]?.Value<long?>();
        if (elementIdLong is > 0)
        {
            var elem = doc.GetElement(ToolHelpers.ToElementId(elementIdLong.Value));
            if (elem == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"Element {elementIdLong} not found");

            var option = elem.DesignOption;
            return CortexResult<object>.Ok(new
            {
                elementId = elementIdLong,
                designOptionId = option != null ? ToolHelpers.GetElementIdValue(option.Id) : (long?)null,
                designOptionName = option?.Name
            });
        }

        // DesignOptionSet is NOT a public Revit API type — it exists in the DB but its
        // class is internal, so OfClass(typeof(DesignOptionSet)) does not compile. The
        // sets are reachable only as plain Elements of OST_DesignOptionSets; Name and Id
        // are all that is needed, and DesignOption (which IS public) links back through
        // its OPTION_SET_ID parameter.
        var sets = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_DesignOptionSets)
            .WhereElementIsNotElementType()
            .Cast<Element>()
            .Select(set => new
            {
                id = ToolHelpers.GetElementIdValue(set.Id),
                name = set.Name,
                options = new FilteredElementCollector(doc)
                    .OfClass(typeof(DesignOption))
                    .Cast<DesignOption>()
                    .Where(o => o.get_Parameter(BuiltInParameter.OPTION_SET_ID)?.AsElementId() == set.Id)
                    .Select(o => new
                    {
                        id = ToolHelpers.GetElementIdValue(o.Id),
                        name = o.Name,
                        isPrimary = o.IsPrimary
                    })
                    .ToList()
            })
            .ToList();

        return CortexResult<object>.Ok(new { count = sets.Count, designOptionSets = sets });
    }
}
