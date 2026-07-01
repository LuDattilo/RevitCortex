using Autodesk.Revit.DB;
using RevitCortex.Core.Discovery;
using System.Linq;

namespace RevitCortex.Plugin.Discovery;

public class DocumentAnalyzer : IDocumentAnalyzer
{
    public void Analyze(object document, DocumentCapabilities caps)
    {
        if (!(document is Document doc)) return;

        caps.Reset();

        caps.HasWorksets = doc.IsWorkshared;
        caps.HasPhases = doc.Phases.Size > 0;

        caps.HasDesignOptions = new FilteredElementCollector(doc)
            .OfClass(typeof(DesignOption))
            .GetElementCount() > 0;

        caps.HasLinkedModels = new FilteredElementCollector(doc)
            .OfClass(typeof(RevitLinkInstance))
            .GetElementCount() > 0;

        var elements = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType();

        foreach (Element elem in elements)
        {
            var cat = elem.Category;
            if (cat != null)
            {
#if REVIT2025_OR_GREATER
                caps.PresentCategories.Add(cat.BuiltInCategory.ToString());
#else
                var bic = (BuiltInCategory)cat.Id.IntegerValue;
                caps.PresentCategories.Add(bic.ToString());
#endif
            }
        }

        if (caps.HasWorksets)
        {
            caps.EnableTool("get_worksets");
            caps.EnableTool("manage_worksets");
            caps.EnableTool("set_element_workset");
        }

        if (caps.HasPhases)
        {
            caps.EnableTool("get_phases");
            caps.EnableTool("set_element_phase");
        }

        if (caps.HasLinkedModels)
        {
            caps.EnableTool("get_linked_file_instances");
            caps.EnableTool("get_link_transform");
            caps.EnableTool("reload_linked_file_from");
            caps.EnableTool("pin_unpin_link_instance");
            caps.EnableTool("move_link_instance");
            caps.EnableTool("align_link_to_host");
            caps.EnableTool("highlight_linked_element");
            caps.EnableTool("show_cross_model_elements");
            caps.EnableTool("get_selected_linked_elements");
        }

        if (caps.PresentCategories.Contains("OST_Rooms"))
        {
            caps.EnableTool("get_room_openings");
        }

        // Dynamo tools: enable the dynamic ones (status + run) only when Dynamo for Revit
        // is installed for this Revit version. The static generate/list tools register
        // unconditionally (IsDynamic=false) and are unaffected.
        var dynamoCaps = new RevitCortex.Tools.Dynamo.Runtime.DynamoCapabilityProbe()
            .Probe(RevitCortex.Tools.Dynamo.Runtime.RevitYear.Current);
        if (dynamoCaps.IsPresent)
        {
            caps.EnableTool("dynamo_get_status");
            caps.EnableTool("dynamo_run_graph");
        }
    }
}
